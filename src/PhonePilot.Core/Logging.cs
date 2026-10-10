namespace PhonePilot.Core;

public sealed record LogEntry(DateTimeOffset Time, string Level, string Source, string Message, string? Serial = null)
{
    public string Text => $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{Level}] [{Source}] [{Serial ?? "Allgemein"}] {Message}";
    public string DisplayMessage => "[" + (Serial ?? "Allgemein") + "] " + Message;
}

public sealed class LogStore
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly string? _directory;
    public event Action<LogEntry>? Added;
    public string? FileError { get; private set; }
    public LogStore(string? directory = null) { _directory = directory; }
    public IReadOnlyList<LogEntry> Snapshot() { lock (_entries) return _entries.ToArray(); }
    public void Write(string source, string message, string? serial = null, string level = "INFO")
    {
        if (message.Length > 16384) message = message[..16384] + " … [gekürzt]";
        var entry = new LogEntry(DateTimeOffset.Now, level, source, message, serial);
        lock (_entries)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > 5000) _entries.Dequeue();
            if (_directory is not null)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    string path = Path.Combine(_directory, "PhonePilot.log");
                    if (File.Exists(path) && new FileInfo(path).Length + System.Text.Encoding.UTF8.GetByteCount(entry.Text + Environment.NewLine) > 5 * 1024 * 1024)
                    {
                        for (int i = 2; i >= 1; i--) { string previous = i == 1 ? path : path + ".1"; if (File.Exists(previous)) File.Move(previous, path + "." + i, true); }
                    }
                    File.AppendAllText(path, entry.Text + Environment.NewLine);
                    FileError = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FileError = "Protokolldatei nicht beschreibbar: " + ex.Message; }
            }
        }
        Added?.Invoke(entry);
    }
}
