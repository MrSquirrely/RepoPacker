using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RepoPacker.UI;

public partial class MarketplaceViewModel : ObservableObject {
    private readonly MarketplaceService _marketplaceService;
    private readonly string _personalAccessToken;
    private List<MarketplaceItem> _allLoadedItems = new List<MarketplaceItem>();

    [ObservableProperty]
    private string _repositorySlug = "MrSquirrely/RepoPacker";

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _selectedTypeFilter = "All";

    public ObservableCollection<MarketplaceItem> DisplayedItems { get; } = new ObservableCollection<MarketplaceItem>();

    public MarketplaceViewModel(string personalAccessToken) {
        _marketplaceService = new MarketplaceService();
        _personalAccessToken = personalAccessToken;
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshMarketplaceAsync() {
        if (IsLoading) {
            return;
        }

        string[] parts = RepositorySlug.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            StatusMessage = "Repository must follow 'owner/repository' format.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Fetching discussions from GitHub...";

        try {
            _allLoadedItems = await _marketplaceService.FetchMarketplaceItemsAsync(parts[0], parts[1], _personalAccessToken, CancellationToken.None);
            ApplyFilter();
            StatusMessage = $"Loaded {_allLoadedItems.Count} community items.";
        }
        catch (Exception ex) {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void InstallItem(MarketplaceItem? item) {
        if (item == null || item.IsInstalled) {
            return;
        }

        try {
            _marketplaceService.InstallItem(item);
            StatusMessage = $"Installed '{item.Title}' successfully.";
        }
        catch (Exception ex) {
            StatusMessage = $"Failed to install '{item.Title}': {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDiscussion(MarketplaceItem? item) {
        if (item == null || string.IsNullOrWhiteSpace(item.DiscussionUrl)) {
            return;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo {
            FileName = item.DiscussionUrl,
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private void ApplyFilter() {
        DisplayedItems.Clear();

        IEnumerable<MarketplaceItem> query = _allLoadedItems;

        if (SelectedTypeFilter == "Presets") {
            query = query.Where(i => i.ItemType == MarketplaceItemType.Preset);
        }
        else if (SelectedTypeFilter == "Themes") {
            query = query.Where(i => i.ItemType == MarketplaceItemType.Theme);
        }

        if (!string.IsNullOrWhiteSpace(SearchFilter)) {
            query = query.Where(i => i.Title.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                     i.Author.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (MarketplaceItem item in query) {
            DisplayedItems.Add(item);
        }
    }
}