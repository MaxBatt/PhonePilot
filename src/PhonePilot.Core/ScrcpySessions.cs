using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PhonePilot.Core;

public interface IScrcpySession : IAsyncDisposable
{
    Task<int> Completion { get; }
    Task WaitUntilReadyAsync(CancellationToken token);
    Task StopAsync();
    bool ShowWindow();
    bool HasWindow { get; }
    Task SetDisplayPowerAsync(bool on, CancellationToken token);
}
public interface IScrcpySessionFactory
{
    IScrcpySession Start(DeviceConfiguration configuration, string transport);
}

public sealed class ScrcpySessionFactory(ToolPaths paths, ProcessRunner runner, LogStore logs) : IScrcpySessionFactory
{
    public IScrcpySession Start(DeviceConfiguration configuration, string transport)
        => new ScrcpyProcess(runner.CreateInfo(paths.Scrcpy, configuration.Stream.BuildArguments(transport, configuration.DisplayName)), logs, configuration.Serial);

    private sealed class ScrcpyProcess : IScrcpySession
    {
        private readonly Process _process;
        private readonly LogStore _logs;
        private readonly string _serial;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim _stop = new(1, 1);
        private string? _lastError;
        private int _disposeRequested;
        private bool _disposed;
        private readonly object _displaySync = new();
        private TaskCompletionSource<bool>? _displayReply;
        public Task<int> Completion { get; }

        public ScrcpyProcess(ProcessStartInfo info, LogStore logs, string serial)
        {
            _logs = logs; _serial = serial;
            _process = Process.Start(info) ?? throw new IOException("scrcpy konnte nicht gestartet werden.");
            Completion = ObserveAsync();
        }
        private async Task DrainAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                string level = line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ? "ERROR" : line.Contains("WARN:", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO";
                if (level == "ERROR") _lastError = line;
                _logs.Write("scrcpy", line, _serial, level);
                if (line.Contains("Device display turned ", StringComparison.Ordinal))
                {
                    bool? on = line.Contains("Device display turned on", StringComparison.Ordinal) ? true :
                        line.Contains("Device display turned off", StringComparison.Ordinal) ? false : null;
                    if (on.HasValue) lock (_displaySync) _displayReply?.TrySetResult(on.Value);
                }
                // scrcpy 4.1 emits the texture size when the video renderer is ready.
                if (line.Contains("Texture:", StringComparison.OrdinalIgnoreCase)) _ready.TrySetResult();
            }
        }
        private async Task<int> ObserveAsync()
        {
            var output = DrainAsync(_process.StandardOutput); var error = DrainAsync(_process.StandardError);
            await _process.WaitForExitAsync();
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception ex) when (ex is IOException or TimeoutException) { }
            _ready.TrySetException(new IOException(_lastError ?? "scrcpy wurde beendet, bevor die Sitzung bereit war."));
            return _process.ExitCode;
        }
        public async Task WaitUntilReadyAsync(CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            // A real HWND is a second readiness signal, without depending solely on log wording.
            try
            {
                while (!_ready.Task.IsCompleted)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    _process.Refresh();
                    if (!_process.HasExited && _process.MainWindowHandle != IntPtr.Zero) _ready.TrySetResult();
                    await Task.WhenAny(_ready.Task, Task.Delay(100, deadline.Token));
                }
                await _ready.Task;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("scrcpy war nach 30 Sekunden noch nicht bereit. Bitte die Logs prüfen."); }
        }
        public async Task StopAsync()
        {
            await _stop.WaitAsync();
            try { await StopCoreAsync(); }
            finally { _stop.Release(); }
        }
        private async Task StopCoreAsync()
        {
            if (_disposed) return;
            if (_process.HasExited) { await Completion; return; }
            _process.CloseMainWindow();
            try { await Completion.WaitAsync(TimeSpan.FromSeconds(4)); }
            catch (TimeoutException) { if (!_process.HasExited) _process.Kill(); await Completion.WaitAsync(TimeSpan.FromSeconds(4)); }
        }
        public bool ShowWindow()
        {
            if (!_stop.Wait(0)) return false;
            try
            {
                if (_disposed || _process.HasExited) return false;
                _process.Refresh(); var handle = _process.MainWindowHandle;
                if (handle == IntPtr.Zero) return false;
                ShowWindowNative(handle, 9);
                return SetForegroundWindow(handle);
            }
            finally { _stop.Release(); }
        }
        public bool HasWindow
        {
            get
            {
                if (!_stop.Wait(0)) return false;
                try { return WindowHandle() != IntPtr.Zero; }
                finally { _stop.Release(); }
            }
        }
        private IntPtr WindowHandle()
        {
            if (_disposed || _process.HasExited) return IntPtr.Zero;
            _process.Refresh();
            var handle = _process.MainWindowHandle;
            return ScrcpyWindow.IsWindow(handle) ? handle : IntPtr.Zero;
        }
        public async Task SetDisplayPowerAsync(bool on, CancellationToken token)
        {
            await _stop.WaitAsync(token);
            var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                token.ThrowIfCancellationRequested();
                lock (_displaySync) _displayReply = reply;
                ScrcpyWindow.SetDisplayPower(WindowHandle(), on);
                var completed = await Task.WhenAny(reply.Task, Completion).WaitAsync(TimeSpan.FromSeconds(4), token);
                if (completed == Completion || await reply.Task != on)
                    throw new IOException("scrcpy hat die Displayaktion nicht bestätigt. Bitte erneut versuchen.");
            }
            catch (TimeoutException) { throw new IOException("scrcpy hat die Displayaktion nicht bestätigt. Bitte erneut versuchen."); }
            finally
            {
                lock (_displaySync) _displayReply = null;
                _stop.Release();
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
            await _stop.WaitAsync();
            try { await StopCoreAsync(); _disposed = true; _process.Dispose(); }
            finally { _stop.Release(); }
        }
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] private static extern bool ShowWindowNative(IntPtr handle, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    }
}
