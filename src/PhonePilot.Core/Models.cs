using System.Globalization;

namespace PhonePilot.Core;

public sealed record ApplicationSettings
{
    public bool AutoRefreshEnabled { get; init; } = true;
    public int RefreshIntervalSeconds { get; init; } = 5;
    public string Theme { get; init; } = "system";
    public string CloseBehavior { get; init; } = "ask";
    public void Validate()
    {
        if (RefreshIntervalSeconds is < 2 or > 60) throw new InvalidDataException("Das Aktualisierungsintervall muss zwischen 2 und 60 Sekunden liegen.");
        if (Theme is not ("system" or "light" or "dark")) throw new InvalidDataException("Unbekanntes Farbschema.");
        if (CloseBehavior is not ("ask" or "tray" or "exit")) throw new InvalidDataException("Unbekanntes Schließverhalten.");
    }
}

public sealed record StreamSettings
{
    public string Preset { get; init; } = "balanced";
    public int? MaxSize { get; init; } = 1920;
    public decimal VideoBitRateMbps { get; init; } = 8;
    public int? MaxFps { get; init; } = 60;
    public string VideoCodec { get; init; } = "h264";
    public bool TurnScreenOff { get; init; } = true;
    public bool AudioEnabled { get; init; } = true;
    public bool AlwaysOnTop { get; init; }
    public bool Fullscreen { get; init; }
    public bool Borderless { get; init; }
    public string? WindowTitleOverride { get; init; }
    public string? VideoEncoder { get; init; }
    public string DisplayOrientation { get; init; } = "auto";

    public StreamSettings WithPreset(string preset) => preset switch
    {
        "quality" => this with { Preset = preset, MaxSize = 2560, VideoBitRateMbps = 20, MaxFps = 60, VideoCodec = "h265" },
        "balanced" => this with { Preset = preset, MaxSize = 1920, VideoBitRateMbps = 8, MaxFps = 60, VideoCodec = "h264" },
        "fast" => this with { Preset = preset, MaxSize = 1280, VideoBitRateMbps = 4, MaxFps = 30, VideoCodec = "h264" },
        "custom" => this with { Preset = preset },
        _ => throw new InvalidDataException("Unbekanntes Leistungsprofil.")
    };

    public void Validate()
    {
        if (Preset is not ("quality" or "balanced" or "fast" or "custom")) throw new InvalidDataException("Unbekanntes Leistungsprofil.");
        if (MaxSize is <= 0 or > 16384) throw new InvalidDataException("Die Bildkante muss zwischen 1 und 16384 px liegen, oder Original sein.");
        if (MaxFps is <= 0 or > 1000) throw new InvalidDataException("Das FPS-Limit muss zwischen 1 und 1000 liegen, oder unbegrenzt sein.");
        if (VideoBitRateMbps is <= 0 or > 1000) throw new InvalidDataException("Die Video-Bitrate muss größer als 0 und höchstens 1000 Mbit/s sein.");
        if (VideoCodec is not ("h264" or "h265")) throw new InvalidDataException("Unterstützte Video-Codecs sind H.264 und H.265.");
        if (DisplayOrientation is not ("auto" or "0" or "90" or "180" or "270")) throw new InvalidDataException("Ungültige Bildausrichtung.");
        if ((WindowTitleOverride?.Length ?? 0) > 200 || WindowTitleOverride?.Any(char.IsControl) == true) throw new InvalidDataException("Der Fenstertitel darf höchstens 200 Zeichen und keine Steuerzeichen enthalten.");
        if ((VideoEncoder?.Length ?? 0) > 200 || VideoEncoder?.Any(char.IsControl) == true) throw new InvalidDataException("Ungültiger Video-Encoder.");
    }

