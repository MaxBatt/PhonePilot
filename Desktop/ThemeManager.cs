using Microsoft.Win32;

namespace PhonePilot.Desktop;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        bool dark = theme == "dark";
        if (theme == "system")
        {
            try { dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
            catch (System.Security.SecurityException) { dark = false; }
        }
        var colors = dark ? new Dictionary<string, string>
        {
            ["BackgroundBrush"] = "#20242C", ["SurfaceBrush"] = "#2B303B", ["TextBrush"] = "#F0F3FA", ["MutedBrush"] = "#AFBACC", ["BorderBrush"] = "#424B5C", ["SelectedBrush"] = "#193C62",
            ["AccentBrush"] = "#63AAFF", ["ErrorBrush"] = "#FF9699", ["ErrorSurfaceBrush"] = "#492F35", ["WarningSurfaceBrush"] = "#473C25"
        } : new Dictionary<string, string>
        {
            ["BackgroundBrush"] = "#F4F7FC", ["SurfaceBrush"] = "#FFFFFF", ["TextBrush"] = "#172033", ["MutedBrush"] = "#626F84", ["BorderBrush"] = "#DFE6F0", ["SelectedBrush"] = "#E4F0FF",
            ["AccentBrush"] = "#0067EF", ["ErrorBrush"] = "#C42B30", ["ErrorSurfaceBrush"] = "#FFF0F0", ["WarningSurfaceBrush"] = "#FFF5DD"
        };
        foreach (var (key, color) in colors) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
