using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.ML.Tokenizers;
using Microsoft.Win32;

namespace RepoPacker.UI;

public partial class MainViewModel : ObservableObject {
    private readonly RepoPackerService packerService;
    private CancellationTokenSource? cancellationTokenSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackRepositoryCommand))]
    private string repositoryUrl = string.Empty;

    [ObservableProperty]
    private string personalAccessToken = string.Empty;

    [ObservableProperty]
    private bool rememberToken;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackRepositoryCommand))]
    private string outputFilePath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private int tokenCount;

    [ObservableProperty]
    private string tokenCountDisplay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool isIndeterminate;

    // Presets Management
    public ObservableCollection<PackingPreset> Presets { get; }

    [ObservableProperty]
    private PackingPreset selectedPreset;

    [ObservableProperty]
    private string excludedDirectoriesInput = string.Empty;

    [ObservableProperty]
    private string excludedExtensionsInput = string.Empty;

    [ObservableProperty]
    private int maxFileSizeKb = 500;

    // Themes Management
    public ObservableCollection<ThemeDefinition> InstalledThemes { get; }

    [ObservableProperty]
    private ThemeDefinition selectedTheme;

    public MainViewModel() {
        packerService = new RepoPackerService();

        // Load JSON Presets
        List<PackingPreset> loadedPresets = PresetService.LoadPresets();
        Presets = new ObservableCollection<PackingPreset>(loadedPresets);
        selectedPreset = Presets.Count > 0 ? Presets[0] : new PackingPreset("Custom", "Custom", ".git", ".dll", true);
        ApplyPreset(selectedPreset);

        // Load Installed Themes
        List<ThemeDefinition> loadedThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes = new ObservableCollection<ThemeDefinition>(loadedThemes);

        // Restore Settings
        UserSettings savedSettings = SettingsService.LoadSettings();
        RememberToken = savedSettings.RememberToken;

        if (RememberToken && !string.IsNullOrWhiteSpace(savedSettings.EncryptedToken)) {
            PersonalAccessToken = SettingsService.DecryptToken(savedSettings.EncryptedToken);
        }

        // Restore active theme
        ThemeDefinition? matchedTheme = InstalledThemes.FirstOrDefault(t => t.Id.Equals(savedSettings.SelectedThemeId, StringComparison.OrdinalIgnoreCase));
        selectedTheme = matchedTheme ?? InstalledThemes[0];
        ThemeService.ApplyTheme(selectedTheme);

        // Restore active preset
        if (string.IsNullOrWhiteSpace(savedSettings.SelectedPresetName)) {
	        return;
        }

        PackingPreset? matchedPreset = Presets.FirstOrDefault(p => p.Name.Equals(savedSettings.SelectedPresetName, StringComparison.OrdinalIgnoreCase));
        if (matchedPreset != null) {
	        SelectedPreset = matchedPreset;
        }
    }

    partial void OnSelectedThemeChanged(ThemeDefinition? value) {
	    if (value == null) {
		    return;
	    }

	    ThemeService.ApplyTheme(value);
	    PersistCurrentSettings();
    }

    partial void OnSelectedPresetChanged(PackingPreset? value) {
	    if (value == null) {
		    return;
	    }

	    ApplyPreset(value);
	    PersistCurrentSettings();
    }

    private void ApplyPreset(PackingPreset preset) {
        ExcludedDirectoriesInput = preset.ExcludedDirectoriesText;
        ExcludedExtensionsInput = preset.ExcludedExtensionsText;
    }

    [RelayCommand]
    private void SaveCurrentPreset() {
	    SelectedPreset.ExcludedDirectoriesText = ExcludedDirectoriesInput.Trim();
        SelectedPreset.ExcludedExtensionsText = ExcludedExtensionsInput.Trim();
        PresetService.SavePresets(Presets);
        StatusMessage = $"Saved preset '{SelectedPreset.Name}' to presets.json";
    }

    [RelayCommand]
    private void CreateNewPreset() {
        string newName = $"Custom Profile {Presets.Count + 1}";
        PackingPreset newPreset = new PackingPreset(newName, "Custom", ExcludedDirectoriesInput.Trim(), ExcludedExtensionsInput.Trim(), isCustom: true);
        Presets.Add(newPreset);
        SelectedPreset = newPreset;
        PresetService.SavePresets(Presets);
        StatusMessage = $"Created and saved '{newName}' to presets.json";
    }

    [RelayCommand]
    private void OpenPresetsFolder() {
        PresetService.OpenPresetsFile();
    }

    [RelayCommand]
    private void OpenThemesFolder() {
        ThemeService.OpenThemesFolder();
    }

    [RelayCommand]
    private void ReloadThemes() {
        List<ThemeDefinition> freshThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes.Clear();
        foreach (ThemeDefinition theme in freshThemes) {
            InstalledThemes.Add(theme);
        }

        ThemeDefinition? fallback = InstalledThemes.FirstOrDefault(t => t.Id == SelectedTheme.Id) ?? InstalledThemes.FirstOrDefault();
        if (fallback != null) {
            SelectedTheme = fallback;
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnRememberTokenChanged(bool value) {
        PersistCurrentSettings();
    }

    private void PersistCurrentSettings() {
        UserSettings settings = new() {
            SelectedThemeId = SelectedTheme.Id,
            SelectedPresetName = SelectedPreset.Name,
            RememberToken = RememberToken,
            EncryptedToken = RememberToken ? SettingsService.EncryptToken(PersonalAccessToken) : string.Empty
        };

        SettingsService.SaveSettings(settings);
    }

    [RelayCommand]
    private void BrowseOutputFile() {
        SaveFileDialog dialog = new SaveFileDialog {
            Title = "Select Destination LLM Context File",
            Filter = "XML File (*.xml)|*.xml|Text File (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = ".xml",
            FileName = "repository_context.xml"
        };

        bool? result = dialog.ShowDialog();
        if (result == true) {
            OutputFilePath = dialog.FileName;
        }
    }

    private bool CanPackRepository() {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(RepositoryUrl) &&
               !string.IsNullOrWhiteSpace(OutputFilePath);
    }

    [RelayCommand(CanExecute = nameof(CanPackRepository))]
    private async Task PackRepositoryAsync() {
        IsBusy = true;
        IsIndeterminate = true;
        TokenCount = 0;
        TokenCountDisplay = string.Empty;
        cancellationTokenSource = new CancellationTokenSource();

        if (RememberToken) {
            PersistCurrentSettings();
        }

        IProgress<string> progressReporter = new Progress<string>(message => {
            StatusMessage = message;
        });

        HashSet<string> excludedDirs = ParseCsvToHashSet(ExcludedDirectoriesInput);
        excludedDirs.Add(".git");

        HashSet<string> excludedExts = ParseCsvToHashSet(ExcludedExtensionsInput);

        long maxFileSizeBytes = MaxFileSizeKb > 0 ? MaxFileSizeKb * 1024L : 0L;

        try {
            string destination = OutputFilePath.Trim();

            await packerService.ProcessRepositoryAsync(
                RepositoryUrl.Trim(),
                PersonalAccessToken.Trim(),
                destination,
                excludedDirs,
                excludedExts,
                maxFileSizeBytes,
                progressReporter,
                cancellationTokenSource.Token);

            progressReporter.Report("Calculating token count...");

            int calculatedTokens = await Task.Run(
                () => CountTokensInFile(destination, cancellationTokenSource.Token),
                cancellationTokenSource.Token);

            TokenCount = calculatedTokens;
            TokenCountDisplay = $"{calculatedTokens:N0} tokens";
            StatusMessage = $"Completed ({TokenCountDisplay}) — saved to: {Path.GetFileName(destination)}";
        }
        catch (OperationCanceledException) {
            StatusMessage = "Operation cancelled by user.";
            TokenCountDisplay = string.Empty;
        }
        catch (Exception ex) {
            StatusMessage = $"Error: {ex.Message}";
            TokenCountDisplay = string.Empty;
        }
        finally {
            IsBusy = false;
            IsIndeterminate = false;
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
        }
    }

    private HashSet<string> ParseCsvToHashSet(string csv) {
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) {
            return set;
        }

        string[] items = csv.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawItem in items) {
            string trimmed = rawItem.Trim();
            if (!string.IsNullOrEmpty(trimmed)) {
                set.Add(trimmed);
            }
        }

        return set;
    }

    [RelayCommand]
    private void Cancel() {
	    if (cancellationTokenSource is not { IsCancellationRequested: false }) {
		    return;
	    }

	    StatusMessage = "Cancelling...";
	    cancellationTokenSource.Cancel();
    }

    private int CountTokensInFile(string filePath, CancellationToken cancellationToken) {
        TiktokenTokenizer tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");
        int totalTokens = 0;
        const int bufferSize = 65536;
        char[] buffer = new char[bufferSize];

        using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (StreamReader reader = new StreamReader(fileStream, Encoding.UTF8)) {
            int charsRead;
            while ((charsRead = reader.Read(buffer, 0, buffer.Length)) > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                ReadOnlySpan<char> chunkSpan = new ReadOnlySpan<char>(buffer, 0, charsRead);
                totalTokens += tokenizer.CountTokens(chunkSpan);
            }
        }

        return totalTokens;
    }

    [RelayCommand]
    private void ExportPresets() {
        SaveFileDialog dialog = new SaveFileDialog {
            Title = "Export Presets Bundle",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = "repopacker-presets.json"
        };

        bool? result = dialog.ShowDialog();
        if (result == true) {
            try {
                PresetService.ExportPresetsToFile(dialog.FileName, Presets);
                StatusMessage = $"Exported {Presets.Count} presets to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex) {
                StatusMessage = $"Export error: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void ImportPresets() {
        OpenFileDialog dialog = new OpenFileDialog {
            Title = "Import Presets Bundle",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        bool? result = dialog.ShowDialog();
        if (result == true) {
            try {
                List<PackingPreset> importedPresets = PresetService.ImportPresetsFromFile(dialog.FileName);
                if (importedPresets.Count == 0) {
                    StatusMessage = "No valid presets found in the selected file.";
                    return;
                }

                int addedCount = 0;
                int updatedCount = 0;

                foreach (PackingPreset imported in importedPresets) {
                    PackingPreset? existing = Presets.FirstOrDefault(p => p.Name.Equals(imported.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) {
                        existing.ExcludedDirectoriesText = imported.ExcludedDirectoriesText;
                        existing.ExcludedExtensionsText = imported.ExcludedExtensionsText;
                        existing.Language = imported.Language;
                        existing.IsCustom = true;
                        updatedCount++;
                    }
                    else {
                        imported.IsCustom = true;
                        Presets.Add(imported);
                        addedCount++;
                    }
                }

                PresetService.SavePresets(Presets);

                PackingPreset? targetToSelect = importedPresets.LastOrDefault();
                if (targetToSelect != null) {
                    PackingPreset? matching = Presets.FirstOrDefault(p => p.Name.Equals(targetToSelect.Name, StringComparison.OrdinalIgnoreCase));
                    if (matching != null) {
                        SelectedPreset = matching;
                    }
                }

                StatusMessage = $"Import complete: {addedCount} added, {updatedCount} updated.";
            }
            catch (Exception ex) {
                StatusMessage = $"Import error: {ex.Message}";
            }
        }
    }
}