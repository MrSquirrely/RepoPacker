using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace RepoPacker.UI;

public static class PresetService {
    public static readonly string PresetsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RepoPacker",
        "presets.json");

    public static List<PackingPreset> LoadPresets() {
        try {
            if (File.Exists(PresetsFilePath)) {
                string json = File.ReadAllText(PresetsFilePath);
                List<PackingPreset>? loaded = JsonSerializer.Deserialize<List<PackingPreset>>(json);
                if (loaded is { Count: > 0 }) {
                    return loaded;
                }
            }
        }
        catch {
            // Fall back to defaults on parsing error
        }

        List<PackingPreset> defaults = GetDefaultPresets();
        SavePresets(defaults);
        return defaults;
    }

    public static void SavePresets(IEnumerable<PackingPreset> presets) {
        try {
            string? directory = Path.GetDirectoryName(PresetsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PresetsFilePath, json);
        }
        catch {
            // Suppress non-critical write failures
        }
    }

    public static void ExportPresetsToFile(string destinationPath, IEnumerable<PackingPreset> presets) {
        string json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(destinationPath, json);
    }

    public static List<PackingPreset> ImportPresetsFromFile(string sourcePath) {
        string json = File.ReadAllText(sourcePath);

        // Supports both multi-preset arrays and single-preset objects
        try {
            List<PackingPreset>? list = JsonSerializer.Deserialize<List<PackingPreset>>(json);
            if (list is { Count: > 0 }) {
                return list;
            }
        }
        catch (JsonException) {
            PackingPreset? single = JsonSerializer.Deserialize<PackingPreset>(json);
            if (single != null) {
                return [single];
            }
        }

        return [];
    }

    public static void OpenPresetsFile() {
        if (!File.Exists(PresetsFilePath)) {
            SavePresets(GetDefaultPresets());
        }

        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = PresetsFilePath,
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private static List<PackingPreset> GetDefaultPresets() {
        return [
	        new PackingPreset("C# - ASP.NET Core (API / Blazor)", "C#",
	                          "bin, obj, .vs, .vscode, TestResults, node_modules",
	                          ".dll, .pdb, .exe, .cache, .user, .suo, .log, .min.js, .min.css"),
	        new PackingPreset("C# - WPF / WinForms Desktop", "C#", "bin, obj, .vs, .vscode, TestResults, x64, x86",
	                          ".dll, .pdb, .exe, .cache, .user, .suo, .baml, .g.cs"),
	        new PackingPreset("C# - Class Library / CLI Tool", "C#",
	                          "bin, obj, .vs, .vscode, TestResults, BenchmarkDotNet.Artifacts",
	                          ".dll, .pdb, .exe, .cache, .user, .suo, .nupkg"),
	        new PackingPreset("Java - Spring Boot REST API", "Java",
	                          "target, build, .gradle, .idea, .mvn, bin, out, logs",
	                          ".class, .jar, .war, .ear, .iml, .log, .hprof"),
	        new PackingPreset("Java - Android Application", "Java", "build, .gradle, .idea, captures, .cxx",
	                          ".apk, .aab, .dex, .class, .so, .aar, .iml"),
	        new PackingPreset("Java - Maven / Gradle CLI", "Java", "target, build, .gradle, .idea, .settings, bin",
	                          ".class, .jar, .iml, .classpath, .project, .log"),
	        new PackingPreset("JavaScript - React / Next.js / Vite", "JavaScript",
	                          "node_modules, .next, dist, build, .cache, coverage",
	                          ".min.js, .min.css, .map, .tsbuildinfo, .lock, package-lock.json"),
	        new PackingPreset("JavaScript - Node.js Express / NestJS", "JavaScript",
	                          "node_modules, dist, build, coverage, logs, .cache",
	                          ".map, .tsbuildinfo, .lock, .log, .sqlite, package-lock.json"),
	        new PackingPreset("JavaScript - Vue / Angular SPA", "JavaScript",
	                          "node_modules, dist, .angular, coverage, .cache",
	                          ".min.js, .min.css, .map, .tsbuildinfo, .lock, .log"),
	        new PackingPreset("Python - FastAPI / Django / Flask", "Python",
	                          "__pycache__, .venv, venv, env, .pytest_cache, staticfiles",
	                          ".pyc, .pyo, .pyd, .sqlite3, .db, .log, poetry.lock"),
	        new PackingPreset("Python - Data Science & ML", "Python",
	                          "__pycache__, .venv, venv, .ipynb_checkpoints, runs, wandb, data",
	                          ".pyc, .pt, .pth, .onnx, .h5, .pkl, .parquet, .csv, .npy"),
	        new PackingPreset("Python - CLI & Library", "Python",
	                          "__pycache__, .venv, venv, build, dist, *.egg-info, .pytest_cache",
	                          ".pyc, .pyo, .pyd, .whl, .egg, .log"),
	        new PackingPreset("Rust - Cargo Binary / Crate", "Rust", "target, .cargo",
	                          ".lock, .rlib, .rmeta, .pdb, Cargo.lock"),
	        new PackingPreset("Go - Module / Backend", "Go", "vendor, bin", ".exe, .test, .out, go.sum"),
	        new PackingPreset("Custom (User Defined)", "Custom", ".git, .vs, bin, obj, node_modules",
	                          ".dll, .exe, .pdb, .bin, .zip, .png, .jpg", isCustom: true)
        ];
    }
}