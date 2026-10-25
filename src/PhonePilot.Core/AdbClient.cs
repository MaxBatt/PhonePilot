namespace PhonePilot.Core;

public interface IAdbClient
{
    Task<DiscoveryResult> DiscoverAsync(CancellationToken token);
    Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token);
    Task DisconnectAsync(string transport, CancellationToken token);
    Task<DisplayPower> ReadDisplayPowerAsync(string serial, string transport, CancellationToken token);
}

public sealed class AdbClient(ToolPaths paths, ProcessRunner runner, LogStore logs) : IAdbClient
{
    private readonly SemaphoreSlim _connections = new(2, 2);
    private readonly Dictionary<string, MdnsService> _richNames = new(StringComparer.Ordinal);
    private int _protoRead;
    private string? _discoverySignature;
    private Task<ProcessResult> Adb(CancellationToken token, params string[] args) => runner.RunAsync(paths.Adb, args, TimeSpan.FromSeconds(4), token);

    public async Task<DisplayPower> ReadDisplayPowerAsync(string serial, string transport, CancellationToken token)
    {
        var identity = await ReadInformationAsync(transport, token);
        if (identity.Serial != serial) throw new IOException("Die Gerätekennung stimmt nicht mehr überein.");
        var result = await Adb(token, "-s", transport, "shell", "dumpsys", "SurfaceFlinger", "--displays");
        if (!result.Success) throw new IOException("Der Displaystatus konnte nicht abgefragt werden. " + result.Text);
        return DisplayPowerParser.Parse(result.Output);
    }

    public async Task StartServerAsync(CancellationToken token)
    {
        var start = await runner.RunAsync(paths.Adb, ["start-server"], TimeSpan.FromSeconds(15), token);
        if (!start.Success) throw new IOException("ADB konnte nicht gestartet werden. " + start.Text);
        logs.Write("ADB", "Lokaler ADB-Server verfügbar.");
    }

    public async Task<DiscoveryResult> DiscoverAsync(CancellationToken token)
    {
        var mdnsTask = Adb(token, "mdns", "services");
        var devicesTask = Adb(token, "devices", "-l");
        await Task.WhenAll(mdnsTask, devicesTask);
        var mdns = await mdnsTask; var devices = await devicesTask;
        // The old richer proto parser remains supported, but is not a six-second poll loop.
        if (Interlocked.Exchange(ref _protoRead, 1) == 0)
        {
            var proto = await runner.RunAsync(paths.Adb, ["mdns", "track-services", "--proto-text"], TimeSpan.FromSeconds(1), token);
            lock (_richNames) foreach (var service in AdbParser.ParseProto(proto.Output)) _richNames[service.Serial] = service;
        }
        var services = AdbParser.ParsePlain(mdns.Output).DistinctBy(s => (s.Serial, s.Endpoint)).ToArray();
        lock (_richNames) services = services.Select(s => _richNames.TryGetValue(s.Serial, out var rich) ? s with { Name = rich.Name } : s).ToArray();
        string signature = mdns.Success + ":" + string.Join(";", services.OrderBy(s => s.Serial).ThenBy(s => s.Endpoint).Select(s => s.Serial + "@" + s.Endpoint));
        bool changed;
        lock (_richNames) { changed = signature != _discoverySignature; _discoverySignature = signature; }
        if (changed) logs.Write("mDNS", mdns.Success ? "WLAN-Verbindungsdienste: " + services.Length + (services.Length == 0 ? "." : ". " + string.Join(", ", services.Select(s => s.Serial + " · " + s.Endpoint))) : "mDNS-Abfrage fehlgeschlagen. " + mdns.Text, level: mdns.Success ? "INFO" : "WARN");
        var transports = new List<AdbTransport>();
        foreach (var transport in AdbParser.ParseDevices(devices.Output))
        {
            if (transport.State == "device")
            {
                try { transports.Add(await ReadInformationAsync(transport.Id, token)); }
                catch (IOException) { transports.Add(transport with { State = "offline" }); }
            }
            else transports.Add(transport);
        }
        string? warning = !devices.Success ? "ADB-Geräteliste konnte nicht gelesen werden. " + devices.Text : !mdns.Success ? "mDNS-Abfrage fehlgeschlagen. " + mdns.Text : null;
        return new(services, transports, mdns.Success, devices.Success, warning);
    }

    public async Task<AdbTransport> ReadInformationAsync(string transport, CancellationToken token)
    {
        if (!AdbParser.IsWirelessTransport(transport)) throw new IOException("Ungültiger WLAN-Transport.");
        var properties = await Adb(token, "-s", transport, "shell", "getprop");
        if (!properties.Success) throw new IOException("Geräteinformationen nicht erreichbar. " + properties.Text);
        var values = AdbParser.ParseProperties(properties.Output);
        if (!values.TryGetValue("ro.serialno", out string? serial) || string.IsNullOrWhiteSpace(serial)) throw new IOException("Die Hardware-Seriennummer konnte nicht bestätigt werden.");
        return new(transport, "device", serial.Trim(), values.GetValueOrDefault("ro.product.model"), values.GetValueOrDefault("ro.build.version.release"), values.GetValueOrDefault("ro.build.display.id"));
    }

