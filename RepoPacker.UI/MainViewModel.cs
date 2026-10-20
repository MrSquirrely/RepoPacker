using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.ML.Tokenizers;
using Microsoft.Win32;

namespace RepoPacker.UI;

public partial class MainViewModel : ObservableObject {
    private readonly RepoPackerService _packerService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isInitialized;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackRepositoryCommand))]
    private string _repositoryUrl = string.Empty;

    [ObservableProperty]
    private string _personalAccessToken = string.Empty;

    [ObservableProperty]
    private bool _rememberToken;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackRepositoryCommand))]
    private string _outputFilePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _tokenCount;

    [ObservableProperty]
    private string _tokenCountDisplay = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool _isIndeterminate;

    // Presets Management
    public ObservableCollection<PackingPreset> Presets { get; }

    [ObservableProperty]
    private PackingPreset _selectedPreset;

    [ObservableProperty]
    private string _excludedDirectoriesInput = string.Empty;

    [ObservableProperty]
    private string _excludedExtensionsInput = string.Empty;

    [ObservableProperty]
    private int _maxFileSizeKb = 500;

    // Themes Management
    public ObservableCollection<ThemeDefinition> InstalledThemes { get; }

    [ObservableProperty]
    private ThemeDefinition _selectedTheme;

    public MainViewModel() {
        _packerService = new RepoPackerService();

        // 1. Load Presets
        List<PackingPreset> loadedPresets = PresetService.LoadPresets();
        Presets = new ObservableCollection<PackingPreset>(loadedPresets);
        _selectedPreset = Presets.Count > 0 ? Presets[0] : new PackingPreset("Custom", "Custom", ".git", ".dll", true);
        ApplyPreset(_selectedPreset);

        // 2. Load Themes
        List<ThemeDefinition> loadedThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes = new ObservableCollection<ThemeDefinition>(loadedThemes);
        _selectedTheme = InstalledThemes.Count > 0 ? InstalledThemes[0] : new ThemeDefinition();

        // 3. Load Saved Settings
        UserSettings savedSettings = SettingsService.LoadSettings();

        // Restore Theme
        if (!string.IsNullOrWhiteSpace(savedSettings.SelectedThemeId)) {
            ThemeDefinition? matchedTheme = InstalledThemes.FirstOrDefault(
                t => t.Id.Equals(savedSettings.SelectedThemeId, StringComparison.OrdinalIgnoreCase));
            if (matchedTheme != null) {
                _selectedTheme = matchedTheme;
            }
        }
        ThemeService.ApplyTheme(_selectedTheme);

        // Restore Preset
        if (!string.IsNullOrWhiteSpace(savedSettings.SelectedPresetName)) {
            PackingPreset? matchedPreset = Presets.FirstOrDefault(
                p => p.Name.Equals(savedSettings.SelectedPresetName, StringComparison.OrdinalIgnoreCase));
            if (matchedPreset != null) {
                _selectedPreset = matchedPreset;
                ApplyPreset(_selectedPreset);
            }
        }

        // Restore Token
        _rememberToken = savedSettings.RememberToken;
        if (_rememberToken && !string.IsNullOrWhiteSpace(savedSettings.EncryptedToken)) {
            PersonalAccessToken = SettingsService.DecryptToken(savedSettings.EncryptedToken);
        }

        // 4. Mark initialized to enable safe persistence on subsequent user changes
        _isInitialized = true;
    }

    partial void OnSelectedThemeChanged(ThemeDefinition value) {
        if (value != null) {
            ThemeService.ApplyTheme(value);
            PersistCurrentSettings();
        }
    }

    partial void OnSelectedPresetChanged(PackingPreset value) {
        if (value != null) {
            ApplyPreset(value);
            PersistCurrentSettings();
        }
    }

    partial void OnRememberTokenChanged(bool value) {
        PersistCurrentSettings();
    }

    private void ApplyPreset(PackingPreset preset) {
        ExcludedDirectoriesInput = preset.ExcludedDirectoriesText;
        ExcludedExtensionsInput = preset.ExcludedExtensionsText;
    }

    private void PersistCurrentSettings() {
        if (!_isInitialized) {
            return;
        }

        UserSettings settings = new UserSettings {
            SelectedThemeId = SelectedTheme?.Id ?? "github-dark",
            SelectedPresetName = SelectedPreset?.Name ?? string.Empty,
            RememberToken = RememberToken,
            EncryptedToken = RememberToken ? SettingsService.EncryptToken(PersonalAccessToken) : string.Empty
        };

        SettingsService.SaveSettings(settings);
    }

    [RelayCommand]
    private void SaveCurrentPreset() {
        if (SelectedPreset == null) {
            return;
        }

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
        _cancellationTokenSource = new CancellationTokenSource();

        if (RememberToken) {
            PersistCurrentSettings();
        }

        IProgress<string> progressReporter = new Progress<string>(message => {
            StatusMessage = message;
        });

        HashSet<string> excludedDirs = ParseCsvToHashSet(ExcludedDirectoriesInput);
        excludedDirs.Add(".git");

        HashSet<string> excludedExts = ParseCsvToHashSet(ExcludedExtensionsInput);

        long maxFileSizeBytes = MaxFileSizeKb > 0 ? (long)MaxFileSizeKb * 1024L : 0L;

        try {
            string destination = OutputFilePath.Trim();

            await _packerService.ProcessRepositoryAsync(
                RepositoryUrl.Trim(),
                PersonalAccessToken.Trim(),
                destination,
                excludedDirs,
                excludedExts,
                maxFileSizeBytes,
                progressReporter,
                _cancellationTokenSource.Token);

            progressReporter.Report("Calculating token count...");

            int calculatedTokens = await Task.Run(
                () => CountTokensInFile(destination, _cancellationTokenSource.Token),
                _cancellationTokenSource.Token);

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
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
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
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested) {
            StatusMessage = "Cancelling...";
            _cancellationTokenSource.Cancel();
        }
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
}