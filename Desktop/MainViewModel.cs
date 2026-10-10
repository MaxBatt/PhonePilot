using System.Windows.Data;
using System.Windows.Threading;

namespace PhonePilot.Desktop;

public sealed class MainViewModel : Observable
{
    public DeviceManager Manager { get; }
    public AdbClient? Adb { get; }
    public CancellationToken LifetimeToken => _lifetime.Token;
    public ToolPaths Paths { get; }
    public LogStore LogStore { get; }
    private readonly ConfigurationStore? _store;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _saving = new(1, 1);
    private readonly DispatcherTimer _saveTimer;
    private Task? _poll;
    private int _refreshRequested, _syncQueued;
    private DeviceViewModel? _selected;
    private string _saveStatus = "Änderungen werden automatisch gespeichert.", _notice = "", _scope = "Dieses Gerät", _source = "Alle Quellen", _level = "Info und höher", _search = "";
    private DateTimeOffset _clearedBefore = DateTimeOffset.MinValue;
    private bool _recoveryNeeded, _closing;
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public ObservableCollection<LogEntry> Logs { get; } = [];
    public ICollectionView LogView { get; }
    public IReadOnlyList<Choice> Presets { get; } = [new("quality", "Qualität"), new("balanced", "Ausgewogen"), new("fast", "Schnell"), new("custom", "Benutzerdefiniert")];
    public IReadOnlyList<Choice> Codecs { get; } = [new("h264", "H.264"), new("h265", "H.265")];
    public IReadOnlyList<Choice> Orientations { get; } = [new("auto", "Automatisch"), new("0", "0°"), new("90", "90°"), new("180", "180°"), new("270", "270°")];
    public IReadOnlyList<string> Sizes { get; } = ["Original", "1280", "1920", "2560", "3840"];
    public IReadOnlyList<string> FpsValues { get; } = ["Unbegrenzt", "30", "60", "90", "120"];
    public IReadOnlyList<string> Scopes { get; } = ["Dieses Gerät", "Alle Meldungen"];
    public IReadOnlyList<string> Sources { get; } = ["Alle Quellen", "ADB", "mDNS", "scrcpy", "Anwendung"];
    public IReadOnlyList<string> Levels { get; } = ["Info und höher", "Warnungen und Fehler", "Nur Fehler"];
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand ShowCommand { get; }
    public AsyncCommand AllConnectCommand { get; }
    public AsyncCommand AllStartCommand { get; }
    public AsyncCommand AllStopCommand { get; }
    public AsyncCommand EncodersCommand { get; }
    public AsyncCommand InformationCommand { get; }
    public AsyncCommand DiagnoseCommand { get; }
    public string SaveStatus { get => _saveStatus; private set { _saveStatus = value; Raise(nameof(SaveStatus)); } }
    public string Notice { get => _notice; set { _notice = value; Raise(nameof(Notice)); Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public bool RecoveryNeeded => _recoveryNeeded;
    public bool SaveFailed { get; private set; }
    public bool ToolsAvailable => Paths.MissingFiles.Count == 0;
    public bool HasSelection => SelectedDevice is not null;
    public bool NoDevices => Devices.Count == 0;
    public string DeviceCount => "Geräte (" + Devices.Count + ")";
    public string SessionCount => Manager.ActiveCount + " von 4 Sitzungen aktiv";
    public string LastUpdated => Manager.LastRefresh is { } time ? "Zuletzt aktualisiert: " + time.ToLocalTime().ToString("HH:mm:ss") : "Noch nicht aktualisiert";
    public string LogCount => LogView.Cast<LogEntry>().Count() + " Einträge" + (LogScope == "Dieses Gerät" && SelectedDevice is not null ? " · " + SelectedDevice.Name : "");
    public bool AutoScroll { get; set; } = true;
    public DeviceViewModel? SelectedDevice
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value; Manager.Select(value?.Serial); Raise(nameof(SelectedDevice)); Raise(nameof(HasSelection)); FilterChanged(); CommandManager.InvalidateRequerySuggested();
        }
    }
    public string LogScope { get => _scope; set { _scope = value; FilterChanged(); } }
    public string LogSource { get => _source; set { _source = value; FilterChanged(); } }
    public string LogLevel { get => _level; set { _level = value; FilterChanged(); } }
    public string LogSearch { get => _search; set { _search = value; FilterChanged(); } }