    public IReadOnlyList<string> BuildArguments(string transport, string displayName)
    {
        Validate();
        if (!AdbParser.IsWirelessTransport(transport)) throw new InvalidDataException("Kein eindeutiger WLAN-Transport für dieses Gerät.");
        var result = new List<string> { "--serial", transport, "--window-title=" + (string.IsNullOrWhiteSpace(WindowTitleOverride) ? displayName : WindowTitleOverride), "--video-codec=" + VideoCodec,
            "--video-bit-rate=" + decimal.Round(VideoBitRateMbps * 1_000_000).ToString("0", CultureInfo.InvariantCulture) };
        if (MaxSize.HasValue) result.Add("--max-size=" + MaxSize.Value);
        if (MaxFps.HasValue) result.Add("--max-fps=" + MaxFps.Value);
        if (TurnScreenOff) result.Add("-S");
        if (!AudioEnabled) result.Add("--no-audio");
        if (AlwaysOnTop) result.Add("--always-on-top");
        if (Fullscreen) result.Add("--fullscreen");
        if (Borderless) result.Add("--window-borderless");
        if (!string.IsNullOrWhiteSpace(VideoEncoder)) result.Add("--video-encoder=" + VideoEncoder);
        if (DisplayOrientation != "auto") result.Add("--display-orientation=" + DisplayOrientation);
        return result;
    }
}

public sealed record DeviceConfiguration
{
    public string Serial { get; init; } = "";
    public string DisplayName { get; init; } = "Android-Gerät";
    public bool ReconnectAdb { get; init; } = true;
    public StreamSettings Stream { get; init; } = new();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Serial) || Serial.Length > 200 || Serial.Any(char.IsControl)) throw new InvalidDataException("Ungültige Gerätekennung.");
        if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 100 || DisplayName.Any(char.IsControl)) throw new InvalidDataException("Der Anzeigename muss 1–100 Zeichen ohne Steuerzeichen enthalten.");
        if (Stream is null) throw new InvalidDataException("Stream-Einstellungen fehlen.");
        Stream.Validate();
    }
}

public sealed record AppConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public ApplicationSettings Application { get; init; } = new();
    public string? SelectedDeviceSerial { get; init; }
    public IReadOnlyList<DeviceConfiguration> Devices { get; init; } = [];
    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Diese Konfigurationsversion wird nicht unterstützt.");
        if (Application is null || Devices is null || Devices.Any(d => d is null)) throw new InvalidDataException("Die Konfiguration ist unvollständig.");
        Application.Validate();
        foreach (var device in Devices) device.Validate();
        if (Devices.Select(d => d.Serial).Distinct(StringComparer.Ordinal).Count() != Devices.Count) throw new InvalidDataException("Die Konfiguration enthält doppelte Gerätekennungen.");
    }
}

public sealed record MdnsService(string Serial, string Name, string Ipv4, int Port)
{
    public string Endpoint => Ipv4 + ":" + Port;
}
public sealed record AdbTransport(string Id, string State, string? Serial = null, string? Model = null, string? AndroidVersion = null, string? Build = null);
public sealed record DiscoveryResult(IReadOnlyList<MdnsService> Services, IReadOnlyList<AdbTransport> Transports, bool MdnsSucceeded = true, bool AdbSucceeded = true, string? Warning = null);
public enum AdbState { Unknown, Disconnected, Connecting, Connected, Disconnecting, Offline, Unauthorized, Error }
public enum SessionState { Stopped, Starting, Running, Stopping, Failed }
public sealed record DeviceSnapshot(DeviceConfiguration Configuration, bool IsKnown, bool? MdnsFound, AdbState Adb, SessionState Session, string? Endpoint, string? Transport,
    string? Model, string? AndroidVersion, string? Build, string? Error, bool Busy, StreamSettings? ActiveStream);

public sealed class SessionSlots(int capacity)
{
    private readonly HashSet<string> _reserved = new(StringComparer.Ordinal);
    public int Count { get { lock (_reserved) return _reserved.Count; } }
    public bool TryReserve(string serial) { lock (_reserved) return _reserved.Count < capacity && _reserved.Add(serial); }
    public void Release(string serial) { lock (_reserved) _reserved.Remove(serial); }
}
