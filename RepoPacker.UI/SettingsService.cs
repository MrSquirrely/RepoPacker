using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoPacker.UI;

public sealed class UserSettings {
    public string SelectedThemeId { get; set; } = "github-dark";
    public string SelectedPresetName { get; set; } = string.Empty;
    public bool RememberToken { get; set; }
    public string EncryptedToken { get; set; } = string.Empty;
}

public static class SettingsService {
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RepoPacker",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
    private static readonly byte[] Entropy = [.. "RepoPacker_DPAPI_Salt"u8];

    public static UserSettings LoadSettings() {
        try {
            if (!File.Exists(SettingsFilePath)) {
                return new UserSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            return settings ?? new UserSettings();
        }
        catch {
            return new UserSettings();
        }
    }

    public static void SaveSettings(UserSettings settings) {
        try {
            string? dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch {
            // Suppress non-critical write failures
        }
    }

    public static string EncryptToken(string plainTextToken) {
        if (string.IsNullOrWhiteSpace(plainTextToken)) {
            return string.Empty;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainTextToken);
        byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipherBytes);
    }

    public static string DecryptToken(string encryptedTokenBase64) {
        if (string.IsNullOrWhiteSpace(encryptedTokenBase64)) {
            return string.Empty;
        }

        try {
            byte[] cipherBytes = Convert.FromBase64String(encryptedTokenBase64);
            byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch {
            return string.Empty;
        }
    }
}