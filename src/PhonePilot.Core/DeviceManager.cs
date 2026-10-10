namespace PhonePilot.Core;

public sealed class DeviceManager : IAsyncDisposable
{
    private sealed class Runtime(DeviceConfiguration configuration, bool known)
    {
        public DeviceConfiguration Configuration = configuration;
        public bool Known = known;
        public bool? Mdns;
        public AdbState Adb;
        public SessionState Session;
        public string? Endpoint, Transport, Model, Android, Build, Error;
        public bool Busy, Suppressed, ReconnectWanted, Stopping;
        public int Revision, Retry;
        public DateTimeOffset NextRetry;
        public StreamSettings? ActiveStream;
        public IScrcpySession? Process;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public CancellationTokenSource? Operation;
    }
    private readonly object _sync = new();
    private readonly Dictionary<string, Runtime> _devices = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SessionSlots _slots = new(4);
    private readonly IAdbClient _adb;
    private readonly IScrcpySessionFactory _sessions;
    private readonly LogStore _logs;
    private readonly TimeProvider _clock;
    private ApplicationSettings _settings;
    private string? _selected;
    private bool _maintenance, _disposed;
    private int _refreshPending;
    public event Action? Changed;
    public event Action? ConfigurationChanged;
    public int ActiveCount => _slots.Count;
    public DateTimeOffset? LastRefresh { get; private set; }
    public string? DiscoveryWarning { get; private set; }

    public DeviceManager(AppConfiguration configuration, IAdbClient adb, IScrcpySessionFactory sessions, LogStore logs, TimeProvider? clock = null)
    {
        configuration.Validate(); _settings = configuration.Application; _selected = configuration.SelectedDeviceSerial;
        _adb = adb; _sessions = sessions; _logs = logs; _clock = clock ?? TimeProvider.System;
        foreach (var device in configuration.Devices) _devices.Add(device.Serial, new(device, true));
    }
    public AppConfiguration Configuration { get { lock (_sync) return new() { Application = _settings, SelectedDeviceSerial = _selected,
        Devices = _devices.Values.Where(d => d.Known).Select(d => d.Configuration).ToArray() }; } }
    public IReadOnlyList<DeviceSnapshot> Snapshot()
    {
        lock (_sync) return _devices.Values.Select(d => new DeviceSnapshot(d.Configuration, d.Known, d.Mdns, d.Adb, d.Session, d.Endpoint, d.Transport,
            d.Model, d.Android, d.Build, d.Error, d.Busy, d.ActiveStream)).ToArray();
    }
    public void Select(string? serial) { lock (_sync) _selected = serial; ConfigurationChanged?.Invoke(); }
    public void UpdateApplication(ApplicationSettings settings) { settings.Validate(); lock (_sync) _settings = settings; ConfigurationChanged?.Invoke(); Changed?.Invoke(); }
    public void UpdateDevice(DeviceConfiguration configuration)
    {
        configuration.Validate(); lock (_sync) Get(configuration.Serial).Configuration = configuration;
        ConfigurationChanged?.Invoke(); Changed?.Invoke();
    }
    private Runtime Get(string serial) => _devices.TryGetValue(serial, out var device) ? device : throw new IOException("Das Gerät ist nicht mehr in der Liste.");

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (!await _refresh.WaitAsync(0, token)) { Interlocked.Exchange(ref _refreshPending, 1); return; }
        try
        {
            do
            {
                Dictionary<string, int> revisions;
                lock (_sync) { if (_maintenance || _disposed) return; revisions = _devices.ToDictionary(p => p.Key, p => p.Value.Revision); }
                var found = await _adb.DiscoverAsync(token);
                var retry = new List<string>();
                bool configChanged = false;
                lock (_sync)
                {
                    if (_maintenance || _disposed) return;
                    DiscoveryWarning = found.Warning;
                    foreach (var service in found.Services)
                        if (!_devices.ContainsKey(service.Serial)) _devices.Add(service.Serial, new(new DeviceConfiguration { Serial = service.Serial, DisplayName = service.Name }, false));
                    foreach (var transport in found.Transports.Where(t => t.State == "device" && !string.IsNullOrWhiteSpace(t.Serial)))
                        if (!_devices.ContainsKey(transport.Serial!)) _devices.Add(transport.Serial!, new(new DeviceConfiguration { Serial = transport.Serial!, DisplayName = transport.Model ?? "Android-Gerät" }, false));
                    foreach (var (serial, device) in _devices)
                    {
                        if (device.Busy || (revisions.TryGetValue(serial, out int revision) && device.Revision != revision)) continue;
                        var service = found.Services.LastOrDefault(s => s.Serial == serial);
                        if (found.MdnsSucceeded) { device.Mdns = service is not null; device.Endpoint = service?.Endpoint; }
                        if (found.AdbSucceeded)
                        {
                            var connected = found.Transports.FirstOrDefault(t => t.State == "device" && t.Serial == serial);
                            if (connected is not null)
                            {
                                bool wasKnown = device.Known;
                                ApplyConnected(device, connected); configChanged |= !wasKnown;
                            }
                            else
                            {
                                var candidate = found.Transports.FirstOrDefault(t => AdbParser.SerialFromInstance(t.Id) == serial || (service is not null && t.Id == service.Endpoint));
                                device.Adb = candidate?.State switch { "unauthorized" => AdbState.Unauthorized, "offline" => AdbState.Offline, _ => AdbState.Disconnected };
                                device.Transport = null;
                            }
                        }
                        if (_settings.AutoRefreshEnabled && device.Known && device.Configuration.ReconnectAdb && device.ReconnectWanted && !device.Suppressed &&
                            device.Adb is AdbState.Disconnected or AdbState.Offline && device.Mdns == true && device.NextRetry <= _clock.GetUtcNow()) retry.Add(serial);
                    }
                    LastRefresh = _clock.GetUtcNow();
                }
                if (configChanged) ConfigurationChanged?.Invoke();
                Changed?.Invoke();
                foreach (string serial in retry)
                {
                    try { await ConnectAsync(serial, token: token, userInitiated: false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex) { _logs.Write("ADB", ex.Message, serial, "WARN"); }
                }
            } while (Interlocked.Exchange(ref _refreshPending, 0) != 0);
        }
        finally { _refresh.Release(); }
    }

