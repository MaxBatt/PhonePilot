using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PhonePilot.Core;

public static class AdbParser
{
    // Kept from the launcher: only complete IPv4 endpoints and wireless instance IDs are eligible.
    public static bool ValidEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':');
        return parts.Length == 2 && Regex.IsMatch(parts[0], @"^\d{1,3}(\.\d{1,3}){3}$") && IPAddress.TryParse(parts[0], out var ip) &&
            ip.AddressFamily == AddressFamily.InterNetwork && int.TryParse(parts[1], out int port) && port is > 0 and <= 65535;
    }
    public static string SerialFromInstance(string instance) => Regex.Match(instance,
        @"^adb-(.+)-[A-Za-z0-9]{6}(?:\._adb-tls-connect\._tcp\.?(?:local\.?)?)?$").Groups[1].Value;
    public static bool IsWirelessTransport(string id) => ValidEndpoint(id) || SerialFromInstance(id).Length > 0;

    public static IEnumerable<MdnsService> ParsePlain(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[1].TrimEnd('.') != "_adb-tls-connect._tcp" || !ValidEndpoint(fields[2])) continue;
            string serial = SerialFromInstance(fields[0]);
            if (serial.Length == 0) continue;
            var endpoint = fields[2].Split(':');
            yield return new(serial, "Android-Gerät", endpoint[0], int.Parse(endpoint[1]));
        }
    }

    public static IEnumerable<MdnsService> ParseProto(string text)
    {
        foreach (Match match in Regex.Matches(text, @"\bservice\s*\{((?:[^{}""]|""(?:\\.|[^""\\])*"")*)\}"))
        {
            string block = match.Groups[1].Value;
            string Field(string key)
            {
                var value = Regex.Match(block, "\\b" + key + "\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")");
                if (!value.Success) return "";
                try { return JsonSerializer.Deserialize<string>(value.Groups[1].Value) ?? ""; }
                catch (JsonException) { return ""; }
            }
            if (Field("service").TrimEnd('.') != "_adb-tls-connect._tcp") continue;
            string serial = Field("serial");
            if (serial.Length == 0) serial = SerialFromInstance(Field("instance"));
            string name = new[] { Field("given_name"), Field("product_model"), Field("device_model"), "Android-Gerät" }.First(n => n.Length > 0);
            string ipv4 = Field("ipv4");
            string port = Regex.Match(block, @"\bport\s*:\s*(\d+)").Groups[1].Value;
            if (serial.Length > 0 && ValidEndpoint(ipv4 + ":" + port)) yield return new(serial, name, ipv4, int.Parse(port));
        }
    }

    public static IEnumerable<AdbTransport> ParseDevices(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"^(\S+)\s+(device|offline|unauthorized)\b(.*)$");
            if (!match.Success || !IsWirelessTransport(match.Groups[1].Value)) continue;
            string model = Regex.Match(match.Groups[3].Value, @"\bmodel:(\S+)").Groups[1].Value.Replace('_', ' ');
            yield return new(match.Groups[1].Value, match.Groups[2].Value, Model: string.IsNullOrEmpty(model) ? null : model);
        }
    }

    public static IReadOnlyDictionary<string, string> ParseProperties(string text) => Regex.Matches(text, @"(?m)^\[([^\]]+)\]: \[([^\r\n]*)\]\r?$")
        .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => g.Last().Groups[2].Value);
}
