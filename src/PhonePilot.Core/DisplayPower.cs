using System.Text.RegularExpressions;

namespace PhonePilot.Core;

public enum DisplayPower { Unknown, Off, On }

public static class DisplayPowerParser
{
    // SurfaceFlinger reports the physical power mode. dumpsys power/display may
    // still report ON while scrcpy has turned the panel off via SurfaceControl.
    public static DisplayPower Parse(string output)
    {
        var sections = Regex.Matches(output,
            @"(?ms)^\s*(?<kind>Virtual Display|Display)\s+\d+[^\r\n]*\r?\n(?<body>.*?)(?=^\s*(?:Virtual Display|Display)\s+\d+|\z)");
        var physical = sections.Cast<Match>().Where(m => m.Groups["kind"].Value == "Display")
            .Select(m => m.Value).ToArray();
        var internalDisplays = physical.Where(s => Regex.IsMatch(s,
            @"\b(?:type|connectionType)\s*[=:]\s*internal\b", RegexOptions.IgnoreCase)).ToArray();
        var targets = internalDisplays.Length > 0 ? internalDisplays :
            physical.Length == 1 && !Regex.IsMatch(physical[0], @"\b(?:type|connectionType)\s*[=:]\s*external\b", RegexOptions.IgnoreCase)
                ? physical : [];
        if (targets.Length == 0) return DisplayPower.Unknown;
        var modes = targets.Select(s => Regex.Match(s, @"\bpowerMode\s*[=:]\s*(?<mode>[A-Z_]+|\d+)", RegexOptions.IgnoreCase))
            .Select(m => m.Groups["mode"].Value.ToUpperInvariant() switch
            {
                "OFF" or "0" => DisplayPower.Off,
                "ON" or "NORMAL" or "2" or "DOZE" or "1" or "DOZE_SUSPEND" or "3" or "ON_SUSPEND" or "4" => DisplayPower.On,
                _ => DisplayPower.Unknown
            }).ToArray();
        if (modes.Contains(DisplayPower.Unknown)) return DisplayPower.Unknown;
        return modes.Contains(DisplayPower.On) ? DisplayPower.On : DisplayPower.Off;
    }
}
