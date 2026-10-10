namespace PhonePilot.Desktop;

public static class Dialogs
{
    private static TextBlock Text(string text, bool muted = false) => new() { Text = text, Style = (Style)Application.Current.FindResource(muted ? "Muted" : typeof(TextBlock)), Margin = new Thickness(0, 0, 0, 10) };
    private static Border Card(string title, params UIElement[] elements)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.FindResource("Heading") });
        foreach (var element in elements) panel.Children.Add(element);
        return new Border { Style = (Style)Application.Current.FindResource("Card"), Child = panel };
    }
    private static Window WindowFor(Window owner, string title, double width = 620) => new()
    {
        Owner = owner.IsLoaded ? owner : null, Icon = owner.Icon, Title = title, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(450, SystemParameters.WorkArea.Height - 70),
        ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
    };
    private static ComboBox Combo(IEnumerable<Choice> choices, string selected) => new() { ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Key", SelectedValue = selected, Margin = new Thickness(0, 0, 0, 10) };
    private static ScrollViewer Scrollable(UIElement content)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        scroll.SetResourceReference(Control.BackgroundProperty, "BackgroundBrush");
        return scroll;
    }

    public static Window GeneralSettings(Window owner, MainViewModel model)
    {
        var window = WindowFor(owner, "Allgemeine Einstellungen");
        var settings = model.Manager.Configuration.Application;
        var enabled = new CheckBox { Content = "Automatisch aktualisieren", IsChecked = settings.AutoRefreshEnabled };
        var interval = new ComboBox { ItemsSource = new[] { 2, 5, 10, 15, 30, 60 }, SelectedItem = settings.RefreshIntervalSeconds, IsEnabled = settings.AutoRefreshEnabled, Margin = new Thickness(0, 0, 0, 12) };
        var theme = Combo([new("system", "Windows-Einstellung übernehmen"), new("light", "Hell"), new("dark", "Dunkel")], settings.Theme);
        var close = Combo([new("ask", "Bei aktiven Sitzungen nachfragen"), new("tray", "Im Infobereich weiterlaufen"), new("exit", "Sitzungen beenden und schließen")], settings.CloseBehavior);
        void Change()
        {
            try
            {
                model.SettingsChanged(model.Manager.Configuration.Application with { AutoRefreshEnabled = enabled.IsChecked == true,
                    RefreshIntervalSeconds = interval.SelectedItem is int seconds ? seconds : 5, Theme = theme.SelectedValue as string ?? "system", CloseBehavior = close.SelectedValue as string ?? "ask" });
                interval.IsEnabled = enabled.IsChecked == true;
            }
            catch (Exception ex) { model.Report(ex); }
        }
        enabled.Checked += (_, _) => Change(); enabled.Unchecked += (_, _) => Change(); interval.SelectionChanged += (_, _) => Change(); theme.SelectionChanged += (_, _) => Change(); close.SelectionChanged += (_, _) => Change();
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Card("Geräteerkennung", enabled, Text("Aktualisierungsintervall (Sekunden)"), interval, Text("Standard: alle 5 Sekunden. Manuelles Aktualisieren ist jederzeit möglich.", true)));
        panel.Children.Add(Card("Darstellung", Text("Farbschema"), theme));
        panel.Children.Add(Card("Fenster schließen", Text("Bei aktiven Sitzungen"), close, Text("Im Infobereich weiterlaufen oder Sitzungen beenden. Eine gemerkte Entscheidung lässt sich hier ändern.", true)));
        panel.Children.Add(Text("Änderungen werden automatisch gespeichert.", true));
        var done = new Button { Content = "Schließen", Style = (Style)Application.Current.FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 120, IsCancel = true };
        done.Click += (_, _) => window.Close(); panel.Children.Add(done);
        window.Content = Scrollable(panel);
        return window;
    }

    public static Task AddDeviceAsync(Window owner, MainViewModel model)
    {
        if (model.Adb is null || !model.ToolsAvailable) { model.Notice = "Bitte zuerst vollständige ADB- und scrcpy-Pakete neben PhonePilot.exe ablegen."; return Task.CompletedTask; }
        var window = WindowFor(owner, "Gerät hinzufügen");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(model.LifetimeToken);
        bool closed = false;
        window.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
        var tabs = new TabControl(); var panel = new StackPanel { Margin = new Thickness(22) };
        var qr = new QrPairingPanel(model.Adb.PairQrAsync, ct => model.Manager.RefreshAsync(ct), cancellation.Token);
        tabs.Items.Add(new TabItem { Header = "Mit QR-Code koppeln", Content = qr });
        var status = Text("", true);
        var ip = new TextBox { Text = "192.168.", Margin = new Thickness(0, 0, 0, 12) };
        var port = new TextBox { Margin = new Thickness(0, 0, 0, 12) };
        var code = new PasswordBox { Padding = new Thickness(9), MaxLength = 6, Margin = new Thickness(0, 0, 0, 14) };
        var pair = new Button { Content = "Koppeln", Style = (Style)Application.Current.FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right };
        var pairing = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        pairing.Children.Add(Text("Am Smartphone: Entwickleroptionen → WLAN-Debugging → Gerät mit Kopplungscode koppeln. Beide Geräte müssen im selben erreichbaren Netzwerk sein."));
        pairing.Children.Add(Text("IPv4-Adresse")); pairing.Children.Add(ip); pairing.Children.Add(Text("Kopplungsport")); pairing.Children.Add(port); pairing.Children.Add(Text("Sechsstelliger Kopplungscode")); pairing.Children.Add(code); pairing.Children.Add(pair);
        pair.Click += async (_, _) =>
        {
            pair.IsEnabled = false; string secret = code.Password; code.Clear();
            try
            {
                status.Text = "Kopplung läuft…";
                await model.Adb.PairAsync(ip.Text.Trim() + ":" + port.Text.Trim(), secret, cancellation.Token);
                secret = "";
                status.Text = "Kopplung erfolgreich. Der Verbindungsport wird gesucht. Falls nötig, unter Manuell verbinden die Adresse der WLAN-Debugging-Hauptseite eingeben.";
                await model.Manager.RefreshAsync(cancellation.Token);
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Kopplung abgebrochen."; }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { secret = ""; if (!closed) pair.IsEnabled = true; }
        };
        tabs.Items.Add(new TabItem { Header = "Mit Code koppeln", Content = pairing });
        var endpoint = new TextBox { Text = "192.168.", Margin = new Thickness(0, 0, 0, 14) };
        var connect = new Button { Content = "Verbinden", Style = (Style)Application.Current.FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right };
        var manual = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        manual.Children.Add(Text("Für bereits gekoppelte Geräte ohne mDNS-Fund. Verwende IP-Adresse und Verbindungsport von der WLAN-Debugging-Hauptseite – nicht den Kopplungsport."));
        manual.Children.Add(Text("Aktuelle Verbindungsadresse (IPv4:Port)")); manual.Children.Add(endpoint); manual.Children.Add(connect);
        connect.Click += async (_, _) =>
        {
            connect.IsEnabled = false;
            try
            {
                status.Text = "Verbindung wird geprüft…";
                var result = await model.Adb.ConnectManualAsync(endpoint.Text.Trim(), cancellation.Token);
                model.Manager.AcceptManualConnection(result); model.SelectSerial(result.Serial!);
                status.Text = "Verbunden; Seriennummer bestätigt. scrcpy kannst du über Starten öffnen.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { if (!closed) connect.IsEnabled = true; }
        };
        tabs.Items.Add(new TabItem { Header = "Manuell verbinden", Content = manual });
        panel.Children.Add(tabs); status.Margin = new Thickness(0, 18, 0, 14); panel.Children.Add(status);
        var done = new Button { Content = "Schließen / Abbrechen", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        done.Click += (_, _) => window.Close(); panel.Children.Add(done);
        window.Content = Scrollable(panel);
        window.ShowDialog(); return Task.CompletedTask;
    }

    public static (string? Decision, bool Remember) Closing(Window owner, int count)
    {
        var window = WindowFor(owner, "PhonePilot schließen", 550); string? decision = null;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(Text("Es sind noch " + count + " eigene Sitzungen aktiv oder im Start. Wie möchtest du fortfahren?"));
        var remember = new CheckBox { Content = "Entscheidung merken", Margin = new Thickness(0, 12, 0, 16) };
        foreach (var choice in new[] { new Choice("tray", "Im Infobereich weiterlaufen"), new Choice("exit", "Sitzungen beenden und schließen") })
        {
            var button = new Button { Content = choice.Label, Margin = new Thickness(0, 0, 0, 10) };
            button.Click += (_, _) => { decision = choice.Key; window.DialogResult = true; }; panel.Children.Add(button);
        }
        panel.Children.Add(remember);
        var cancel = new Button { Content = "Abbrechen", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => { decision = null; window.DialogResult = false; }; panel.Children.Add(cancel);
        window.Content = panel; window.ShowDialog(); return (decision, decision is not null && remember.IsChecked == true);
    }
}
