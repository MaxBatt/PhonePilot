namespace PhonePilot.Desktop;

internal sealed class DeviceContextMenu : ContextMenu
{
    private readonly MainViewModel _model;
    private readonly DeviceViewModel _device;
    private CancellationTokenSource? _polling;
    private bool? _turnOn;
    internal MenuItem ShowItem { get; }
    internal MenuItem DisplayItem { get; }
    internal MenuItem StartItem { get; }
    internal MenuItem StopItem { get; }

    public DeviceContextMenu(MainViewModel model, DeviceViewModel device, Func<Task> forget)
    {
        _model = model; _device = device;
        SetResourceReference(StyleProperty, "DeviceContextMenu");
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        BorderThickness = new Thickness(1); Padding = new Thickness(4);
        Resources[typeof(MenuItem)] = FindResource("DeviceMenuItem");
        StartItem = Action("Starten", model.StartCommand);
        StopItem = Action("Beenden", model.StopCommand);
        ShowItem = Action("Fenster anzeigen", model.ShowCommand);
        DisplayItem = new MenuItem { Header = "Displaystatus wird geprüft …", IsEnabled = false };
        DisplayItem.Click += ChangeDisplay;
        Items.Add(DisplayItem);
        var separator = new Separator();
        separator.SetResourceReference(BackgroundProperty, "BorderBrush");
        Items.Add(separator);
        var forgetItem = new MenuItem { Header = "Gerät vergessen" };
        forgetItem.SetResourceReference(ForegroundProperty, "ErrorBrush");
        forgetItem.Click += async (_, _) => await forget();
        Items.Add(forgetItem);
        RefreshAvailability();
        Opened += async (_, _) => await PollAsync();
        Closed += (_, _) => _polling?.Cancel();
    }

    private MenuItem Action(string label, ICommand command)
    {
        var item = new MenuItem { Header = label, Command = command, CommandParameter = _device };
        Items.Add(item); return item;
    }

    private bool RefreshAvailability()
    {
        ShowItem.Visibility = _model.Manager.HasWindow(_device.Serial) ? Visibility.Visible : Visibility.Collapsed;
        var state = _model.Manager.Snapshot().FirstOrDefault(d => d.Configuration.Serial == _device.Serial);
        bool running = state?.Session == SessionState.Running;
        DisplayItem.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (!running || state!.Busy || state.Adb != AdbState.Connected)
        {
            _turnOn = null;
            DisplayItem.IsEnabled = false;
            DisplayItem.Header = "Displaystatus nicht verfügbar";
            return false;
        }
        return true;
    }

    private async Task PollAsync()
    {
        using var polling = CancellationTokenSource.CreateLinkedTokenSource(_model.LifetimeToken);
        _polling?.Cancel(); _polling = polling;
        try
        {
            do
            {
                await RefreshDisplayAsync(polling.Token);
                await Task.Delay(1000, polling.Token);
            } while (IsOpen);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_polling, polling)) _polling = null; }
    }

    internal async Task RefreshDisplayAsync(CancellationToken token)
    {
        if (!RefreshAvailability()) return;
        DisplayPower power;
        string? error = null;
        try { power = await _model.Manager.ReadDisplayPowerAsync(_device.Serial, token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { power = DisplayPower.Unknown; error = ex.Message; }
        token.ThrowIfCancellationRequested();
        if (!RefreshAvailability()) return;
        _turnOn = power switch { DisplayPower.Off => true, DisplayPower.On => false, _ => null };
        DisplayItem.Header = _turnOn switch
        {
            true => "Handy-Display einschalten",
            false => "Handy-Display ausschalten",
            _ => "Displaystatus nicht verfügbar"
        };
        DisplayItem.IsEnabled = _turnOn.HasValue;
        DisplayItem.ToolTip = error ?? (_turnOn.HasValue ? "Die Spiegelung am PC läuft weiter." :
            "Der physische Displayzustand wird von diesem Gerät nicht eindeutig gemeldet.");
    }

    private async void ChangeDisplay(object sender, RoutedEventArgs e)
    {
        if (_turnOn is not bool on) return;
        _turnOn = null; DisplayItem.IsEnabled = false;
        try { await _model.Manager.SetDisplayPowerAsync(_device.Serial, on, _model.LifetimeToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _model.Report(ex); }
    }
}
