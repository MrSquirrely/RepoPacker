using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.ML.Tokenizers;
using Microsoft.Win32;

namespace RepoPacker.UI;

public partial class MainViewModel : ObservableObject {
    #region Fields & Dependencies

    private static readonly Lazy<TiktokenTokenizer> TokenizerInstance = new Lazy<TiktokenTokenizer>(
        () => TiktokenTokenizer.CreateForModel("gpt-4o"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly RepoPackerService _packerService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isInitialized;

    #endregion

    #region Observable Properties - Repository & Output

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

    #endregion

    #region Observable Properties - Execution State & Metrics

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

    #endregion

    #region Observable Properties - Presets & Exclusions

    public ObservableCollection<PackingPreset> Presets { get; }

    [ObservableProperty]
    private PackingPreset _selectedPreset;

    [ObservableProperty]
    private string _excludedDirectoriesInput = string.Empty;

    [ObservableProperty]
    private string _excludedExtensionsInput = string.Empty;

    [ObservableProperty]
    private int _maxFileSizeKb = 500;

    #endregion

    #region Observable Properties - Themes Management

    public ObservableCollection<ThemeDefinition> InstalledThemes { get; }

    [ObservableProperty]
    private ThemeDefinition _selectedTheme;

    #endregion

    #region Constructor & Initialization

    public MainViewModel() {
        _packerService = new RepoPackerService();

        // 1. Load Presets from %APPDATA%\RepoPacker\Presets\
        List<PackingPreset> loadedPresets = PresetService.LoadPresets();
        Presets = new ObservableCollection<PackingPreset>(loadedPresets);
        _selectedPreset = Presets.Count > 0
            ? Presets[0]
            : new PackingPreset("Custom", "Custom", ".git", ".dll", isCustom: true, isDefault: false);
        ApplyPreset(_selectedPreset);

        // 2. Load Installed Themes from %APPDATA%\RepoPacker\Themes\
        List<ThemeDefinition> loadedThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes = new ObservableCollection<ThemeDefinition>(loadedThemes);
        _selectedTheme = InstalledThemes.Count > 0 ? InstalledThemes[0] : new ThemeDefinition();

        // 3. Load Saved Persistent Settings
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

        // Restore DPAPI Protected Token
        _rememberToken = savedSettings.RememberToken;
        if (_rememberToken && !string.IsNullOrWhiteSpace(savedSettings.EncryptedToken)) {
            PersonalAccessToken = SettingsService.DecryptToken(savedSettings.EncryptedToken);
        }

        // 4. Mark initialized to enable safe persistence on subsequent changes
        _isInitialized = true;
    }

    #endregion

    #region Property Change Handlers

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
            DeleteCurrentPresetCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnRememberTokenChanged(bool value) {
        PersistCurrentSettings();
    }

    #endregion

    #region Commands - Preset Management

    [RelayCommand]
    private void SaveCurrentPreset() {
        if (SelectedPreset == null) {
            return;
        }

        SelectedPreset.ExcludedDirectoriesText = ExcludedDirectoriesInput.Trim();
        SelectedPreset.ExcludedExtensionsText = ExcludedExtensionsInput.Trim();
        PresetService.SavePreset(SelectedPreset);
        string fileName = PresetService.GetValidFileName(SelectedPreset.Name) + ".json";
        StatusMessage = $"Saved preset '{SelectedPreset.Name}' to {fileName}";
    }

    [RelayCommand]
    private void CreateNewPreset() {
        string newName = $"Custom Profile {Presets.Count + 1}";
        PackingPreset newPreset = new PackingPreset(
            newName,
            "Custom",
            ExcludedDirectoriesInput.Trim(),
            ExcludedExtensionsInput.Trim(),
            isCustom: true,
            isDefault: false);

        Presets.Add(newPreset);
        SelectedPreset = newPreset;
        PresetService.SavePreset(newPreset);
        string fileName = PresetService.GetValidFileName(newName) + ".json";
        StatusMessage = $"Created and saved '{fileName}'";
    }

    private bool CanDeleteCurrentPreset() {
        return SelectedPreset != null && !SelectedPreset.IsDefault;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentPreset))]
    private void DeleteCurrentPreset() {
        if (SelectedPreset == null || SelectedPreset.IsDefault) {
            return;
        }

        string presetNameToDelete = SelectedPreset.Name;
        bool deleted = PresetService.DeletePreset(presetNameToDelete);

        if (deleted) {
            Presets.Remove(SelectedPreset);
            SelectedPreset = Presets.FirstOrDefault(p => p.IsDefault)
                             ?? Presets.FirstOrDefault()
                             ?? new PackingPreset("Custom", "Custom", ".git", ".dll", isCustom: true, isDefault: false);
            ApplyPreset(SelectedPreset);
            PersistCurrentSettings();
            StatusMessage = $"Uninstalled preset '{presetNameToDelete}'";
        }
        else {
            StatusMessage = $"Unable to uninstall preset '{presetNameToDelete}'";
        }
    }

