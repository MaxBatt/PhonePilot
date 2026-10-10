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
            var logs = new LogStore(); var manager = new DeviceManager(config, new PreviewAdb(), new PreviewSessions(), logs);
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
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken token) => Task.FromResult(new DiscoveryResult([new("DEMO-PHONE-01", "Pixel 10 Pro", "192.0.2.10", 35789)], [new("192.0.2.10:35789", "device", "DEMO-PHONE-01", "Pixel 10 Pro", "15"), new("192.0.2.11:35790", "device", "DEMO-PHONE-02", "Pixel 8", "15")]));
        public Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token) => Task.FromResult(new AdbTransport("192.0.2.10:35789", "device", serial, "Pixel 10 Pro", "15"));
        public Task DisconnectAsync(string transport, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class PreviewSessions : IScrcpySessionFactory
    {
        public IScrcpySession Start(DeviceConfiguration configuration, string transport) => new PreviewSession();
        private sealed class PreviewSession : IScrcpySession
        {
            private readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<int> Completion => _done.Task;
            public Task WaitUntilReadyAsync(CancellationToken token) => Task.CompletedTask;
            public Task StopAsync() { _done.TrySetResult(0); return Task.CompletedTask; }
            public bool ShowWindow() => true;
            public ValueTask DisposeAsync() { _done.TrySetResult(0); return ValueTask.CompletedTask; }
        }
    }
}
