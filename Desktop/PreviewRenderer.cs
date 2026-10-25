using System.Windows.Media.Imaging;

namespace PhonePilot.Desktop;

// Offline UI validation; no ADB, personal configuration or desktop capture.
internal static class PreviewRenderer
{
    public static int Render(string directory, bool checkLayout = false)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Desktop/Themes.xaml", UriKind.Relative) });
            var config = new AppConfiguration { Application = new() { Theme = "light" }, Devices = [new() { Serial = "DEMO-PHONE-01", DisplayName = "Pixel 10 Pro" }, new() { Serial = "DEMO-PHONE-02", DisplayName = "Pixel 8" }] };
            var previewAdb = new PreviewAdb(); var previewSessions = new PreviewSessions();
            var logs = new LogStore(); var manager = new DeviceManager(config, previewAdb, previewSessions, logs);
            manager.RefreshAsync().GetAwaiter().GetResult(); manager.StartAsync(config.Devices[0].Serial).GetAwaiter().GetResult();
            var model = new MainViewModel(manager, null, new ToolPaths(AppContext.BaseDirectory), logs, null, new(config));
            var window = new MainWindow(model, true) { Width = 1440, Height = 940 };
            if (window.Title != "PhonePilot" || window.Icon is not BitmapSource { PixelWidth: > 0 }) throw new InvalidOperationException("Anwendungsname oder eingebettetes Fenstersymbol fehlen.");
            using (var trayIcon = MainWindow.LoadTrayIcon())
                if (trayIcon.Width != 32 || trayIcon.Height != 32) throw new InvalidOperationException("Das eingebettete Infobereich-Symbol konnte nicht geladen werden.");
            if (checkLayout)
            {
                try { File.WriteAllText(Path.Combine(directory, "layout-check.txt"), FocusLayoutProbe.Check(window)); }
                finally { manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                return 0;
            }
            CheckContextMenu(model, previewAdb, previewSessions, directory);
            for (int i = 0; i < 4; i++)
            {
                window.SelectPreviewTab(i);
                for (int line = 0; line < (i == 3 ? 10 : 0); line++) logs.Write("ADB", "Verbindung bestätigt – Beispielmeldung " + (line + 1), config.Devices[0].Serial);
                Save(window, Path.Combine(directory, new[] { "01-steuerung.png", "03-einstellungen.png", "04-erweitert.png", "05-logs.png" }[i]));
            }
            var general = Dialogs.GeneralSettings(window, model); general.SizeToContent = SizeToContent.Manual; general.Height = 700;
            if (general.Icon is null) throw new InvalidOperationException("Das Dialogsymbol fehlt.");
            Save(general, Path.Combine(directory, "02-allgemein.png"));
            // A fresh offline-only session verifies QR generation without polling ADB.
            var qrPanel = new QrPairingPanel((_, _) => Task.CompletedTask, _ => Task.CompletedTask, CancellationToken.None, startAutomatically: false);
            qrPanel.Prepare();
            var qrBorder = new Border { Padding = new Thickness(22), Child = qrPanel };
            qrBorder.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
            var qrWindow = new Window { Title = "Gerät hinzufügen – QR-Code", Icon = window.Icon, Width = 620, Height = 640, Content = qrBorder };
            Save(qrWindow, Path.Combine(directory, "08-qr-kopplung.png"));
            ThemeManager.Apply("dark"); window.SelectPreviewTab(0); Save(window, Path.Combine(directory, "06-dunkel.png"));
            ThemeManager.Apply("light"); window.Width = 960; window.Height = 680; Save(window, Path.Combine(directory, "07-kompakt.png"));
            if (model.StartCommand.CanExecute(null) || !model.StopCommand.CanExecute(null)) throw new InvalidOperationException("Sitzungsaktionen entsprechen nicht dem aktiven Zustand.");
            var selected = model.SelectedDevice!;
            selected.MaxSize = "1280";
            try { selected.MaxSize = "0"; throw new InvalidOperationException("Ungültige Bildkante wurde akzeptiert."); } catch (ArgumentException) { }
            if (selected.State.Configuration.Stream.MaxSize != 1280) throw new InvalidOperationException("Ungültige Eingabe hat den gültigen Wert überschrieben.");
            File.WriteAllText(Path.Combine(directory, "checks.txt"), "PASS: PhonePilot-Identität, eingebettete Fenster-/Dialog-/Infobereich-Symbole, vier Tabs, globale Einstellungen, Hell/Dunkel, 960×680, Zustandsaktionen und Eingabevalidierung.\nOffline-Rendering mit Beispieldaten; kein Hardwaretest.");
            manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "preview-error.txt"), ex.ToString()); return 1; }
    }
    private static void CheckContextMenu(MainViewModel model, PreviewAdb adb, PreviewSessions sessions, string directory)
    {
        var running = model.Devices[0]; var stopped = model.Devices[1];
        model.SelectedDevice = stopped;
        var menu = new DeviceContextMenu(model, running, () => Task.CompletedTask);
        if (menu.StartItem.Command!.CanExecute(running) || !menu.StopItem.Command!.CanExecute(running) ||
            !model.StartCommand.CanExecute(stopped) || model.StopCommand.CanExecute(stopped))
            throw new InvalidOperationException("Kontextaktionen verwenden nicht das angeklickte Gerät.");
        if (menu.ShowItem.Visibility != Visibility.Visible) throw new InvalidOperationException("Laufendes Fenster fehlt im Menü.");
        menu.RefreshDisplayAsync(default).GetAwaiter().GetResult();
        if (!menu.DisplayItem.IsEnabled || !Equals(menu.DisplayItem.Header, "Handy-Display einschalten"))
            throw new InvalidOperationException("Ausgeschaltetes Display wurde nicht erkannt.");
        SaveMenu(menu, Path.Combine(directory, "09-kontextmenue.png"));
        ThemeManager.Apply("dark");
        var darkMenu = new DeviceContextMenu(model, running, () => Task.CompletedTask);
        darkMenu.RefreshDisplayAsync(default).GetAwaiter().GetResult();
        SaveMenu(darkMenu, Path.Combine(directory, "10-kontextmenue-dunkel.png"));
        if (darkMenu.Background is not SolidColorBrush { Color: var color } || color != Color.FromRgb(43, 48, 59))
            throw new InvalidOperationException("Das Kontextmenü übernimmt das dunkle Farbschema nicht.");
        ThemeManager.Apply("light");
        adb.Power = DisplayPower.On;
        menu.RefreshDisplayAsync(default).GetAwaiter().GetResult();
        if (!Equals(menu.DisplayItem.Header, "Handy-Display ausschalten")) throw new InvalidOperationException("Geänderter Displaystatus fehlt im Menü.");
        adb.Power = DisplayPower.Unknown;
        menu.RefreshDisplayAsync(default).GetAwaiter().GetResult();
        if (menu.DisplayItem.IsEnabled) throw new InvalidOperationException("Unbekannter Displaystatus ist bedienbar.");
        sessions.WindowAvailable = false;
        menu.RefreshDisplayAsync(default).GetAwaiter().GetResult();
        if (menu.ShowItem.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Fensteraktion ist ohne Fenster sichtbar.");
        var inactive = new DeviceContextMenu(model, stopped, () => Task.CompletedTask);
        if (inactive.ShowItem.Visibility != Visibility.Collapsed || inactive.DisplayItem.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Sitzungsaktionen sind bei einem gestoppten Gerät sichtbar.");
        sessions.WindowAvailable = true; adb.Power = DisplayPower.Off; model.SelectedDevice = running;
        File.WriteAllText(Path.Combine(directory, "context-menu-checks.txt"),
            "PASS: Gerätezuordnung trotz anderer Auswahl, Start/Beenden, tatsächliche Fensterverfügbarkeit, Display an/aus/unbekannt, inaktive Sitzung, Hell/Dunkel.");
    }
    private static void SaveMenu(ContextMenu menu, string path)
    {
        menu.Visibility = Visibility.Visible;
        menu.Measure(new Size(400, 400));
        menu.Arrange(new Rect(new Point(), menu.DesiredSize)); menu.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(menu);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); png.Save(output);
    }
    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height)); content.Arrange(new Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
    }
    private sealed class PreviewAdb : IAdbClient
    {
        public DisplayPower Power = DisplayPower.Off;
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new DiscoveryResult([new("DEMO-PHONE-01", "Pixel 10 Pro", "192.0.2.10", 35789)], [new("192.0.2.10:35789", "device", "DEMO-PHONE-01", "Pixel 10 Pro", "15"), new("192.0.2.11:35790", "device", "DEMO-PHONE-02", "Pixel 8", "15")]));
        public Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token) => Task.FromResult(new AdbTransport("192.0.2.10:35789", "device", serial, "Pixel 10 Pro", "15"));
        public Task DisconnectAsync(string transport, CancellationToken token) => Task.CompletedTask;
        public Task<DisplayPower> ReadDisplayPowerAsync(string serial, string transport, CancellationToken token) => Task.FromResult(Power);
    }
    private sealed class PreviewSessions : IScrcpySessionFactory
    {
        public bool WindowAvailable = true;
        public IScrcpySession Start(DeviceConfiguration configuration, string transport) => new PreviewSession(this);
        private sealed class PreviewSession(PreviewSessions owner) : IScrcpySession
        {
            private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<int> Completion => _done.Task;
            public Task WaitUntilReadyAsync(CancellationToken token) => Task.CompletedTask;
            public Task StopAsync() { _done.TrySetResult(0); return Task.CompletedTask; }
            public bool ShowWindow() => true;
            public bool HasWindow => owner.WindowAvailable && !Completion.IsCompleted;
            public Task SetDisplayPowerAsync(bool on, CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() { _done.TrySetResult(0); return ValueTask.CompletedTask; }
        }
    }
}
