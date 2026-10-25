using PhonePilot.Core;

namespace PhonePilot.Tests;

public sealed class DisplayPowerTests
{
    [Theory]
    [InlineData("OFF", DisplayPower.Off)]
    [InlineData("0", DisplayPower.Off)]
    [InlineData("ON", DisplayPower.On)]
    [InlineData("2", DisplayPower.On)]
    [InlineData("DOZE", DisplayPower.On)]
    [InlineData("DOZE_SUSPEND", DisplayPower.On)]
    [InlineData("UNKNOWN", DisplayPower.Unknown)]
    public void ReadsPhysicalPowerEvenWhenAndroidLogicalStateIsOn(string mode, DisplayPower expected)
    {
        var dump = "mWakefulness=Awake\nDisplay 4619827259835644672:\n  connectionType=Internal\n  powerMode=" + mode +
            "\nVirtual Display 99:\n  powerMode=ON\n";
        Assert.Equal(expected, DisplayPowerParser.Parse(dump));
    }

    [Fact]
    public void ExternalAndVirtualDisplaysCannotOverrideTheHandyDisplay()
    {
        const string dump = """
            Display 987:
              connectionType=External
              powerMode=ON
            Display 123:
              connectionType=Internal
              powerMode=OFF
            Virtual Display 456:
              powerMode=ON
            """;
        Assert.Equal(DisplayPower.Off, DisplayPowerParser.Parse(dump));
    }

    [Theory]
    [InlineData("")]
    [InlineData("mWakefulness=Awake\nDisplay Power: state=ON")]
    [InlineData("Permission Denial: can't dump SurfaceFlinger")]
    [InlineData("Virtual Display 123:\n  powerMode=ON")]
    [InlineData("Display 123:\n  connectionType=Internal\nVirtual Display 456:\n  powerMode=ON")]
    [InlineData("Display 123:\n  powerMode=ON\nDisplay 456:\n  powerMode=OFF")]
    [InlineData("Display 123:\n  connectionType=External\n  powerMode=ON")]
    public void DoesNotGuessForMissingOrAmbiguousPhysicalState(string dump)
        => Assert.Equal(DisplayPower.Unknown, DisplayPowerParser.Parse(dump));

    [Fact]
    public void FoldableWithOneLitInternalPanelIsOn()
        => Assert.Equal(DisplayPower.On, DisplayPowerParser.Parse(
            "Display 1:\n  type=internal\n  powerMode=0\nDisplay 2:\n  type=internal\n  powerMode=2"));

    private static DeviceManager Manager(FakeAdb adb, FakeSessions sessions)
        => new(new AppConfiguration { Devices = [
            new() { Serial = "FIRST" }, new() { Serial = "SECOND" }
        ] }, adb, sessions, new LogStore());

    [Fact]
    public async Task DisplayActionTargetsCapturedDeviceAndPreservesStartSettings()
    {
        var adb = new FakeAdb(); var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST"); await manager.StartAsync("SECOND");
        manager.Select("SECOND");
        var settings = manager.Configuration;
        await manager.SetDisplayPowerAsync("FIRST", true);
        Assert.Equal(new[] { true }, sessions.Items["FIRST"].Requests);
        Assert.Empty(sessions.Items["SECOND"].Requests);
        Assert.Equal(DisplayPower.On, await manager.ReadDisplayPowerAsync("FIRST"));
        Assert.Equal(DisplayPower.Off, await manager.ReadDisplayPowerAsync("SECOND"));
        Assert.Equal(ConfigurationStore.Serialize(settings), ConfigurationStore.Serialize(manager.Configuration));
    }

    [Fact]
    public async Task StoppedSessionCannotReceiveDisplayCommands()
    {
        var adb = new FakeAdb(); var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST"); await manager.StopAsync("FIRST");
        Assert.False(manager.HasWindow("FIRST"));
        await Assert.ThrowsAsync<IOException>(() => manager.SetDisplayPowerAsync("FIRST", true));
        Assert.Empty(sessions.Items["FIRST"].Requests);
    }

    [Fact]
    public async Task UnknownStateNeverSendsAnAssumedToggle()
    {
        var adb = new FakeAdb { Read = (_, _) => Task.FromResult(DisplayPower.Unknown) };
        var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST");
        await Assert.ThrowsAsync<IOException>(() => manager.SetDisplayPowerAsync("FIRST", true));
        Assert.Empty(sessions.Items["FIRST"].Requests);
    }

    [Fact]
    public async Task LateReadFromOldSessionIsDiscarded()
    {
        var adb = new FakeAdb(); var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST");
        var pending = new TaskCompletionSource<DisplayPower>(TaskCreationOptions.RunContinuationsAsynchronously);
        adb.Read = (_, _) => pending.Task;
        var read = manager.ReadDisplayPowerAsync("FIRST");
        await manager.StopAsync("FIRST"); await manager.StartAsync("FIRST");
        pending.SetResult(DisplayPower.On);
        Assert.Equal(DisplayPower.Unknown, await read);
    }

    [Fact]
    public async Task StoppingDuringStateVerificationCancelsTheCommand()
    {
        var adb = new FakeAdb(); var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adb.Read = async (_, ct) => { entered.SetResult(); await Task.Delay(30000, ct); return DisplayPower.Off; };
        var change = manager.SetDisplayPowerAsync("FIRST", true);
        await entered.Task;
        await manager.StopAsync("FIRST");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);
        Assert.Empty(sessions.Items["FIRST"].Requests);
    }

    [Fact]
    public async Task ChangingPhysicalStateOutsidePhonePilotIsReadAgain()
    {
        var adb = new FakeAdb(); var sessions = new FakeSessions(adb);
        await using var manager = Manager(adb, sessions);
        await manager.StartAsync("FIRST");
        Assert.Equal(DisplayPower.Off, await manager.ReadDisplayPowerAsync("FIRST"));
        adb.Power["FIRST"] = DisplayPower.On;
        Assert.Equal(DisplayPower.On, await manager.ReadDisplayPowerAsync("FIRST"));
    }

    private sealed class FakeAdb : IAdbClient
    {
        public Dictionary<string, DisplayPower> Power = [];
        public Func<string, CancellationToken, Task<DisplayPower>>? Read;
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new DiscoveryResult([], []));
        public Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token)
            => Task.FromResult(new AdbTransport(serial == "FIRST" ? "192.0.2.1:5555" : "192.0.2.2:5555", "device", serial));
        public Task DisconnectAsync(string transport, CancellationToken token) => Task.CompletedTask;
        public Task<DisplayPower> ReadDisplayPowerAsync(string serial, string transport, CancellationToken token)
            => Read?.Invoke(serial, token) ?? Task.FromResult(Power.GetValueOrDefault(serial, DisplayPower.Off));
    }
    private sealed class FakeSessions(FakeAdb adb) : IScrcpySessionFactory
    {
        public Dictionary<string, FakeSession> Items = [];
        public IScrcpySession Start(DeviceConfiguration configuration, string transport)
            => Items[configuration.Serial] = new FakeSession(adb, configuration.Serial);
    }
    private sealed class FakeSession(FakeAdb adb, string serial) : IScrcpySession
    {
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<bool> Requests = [];
        public Task<int> Completion => _done.Task;
        public bool HasWindow => !Completion.IsCompleted;
        public Task WaitUntilReadyAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() { _done.TrySetResult(0); return Task.CompletedTask; }
        public bool ShowWindow() => HasWindow;
        public Task SetDisplayPowerAsync(bool on, CancellationToken token)
        { Requests.Add(on); adb.Power[serial] = on ? DisplayPower.On : DisplayPower.Off; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
