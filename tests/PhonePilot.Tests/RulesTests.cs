using PhonePilot.Core;

namespace PhonePilot.Tests;

public sealed class RulesTests
{
    [Fact]
    public void LegacyMigrationPreservesOriginalStreamWithoutNewLimits()
    {
        var result = ConfigurationStore.Parse("{\"DeviceSerial\":\"PIXEL-123\"}");
        var device = Assert.Single(result.Configuration.Devices);
        Assert.Equal("PIXEL-123", device.Serial);
        Assert.Equal("custom", device.Stream.Preset);
        Assert.Equal("h265", device.Stream.VideoCodec);
        Assert.Equal(40, device.Stream.VideoBitRateMbps);
        Assert.Null(device.Stream.MaxSize);
        Assert.Null(device.Stream.MaxFps);
        Assert.True(device.Stream.TurnScreenOff);
        Assert.True(result.IsLegacy);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{\"schemaVersion\":1,\"devices\":[{\"serial\":\"A\"},{\"serial\":\"A\"}]}")]
    public void InvalidOrFutureConfigurationIsProtectedFromOverwrite(string json)
    {
        var result = ConfigurationStore.Parse(json);
        Assert.False(result.CanSave);
        Assert.NotEmpty(result.Warning!);
    }

    [Fact]
    public void PresetChangesOnlyFourCoreSettings()
    {
        var settings = new StreamSettings { AudioEnabled = false, Fullscreen = true, VideoEncoder = "explicit.encoder" };
        var fast = settings.WithPreset("fast");
        Assert.Equal(1280, fast.MaxSize);
        Assert.Equal(4, fast.VideoBitRateMbps);
        Assert.Equal(30, fast.MaxFps);
        Assert.Equal("h264", fast.VideoCodec);
        Assert.False(fast.AudioEnabled);
        Assert.True(fast.Fullscreen);
        Assert.Equal("explicit.encoder", fast.VideoEncoder);
    }

    [Fact]
    public void ArgumentsTargetExactTransportAndKeepTitleAsOneArgument()
    {
        var settings = new StreamSettings { WindowTitleOverride = "Pixel & Büro \"eins\"", AudioEnabled = false };
        var args = settings.BuildArguments("192.168.1.5:38911", "Pixel");
        Assert.Equal(new[] { "--serial", "192.168.1.5:38911" }, args.Take(2));
        Assert.Contains("--window-title=Pixel & Büro \"eins\"", args);
        Assert.Contains("--no-audio", args);
        Assert.DoesNotContain("--kill-adb-on-close", args);
    }

    [Theory]
    [InlineData("192.168.1.5:5555", true)]
    [InlineData("999.168.1.5:5555", false)]
    [InlineData("127.1:5555", false)]
    [InlineData("192.168.1.5:0", false)]
    [InlineData("192.168.1.5:65536", false)]
    public void EndpointValidationRejectsAmbiguousOrInvalidAddresses(string value, bool valid)
        => Assert.Equal(valid, AdbParser.ValidEndpoint(value));

    [Fact]
    public void DiscoveryIgnoresPairingServicesAndPreservesHardwareIdentity()
    {
        const string text = "adb-PIXEL-ABC-a1B2c3 _adb-tls-connect._tcp 192.168.1.2:34567\n" +
            "adb-OTHER-xxxxxx _adb-tls-pairing._tcp 192.168.1.3:45678";
        var found = Assert.Single(AdbParser.ParsePlain(text));
        Assert.Equal("PIXEL-ABC", found.Serial);
        Assert.Equal("192.168.1.2:34567", found.Endpoint);
    }

    [Fact]
    public void ProtoDiscoveryRetainsNameAndAcceptsOnlyValidConnectService()
    {
        const string text = "tls { service { instance: \"adb-A-abcdef\" service: \"_adb-tls-connect._tcp\" " +
            "serial: \"A\" given_name: \"Büro Pixel\" ipv4: \"192.168.1.2\" port: 35678 } }";
        var service = Assert.Single(AdbParser.ParseProto(text));
        Assert.Equal("Büro Pixel", service.Name);
        Assert.Equal("A", service.Serial);
    }

    [Fact]
    public void FourConcurrentReservationsAndDuplicateProtectionAreAtomic()
    {
        var slots = new SessionSlots(4);
        var accepted = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 30, i => { if (slots.TryReserve("S" + i)) accepted.Add("S" + i); });
        Assert.Equal(4, accepted.Count);
        Assert.False(slots.TryReserve(accepted.First()));
        slots.Release(accepted.First());
        Assert.True(slots.TryReserve("NEW"));
    }

    [Fact]
    public void ConfigurationContainsNoTransientEndpointOrSessionState()
    {
        var config = new AppConfiguration { Devices = [new DeviceConfiguration { Serial = "ABC" }] };
        var json = ConfigurationStore.Serialize(config);
        Assert.DoesNotContain("endpoint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("processId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pairingCode", json, StringComparison.OrdinalIgnoreCase);
    }
}
