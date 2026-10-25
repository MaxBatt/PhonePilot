using System.Runtime.InteropServices;

namespace PhonePilot.Core;

internal static class ScrcpyWindow
{
    public static void SetDisplayPower(IntPtr handle, bool on)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle)) throw new IOException("Das scrcpy-Fenster ist nicht mehr geöffnet.");
        // Do not mix a shortcut with keys the user is currently holding.
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x4F }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
            throw new IOException("Bitte die Tastaturtasten loslassen und die Displayaktion erneut wählen.");
        bool alt = false, shift = false, letter = false;
        try
        {
            // SDL consumes window messages, including modifiers, even when the
            // window is minimized/unfocused. Never send global keyboard input.
            Post(handle, 0x104, 0x12, 0x38); alt = true;
            if (on) { Post(handle, 0x100, 0x10, 0x2A); shift = true; }
            Post(handle, 0x100, 0x4F, 0x18); letter = true;
        }
        finally
        {
            if (letter) Release(handle, 0x101, 0x4F, 0x18);
            if (shift) Release(handle, 0x101, 0x10, 0x2A);
            if (alt) Release(handle, 0x105, 0x12, 0x38);
        }
    }

    private static void Post(IntPtr handle, uint message, int key, int scan)
    {
        if (!PostMessage(handle, message, (IntPtr)key, (IntPtr)(1L | ((long)scan << 16))))
            throw new IOException("Die Displayaktion konnte nicht an das scrcpy-Fenster gesendet werden.");
    }
    private static void Release(IntPtr handle, uint message, int key, int scan)
        => PostMessage(handle, message, (IntPtr)key, (IntPtr)(0xC0000001L | ((long)scan << 16)));

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
