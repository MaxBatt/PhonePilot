using PhonePilot.Core;

namespace PhonePilot.Tests;

public sealed class DeviceManagerTests
{
    private static AppConfiguration Known(int count = 1) => new() { Devices = Enumerable.Range(0, count).Select(i => new DeviceConfiguration { Serial = "P" + i, DisplayName = "Pixel " + i }).ToArray() };

    [Fact]
    public async Task DisappearingMdnsDoesNotDisconnectVerifiedAdb()
    {
        var adb = new FakeAdb { Current = new([], [new("192.168.1.2:12345", "device", "P0")]) };
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        await manager.RefreshAsync();
        var state = Assert.Single(manager.Snapshot());
        Assert.Equal(AdbState.Connected, state.Adb);
        Assert.False(state.MdnsFound);
        Assert.Equal(0, adb.Connections);
    }

    [Fact]
    public async Task WrongIdentityNeverStartsScrcpyAndReleasesSlot()
    {
        var adb = new FakeAdb { ConnectedSerial = "OTHER" }; var factory = new FakeFactory();
        await using var manager = new DeviceManager(Known(), adb, factory, new LogStore());
        await Assert.ThrowsAsync<IOException>(() => manager.StartAsync("P0"));
        Assert.Empty(factory.Sessions);
        Assert.Equal(0, manager.ActiveCount);
    }