    private void ApplyConnected(Runtime device, AdbTransport transport)
    {
        if (transport.Serial != device.Configuration.Serial || transport.State != "device" || !AdbParser.IsWirelessTransport(transport.Id))
            throw new IOException("Die Hardware-Seriennummer des verbundenen Geräts stimmt nicht überein.");
        device.Adb = AdbState.Connected; device.Transport = transport.Id; device.Model = transport.Model; device.Android = transport.AndroidVersion; device.Build = transport.Build;
        device.ReconnectWanted = true; device.Retry = 0; device.NextRetry = default; device.Known = true;
        if (device.Session != SessionState.Failed) device.Error = null;
        if (device.Configuration.DisplayName == "Android-Gerät" && !string.IsNullOrWhiteSpace(transport.Model)) device.Configuration = device.Configuration with { DisplayName = transport.Model };
    }

    public void AcceptManualConnection(AdbTransport transport)
    {
        if (string.IsNullOrWhiteSpace(transport.Serial)) throw new IOException("Keine bestätigte Gerätekennung.");
        lock (_sync)
        {
            if (!_devices.TryGetValue(transport.Serial, out var device)) _devices.Add(transport.Serial, device = new(new DeviceConfiguration { Serial = transport.Serial, DisplayName = transport.Model ?? "Android-Gerät" }, true));
            if (device.Busy) throw new IOException("Für dieses Gerät läuft bereits eine Aktion.");
            device.Revision++; device.Suppressed = false; ApplyConnected(device, transport);
        }
        ConfigurationChanged?.Invoke(); Changed?.Invoke();
    }

