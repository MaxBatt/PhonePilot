using System.Text.Json;

namespace PhonePilot.Core;

public sealed record ConfigurationLoad(AppConfiguration Configuration, bool CanSave = true, string? Warning = null, bool IsLegacy = false);

public sealed class ConfigurationStore(string path)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private bool _canSave = true;
    private bool _legacy;
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static ConfigurationLoad Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ein JSON-Objekt wird erwartet.");
            var properties = document.RootElement.EnumerateObject().ToArray();
            bool hasVersion = properties.Any(p => p.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase));
            if (!hasVersion)
            {
                if (!properties.Any(p => p.Name.Equals("DeviceSerial", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Die Konfigurationsversion oder Gerätekennung fehlt.");
                var legacy = properties.First(p => p.Name.Equals("DeviceSerial", StringComparison.OrdinalIgnoreCase));
                if (legacy.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(legacy.Value.GetString()))
                    throw new InvalidDataException("Die Konfigurationsversion oder Gerätekennung fehlt.");
                string serial = legacy.Value.GetString()!.Trim();
                var configuration = new AppConfiguration { SelectedDeviceSerial = serial, Devices = [new DeviceConfiguration { Serial = serial,
                    Stream = new StreamSettings { Preset = "custom", MaxSize = null, MaxFps = null, VideoCodec = "h265", VideoBitRateMbps = 40 } }] };
                configuration.Validate();
                return new(configuration, IsLegacy: true);
            }
            var version = properties.First(p => p.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase));
            if (!version.Value.TryGetInt32(out int schema) || schema != 1) throw new InvalidDataException("Unbekannte Konfigurationsversion; die Originaldatei bleibt erhalten.");
            var parsed = JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? throw new InvalidDataException("Die Konfiguration ist leer.");
            parsed.Validate();
            return new(parsed);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            return new(new AppConfiguration(), false, "Die config.json konnte nicht geladen werden. " + ex.Message + " Sie wird nicht überschrieben.");
        }
    }

    public async Task<ConfigurationLoad> LoadAsync(CancellationToken token = default)
    {
        ConfigurationLoad loaded;
        try { loaded = File.Exists(Path) ? Parse(await File.ReadAllTextAsync(Path, token)) : new(new AppConfiguration()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { loaded = new(new AppConfiguration(), false, "Die config.json ist nicht lesbar: " + ex.Message); }
        _canSave = loaded.CanSave;
        _legacy = loaded.IsLegacy;
        return loaded;
    }

    public static string Serialize(AppConfiguration configuration) { configuration.Validate(); return JsonSerializer.Serialize(configuration, JsonOptions); }

    public async Task SaveAsync(AppConfiguration configuration, CancellationToken token = default)
    {
        string json = Serialize(configuration);
        await _writeGate.WaitAsync(token);
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!_canSave) throw new IOException("Die bestehende Konfiguration ist geschützt. Bitte zuerst eine Sicherung und Wiederherstellung bestätigen.");
            if (_legacy && File.Exists(Path)) { File.Copy(Path, Path + ".legacy-" + Guid.NewGuid().ToString("N") + ".bak", false); _legacy = false; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            await File.WriteAllTextAsync(temporary, json, token);
            File.Move(temporary, Path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } _writeGate.Release(); }
    }

    public async Task AllowRecoveryAsync(CancellationToken token = default)
    {
        await _writeGate.WaitAsync(token);
        try
        {
            if (File.Exists(Path)) File.Copy(Path, Path + ".recovery-" + Guid.NewGuid().ToString("N") + ".bak", false);
            _canSave = true;
        }
        finally { _writeGate.Release(); }
    }
}