    [Fact]
    public async Task FourSessionsAreIndependentAndFifthIsRefused()
    {
        var factory = new FakeFactory();
        await using var manager = new DeviceManager(Known(5), new FakeAdb(), factory, new LogStore());
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => manager.StartAsync("P" + i)));
        await Assert.ThrowsAsync<IOException>(() => manager.StartAsync("P4"));
        Assert.Equal(4, manager.ActiveCount);
        await manager.StopAsync("P1");
        Assert.Equal(3, manager.ActiveCount);
        Assert.Equal(1, factory.Sessions["P1"].Stops);
        Assert.Equal(0, factory.Sessions["P0"].Stops);
        await manager.StartAsync("P4");
        Assert.Equal(4, manager.ActiveCount);
    }

    [Fact]
    public async Task DisconnectSuppressesReconnectUntilExplicitConnect()
    {
        var adb = new FakeAdb();
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        await manager.ConnectAsync("P0");
        await manager.DisconnectAsync("P0");
        adb.Current = new([new("P0", "Pixel", "192.168.1.2", 12345)], []);
        await manager.RefreshAsync();
        Assert.Equal(1, adb.Connections);
        await manager.ConnectAsync("P0");
        Assert.Equal(2, adb.Connections);
    }

    [Fact]
    public async Task DeviceReturnReconnectsAdbWithoutStartingSession()
    {
        var adb = new FakeAdb(); var factory = new FakeFactory();
        await using var manager = new DeviceManager(Known(), adb, factory, new LogStore());
        await manager.ConnectAsync("P0");
        adb.Current = new([new("P0", "Pixel", "192.168.1.2", 54321)], []);
        await manager.RefreshAsync();
        Assert.Equal(2, adb.Connections);
        Assert.Empty(factory.Sessions);
    }

    [Fact]
    public async Task FailedQueryDoesNotInventDisconnection()
    {
        var adb = new FakeAdb();
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        await manager.ConnectAsync("P0");
        adb.Current = new([], [], false, false, "Abfrage fehlgeschlagen");
        await manager.RefreshAsync();
        Assert.Equal(AdbState.Connected, manager.Snapshot()[0].Adb);
    }

    [Fact]
    public async Task SettingsChangedDuringSessionDoNotChangeActiveSnapshot()
    {
        await using var manager = new DeviceManager(Known(), new FakeAdb(), new FakeFactory(), new LogStore());
        await manager.StartAsync("P0");
        var original = manager.Snapshot()[0];
        manager.UpdateDevice(original.Configuration with { Stream = original.Configuration.Stream.WithPreset("fast") });
        var changed = manager.Snapshot()[0];
        Assert.Equal(1920, changed.ActiveStream!.MaxSize);
        Assert.Equal(1280, changed.Configuration.Stream.MaxSize);
    }

    private sealed class FakeAdb : IAdbClient
    {
        public DiscoveryResult Current = new([], []);
        public string? ConnectedSerial;
        public int Connections;
        public Func<CancellationToken, Task<DiscoveryResult>>? Discovery;
        public Func<string, CancellationToken, Task<AdbTransport>>? Connection;
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken token) => Discovery?.Invoke(token) ?? Task.FromResult(Current);
        public Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token)
        { Interlocked.Increment(ref Connections); return Connection?.Invoke(serial, token) ?? Task.FromResult(new AdbTransport("192.168.1.2:12345", "device", ConnectedSerial ?? serial)); }
        public Task DisconnectAsync(string transport, CancellationToken token) => Task.CompletedTask;
        public Task<DisplayPower> ReadDisplayPowerAsync(string serial, string transport, CancellationToken token) => Task.FromResult(DisplayPower.Off);
    }
    private sealed class FakeFactory : IScrcpySessionFactory
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, FakeSession> Sessions = new();
        public Task? Ready;
        public int Starts;
        public IScrcpySession Start(DeviceConfiguration configuration, string transport) { Interlocked.Increment(ref Starts); var session = new FakeSession { Ready = Ready }; Sessions[configuration.Serial] = session; return session; }
    }
    private sealed class FakeSession : IScrcpySession
    {
        private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Stops;
        public Task? Ready;
        public Task<int> Completion => _done.Task;
        public Task WaitUntilReadyAsync(CancellationToken token) => Ready?.WaitAsync(token) ?? Task.CompletedTask;
        public void Exit(int code) => _done.TrySetResult(code);
        public Task StopAsync() { if (_done.TrySetResult(0)) Interlocked.Increment(ref Stops); return Task.CompletedTask; }
        public bool ShowWindow() => true;
        public bool HasWindow => !Completion.IsCompleted;
        public Task SetDisplayPowerAsync(bool on, CancellationToken token) => Task.CompletedTask;
        public async ValueTask DisposeAsync() => await StopAsync();
    }

    [Fact]
    public async Task StartupDiscoveryDoesNotAutoConnectSavedDevices()
    {
        var adb = new FakeAdb { Current = new([new("P0", "Pixel", "192.168.1.2", 12345)], []) };
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        await manager.RefreshAsync();
        Assert.Equal(0, adb.Connections); Assert.Equal(AdbState.Disconnected, manager.Snapshot()[0].Adb);
    }

    [Fact]
    public async Task ConcurrentStartsForSameDeviceCreateExactlyOneProcess()
    {
        var factory = new FakeFactory();
        await using var manager = new DeviceManager(Known(), new FakeAdb(), factory, new LogStore());
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => manager.StartAsync("P0")));
        Assert.Equal(1, factory.Starts); Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public async Task StartingSessionsReserveCapacityAndCanBeCancelledIndividually()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeFactory { Ready = ready.Task };
        await using var manager = new DeviceManager(Known(5), new FakeAdb(), factory, new LogStore());
        var starts = Enumerable.Range(0, 4).Select(i => manager.StartAsync("P" + i)).ToArray();
        Assert.Equal(4, manager.ActiveCount);
        await Assert.ThrowsAsync<IOException>(() => manager.StartAsync("P4"));
        await manager.StopAsync("P1");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starts[1]);
        Assert.Equal(3, manager.ActiveCount);
        ready.SetResult();
        await Task.WhenAll(starts.Where((_, i) => i != 1));
        await manager.StartAsync("P4"); Assert.Equal(4, manager.ActiveCount);
    }

    [Fact]
    public async Task OldDiscoveryCannotOverwriteNewUserConnection()
    {
        var discovery = new TaskCompletionSource<DiscoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adb = new FakeAdb { Discovery = _ => discovery.Task };
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        var refresh = manager.RefreshAsync(); await manager.ConnectAsync("P0");
        discovery.SetResult(new([], [])); await refresh;
        Assert.Equal(AdbState.Connected, manager.Snapshot()[0].Adb);
    }

    [Fact]
    public async Task RepeatedRefreshRequestsCoalesceWithoutOverlappingQueries()
    {
        var first = new TaskCompletionSource<DiscoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, active = 0, max = 0;
        var adb = new FakeAdb { Discovery = async _ =>
        {
            int running = Interlocked.Increment(ref active); max = Math.Max(max, running);
            int call = Interlocked.Increment(ref calls);
            try { return call == 1 ? await first.Task : new([], []); }
            finally { Interlocked.Decrement(ref active); }
        } };
        await using var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        var refresh = manager.RefreshAsync();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => manager.RefreshAsync()));
        first.SetResult(new([], [])); await refresh;
        Assert.Equal(1, max); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GlobalStartContinuesAfterWrongIdentityOnOneDevice()
    {
        var adb = new FakeAdb { Current = new(Enumerable.Range(0, 4).Select(i => new MdnsService("P" + i, "Pixel", "192.168.1.2", 12345 + i)).ToArray(), []),
            Connection = (serial, _) => Task.FromResult(new AdbTransport("192.168.1.2:12345", "device", serial == "P0" ? "WRONG" : serial)) };
        var factory = new FakeFactory();
        await using var manager = new DeviceManager(Known(4), adb, factory, new LogStore());
        await manager.RefreshAsync(); var results = await manager.AllAsync("start");
        Assert.Equal(4, results.Count); Assert.Equal(3, manager.ActiveCount); Assert.Equal(3, factory.Starts);
    }

    [Fact]
    public async Task UnexpectedSessionExitReleasesSlotWithoutRestarting()
    {
        var factory = new FakeFactory(); var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var manager = new DeviceManager(Known(), new FakeAdb(), factory, new LogStore());
        await manager.StartAsync("P0");
        manager.Changed += () => { if (manager.Snapshot()[0].Session == SessionState.Failed) changed.TrySetResult(); };
        factory.Sessions["P0"].Exit(2); await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, manager.ActiveCount); Assert.Equal(1, factory.Starts);
        Assert.Equal(AdbState.Connected, manager.Snapshot()[0].Adb);
    }

    [Fact]
    public async Task ClosingCancelsAnInFlightConnectionAndClearsReservation()
    {
        var connecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adb = new FakeAdb { Connection = async (serial, token) => { connecting.SetResult(); await Task.Delay(30000, token); return new("192.168.1.2:12345", "device", serial); } };
        var manager = new DeviceManager(Known(), adb, new FakeFactory(), new LogStore());
        var start = manager.StartAsync("P0"); await connecting.Task;
        await manager.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(0, manager.ActiveCount); Assert.False(manager.Snapshot()[0].Busy);
    }

    [Fact]
    public async Task ConnectedAdbRefreshDoesNotEraseSessionFailureExplanation()
    {
        var factory = new FakeFactory(); var adb = new FakeAdb();
        await using var manager = new DeviceManager(Known(), adb, factory, new LogStore());
        await manager.StartAsync("P0");
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.Changed += () => { if (manager.Snapshot()[0].Session == SessionState.Failed) ended.TrySetResult(); };
        factory.Sessions["P0"].Exit(2); await ended.Task.WaitAsync(TimeSpan.FromSeconds(3));
        adb.Current = new([], [new("192.168.1.2:12345", "device", "P0")]);
        await manager.RefreshAsync();
        Assert.Equal(SessionState.Failed, manager.Snapshot()[0].Session); Assert.NotNull(manager.Snapshot()[0].Error);
    }

    [Fact]
    public async Task LateDiagnosticResultCannotUndoManualDisconnect()
    {
        await using var manager = new DeviceManager(Known(), new FakeAdb(), new FakeFactory(), new LogStore());
        await manager.ConnectAsync("P0");
        var information = new TaskCompletionSource<AdbTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspect = manager.InspectAsync("P0", (_, _) => information.Task);
        var disconnect = manager.DisconnectAsync("P0");
        information.SetResult(new("192.168.1.2:12345", "device", "P0"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspect); await disconnect;
        Assert.Equal(AdbState.Disconnected, manager.Snapshot()[0].Adb);
    }
}
