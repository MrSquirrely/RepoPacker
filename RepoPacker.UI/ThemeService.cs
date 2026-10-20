using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace RepoPacker.UI;

public static class ThemeService {
    public static readonly string ThemesDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RepoPacker",
        "Themes");

    private static readonly HashSet<string> DefaultThemeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "github-dark",
        "github-light",
        "github-dark-dimmed",
        "github-dark-high-contrast"
    };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public static bool IsDefaultTheme(string themeId) {
        return DefaultThemeIds.Contains(themeId.Trim());
    }

    public static List<ThemeDefinition> LoadInstalledThemes() {
        EnsureDefaultThemesExist();

        List<ThemeDefinition> themes = new List<ThemeDefinition>();
        string[] files = Directory.GetFiles(ThemesDirectory, "*.json");

        foreach (string file in files) {
            try {
                string json = File.ReadAllText(file);
                ThemeDefinition? theme = JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions);
                if ((theme != null) && !string.IsNullOrWhiteSpace(theme.DisplayName)) {
                    theme.IsDefault = IsDefaultTheme(theme.Id);
                    themes.Add(theme);
                }
            }
            catch {
                // Ignore malformed custom theme files
            }
        }

        if (themes.Count == 0) {
            themes.Add(CreateGitHubDark());
        }

        return themes;
    }

    public static void SaveTheme(ThemeDefinition theme) {
        if (!Directory.Exists(ThemesDirectory)) {
            Directory.CreateDirectory(ThemesDirectory);
        }

        theme.IsDefault = IsDefaultTheme(theme.Id);
        string themeId = string.IsNullOrWhiteSpace(theme.Id) ? theme.DisplayName : theme.Id;
        string safeFileName = PresetService.GetValidFileName(themeId) + ".json";
        string filePath = Path.Combine(ThemesDirectory, safeFileName);
        string json = JsonSerializer.Serialize(theme, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static bool DeleteTheme(ThemeDefinition theme) {
        if (IsDefaultTheme(theme.Id)) {
            return false;
        }

        try {
            string themeId = string.IsNullOrWhiteSpace(theme.Id) ? theme.DisplayName : theme.Id;
            string safeFileName = PresetService.GetValidFileName(themeId) + ".json";
            string fullPath = Path.Combine(ThemesDirectory, safeFileName);
            if (File.Exists(fullPath)) {
                File.Delete(fullPath);
                return true;
            }

            foreach (string file in Directory.EnumerateFiles(ThemesDirectory, "*.json")) {
                try {
                    string json = File.ReadAllText(file);
                    ThemeDefinition? t = JsonSerializer.Deserialize<ThemeDefinition>(json, JsonOptions);
                    if (t != null && (t.Id.Equals(theme.Id, StringComparison.OrdinalIgnoreCase) ||
                                     t.DisplayName.Equals(theme.DisplayName, StringComparison.OrdinalIgnoreCase))) {
                        File.Delete(file);
                        return true;
                    }
                }
                catch {
                    // Skip unreadable files
                }
            }
        }
        catch {
            return false;
        }

        return false;
    }

    public static void ApplyTheme(ThemeDefinition? theme) {
        if ((theme == null) || (Application.Current == null)) {
            return;
        }

        ResourceDictionary resources = Application.Current.Resources;
        SetDynamicBrush(resources, "CanvasBrush", theme.CanvasColor);
        SetDynamicBrush(resources, "CardBrush", theme.CardColor);
        SetDynamicBrush(resources, "TitleBarBrush", theme.TitleBarColor);
        SetDynamicBrush(resources, "BorderBrush", theme.BorderColor);
        SetDynamicBrush(resources, "TextPrimaryBrush", theme.TextPrimaryColor);
        SetDynamicBrush(resources, "TextSecondaryBrush", theme.TextSecondaryColor);
        SetDynamicBrush(resources, "InputBgBrush", theme.InputBgColor);
        SetDynamicBrush(resources, "BtnSecondaryBgBrush", theme.BtnSecondaryBgColor);
        SetDynamicBrush(resources, "BtnSecondaryHoverBrush", theme.BtnSecondaryHoverColor);
        SetDynamicBrush(resources, "ItemHoverBrush", theme.ItemHoverColor);
        SetDynamicBrush(resources, "ItemSelectedBrush", theme.ItemSelectedColor);
        SetDynamicBrush(resources, "TitleBtnHoverBrush", theme.TitleBtnHoverColor);
    }

    public static void OpenThemesFolder() {
        if (!Directory.Exists(ThemesDirectory)) {
            Directory.CreateDirectory(ThemesDirectory);
        }

        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = ThemesDirectory,
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private static void SetDynamicBrush(ResourceDictionary resources, string key, string hexCode) {
        try {
            Color color = (Color)ColorConverter.ConvertFromString(hexCode);
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
        catch {
            // Retain existing brush if color syntax fails
        }
    }

    private static void EnsureDefaultThemesExist() {
        if (!Directory.Exists(ThemesDirectory)) {
            Directory.CreateDirectory(ThemesDirectory);
        }

        EnsureThemeFile("github-dark.json", CreateGitHubDark);
        EnsureThemeFile("github-light.json", CreateGitHubLight);
        EnsureThemeFile("github-dark-dimmed.json", CreateGitHubDarkDimmed);
        EnsureThemeFile("github-dark-high-contrast.json", CreateGitHubHighContrast);
    }

    private static void EnsureThemeFile(string fileName, Func<ThemeDefinition> factory) {
        string path = Path.Combine(ThemesDirectory, fileName);
        if (File.Exists(path)) {
            return;
        }

        ThemeDefinition theme = factory();
        string json = JsonSerializer.Serialize(theme, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static ThemeDefinition CreateGitHubDark() => new ThemeDefinition {
        Id = "github-dark",
        DisplayName = "GitHub Dark",
        IsDefault = true,
        CanvasColor = "#0D1117",
        CardColor = "#161B22",
        TitleBarColor = "#161B22",
        BorderColor = "#30363D",
        TextPrimaryColor = "#E6EDF3",
        TextSecondaryColor = "#8B949E",
        InputBgColor = "#010409",
        BtnSecondaryBgColor = "#21262D",
        BtnSecondaryHoverColor = "#30363D",
        ItemHoverColor = "#21262D",
        ItemSelectedColor = "#1F6FEB",
        TitleBtnHoverColor = "#30363D"
    };

    private static ThemeDefinition CreateGitHubLight() => new ThemeDefinition {
        Id = "github-light",
        DisplayName = "GitHub Light",
        IsDefault = true,
        CanvasColor = "#F6F8FA",
        CardColor = "#FFFFFF",
        TitleBarColor = "#FFFFFF",
        BorderColor = "#D0D7DE",
        TextPrimaryColor = "#1F2328",
        TextSecondaryColor = "#656D76",
        InputBgColor = "#FFFFFF",
        BtnSecondaryBgColor = "#F6F8FA",
        BtnSecondaryHoverColor = "#F3F4F6",
        ItemHoverColor = "#EAEEF2",
        ItemSelectedColor = "#0969DA",
        TitleBtnHoverColor = "#EAEEF2"
    };

    private static ThemeDefinition CreateGitHubDarkDimmed() => new ThemeDefinition {
        Id = "github-dark-dimmed",
        DisplayName = "GitHub Dark Dimmed",
        IsDefault = true,
        CanvasColor = "#22272E",
        CardColor = "#2D333B",
        TitleBarColor = "#2D333B",
        BorderColor = "#444C56",
        TextPrimaryColor = "#ADBAC7",
        TextSecondaryColor = "#768390",
        InputBgColor = "#1C2128",
        BtnSecondaryBgColor = "#373E47",
        BtnSecondaryHoverColor = "#444C56",
        ItemHoverColor = "#373E47",
        ItemSelectedColor = "#316DCA",
        TitleBtnHoverColor = "#444C56"
    };

    private static ThemeDefinition CreateGitHubHighContrast() => new ThemeDefinition {
        Id = "github-dark-high-contrast",
        DisplayName = "GitHub High Contrast",
        IsDefault = true,
        CanvasColor = "#010409",
        CardColor = "#0D1117",
        TitleBarColor = "#0D1117",
        BorderColor = "#7A828E",
        TextPrimaryColor = "#FFFFFF",
        TextSecondaryColor = "#F0F6FC",
        InputBgColor = "#010409",
        BtnSecondaryBgColor = "#21262D",
        BtnSecondaryHoverColor = "#30363D",
        ItemHoverColor = "#30363D",
        ItemSelectedColor = "#255AB0",
        TitleBtnHoverColor = "#30363D"
    };
}