    public MainViewModel(DeviceManager manager, AdbClient? adb, ToolPaths paths, LogStore logs, ConfigurationStore? store, ConfigurationLoad loaded)
    {
        Manager = manager; Adb = adb; Paths = paths; LogStore = logs; _store = store; _recoveryNeeded = !loaded.CanSave;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await FlushSaveAsync(); };
        LogView = CollectionViewSource.GetDefaultView(Logs);
        LogView.Filter = item => item is LogEntry entry && entry.Time > _clearedBefore && (LogScope != "Dieses Gerät" || entry.Serial == SelectedDevice?.Serial) &&
            (LogSource == "Alle Quellen" || entry.Source == LogSource) && (LogLevel == "Info und höher" || entry.Level == "ERROR" || (LogLevel == "Warnungen und Fehler" && entry.Level == "WARN")) &&
            (string.IsNullOrWhiteSpace(LogSearch) || entry.Text.Contains(LogSearch, StringComparison.CurrentCultureIgnoreCase));
        Manager.Changed += ScheduleSynchronize;
        Manager.ConfigurationChanged += ScheduleSave;
        logs.Added += OnLog;
        RefreshCommand = Command(() => manager.RefreshAsync(_lifetime.Token), () => ToolsAvailable);
        ConnectCommand = Command(() => manager.ConnectAsync(SelectedDevice!.Serial, _lifetime.Token), () => CanChange && SelectedDevice!.State.Adb != AdbState.Connected);
        StartCommand = Command(() => manager.StartAsync(SelectedDevice!.Serial, _lifetime.Token), () => CanChange && SelectedDevice!.State.Session is SessionState.Stopped or SessionState.Failed && Manager.ActiveCount < 4);
        StopCommand = Command(() => manager.StopAsync(SelectedDevice!.Serial), () => SelectedDevice is not null && (SelectedDevice.State.Session is SessionState.Starting or SessionState.Running || SelectedDevice.State.Busy));
        ShowCommand = Command(() => { if (!manager.ShowWindow(SelectedDevice!.Serial)) Notice = "Das Sitzungsfenster konnte nicht aktiviert werden. Bitte das scrcpy-Fenster über die Taskleiste öffnen."; return Task.CompletedTask; }, () => SelectedDevice?.IsRunning == true);
        AllConnectCommand = Command(() => AllAsync("connect"), () => ToolsAvailable);
        AllStartCommand = Command(() => AllAsync("start"), () => ToolsAvailable && Manager.ActiveCount < 4);
        AllStopCommand = Command(() => AllAsync("stop"), () => Manager.ActiveCount > 0);
        EncodersCommand = Command(LoadEncodersAsync, () => Adb is not null && SelectedDevice?.State.Adb == AdbState.Connected && !SelectedDevice.State.Busy);
        InformationCommand = Command(ReadInformationAsync, () => Adb is not null && SelectedDevice?.State.Adb == AdbState.Connected && !SelectedDevice.State.Busy);
        DiagnoseCommand = Command(DiagnoseAsync, () => ToolsAvailable && SelectedDevice is not null && !SelectedDevice.State.Busy);
        Synchronize();
        if (loaded.Warning is not null) { Notice = loaded.Warning; SaveStatus = "Konfiguration geschützt – nicht gespeichert."; }
        if (loaded.IsLegacy) SaveStatus = "Bisherige Geräteauswahl wird mit Sicherung übernommen.";
    }
    private bool CanChange => ToolsAvailable && SelectedDevice is not null && !SelectedDevice.State.Busy;
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null) => new(action, enabled, Report);
    public void Report(Exception error) { Notice = error.Message; LogStore.Write("Anwendung", error.Message, SelectedDevice?.Serial, "ERROR"); }

    public async Task InitializeAsync()
    {
        if (!ToolsAvailable) { Notice = "Werkzeugdateien fehlen: " + string.Join(", ", Paths.MissingFiles.Select(Path.GetFileName)) + ". Bitte vollständige adb- und scrcpy-Pakete neben PhonePilot.exe ablegen."; return; }
        try { if (Adb is not null) await Adb.StartServerAsync(_lifetime.Token); await Manager.RefreshAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception ex) when (ex is not OperationCanceledException) { Report(ex); }
        if (_closing) return;
        _poll = PollAsync();
    }
    private async Task PollAsync()
    {
        DateTimeOffset next = DateTimeOffset.UtcNow.AddSeconds(Manager.Configuration.Application.RefreshIntervalSeconds);
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(250, _lifetime.Token);
                var settings = Manager.Configuration.Application;
                bool requested = Interlocked.Exchange(ref _refreshRequested, 0) != 0;
                if (!settings.AutoRefreshEnabled) continue;
                if (!requested && DateTimeOffset.UtcNow < next) continue;
                try { await Manager.RefreshAsync(_lifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Report(ex); }
                next = DateTimeOffset.UtcNow.AddSeconds(settings.RefreshIntervalSeconds);
            }
        }
        catch (OperationCanceledException) { }
    }
    public void SettingsChanged(ApplicationSettings settings)
    {
        Manager.UpdateApplication(settings); Interlocked.Exchange(ref _refreshRequested, 1); ThemeManager.Apply(settings.Theme);
    }
    private void ScheduleSynchronize()
    {
        if (Interlocked.Exchange(ref _syncQueued, 1) == 0) _dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref _syncQueued, 0); Synchronize(); });
    }
    private void Synchronize()
    {
        var snapshots = Manager.Snapshot();
        foreach (var existing in Devices.Where(d => !snapshots.Any(s => s.Configuration.Serial == d.Serial)).ToArray()) Devices.Remove(existing);
        foreach (var snapshot in snapshots)
        {
            var model = Devices.FirstOrDefault(d => d.Serial == snapshot.Configuration.Serial);
            if (model is null) Devices.Add(new DeviceViewModel(Manager, snapshot)); else model.Update(snapshot);
        }
        if (SelectedDevice is null || !Devices.Contains(SelectedDevice))
            SelectedDevice = Devices.FirstOrDefault(d => d.Serial == Manager.Configuration.SelectedDeviceSerial) ?? Devices.FirstOrDefault();
        if (!_recoveryNeeded && !string.IsNullOrWhiteSpace(Manager.DiscoveryWarning)) Notice = Manager.DiscoveryWarning!;
        if (LogStore.FileError is not null) Notice = LogStore.FileError;
        Raise(nameof(DeviceCount)); Raise(nameof(SessionCount)); Raise(nameof(LastUpdated)); Raise(nameof(NoDevices)); Raise(nameof(HasSelection));
        CommandManager.InvalidateRequerySuggested();
    }
    public void SelectSerial(string serial) { Synchronize(); SelectedDevice = Devices.FirstOrDefault(d => d.Serial == serial); }
    private void ScheduleSave() => _dispatcher.BeginInvoke(() => { if (_closing) return; _saveTimer.Stop(); _saveTimer.Start(); });
    public async Task FlushSaveAsync()
    {
        if (_store is null) return;
        await _saving.WaitAsync();
        try
        {
            if (_recoveryNeeded) return;
            await _store.SaveAsync(Manager.Configuration);
            SaveFailed = false;
            SaveStatus = "Änderungen werden automatisch gespeichert.";
        }
        catch (Exception ex) { SaveFailed = true; SaveStatus = "Nicht gespeichert – Ordner und Schreibrechte prüfen."; Report(ex); }
        finally { _saving.Release(); }
    }
    public async Task RecoverConfigurationAsync()
    {
        if (_store is null) return;
        await _store.AllowRecoveryAsync(); _recoveryNeeded = false; Raise(nameof(RecoveryNeeded)); Notice = "Originalkonfiguration gesichert. Aktuelle Einstellungen werden neu gespeichert.";
        await FlushSaveAsync();
    }
    private void OnLog(LogEntry entry) => _dispatcher.BeginInvoke(() =>
    {
        Logs.Add(entry); while (Logs.Count > 5000) Logs.RemoveAt(0); Raise(nameof(LogCount));
    });
    private void FilterChanged() { LogView.Refresh(); Raise(nameof(LogCount)); }
    public void ClearLogView() { _clearedBefore = DateTimeOffset.Now; FilterChanged(); }
    public string ExportLogs() => string.Join(Environment.NewLine, LogView.Cast<LogEntry>().Select(e => e.Text));
    private async Task AllAsync(string action)
    {
        var results = await Manager.AllAsync(action, _lifetime.Token);
        Notice = results.Count == 0 ? "Keine passenden Geräte für diese Aktion verfügbar." : string.Join(" · ", results);
    }
    private async Task ReadInformationAsync()
    {
        var selected = SelectedDevice!;
        await Manager.InspectAsync(selected.Serial, async (transport, ct) =>
        {
            var result = await Adb!.ReadInformationAsync(transport, ct);
            if (result.Serial != selected.Serial) throw new IOException("Die Geräteidentität hat sich geändert. Bitte erneut verbinden.");
            LogStore.Write("ADB", "Geräteinformationen: " + result.Model + " · Android " + result.AndroidVersion + " · " + result.Build, selected.Serial);
            return result;
        }, _lifetime.Token);
        Notice = "Geräteinformationen aktualisiert.";
    }
    private async Task DiagnoseAsync()
    {
        var selected = SelectedDevice!;
        await Manager.RefreshAsync(_lifetime.Token);
        var current = Manager.Snapshot().FirstOrDefault(s => s.Configuration.Serial == selected.Serial) ?? throw new IOException("Gerät ist nicht mehr vorhanden.");
        string result = "mDNS: " + (current.MdnsFound == true ? "gefunden" : current.MdnsFound == false ? "nicht gefunden" : "unbekannt") +
            "; ADB: " + current.Adb + "; scrcpy: " + current.Session + (current.Adb == AdbState.Connected ? "; Seriennummer bestätigt: " + selected.Serial : "; keine bestätigte aktive Verbindung.");
        LogStore.Write("ADB", result, selected.Serial, current.Adb == AdbState.Connected ? "INFO" : "WARN");
        Notice = "Verbindungsprüfung für „" + selected.Name + "“ abgeschlossen. Details stehen in Logs.";
    }
    private async Task LoadEncodersAsync()
    {
        var selected = SelectedDevice!;
        IReadOnlyList<string> encoders = [];
        string codec = selected.Codec;
        await Manager.InspectAsync(selected.Serial, async (transport, ct) =>
        {
            var info = await Adb!.ReadInformationAsync(transport, ct);
            if (info.Serial != selected.Serial) throw new IOException("Die Geräteidentität stimmt nicht überein.");
            encoders = await Adb.EncodersAsync(info.Id, codec, ct);
            return info;
        }, _lifetime.Token);
        if (codec != selected.Codec) { Notice = "Der Codec wurde während der Abfrage geändert. Bitte Encoder erneut ermitteln."; return; }
        selected.Encoders.Clear(); selected.Encoders.Add("Automatisch"); foreach (var encoder in encoders) selected.Encoders.Add(encoder);
        if (selected.State.Configuration.Stream.VideoEncoder is { } configured && !selected.Encoders.Contains(configured)) selected.Encoders.Add(configured);
        selected.RefreshEncoderSelection();
        Notice = encoders.Count + " Encoder für " + selected.Codec.ToUpperInvariant() + " gefunden.";
    }
    public async Task ShutdownAsync()
    {
        _closing = true; _saveTimer.Stop(); _lifetime.Cancel();
        if (_poll is not null) try { await _poll; } catch (OperationCanceledException) { }
        await Manager.DisposeAsync(); await FlushSaveAsync();
        Manager.Changed -= ScheduleSynchronize; Manager.ConfigurationChanged -= ScheduleSave; LogStore.Added -= OnLog;
    }
}
