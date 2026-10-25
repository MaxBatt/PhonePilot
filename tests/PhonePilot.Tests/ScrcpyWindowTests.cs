using System.Runtime.InteropServices;
using PhonePilot.Core;

namespace PhonePilot.Tests;

// Exercise the exact messages against the bundled SDL, with an invisible window.
// No Android device, global keyboard injection or foreground activation.
public sealed class ScrcpyWindowTests
{
    static ScrcpyWindowTests()
    {
        NativeLibrary.SetDllImportResolver(typeof(ScrcpyWindowTests).Assembly, (name, _, _) =>
            name == "PhonePilot.Test.SDL3" ? NativeLibrary.Load(Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "../../../../../scrcpy/SDL3.dll"))) : IntPtr.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisplayShortcutReachesUnfocusedSdlWindowWithoutStealingFocus(bool on)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { Check(on); } catch (Exception ex) { error = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "SDL probe timed out");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void Check(bool on)
    {
        var foreground = GetForegroundWindow();
        Assert.True(SDL_Init(0x20));
        var window = SDL_CreateWindow("PhonePilot invisible SDL test", 64, 64, 8);
        var buffer = Marshal.AllocHGlobal(128);
        try
        {
            Assert.NotEqual(IntPtr.Zero, window);
            var handle = SDL_GetPointerProperty(SDL_GetWindowProperties(window), "SDL.window.win32.hwnd", IntPtr.Zero);
            while (SDL_PollEvent(buffer)) { }
            var command = typeof(ScrcpySessionFactory).Assembly.GetType("PhonePilot.Core.ScrcpyWindow")!.GetMethod("SetDisplayPower")!;
            command.Invoke(null, [handle, on]);
            var keys = new List<(int Scan, short Mod)>();
            for (int n = 0; n < 3; n++)
                while (SDL_PollEvent(buffer))
                    if (Marshal.ReadInt32(buffer) == 0x300) keys.Add((Marshal.ReadInt32(buffer, 24), Marshal.ReadInt16(buffer, 32)));
            var letter = Assert.Single(keys, k => k.Scan == 18);
            Assert.Equal(0x100, letter.Mod & 0x100); // Left Alt
            Assert.Equal(on ? 1 : 0, letter.Mod & 3); // Left Shift only when turning on
            Assert.Equal(foreground, GetForegroundWindow());
            Assert.Equal(0, SDL_GetModState() & 0x303); // No stuck Alt or Shift
        }
        finally { Marshal.FreeHGlobal(buffer); SDL_DestroyWindow(window); SDL_Quit(); }
    }

    [DllImport("PhonePilot.Test.SDL3")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_Init(uint flags);
    [DllImport("PhonePilot.Test.SDL3")] private static extern IntPtr SDL_CreateWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int width, int height, ulong flags);
    [DllImport("PhonePilot.Test.SDL3")] private static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport("PhonePilot.Test.SDL3")] private static extern IntPtr SDL_GetPointerProperty(uint properties, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr fallback);
    [DllImport("PhonePilot.Test.SDL3")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_PollEvent(IntPtr buffer);
    [DllImport("PhonePilot.Test.SDL3")] private static extern ushort SDL_GetModState();
    [DllImport("PhonePilot.Test.SDL3")] private static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport("PhonePilot.Test.SDL3")] private static extern void SDL_Quit();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