    [RelayCommand]
    private void OpenPresetsFolder() {
        PresetService.OpenPresetsFolder();
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
                        PresetService.SavePreset(existing);
                        updatedCount++;
                    }
                    else {
                        imported.IsCustom = true;
                        Presets.Add(imported);
                        PresetService.SavePreset(imported);
                        addedCount++;
                    }
                }

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

    #endregion

    #region Commands - Theme Management

    [RelayCommand]
    private void OpenThemesFolder() {
	    ProcessStartInfo startInfo = new ProcessStartInfo {
		    FileName = Path.Combine(
			    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			    "RepoPacker"),
		    UseShellExecute = true
	    };
	    Process.Start(startInfo);
    }

    #endregion

    #region Commands - Community Marketplace

    [RelayCommand]
    private async Task OpenMarketplaceAsync() {
        string token = !string.IsNullOrWhiteSpace(PersonalAccessToken)
            ? PersonalAccessToken
            : SettingsService.DecryptToken(SettingsService.LoadSettings().EncryptedToken);

        string currentPresetName = SelectedPreset?.Name ?? string.Empty;
        string currentThemeId = SelectedTheme?.Id ?? string.Empty;

        MarketplaceWindow dialog = new MarketplaceWindow(token) {
            Owner = Application.Current.MainWindow
        };

        MarketplaceViewModel? vm = dialog.DataContext as MarketplaceViewModel;
        if (vm != null) {
            await vm.RefreshMarketplaceAsync();
        }

        dialog.ShowDialog();

        // 1. Refresh Presets and maintain selection
        string targetPresetName = vm?.LastInstalledPresetName ?? currentPresetName;
        List<PackingPreset> freshPresets = PresetService.LoadPresets();
        Presets.Clear();
        foreach (PackingPreset preset in freshPresets) {
            Presets.Add(preset);
        }

        PackingPreset? matchedPreset = Presets.FirstOrDefault(p => p.Name.Equals(targetPresetName, StringComparison.OrdinalIgnoreCase))
                                       ?? Presets.FirstOrDefault(p => p.Name.Equals(currentPresetName, StringComparison.OrdinalIgnoreCase))
                                       ?? Presets.FirstOrDefault();
        if (matchedPreset != null) {
            SelectedPreset = matchedPreset;
            ApplyPreset(matchedPreset);
        }

        // 2. Refresh Themes and maintain selection
        string targetThemeId = vm?.LastInstalledThemeId ?? currentThemeId;
        List<ThemeDefinition> freshThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes.Clear();
        foreach (ThemeDefinition theme in freshThemes) {
            InstalledThemes.Add(theme);
        }

        ThemeDefinition? matchedTheme = InstalledThemes.FirstOrDefault(t => t.Id.Equals(targetThemeId, StringComparison.OrdinalIgnoreCase))
                                        ?? InstalledThemes.FirstOrDefault(t => t.Id.Equals(currentThemeId, StringComparison.OrdinalIgnoreCase))
                                        ?? InstalledThemes.FirstOrDefault();
        if (matchedTheme != null) {
            SelectedTheme = matchedTheme;
            ThemeService.ApplyTheme(matchedTheme);
        }

        PersistCurrentSettings();
    }

    #endregion

    #region Commands - Repository Packing Pipeline

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

    [RelayCommand]
    private void Cancel() {
        if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested) {
            StatusMessage = "Cancelling...";
            _cancellationTokenSource.Cancel();
        }
    }

    #endregion

    #region Internal Packing & Tokenization Helpers

    private int CountTokensInFile(string filePath, CancellationToken cancellationToken) {
        TiktokenTokenizer tokenizer = TokenizerInstance.Value;
        int totalTokens = 0;
        const int bufferSize = 32768; // 32,768 chars = 64 KB, strictly beneath the 85 KB LOH threshold
        char[] buffer = ArrayPool<char>.Shared.Rent(bufferSize);

        try {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            using (StreamReader reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096)) {
                int charsRead;
                while ((charsRead = reader.Read(buffer, 0, bufferSize)) > 0) {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlySpan<char> chunkSpan = new ReadOnlySpan<char>(buffer, 0, charsRead);
                    totalTokens += tokenizer.CountTokens(chunkSpan);
                }
            }
        }
        finally {
            ArrayPool<char>.Shared.Return(buffer);
        }

        return totalTokens;
    }

    private static HashSet<string> ParseCsvToHashSet(string csv) {
        HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) {
            return set;
        }

        ReadOnlySpan<char> span = csv.AsSpan();
        foreach (Range range in span.SplitAny(',', ';', '\r', '\n')) {
            ReadOnlySpan<char> itemSpan = span[range].Trim();
            if (!itemSpan.IsEmpty) {
                set.Add(itemSpan.ToString());
            }
        }

        return set;
    }

    #endregion

    #region Settings & Persistence Helpers

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

    #endregion
}