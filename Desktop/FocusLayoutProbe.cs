using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PhonePilot.Desktop;

// Exercises actual WPF keyboard focus on an invisible test window, without devices.
internal static class FocusLayoutProbe
{
    private sealed record Geometry(Point Position, Size Size);

    public static string Check(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000; window.Top = -10000;
        window.ShowInTaskbar = false; window.Opacity = 0;
        window.Show();
        int checkedTargets = 0;
        string iconReport;
        var failures = new List<string>();
        try
        {
            iconReport = CheckWindowIcon(window);
            foreach (double width in new[] { 1440d, 960d })
            {
                window.Width = width;
                Settle(window);
                var targets = Descendants(window).Where(e => e is Button { IsEnabled: true, IsVisible: true } or ListBoxItem { IsSelected: true, IsVisible: true }).ToArray();
                foreach (var target in targets)
                {
                    // Normalize intentional scrolling before comparing focus geometry.
                    target.BringIntoView(); Settle(window);
                    Keyboard.ClearFocus(); Settle(window);
                    var before = Snapshot(window);
                    if (!target.Focus() || !target.IsKeyboardFocusWithin) throw new InvalidOperationException("Der Layouttest konnte den Tastaturfokus nicht setzen.");
                    Settle(window);
                    checkedTargets++;
                    var after = Snapshot(window);
                    var shifted = before.Where(pair => after.TryGetValue(pair.Key, out var bounds) &&
                        ((bounds.Position - pair.Value.Position).Length > 0.01 || Math.Abs(bounds.Size.Width - pair.Value.Size.Width) > 0.01 || Math.Abs(bounds.Size.Height - pair.Value.Size.Height) > 0.01)).ToArray();
                    if (shifted.Length > 0) failures.Add(width + " px · " + (target is ContentControl content ? content.Content?.ToString() : target.GetType().Name) + ": " + shifted.Length + " Elemente verschoben oder vergrößert.");
                }
            }
        }
        finally { window.Close(); }
        string report = "Geprüfte Fokuswechsel: " + checkedTargets + Environment.NewLine + string.Join(Environment.NewLine, failures);
        if (failures.Count > 0) throw new InvalidOperationException(report);
        return "PASS: " + checkedTargets + " Fokuswechsel bei 1440 und 960 px ohne Layoutverschiebung. Keine Geräteverbindungen." + Environment.NewLine + iconReport;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string CheckWindowIcon(Window window)
    {
        // Check the HICON supplied by the actual WPF window to Windows, rather
        // than only inspecting the icon stored in the executable file.
        var handle = SendMessage(new WindowInteropHelper(window).Handle, 0x007F, new IntPtr(1), IntPtr.Zero);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Das native Fenstersymbol fehlt.");
        using var icon = System.Drawing.Icon.FromHandle(handle);
        using var bitmap = icon.ToBitmap();
        int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).A >= 128)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (bitmap.Width != bitmap.Height || right - left + 1 != bitmap.Width || bottom - top + 1 != bitmap.Height)
            throw new InvalidOperationException("Das native Fenstersymbol nutzt keine volle quadratische Fläche.");
        return $"PASS: Natives WPF-Fenstersymbol sichtbar {bitmap.Width}×{bitmap.Height}, volle quadratische Fläche.";
    }

    private static Dictionary<FrameworkElement, Geometry> Snapshot(Window window)
    {
        var root = (FrameworkElement)window.Content;
        return Descendants(root).Where(e => e.IsVisible && e.ActualWidth > 0 && e.ActualHeight > 0 && e is Button or TextBlock or ListBoxItem or TabControl)
            .ToDictionary(e => e, e => new Geometry(e.TransformToAncestor(root).Transform(new Point()), new Size(e.ActualWidth, e.ActualHeight)));
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }
}
