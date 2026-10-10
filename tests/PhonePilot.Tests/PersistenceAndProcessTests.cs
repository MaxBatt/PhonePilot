using PhonePilot.Core;

namespace PhonePilot.Tests;

public sealed class PersistenceAndProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PhonePilot.Tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "config.json");
    public PersistenceAndProcessTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task MigrationBacksUpExactOriginalAndRoundTripsSettings()
    {
        const string legacy = "{\"DeviceSerial\":\"P0\"}";
        await File.WriteAllTextAsync(FilePath, legacy);
        var store = new ConfigurationStore(FilePath);
        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded.Configuration);
        await store.SaveAsync(loaded.Configuration);
        Assert.Equal(legacy, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory, "*.bak"))));
        var saved = await new ConfigurationStore(FilePath).LoadAsync();
        Assert.True(saved.CanSave); Assert.False(saved.IsLegacy);
        Assert.Equal("h265", saved.Configuration.Devices[0].Stream.VideoCodec);
        Assert.Equal(40, saved.Configuration.Devices[0].Stream.VideoBitRateMbps);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task FutureSchemaCannotBeOverwrittenWithoutBackupRecovery()
    {
        const string original = "{\"schemaVersion\":999,\"futureData\":true}";
        await File.WriteAllTextAsync(FilePath, original);
        var store = new ConfigurationStore(FilePath);
        Assert.False((await store.LoadAsync()).CanSave);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new()));
        Assert.Equal(original, await File.ReadAllTextAsync(FilePath));
        await store.AllowRecoveryAsync(); await store.SaveAsync(new());
        Assert.Equal(original, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory, "*.bak"))));
    }

    [Fact]
    public async Task LockedConfigurationSurvivesFailedSaveAndTemporaryIsRemoved()
    {
        await File.WriteAllTextAsync(FilePath, ConfigurationStore.Serialize(new()));
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Record.ExceptionAsync(() => new ConfigurationStore(FilePath).SaveAsync(new() { Application = new() { Theme = "dark" } }));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        Assert.Equal("system", (await new ConfigurationStore(FilePath).LoadAsync()).Configuration.Application.Theme);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentSavesProduceCompleteJson()
    {
        var store = new ConfigurationStore(FilePath);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveAsync(new() { Application = new() { RefreshIntervalSeconds = 2 + i } })));
        Assert.True((await store.LoadAsync()).CanSave);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private static (ProcessRunner Runner, string Tool, LogStore Logs) Child()
    {
        var logs = new LogStore();
        return (new(new ToolPaths(AppContext.BaseDirectory), logs), Path.Combine(AppContext.BaseDirectory, "fake", "PhonePilot.FakeTool.exe"), logs);
    }

    [Fact]
    public async Task ChildOutputAndErrorDrainConcurrentlyWithoutDeadlock()
    {
        var (runner, tool, _) = Child();
        var result = await runner.RunAsync(tool, ["flood"], TimeSpan.FromSeconds(10), default);
        Assert.True(result.Success); Assert.Contains("out-2499", result.Output); Assert.Contains("err-2499", result.Error);
    }

    [Theory]
    [InlineData("829104")]
    [InlineData("A1234567890B1234567890C1234567890D")]
    public async Task PairingSecretIsAbsentFromResultsAndLogs(string pairingSecret)
    {
        var (runner, tool, logs) = Child();
        var result = await runner.RunAsync(tool, ["echo-secret"], TimeSpan.FromSeconds(10), default, input: pairingSecret, logOutput: true);
        Assert.True(result.Success); Assert.DoesNotContain(pairingSecret, result.Text);
        Assert.All(logs.Snapshot(), line => Assert.DoesNotContain(pairingSecret, line.Text));
        Assert.Contains("Kopplungscode ausgeblendet", result.Text);
    }

    [Fact]
    public async Task TimeoutStopsOnlyOwnedChildAndReturnsPartialOutput()
    {
        var (runner, tool, _) = Child();
        var result = await runner.RunAsync(tool, ["delay"], TimeSpan.FromSeconds(2), default);
        Assert.True(result.TimedOut); Assert.False(result.Success); Assert.Contains("started", result.Output);
    }

    [Fact]
    public async Task UserCancellationPropagates()
    {
        var (runner, tool, _) = Child();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(tool, ["delay"], TimeSpan.FromSeconds(20), cancel.Token));
    }

    [Fact]
    public void LogMemoryRemainsBoundedWhenDiskIsUnavailable()
    {
        var path = Path.Combine(_directory, "file"); File.WriteAllText(path, "occupied");
        var logs = new LogStore(path);
        for (int i = 0; i < 5005; i++) logs.Write("Anwendung", "Entry " + i);
        Assert.Equal(5000, logs.Snapshot().Count); Assert.NotNull(logs.FileError);
        Assert.Equal("Entry 5", logs.Snapshot()[0].Message);
    }

    [Fact]
    public async Task RealSessionProcessesStopIndependentlyAndDisposeSafely()
    {
        string target = Path.Combine(_directory, "scrcpy"); Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fake")))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file) == "PhonePilot.FakeTool.exe" ? "scrcpy.exe" : Path.GetFileName(file)));
        var paths = new ToolPaths(_directory); var logs = new LogStore();
        var factory = new ScrcpySessionFactory(paths, new ProcessRunner(paths, logs), logs);
        await using var first = factory.Start(new() { Serial = "ONE" }, "192.168.1.2:12345");
        await using var second = factory.Start(new() { Serial = "TWO" }, "192.168.1.3:12345");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(first.WaitUntilReadyAsync(deadline.Token), second.WaitUntilReadyAsync(deadline.Token));
        await Task.WhenAll(first.StopAsync(), first.DisposeAsync().AsTask(), first.StopAsync());
        Assert.True(first.Completion.IsCompleted); Assert.False(second.Completion.IsCompleted);
        Assert.Contains(logs.Snapshot(), entry => entry.Serial == "TWO" && entry.Message.Contains("Texture:"));
    }

    [Fact]
    public void LogRotationKeepsThreeBoundedFilesAndTruncatesHugeLines()
    {
        var logs = new LogStore(_directory); string path = Path.Combine(_directory, "PhonePilot.log");
        for (int i = 0; i < 4; i++)
        {
            File.WriteAllText(path, new string('x', 5 * 1024 * 1024));
            logs.Write("Anwendung", "Round " + i);
        }
        logs.Write("Anwendung", new string('y', 100000));
        Assert.Equal(3, Directory.GetFiles(_directory, "PhonePilot.log*").Length);
        Assert.All(Directory.GetFiles(_directory, "PhonePilot.log*"), file => Assert.True(new FileInfo(file).Length <= 5 * 1024 * 1024));
        Assert.True(logs.Snapshot().Last().Message.Length < 17000);
    }
}
