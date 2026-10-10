using System.Security.Cryptography;

namespace PhonePilot.Core;

// Ephemeral credentials: never persisted, printed, or used as command arguments.
public sealed class QrPairingSession
{
    public string ServiceName { get; } = "studio-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public string Secret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string Payload => $"WIFI:T:ADB;S:{ServiceName};P:{Secret};;";
    public override string ToString() => "QR-Kopplung";

    public string? FindEndpoint(string services)
    {
        foreach (var line in services.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 3 || fields[1].TrimEnd('.') != "_adb-tls-pairing._tcp" || !AdbParser.ValidEndpoint(fields[2])) continue;
            var instance = fields[0].TrimEnd('.');
            if (instance == ServiceName || instance == ServiceName + "._adb-tls-pairing._tcp" || instance == ServiceName + "._adb-tls-pairing._tcp.local") return fields[2];
        }
        return null;
    }

    public async Task WaitAndPairAsync(Func<CancellationToken, Task<string>> discover,
        Func<string, string, CancellationToken, Task> pair, CancellationToken token, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
        try
        {
            while (true)
            {
                limit.Token.ThrowIfCancellationRequested();
                var endpoint = FindEndpoint(await discover(limit.Token));
                if (endpoint is not null)
                {
                    limit.Token.ThrowIfCancellationRequested();
                    await pair(endpoint, Secret, limit.Token);
                    return;
                }
                await Task.Delay(750, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Der QR-Code ist abgelaufen. Bitte einen neuen QR-Code erzeugen und erneut scannen."); }
    }
}