    public async Task<AdbTransport> ConnectAsync(string serial, string? endpoint, CancellationToken token)
    {
        await _connections.WaitAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var snapshot = await DiscoverAsync(deadline.Token);
            foreach (var current in snapshot.Transports.Where(t => t.State == "device" && t.Serial == serial))
            {
                var verified = await ReadInformationAsync(current.Id, deadline.Token);
                if (verified.Serial == serial) return verified;
            }
            var endpoints = snapshot.Services.Where(s => s.Serial == serial).Select(s => s.Endpoint).ToList();
            if (endpoint is not null && AdbParser.ValidEndpoint(endpoint)) endpoints.Add(endpoint);
            foreach (string candidate in endpoints.Distinct())
            {
                var connect = await Adb(deadline.Token, "connect", candidate);
                logs.Write("ADB", "Verbindungsversuch: " + candidate, serial);
                for (int i = 0; i < 3; i++)
                {
                    var listed = await Adb(deadline.Token, "devices");
                    foreach (var transport in AdbParser.ParseDevices(listed.Output).Where(t => t.State == "device" && (t.Id == candidate || AdbParser.SerialFromInstance(t.Id) == serial)))
                    {
                        var info = await ReadInformationAsync(transport.Id, deadline.Token);
                        if (info.Serial == serial) return info;
                        throw new IOException("Die Seriennummer stimmt nicht überein. Der Start wurde zum Schutz vor einer falschen Gerätezuordnung abgebrochen.");
                    }
                    await Task.Delay(350, deadline.Token);
                }
                logs.Write("ADB", connect.Text, serial, "WARN");
            }
            throw new IOException("Gerät nicht verbunden. Bitte WLAN-Debugging, aktuelle Adresse und Kopplung prüfen.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Zeitlimit beim Verbinden. Bitte WLAN-Debugging und Kopplung prüfen."); }
        finally { _connections.Release(); }
    }

    public async Task DisconnectAsync(string transport, CancellationToken token)
    {
        if (!AdbParser.IsWirelessTransport(transport)) throw new IOException("Kein WLAN-Transport zum Trennen vorhanden.");
        var result = await Adb(token, "disconnect", transport);
        if (!result.Success) throw new IOException("Trennen fehlgeschlagen. " + result.Text);
    }

    public async Task PairAsync(string endpoint, string code, CancellationToken token)
    {
        if (!AdbParser.ValidEndpoint(endpoint) || code.Length != 6 || !code.All(char.IsAsciiDigit)) throw new IOException("Bitte eine gültige IPv4-Adresse, einen Port und den sechsstelligen Kopplungscode eingeben.");
        await PairWithSecretAsync(endpoint, code, token);
    }

    public Task PairQrAsync(QrPairingSession session, CancellationToken token) => session.WaitAndPairAsync(async ct =>
    {
        var services = await Adb(ct, "mdns", "services");
        if (!services.Success) throw new IOException("Die QR-Kopplung benötigt mDNS. Bitte WLAN-Debugging, Netzwerk und Firewall prüfen oder mit Code koppeln.");
        return services.Output;
    }, PairWithSecretAsync, token);

    private async Task PairWithSecretAsync(string endpoint, string code, CancellationToken token)
    {
        logs.Write("ADB", "Kopplung wird gestartet (Code wird nicht protokolliert).");
        var result = await runner.RunAsync(paths.Adb, ["pair", endpoint], TimeSpan.FromSeconds(30), token, input: code);
        if (!result.Success || !result.Text.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase)) throw new IOException("Kopplung fehlgeschlagen. Bitte aktuellen Kopplungsport und Code prüfen. " + result.Text);
        logs.Write("ADB", "Kopplung erfolgreich. Der Verbindungsport wird getrennt ermittelt.");
    }

    public async Task<AdbTransport> ConnectManualAsync(string endpoint, CancellationToken token)
    {
        if (!AdbParser.ValidEndpoint(endpoint)) throw new IOException("Bitte eine gültige IPv4-Adresse mit Verbindungsport eingeben.");
        var result = await Adb(token, "connect", endpoint);
        if (!result.Success) throw new IOException(result.Text);
        return await ReadInformationAsync(endpoint, token);
    }

    public async Task<IReadOnlyList<string>> EncodersAsync(string transport, string codec, CancellationToken token)
    {
        var result = await runner.RunAsync(paths.Scrcpy, ["--serial", transport, "--list-encoders"], TimeSpan.FromSeconds(15), token, "scrcpy");
        if (!result.Success) throw new IOException("Encoderliste konnte nicht gelesen werden. " + result.Text);
        return System.Text.RegularExpressions.Regex.Matches(result.Text, @"--video-codec=" + codec + @"\s+--video-encoder=([^\s]+)")
            .Select(m => m.Groups[1].Value.Trim('\'', '"')).Distinct().ToArray();
    }

    public async Task RestartServerAsync(CancellationToken token)
    {
        var stop = await Adb(token, "kill-server");
        if (!stop.Success) throw new IOException("ADB-Server konnte nicht beendet werden. " + stop.Text);
        Interlocked.Exchange(ref _protoRead, 0);
        await StartServerAsync(token);
    }
}
