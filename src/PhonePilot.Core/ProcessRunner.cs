using System.Diagnostics;
using System.Text;

namespace PhonePilot.Core;

public sealed record ToolPaths(string Root)
{
    public string Adb => Path.Combine(Root, "adb", "adb.exe");
    public string Scrcpy => Path.Combine(Root, "scrcpy", "scrcpy.exe");
    public string Server => Path.Combine(Root, "scrcpy", "scrcpy-server");
    public IReadOnlyList<string> MissingFiles => new[] { Adb, Path.Combine(Root, "adb", "AdbWinApi.dll"), Path.Combine(Root, "adb", "AdbWinUsbApi.dll"), Scrcpy, Server,
        Path.Combine(Root, "scrcpy", "SDL3.dll"), Path.Combine(Root, "scrcpy", "libusb-1.0.dll") }.Where(p => !File.Exists(p))
        .Concat(new[] { "avcodec-*.dll", "avformat-*.dll", "avutil-*.dll", "swresample-*.dll" }.Where(pattern => !Directory.Exists(Path.GetDirectoryName(Scrcpy)) || !Directory.EnumerateFiles(Path.GetDirectoryName(Scrcpy)!, pattern).Any())
        .Select(pattern => Path.Combine(Root, "scrcpy", pattern))).ToArray();
}
public sealed record ProcessResult(int Code, string Output, string Error, bool TimedOut = false)
{
    public string Text => (Output + "\n" + Error).Trim();
    public bool Success => Code == 0 && !TimedOut;
}

public sealed class ProcessRunner(ToolPaths paths, LogStore logs)
{
    public ProcessStartInfo CreateInfo(string file, IEnumerable<string> arguments, bool input = false)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file)!,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["ADB"] = paths.Adb;
        info.Environment["SCRCPY_SERVER_PATH"] = paths.Server;
        info.Environment["ADB_MDNS_AUTO_CONNECT"] = "0";
        foreach (string key in new[] { "ADB_SERVER_SOCKET", "ANDROID_ADB_SERVER_ADDRESS", "ANDROID_ADB_SERVER_PORT", "ANDROID_SERIAL" }) info.Environment.Remove(key);
        return info;
    }

    public async Task<ProcessResult> RunAsync(string file, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token,
        string source = "ADB", string? serial = null, string? input = null, bool logOutput = false)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(CreateInfo(file, arguments, input is not null)) ?? throw new IOException("Programm konnte nicht gestartet werden: " + file);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        string Sanitize(string text) => input is null ? text : text.Replace(input, "[Kopplungscode ausgeblendet]", StringComparison.Ordinal);
        async Task Drain(StreamReader reader, StringBuilder buffer)
        {
            while (await reader.ReadLineAsync(limit.Token) is { } line)
            {
                line = Sanitize(line);
                if (buffer.Length < 128 * 1024) buffer.AppendLine(line);
                if (logOutput) logs.Write(source, line, serial, line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ? "ERROR" : line.Contains("WARN", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO");
            }
        }
        var outputTask = Drain(process.StandardOutput, stdout); var errorTask = Drain(process.StandardError, stderr);
        try
        {
            if (input is not null) { await process.StandardInput.WriteLineAsync(input.AsMemory(), limit.Token); process.StandardInput.Close(); }
            await process.WaitForExitAsync(limit.Token);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(limit.Token);
            return new(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
            token.ThrowIfCancellationRequested();
            return new(-1, stdout.ToString(), stderr.ToString(), true);
        }
    }
}
