using PhonePilot.Core;

namespace PhonePilot.Tests;

public sealed class QrPairingTests
{
    [Fact]
    public void CredentialsAreFreshAndPayloadUsesAndroidAdbFormat()
    {
        var sessions = Enumerable.Range(0, 100).Select(_ => new QrPairingSession()).ToArray();
        Assert.Equal(100, sessions.Select(s => s.ServiceName).Distinct().Count());
        Assert.Equal(100, sessions.Select(s => s.Secret).Distinct().Count());
        foreach (var session in sessions)
        {
            Assert.StartsWith("studio-", session.ServiceName);
            Assert.Matches("^[A-F0-9]{32}$", session.Secret);
            Assert.Equal($"WIFI:T:ADB;S:{session.ServiceName};P:{session.Secret};;", session.Payload);
            Assert.DoesNotContain(session.Secret, session.ToString());
        }
    }

    [Fact]
    public void DiscoveryMatchesOnlyThisQrSessionAndPairingService()
    {
        var session = new QrPairingSession();
        string services = $"studio-other _adb-tls-pairing._tcp 192.168.1.2:1234\n" +
            $"{session.ServiceName} _adb-tls-connect._tcp 192.168.1.2:2345\n" +
            $"{session.ServiceName}-other _adb-tls-pairing._tcp 192.168.1.2:3456\n" +
            $"{session.ServiceName} _adb-tls-pairing._tcp 999.1.1.1:4567\n" +
            $"{session.ServiceName}._adb-tls-pairing._tcp.local. _adb-tls-pairing._tcp. 192.168.1.5:5678\r\n";
        Assert.Equal("192.168.1.5:5678", session.FindEndpoint(services));
        Assert.Empty(AdbParser.ParsePlain(services));
        Assert.Null(new QrPairingSession().FindEndpoint(services));
    }

    [Fact]
    public async Task MatchingScanPairsExactlyOnceWithGeneratedSecret()
    {
        var session = new QrPairingSession(); int calls = 0;
        await session.WaitAndPairAsync(_ => Task.FromResult($"{session.ServiceName} _adb-tls-pairing._tcp 192.168.1.2:4567"),
            (endpoint, secret, _) => { Assert.Equal("192.168.1.2:4567", endpoint); Assert.Equal(session.Secret, secret); calls++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExpiryDoesNotPairUnrelatedPhone()
    {
        var session = new QrPairingSession(); bool paired = false;
        var error = await Assert.ThrowsAsync<IOException>(() => session.WaitAndPairAsync(_ => Task.FromResult("studio-other _adb-tls-pairing._tcp 192.168.1.2:1234"),
            (_, _, _) => { paired = true; return Task.CompletedTask; }, CancellationToken.None, TimeSpan.FromMilliseconds(20)));
        Assert.Contains("abgelaufen", error.Message);
        Assert.DoesNotContain(session.Secret, error.Message); Assert.False(paired);
    }

    [Fact]
    public async Task ClosingCancelsPendingScanWithoutPairing()
    {
        var session = new QrPairingSession(); using var cancel = new CancellationTokenSource();
        var waiting = session.WaitAndPairAsync(_ => { cancel.Cancel(); return Task.FromResult(""); },
            (_, _, _) => throw new InvalidOperationException("Must not pair after closing"), cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
