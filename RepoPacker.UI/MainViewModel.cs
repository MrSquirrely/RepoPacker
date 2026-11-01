using System;
using System.Buffers;
using System.Collections.Concurrent;
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

    private const string UtilizationGreen = "#3FB950";
    private const string UtilizationAmber = "#D29922";
    private const string UtilizationRed = "#F85149";
    private const double UtilizationAmberThreshold = 60.0;
    private const double UtilizationRedThreshold = 85.0;

    private static readonly ConcurrentDictionary<string, TiktokenTokenizer> TokenizerCache =
	    new ConcurrentDictionary<string, TiktokenTokenizer>(StringComparer.OrdinalIgnoreCase);

    private readonly RepoPackerService _packerService;
    private readonly Dictionary<string, int> _tokenCountsByModel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _lastOutputFilePath;
    private bool _isInitialized;

    #endregion

    #region Observable Properties - Repository & Output

    public ObservableCollection<ContextBudgetProfile> BudgetProfiles { get; }

    [ObservableProperty]
    private ContextBudgetProfile _selectedBudgetProfile;

    [ObservableProperty]
    private double _contextUtilizationPercentage;

    [ObservableProperty]
    private string _contextUtilizationDisplay = string.Empty;

    [ObservableProperty]
    private string _contextUtilizationColor = UtilizationGreen;

    [ObservableProperty]
    private bool _hasContextUtilization;

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
    [NotifyCanExecuteChangedFor(nameof(PackRepositoryCommand))]
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

        List<ContextBudgetProfile> defaultProfiles = new List<ContextBudgetProfile> {
	        new ContextBudgetProfile("GPT-4o (128k)", 128_000, "gpt-4o"),
	        new ContextBudgetProfile("GPT-4.1 (1M)", 1_000_000, "gpt-4o"),
	        new ContextBudgetProfile("GPT-4 Turbo (128k)", 128_000, "gpt-4"),
	        new ContextBudgetProfile("Claude 3.5 / 3.7 Sonnet (200k)", 200_000, "gpt-4o"),
	        new ContextBudgetProfile("o1 / o3-mini (200k)", 200_000, "gpt-4o"),
	        new ContextBudgetProfile("DeepSeek V3 / R1 (64k)", 64_000, "gpt-4o"),
	        new ContextBudgetProfile("Gemini 1.5 / 2.5 Flash (1M)", 1_000_000, "gpt-4o"),
	        new ContextBudgetProfile("Llama 3.3 (128k)", 128_000, "gpt-4o")
        };

        BudgetProfiles = new ObservableCollection<ContextBudgetProfile>(defaultProfiles);
        _selectedBudgetProfile = BudgetProfiles[0];

        // Restore saved profile
        if (!string.IsNullOrWhiteSpace(savedSettings.SelectedBudgetProfileName)) {
	        ContextBudgetProfile? matchedProfile = BudgetProfiles.FirstOrDefault(
		        p => p.DisplayName.Equals(savedSettings.SelectedBudgetProfileName, StringComparison.OrdinalIgnoreCase));
	        if (matchedProfile != null) {
		        _selectedBudgetProfile = matchedProfile;
	        }
        }

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

    partial void OnSelectedBudgetProfileChanged(ContextBudgetProfile value) {
        if (value != null) {
            PersistCurrentSettings();
            _ = RefreshTokenCountForProfileAsync(value);
        }
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
        string newName = GetNextCustomPresetName();
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

    private string GetNextCustomPresetName() {
        int number = Presets.Count + 1;
        string candidate = $"Custom Profile {number}";
        while (Presets.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) {
            number++;
            candidate = $"Custom Profile {number}";
        }

        return candidate;
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

        if (dialog.DataContext is MarketplaceViewModel vm) {
            await vm.RefreshMarketplaceAsync();
        }

        dialog.ShowDialog();

        // 1. Refresh Presets and preserve the currently selected preset
        List<PackingPreset> freshPresets = PresetService.LoadPresets();
        Presets.Clear();
        foreach (PackingPreset preset in freshPresets) {
            Presets.Add(preset);
        }

        PackingPreset? matchedPreset = Presets.FirstOrDefault(p => p.Name.Equals(currentPresetName, StringComparison.OrdinalIgnoreCase))
                                       ?? Presets.FirstOrDefault();
        if (matchedPreset != null) {
            SelectedPreset = matchedPreset;
            ApplyPreset(matchedPreset);
        }

        // 2. Refresh Themes and preserve the currently selected theme
        List<ThemeDefinition> freshThemes = ThemeService.LoadInstalledThemes();
        InstalledThemes.Clear();
        foreach (ThemeDefinition theme in freshThemes) {
            InstalledThemes.Add(theme);
        }

        ThemeDefinition? matchedTheme = InstalledThemes.FirstOrDefault(t => t.Id.Equals(currentThemeId, StringComparison.OrdinalIgnoreCase))
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
        ClearTokenMetrics();
        _tokenCountsByModel.Clear();
        _lastOutputFilePath = null;
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

            _lastOutputFilePath = destination;

            progressReporter.Report("Calculating token count...");

            int calculatedTokens = await GetTokenCountAsync(destination, SelectedBudgetProfile.TokenizerModel, _cancellationTokenSource.Token);

            ApplyTokenCount(calculatedTokens);
            StatusMessage = $"Completed ({TokenCountDisplay}) — saved to: {Path.GetFileName(destination)}";
        }
        catch (OperationCanceledException) {
            StatusMessage = "Operation cancelled by user.";
            ClearTokenMetrics();
        }
        catch (Exception ex) {
            StatusMessage = $"Error: {ex.Message}";
            ClearTokenMetrics();
        }
        finally {
            IsBusy = false;
            IsIndeterminate = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task RefreshTokenCountForProfileAsync(ContextBudgetProfile profile) {
        if (_lastOutputFilePath == null) {
            return;
        }

        if (_tokenCountsByModel.TryGetValue(profile.TokenizerModel, out int cachedTokens)) {
            ApplyTokenCount(cachedTokens);
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ClearTokenMetrics();
        StatusMessage = $"Counting tokens for {profile.DisplayName}...";
        _cancellationTokenSource = new CancellationTokenSource();

        try {
            int calculatedTokens = await GetTokenCountAsync(_lastOutputFilePath, profile.TokenizerModel, _cancellationTokenSource.Token);
            ApplyTokenCount(calculatedTokens);
            StatusMessage = $"Counted {TokenCountDisplay} for {profile.DisplayName}";
        }
        catch (OperationCanceledException) {
            StatusMessage = "Operation cancelled by user.";
        }
        catch (Exception ex) {
            StatusMessage = $"Error: {ex.Message}";
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

    private async Task<int> GetTokenCountAsync(string filePath, string tokenizerModel, CancellationToken cancellationToken) {
        if (_tokenCountsByModel.TryGetValue(tokenizerModel, out int cachedTokens)) {
            return cachedTokens;
        }

        int calculatedTokens = await Task.Run(
            () => CountTokensInFile(filePath, tokenizerModel, cancellationToken),
            cancellationToken);

        _tokenCountsByModel[tokenizerModel] = calculatedTokens;
        return calculatedTokens;
    }

    private void ApplyTokenCount(int tokenCount) {
        TokenCount = tokenCount;
        TokenCountDisplay = $"{tokenCount:N0} tokens";
        UpdateContextUtilization();
    }

    private void ClearTokenMetrics() {
        TokenCount = 0;
        TokenCountDisplay = string.Empty;
        UpdateContextUtilization();
    }

    private void UpdateContextUtilization() {
        if (TokenCount <= 0 || SelectedBudgetProfile == null) {
            HasContextUtilization = false;
            ContextUtilizationPercentage = 0;
            ContextUtilizationDisplay = string.Empty;
            ContextUtilizationColor = UtilizationGreen;
            return;
        }

        int maxTokens = SelectedBudgetProfile.MaxTokens;
        double percentage = TokenCount * 100.0 / maxTokens;

        ContextUtilizationPercentage = percentage;
        ContextUtilizationDisplay = $"{TokenCount:N0} / {maxTokens:N0} tokens ({percentage:F1}%)";
        ContextUtilizationColor = percentage > UtilizationRedThreshold
            ? UtilizationRed
            : percentage >= UtilizationAmberThreshold
                ? UtilizationAmber
                : UtilizationGreen;
        HasContextUtilization = true;
    }

    private static int CountTokensInFile(string filePath, string tokenizerModel, CancellationToken cancellationToken) {
        TiktokenTokenizer tokenizer = TokenizerCache.GetOrAdd(tokenizerModel, static model => TiktokenTokenizer.CreateForModel(model));
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
            SelectedBudgetProfileName = SelectedBudgetProfile?.DisplayName ?? string.Empty,
            RememberToken = RememberToken,
            EncryptedToken = RememberToken ? SettingsService.EncryptToken(PersonalAccessToken) : string.Empty
        };

        SettingsService.SaveSettings(settings);
    }

    #endregion
}