    private async Task InDeviceAsync(string serial, Func<Runtime, CancellationToken, Task> action, CancellationToken token)
    {
        Runtime device;
        lock (_sync) { if (_maintenance || _disposed) throw new IOException("PhonePilot wird beendet oder der ADB-Server wird gewartet."); device = Get(serial); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdown.Token);
        await device.Gate.WaitAsync(linked.Token);
        try
        {
            lock (_sync)
            {
                if (_maintenance || _disposed || !_devices.TryGetValue(serial, out var current) || !ReferenceEquals(current, device)) throw new IOException("Die Aktion ist nicht mehr verfügbar.");
                device.Operation = linked; device.Busy = true; device.Revision++; device.Error = null;
            }
            Changed?.Invoke();
            await action(device, linked.Token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            lock (_sync) { device.Error = ex.Message; if (device.Adb is AdbState.Connecting or AdbState.Disconnecting) device.Adb = AdbState.Error; }
            _logs.Write("Anwendung", ex.Message, serial, "ERROR"); throw;
        }
        finally
        {
            lock (_sync) { device.Operation = null; device.Busy = false; if (device.Adb is AdbState.Connecting or AdbState.Disconnecting) device.Adb = AdbState.Disconnected; }
            device.Gate.Release(); Changed?.Invoke();
        }
    }

    private async Task ConnectCoreAsync(Runtime device, CancellationToken token, bool userInitiated)
    {
        lock (_sync) { if (userInitiated) device.Suppressed = false; else if (device.Suppressed) return; device.Adb = AdbState.Connecting; }
        Changed?.Invoke();
        try
        {
            var transport = await _adb.ConnectAsync(device.Configuration.Serial, device.Endpoint, token);
            lock (_sync) ApplyConnected(device, transport);
            _logs.Write("ADB", "Gerät verbunden; Seriennummer bestätigt.", device.Configuration.Serial);
            ConfigurationChanged?.Invoke();
        }
        catch
        {
            lock (_sync) { device.Retry++; device.NextRetry = _clock.GetUtcNow().AddSeconds(Math.Min(30, 5 * Math.Pow(2, Math.Min(device.Retry - 1, 3)))); }
            throw;
        }
    }

    public Task ConnectAsync(string serial, CancellationToken token = default, bool userInitiated = true)
        => InDeviceAsync(serial, (device, ct) => ConnectCoreAsync(device, ct, userInitiated), token);

    public Task InspectAsync(string serial, Func<string, CancellationToken, Task<AdbTransport>> inspect, CancellationToken token = default)
        => InDeviceAsync(serial, async (device, ct) =>
        {
            string transport;
            lock (_sync) transport = device.Transport ?? throw new IOException("Bitte das Gerät zuerst verbinden.");
            var information = await inspect(transport, ct);
            ct.ThrowIfCancellationRequested();
            lock (_sync) ApplyConnected(device, information);
            ConfigurationChanged?.Invoke();
        }, token);

    public Task StartAsync(string serial, CancellationToken token = default) => InDeviceAsync(serial, async (device, ct) =>
    {
        lock (_sync) { if (device.Process is not null) return; }
        if (!_slots.TryReserve(serial)) throw new IOException("Maximal vier Sitzungen sind gleichzeitig möglich. Bitte zuerst eine Sitzung stoppen.");
        IScrcpySession? process = null;
        try
        {
            lock (_sync) device.Session = SessionState.Starting;
            Changed?.Invoke();
            await ConnectCoreAsync(device, ct, true);
            DeviceConfiguration configuration; string transport;
            lock (_sync) { configuration = device.Configuration; transport = device.Transport ?? throw new IOException("Kein bestätigter ADB-Transport."); }
            process = _sessions.Start(configuration, transport);
            lock (_sync) { device.Process = process; device.ActiveStream = configuration.Stream; device.Stopping = false; }
            _ = ObserveSessionAsync(device, process);
            await process.WaitUntilReadyAsync(ct);
            lock (_sync)
            {
                if (!ReferenceEquals(device.Process, process) || process.Completion.IsCompleted) throw new IOException("scrcpy wurde während des Starts beendet.");
                device.Session = SessionState.Running;
            }
            _logs.Write("scrcpy", "Sitzung läuft im eigenen Fenster.", serial);
        }
        catch (Exception ex)
        {
            if (process is not null) { lock (_sync) device.Stopping = true; try { await process.StopAsync(); } catch (Exception stopError) { _logs.Write("scrcpy", stopError.Message, serial, "WARN"); } }
            lock (_sync) { device.Session = ex is OperationCanceledException ? SessionState.Stopped : SessionState.Failed; device.ActiveStream = null; device.Process = null; }
            _slots.Release(serial); throw;
        }
    }, token);

    private async Task ObserveSessionAsync(Runtime device, IScrcpySession process)
    {
        int code = -1;
        try { code = await process.Completion; }
        catch (Exception ex) { _logs.Write("scrcpy", ex.Message, device.Configuration.Serial, "ERROR"); }
        lock (_sync)
        {
            if (ReferenceEquals(device.Process, process))
            {
                device.Process = null; device.ActiveStream = null;
                device.Session = device.Stopping || code == 0 ? SessionState.Stopped : SessionState.Failed;
                if (device.Session == SessionState.Failed) device.Error = code == 2 ? "Die Geräteverbindung wurde während der Sitzung unterbrochen. Erneut starten, sobald ADB verbunden ist." : "scrcpy wurde mit einem Fehler beendet. Details stehen in Logs.";
                _slots.Release(device.Configuration.Serial);
            }
        }
        _logs.Write("scrcpy", "Sitzung beendet (Exit " + code + ").", device.Configuration.Serial, code == 0 || device.Stopping ? "INFO" : "WARN");
        Changed?.Invoke();
        try { await process.DisposeAsync(); } catch (Exception ex) { _logs.Write("scrcpy", ex.Message, device.Configuration.Serial, "WARN"); }
    }

    public async Task StopAsync(string serial, CancellationToken token = default)
    {
        Runtime device;
        lock (_sync) { device = Get(serial); device.Operation?.Cancel(); device.Stopping = true; }
        await device.Gate.WaitAsync(token);
        try
        {
            IScrcpySession? process;
            lock (_sync) { process = device.Process; device.Session = process is null ? SessionState.Stopped : SessionState.Stopping; device.Revision++; }
            Changed?.Invoke();
            if (process is not null) await process.StopAsync();
            lock (_sync) { if (ReferenceEquals(device.Process, process)) device.Process = null; device.Session = SessionState.Stopped; device.ActiveStream = null; _slots.Release(serial); }
        }
        finally { device.Gate.Release(); Changed?.Invoke(); }
    }

    public async Task DisconnectAsync(string serial, CancellationToken token = default)
    {
        lock (_sync) { var device = Get(serial); device.Suppressed = true; device.Operation?.Cancel(); }
        await StopAsync(serial, token);
        await InDeviceAsync(serial, async (device, ct) =>
        {
            string? transport;
            lock (_sync) { device.Suppressed = true; transport = device.Transport; device.Adb = AdbState.Disconnecting; }
            if (transport is not null) await _adb.DisconnectAsync(transport, ct);
            lock (_sync) { device.Adb = AdbState.Disconnected; device.Transport = null; }
            _logs.Write("ADB", "Bewusst getrennt; automatische Wiederverbindung ausgesetzt.", serial);
        }, token);
    }

    public async Task ForgetAsync(string serial, CancellationToken token = default)
    {
        await StopAsync(serial, token);
        lock (_sync) { var device = Get(serial); if (device.Busy) throw new IOException("Gerät ist beschäftigt."); _devices.Remove(serial); if (_selected == serial) _selected = null; }
        ConfigurationChanged?.Invoke(); Changed?.Invoke();
    }
    public bool ShowWindow(string serial) { lock (_sync) return Get(serial).Process?.ShowWindow() == true; }
    public async Task<IReadOnlyList<string>> AllAsync(string action, CancellationToken token = default)
    {
        string[] targets;
        lock (_sync) targets = _devices.Where(p => action == "stop" ? p.Value.Process is not null || p.Value.Busy :
            p.Value.Known && (p.Value.Mdns == true || p.Value.Adb == AdbState.Connected)).Select(p => p.Key).ToArray();
        var results = new List<string>();
        foreach (string serial in targets)
        {
            var state = Snapshot().FirstOrDefault(s => s.Configuration.Serial == serial);
            if (state is null) continue;
            if ((action == "start" && state.Session is SessionState.Running or SessionState.Starting) || (action == "connect" && state.Adb == AdbState.Connected)) { results.Add(state.Configuration.DisplayName + ": bereits aktiv, übersprungen"); continue; }
            try
            {
                if (action == "stop") await StopAsync(serial, token);
                else if (action == "connect") await ConnectAsync(serial, token);
                else await StartAsync(serial, token);
                results.Add(state.Configuration.DisplayName + ": erfolgreich");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { results.Add(state.Configuration.DisplayName + ": " + ex.Message); }
        }
        return results;
    }
    public async Task MaintenanceAsync(Func<CancellationToken, Task> action, CancellationToken token = default)
    {
        lock (_sync) { if (_maintenance) throw new IOException("Wartung läuft bereits."); _maintenance = true; foreach (var device in _devices.Values) { device.Revision++; device.Operation?.Cancel(); } }
        bool entered = false;
        try { await _refresh.WaitAsync(token); entered = true; await AllAsync("stop", token); await action(token); }
        finally { if (entered) _refresh.Release(); lock (_sync) _maintenance = false; Changed?.Invoke(); }
        await RefreshAsync(token);
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        _shutdown.Cancel();
        await AllAsync("stop");
    }
}
