namespace ESergi.Mobile;

internal static class ThemePalette
{
    public static void Initialize(Application app)
    {
        var mode = Preferences.Default.Get("esergi-theme-mode", "system");
        Apply(app, mode, false);
        app.RequestedThemeChanged += (_, _) => { var current = Preferences.Default.Get("esergi-theme-mode", "system"); if (current == "system") Apply(app, current); };
    }

    public static Color Get(string role) => Application.Current?.Resources.TryGetValue(role, out var value) == true && value is Color color ? color : Colors.Transparent;

    public static void Apply(Application app, string mode, bool restart = true)
    {
        Preferences.Default.Set("esergi-theme-mode", mode);
        app.UserAppTheme = mode switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
        var dark = mode == "dark" || (mode == "system" && app.RequestedTheme == AppTheme.Dark);
        var primary = DynamicPrimary(dark);
        var primaryContainer = dark ? Color.FromArgb("#4F378B") : Color.FromArgb("#EADDFF");
        var colors = new Dictionary<string, Color>
        {
            ["Primary"] = primary, ["OnPrimary"] = DynamicRole(dark ? "system_accent1_800" : "system_accent1_0", dark ? Color.FromArgb("#381E72") : Colors.White),
            ["PrimaryContainer"] = DynamicRole(dark ? "system_accent1_700" : "system_accent1_100", primaryContainer), ["OnPrimaryContainer"] = DynamicRole(dark ? "system_accent1_100" : "system_accent1_900", dark ? Color.FromArgb("#EADDFF") : Color.FromArgb("#21005D")),
            ["Secondary"] = DynamicRole(dark ? "system_accent2_200" : "system_accent2_600", dark ? Color.FromArgb("#CCC2DC") : Color.FromArgb("#625B71")),
            ["SecondaryContainer"] = DynamicRole(dark ? "system_accent2_700" : "system_accent2_100", dark ? Color.FromArgb("#4A4458") : Color.FromArgb("#E8DEF8")),
            ["OnSecondaryContainer"] = DynamicRole(dark ? "system_accent2_100" : "system_accent2_900", dark ? Color.FromArgb("#E8DEF8") : Color.FromArgb("#1D192B")),
            ["OutlineVariant"] = DynamicRole(dark ? "system_neutral2_700" : "system_neutral2_300", dark ? Color.FromArgb("#49454F") : Color.FromArgb("#CAC4D0")),
            ["Background"] = DynamicRole(dark ? "system_neutral1_900" : "system_neutral1_10", dark ? Color.FromArgb("#141218") : Color.FromArgb("#FFFBFE")),
            ["Surface"] = DynamicRole(dark ? "system_neutral1_900" : "system_neutral1_10", dark ? Color.FromArgb("#141218") : Color.FromArgb("#FFFBFE")),
            ["SurfaceContainer"] = DynamicRole(dark ? "system_neutral1_800" : "system_neutral1_50", dark ? Color.FromArgb("#211F26") : Color.FromArgb("#F3EFF7")),
            ["SurfaceContainerHigh"] = DynamicRole(dark ? "system_neutral1_700" : "system_neutral1_100", dark ? Color.FromArgb("#2B2930") : Color.FromArgb("#ECE6F0")),
            ["OnSurface"] = DynamicRole(dark ? "system_neutral1_100" : "system_neutral1_900", dark ? Color.FromArgb("#E6E0E9") : Color.FromArgb("#1C1B1F")),
            ["OnSurfaceVariant"] = DynamicRole(dark ? "system_neutral2_200" : "system_neutral2_700", dark ? Color.FromArgb("#CAC4D0") : Color.FromArgb("#49454F")),
            ["Outline"] = DynamicRole(dark ? "system_neutral2_400" : "system_neutral2_500", dark ? Color.FromArgb("#938F99") : Color.FromArgb("#79747E")),
            ["Error"] = dark ? Color.FromArgb("#F2B8B5") : Color.FromArgb("#B3261E"),
            ["ErrorContainer"] = dark ? Color.FromArgb("#8C1D18") : Color.FromArgb("#F9DEDC"),
            ["OnErrorContainer"] = dark ? Color.FromArgb("#F9DEDC") : Color.FromArgb("#410E0B"),
            ["OnError"] = dark ? Color.FromArgb("#601410") : Colors.White,
            ["Success"] = dark ? Color.FromArgb("#6FBF73") : Color.FromArgb("#2E7D32"),
            ["OnSuccess"] = Colors.White,
            ["RankGold"] = Color.FromArgb(dark ? "#765B18" : "#F7E3A1"), ["OnRankGold"] = Color.FromArgb(dark ? "#FFF3D0" : "#4A3500"),
            ["RankSilver"] = Color.FromArgb(dark ? "#4B515B" : "#E0E4EA"), ["OnRankSilver"] = Color.FromArgb(dark ? "#F0F2F5" : "#303640"),
            ["RankBronze"] = Color.FromArgb(dark ? "#69412D" : "#F0D1BD"), ["OnRankBronze"] = Color.FromArgb(dark ? "#FFE5D5" : "#492613"),
            ["Scrim"] = Color.FromArgb(dark ? "#FF000000" : "#FF101114"),
            ["InverseSurface"] = Color.FromArgb("#313033"), ["InverseOnSurface"] = Color.FromArgb("#F4EFF4"),
            ["Star"] = dark ? Color.FromArgb("#F2C66D") : Color.FromArgb("#765A00")
        };
        foreach (var (key, value) in colors) app.Resources[key] = value;
        if (restart && app.Windows.FirstOrDefault() is { } window) window.Page = new AppShell();
    }

    private static Color DynamicPrimary(bool dark) => DynamicRole(dark ? "system_accent1_200" : "system_accent1_600", dark ? Color.FromArgb("#D0BCFF") : Color.FromArgb("#6750A4"));

    private static Color DynamicRole(string resource, Color fallback)
    {
#if ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                var context = Android.App.Application.Context;
                var id = context.Resources?.GetIdentifier(resource, "color", "android") ?? 0;
                if (id != 0) return Color.FromArgb($"#{unchecked((uint)context.GetColor(id)):X8}");
            }
        }
        catch { }
#endif
        return fallback;
    }

    public static async Task Choose(Application app)
    {
        var options = new[] { "Sistem varsayılanı", "Açık tema", "Koyu tema" };
        var choice = await Application.Current!.Windows.First().Page!.DisplayActionSheetAsync("Görünüm", "Vazgeç", null, options);
        if (choice is null or "Vazgeç") return;
        Apply(app, choice switch { "Açık tema" => "light", "Koyu tema" => "dark", _ => "system" });
    }
}

