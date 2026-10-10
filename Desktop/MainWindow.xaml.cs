using System.Diagnostics;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PhonePilot.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private bool _closing, _allowClose, _explicitExit;
    public MainWindow(MainViewModel model, bool preview = false)
    {
        InitializeComponent(); _model = model; DataContext = model;
        ThemeManager.Apply(model.Manager.Configuration.Application.Theme);
        ToolsText.Text = model.ToolsAvailable ? "ADB und scrcpy: verfügbar (lokale Werkzeugpakete)" : "Fehlende Dateien: " + string.Join(", ", model.Paths.MissingFiles.Select(Path.GetFileName));
        model.Logs.CollectionChanged += (_, _) => { if (model.AutoScroll && LogGrid.Items.Count > 0) LogGrid.ScrollIntoView(LogGrid.Items[LogGrid.Items.Count - 1]); };
        if (!preview)
        {
            _trayIcon = LoadTrayIcon();
            _tray = new Forms.NotifyIcon { Text = "PhonePilot", Icon = _trayIcon, Visible = false };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("PhonePilot öffnen", null, (_, _) => Dispatcher.BeginInvoke(OpenWindow));
            menu.Items.Add("Alle Sitzungen stoppen", null, async (_, _) => await RunAsync(async () => { await _model.Manager.AllAsync("stop"); }));
            menu.Items.Add("Beenden", null, (_, _) => Dispatcher.BeginInvoke(() => { _explicitExit = true; OpenWindow(); Close(); }));
            _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(OpenWindow);
            Closing += OnClosing; SystemEvents.UserPreferenceChanged += SystemThemeChanged;
            Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= SystemThemeChanged; _tray.Dispose(); _trayIcon.Dispose(); };
        }
    }
    internal static System.Drawing.Icon LoadTrayIcon()
    {
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/PhonePilot.ico")).Stream;
        using var resourceIcon = new System.Drawing.Icon(iconStream, 32, 32);
        return (System.Drawing.Icon)resourceIcon.Clone();
    }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => ThemeManager.Apply(_model.Manager.Configuration.Application.Theme));
    private void OpenWindow() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); if (_tray is not null) _tray.Visible = false; }
    internal void SelectPreviewTab(int index) => DeviceTabs.SelectedIndex = index;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return; e.Cancel = true; if (_closing) return; _closing = true;
        try
        {
            Keyboard.ClearFocus(); int active = Math.Max(_model.Manager.ActiveCount, _model.Manager.Snapshot().Count(d => d.Busy)); string? decision = "exit";
            if (active > 0)
            {
                if (_explicitExit)
                { if (MessageBox.Show(this, "Eigene Sitzungen beenden und PhonePilot vollständig schließen?", "PhonePilot beenden", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return; }
                else
                {
                    decision = _model.Manager.Configuration.Application.CloseBehavior;
                    if (decision == "ask")
                    { var choice = Dialogs.Closing(this, active); decision = choice.Decision; if (choice.Remember) _model.SettingsChanged(_model.Manager.Configuration.Application with { CloseBehavior = decision! }); }
                }
            }
            if (decision is null) return;
            if (decision == "tray") { if (_tray is not null) _tray.Visible = true; Hide(); await _model.FlushSaveAsync(); return; }
            await _model.FlushSaveAsync();
            if (_model.SaveFailed && MessageBox.Show(this, "Die aktuellen Einstellungen konnten nicht gespeichert werden. Trotzdem beenden?", "Nicht gespeicherte Einstellungen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            IsEnabled = false; await _model.ShutdownAsync(); _allowClose = true; Close(); Application.Current.Shutdown();
        }
        catch (Exception ex) { IsEnabled = true; _model.Report(ex); }
        finally { _closing = false; _explicitExit = false; }
    }
    private async Task RunAsync(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { _model.Report(ex); } }
    private void SettingsClick(object sender, RoutedEventArgs e) => Dialogs.GeneralSettings(this, _model).ShowDialog();
    private async void AddClick(object sender, RoutedEventArgs e) => await RunAsync(() => Dialogs.AddDeviceAsync(this, _model));
    private void DismissNotice(object sender, RoutedEventArgs e) => _model.Notice = "";
    private void DeviceSettingsClick(object sender, RoutedEventArgs e) => DeviceTabs.SelectedIndex = 1;
    private void LogsClick(object sender, RoutedEventArgs e) => DeviceTabs.SelectedIndex = 3;
    private async void CopySerialClick(object sender, RoutedEventArgs e) => await RunAsync(() => { if (_model.SelectedDevice is not null) Clipboard.SetText(_model.SelectedDevice.Serial); return Task.CompletedTask; });
    private async void CopyLogsClick(object sender, RoutedEventArgs e) => await RunAsync(() => { Clipboard.SetText(_model.ExportLogs()); return Task.CompletedTask; });
    private void ClearLogsClick(object sender, RoutedEventArgs e) => _model.ClearLogView();
    private async void ExportLogsClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "PhonePilot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log", Filter = "Textprotokoll (*.log)|*.log|Textdatei (*.txt)|*.txt", DefaultExt = ".log" };
        if (dialog.ShowDialog(this) == true) await File.WriteAllTextAsync(dialog.FileName, _model.ExportLogs(), System.Text.Encoding.UTF8);
    });
    private async void DisconnectClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var selected = _model.SelectedDevice; if (selected is null) return;
        if ((selected.State.Session is SessionState.Running or SessionState.Starting || selected.State.Busy) && MessageBox.Show(this, "Die eigene Sitzung wird beendet und die ADB-Verbindung getrennt. Fortfahren?", "Verbindung trennen", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await _model.Manager.DisconnectAsync(selected.Serial, _model.LifetimeToken);
    });
    private async void ForgetClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var selected = _model.SelectedDevice; if (selected is null) return;
        if (MessageBox.Show(this, "„" + selected.Name + "“ und seine gespeicherten Einstellungen entfernen? Eine eigene aktive Sitzung wird beendet. Die ADB-Kopplung bleibt bestehen; das Gerät kann erneut gefunden werden.", "Gerät vergessen", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await _model.Manager.ForgetAsync(selected.Serial, _model.LifetimeToken);
    });
    private async void RestartAdbClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_model.Adb is null || !_model.ToolsAvailable) throw new IOException("ADB ist nicht verfügbar.");
        if (MessageBox.Show(this, "Der gemeinsam genutzte ADB-Server wird neu gestartet. Alle ADB-Verbindungen auf diesem PC können unterbrochen werden. Eigene scrcpy-Sitzungen werden beendet und nicht automatisch neu gestartet. Fortfahren?", "ADB-Server neu starten", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await _model.Manager.MaintenanceAsync(ct => _model.Adb.RestartServerAsync(ct), _model.LifetimeToken);
    });
    private async void OpenToolsClick(object sender, RoutedEventArgs e) => await RunAsync(() => { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { _model.Paths.Root } }); return Task.CompletedTask; });
    private async void RecoverClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (MessageBox.Show(this, "Die bestehende config.json wird zuerst unter einem eigenen Namen gesichert. Anschließend werden die aktuell angezeigten Einstellungen als neue Konfiguration gespeichert. Fortfahren?", "Konfiguration wiederherstellen", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK) await _model.RecoverConfigurationAsync();
    });
}
