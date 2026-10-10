using System.Globalization;

namespace PhonePilot.Desktop;

public sealed class DeviceViewModel(DeviceManager manager, DeviceSnapshot state) : Observable
{
    public DeviceSnapshot State { get; private set; } = state;
    public string Serial => State.Configuration.Serial;
    public string Name { get => State.Configuration.DisplayName; set => Edit(c => c with { DisplayName = value.Trim() }); }
    public string Subtitle => State.AndroidVersion is null ? "Android · WLAN-Debugging" : "Android " + State.AndroidVersion + (State.Build is null ? "" : " · " + State.Build);
    public string AdbText => State.Adb switch { AdbState.Connected => "Verbunden", AdbState.Connecting => "Wird verbunden…", AdbState.Disconnecting => "Wird getrennt…", AdbState.Offline => "Offline", AdbState.Unauthorized => "Nicht autorisiert", AdbState.Error => "Fehler", AdbState.Disconnected => "Getrennt", _ => "Noch nicht geprüft" };
    public string SessionText => State.Session switch { SessionState.Running => "Läuft · eigenes Fenster", SessionState.Starting => "Startet…", SessionState.Stopping => "Wird beendet…", SessionState.Failed => "Fehlgeschlagen", _ => "Gestoppt" };
    public string Badge => State.Session == SessionState.Running ? "scrcpy läuft" : State.Busy ? "Aktion läuft…" : State.Adb == AdbState.Connected ? "ADB verbunden" : AdbText;
    public string MdnsText => State.MdnsFound switch { true => "Gefunden" + (State.Endpoint is null ? "" : " · " + State.Endpoint), false => "Nicht gefunden", _ => "Noch nicht geprüft" };
    public Brush AdbBrush => State.Adb == AdbState.Connected ? Brushes.ForestGreen : State.Adb is AdbState.Error ? Brushes.IndianRed : State.Adb is AdbState.Connecting or AdbState.Unauthorized or AdbState.Offline ? Brushes.DarkOrange : Brushes.Gray;
    public Brush SessionBrush => State.Session == SessionState.Running ? Brushes.ForestGreen : State.Session == SessionState.Failed ? Brushes.IndianRed : State.Session is SessionState.Starting or SessionState.Stopping ? Brushes.DarkOrange : Brushes.Gray;
    public Brush MdnsBrush => State.MdnsFound == true ? Brushes.ForestGreen : Brushes.Gray;
    public string? EditError { get; private set; }
    public string? Error => EditError ?? State.Error;
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsRunning => State.Session == SessionState.Running;
    public bool ShowConnect => State.Adb != AdbState.Connected && !State.Busy;
    public bool ShowDisconnect => State.Adb == AdbState.Connected || State.Busy;
    public bool CanEdit => true;
    public string ChangesHint => State.ActiveStream is { } active && active != State.Configuration.Stream ? "Gespeichert – gilt beim nächsten Start. Die aktive Sitzung bleibt unverändert." : "Stream- und Fensteränderungen gelten beim nächsten Start.";
    public void Update(DeviceSnapshot state)
    {
        bool settingsChanged = State.Configuration != state.Configuration;
        State = state;
        if (state.Configuration.Stream.VideoEncoder is { } encoder && !Encoders.Contains(encoder)) Encoders.Add(encoder);
        if (settingsChanged) { Raise(); return; }
        // Periodic status updates must preserve text that is still being edited.
        foreach (string property in new[] { nameof(State), nameof(Subtitle), nameof(AdbText), nameof(SessionText), nameof(Badge), nameof(MdnsText),
            nameof(AdbBrush), nameof(SessionBrush), nameof(MdnsBrush), nameof(Error), nameof(HasError), nameof(IsRunning), nameof(ShowConnect), nameof(ShowDisconnect), nameof(ChangesHint) }) Raise(property);
    }

    private void Edit(Func<DeviceConfiguration, DeviceConfiguration> change)
    {
        try
        {
            var configuration = change(State.Configuration); configuration.Validate();
            manager.UpdateDevice(configuration); State = State with { Configuration = configuration }; EditError = null; Raise();
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
        {
            EditError = ex.Message; Raise(nameof(Error)); Raise(nameof(HasError)); throw new ArgumentException(ex.Message);
        }
    }
    private void Stream(Func<StreamSettings, StreamSettings> change, bool core = false) => Edit(c => c with { Stream = core ? change(c.Stream) with { Preset = "custom" } : change(c.Stream) });
    public string Preset { get => State.Configuration.Stream.Preset; set { if (value is not null && value != Preset) Stream(s => s.WithPreset(value)); } }
    public string MaxSize { get => State.Configuration.Stream.MaxSize?.ToString() ?? "Original"; set => Stream(s => s with { MaxSize = OptionalNumber(value, "Original") }, true); }
    public string BitRate { get => State.Configuration.Stream.VideoBitRateMbps.ToString(CultureInfo.CurrentCulture); set => Stream(s => s with { VideoBitRateMbps = decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) }, true); }
    public string MaxFps { get => State.Configuration.Stream.MaxFps?.ToString() ?? "Unbegrenzt"; set => Stream(s => s with { MaxFps = OptionalNumber(value, "Unbegrenzt") }, true); }
    public string Codec { get => State.Configuration.Stream.VideoCodec; set { if (value is not null && value != Codec) Stream(s => s with { VideoCodec = value }, true); } }
    public bool ScreenOff { get => State.Configuration.Stream.TurnScreenOff; set => Stream(s => s with { TurnScreenOff = value }); }
    public bool Audio { get => State.Configuration.Stream.AudioEnabled; set => Stream(s => s with { AudioEnabled = value }); }
    public bool OnTop { get => State.Configuration.Stream.AlwaysOnTop; set => Stream(s => s with { AlwaysOnTop = value }); }
    public bool Fullscreen { get => State.Configuration.Stream.Fullscreen; set => Stream(s => s with { Fullscreen = value }); }
    public bool Borderless { get => State.Configuration.Stream.Borderless; set => Stream(s => s with { Borderless = value }); }
    public bool Reconnect { get => State.Configuration.ReconnectAdb; set => Edit(c => c with { ReconnectAdb = value }); }
    public string WindowTitle { get => State.Configuration.Stream.WindowTitleOverride ?? ""; set => Stream(s => s with { WindowTitleOverride = string.IsNullOrWhiteSpace(value) ? null : value.Trim() }); }
    public string Orientation { get => State.Configuration.Stream.DisplayOrientation; set { if (value is not null) Stream(s => s with { DisplayOrientation = value }); } }
    public string Encoder { get => State.Configuration.Stream.VideoEncoder ?? "Automatisch"; set { if (value is not null) Stream(s => s with { VideoEncoder = value == "Automatisch" ? null : value }); } }
    public ObservableCollection<string> Encoders { get; } = state.Configuration.Stream.VideoEncoder is { } encoder ? ["Automatisch", encoder] : ["Automatisch"];
    public void RefreshEncoderSelection() => Raise(nameof(Encoder));
    private static int? OptionalNumber(string value, string unset) => value.Trim().Equals(unset, StringComparison.OrdinalIgnoreCase) ? null : int.Parse(value.Trim(), CultureInfo.InvariantCulture);
}
