using CommunityToolkit.Mvvm.ComponentModel;

namespace RepoPacker.UI;

public enum MarketplaceItemType {
    Preset,
    Theme
}

public partial class MarketplaceItem : ObservableObject {
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string DiscussionUrl { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public int UpvoteCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public MarketplaceItemType ItemType { get; init; }

    public PackingPreset? PresetPayload { get; init; }
    public ThemeDefinition? ThemePayload { get; init; }

    [ObservableProperty]
    private bool _isInstalled;

    public string BadgeText => ItemType == MarketplaceItemType.Preset ? "PRESET" : "THEME";
    public string AuthorDisplay => $"by @{Author} • {CreatedAt:yyyy-MM-dd}";
}