using PhonePilot.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--render-preview" or "--check-layout") return PreviewRenderer.Render(args[1], args[0] == "--check-layout");
        using var instance = new Mutex(false, @"Local\PhonePilot.Launcher");
        bool acquired;
        try { acquired = instance.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\PhonePilot.Show"); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("PhonePilot wird bereits gestartet.", "PhonePilot"); }
            return 0;
        }
        try
        {
            using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PhonePilot.Show");
            using var exit = new ManualResetEvent(false);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Desktop/Themes.xaml", UriKind.Relative) });
            app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "PhonePilot – Fehler", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
            app.Startup += async (_, _) =>
            {
                try
                {
                    var root = AppContext.BaseDirectory;
                    var store = new ConfigurationStore(Path.Combine(root, "config.json"));
                    var loaded = await store.LoadAsync();
                    var paths = new ToolPaths(root);
                    var logs = new LogStore(Path.Combine(Path.GetTempPath(), "PhonePilot", "Logs"));
                    var runner = new ProcessRunner(paths, logs);
                    var adb = new AdbClient(paths, runner, logs);
                    var manager = new DeviceManager(loaded.Configuration, adb, new ScrcpySessionFactory(paths, runner, logs), logs);
                    var model = new MainViewModel(manager, adb, paths, logs, store, loaded);
                    var window = new MainWindow(model); app.MainWindow = window; window.Show();
                    _ = Task.Run(() =>
                    {
                        while (WaitHandle.WaitAny([showRequest, exit]) == 0)
                            app.Dispatcher.BeginInvoke(() => { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); });
                    });
                    await model.InitializeAsync();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "PhonePilot – Start fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error); app.Shutdown(1); }
            };
            app.Exit += (_, _) => exit.Set();
            return app.Run();
        }
        finally { instance.ReleaseMutex(); }
    }
}
