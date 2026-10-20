using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepoPacker.UI;

public sealed class MarketplaceService {
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly Regex JsonCodeBlockRegex = new Regex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public async Task<List<MarketplaceItem>> FetchMarketplaceItemsAsync(
        string repoOwner,
        string repoName,
        string personalAccessToken,
        CancellationToken cancellationToken) {

        List<MarketplaceItem> results = new List<MarketplaceItem>();

        string graphQlQuery = """
        query($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            discussions(first: 50, orderBy: {field: CREATED_AT, direction: DESC}) {
              nodes {
                id
                title
                body
                url
                createdAt
                upvoteCount
                author {
                  login
                }
                category {
                  name
                }
              }
            }
          }
        }
        """;

        Dictionary<string, object> requestObject = new Dictionary<string, object> {
            { "query", graphQlQuery },
            { "variables", new Dictionary<string, string> {
                { "owner", repoOwner },
                { "name", repoName }
            }}
        };

        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RepoPacker", "1.0"));

        if (!string.IsNullOrWhiteSpace(personalAccessToken)) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
        }

        request.Content = new StringContent(JsonSerializer.Serialize(requestObject), Encoding.UTF8, "application/json");

        HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) {
            string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"GitHub API Error ({response.StatusCode}): {errorBody}");
        }

        using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        using (JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)) {
            if (doc.RootElement.TryGetProperty("errors", out JsonElement errorsElement)) {
                throw new InvalidOperationException($"GraphQL Error: {errorsElement[0].GetProperty("message").GetString()}");
            }

            JsonElement discussionsElement = doc.RootElement
                .GetProperty("data")
                .GetProperty("repository")
                .GetProperty("discussions")
                .GetProperty("nodes");

            List<PackingPreset> existingPresets = PresetService.LoadPresets();
            List<ThemeDefinition> existingThemes = ThemeService.LoadInstalledThemes();

            foreach (JsonElement node in discussionsElement.EnumerateArray()) {
                string body = node.GetProperty("body").GetString() ?? string.Empty;
                Match match = JsonCodeBlockRegex.Match(body);
                if (!match.Success) {
                    continue;
                }

                string rawJson = match.Groups[1].Value.Trim();
                string category = node.GetProperty("category").GetProperty("name").GetString() ?? string.Empty;
                string author = node.TryGetProperty("author", out JsonElement authorElem) && authorElem.ValueKind != JsonValueKind.Null
                    ? authorElem.GetProperty("login").GetString() ?? "unknown"
                    : "ghost";

                string title = node.GetProperty("title").GetString() ?? string.Empty;
                string discussionUrl = node.GetProperty("url").GetString() ?? string.Empty;
                int upvotes = node.GetProperty("upvoteCount").GetInt32();
                DateTime createdAt = node.GetProperty("createdAt").GetDateTime();
                string discussionId = node.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();

                if (rawJson.Contains("\"excludedDirectories\"") || rawJson.Contains("\"excludedExtensions\"")) {
                    try {
                        PackingPreset? preset = JsonSerializer.Deserialize<PackingPreset>(rawJson, JsonOptions);
                        if (preset != null && !string.IsNullOrWhiteSpace(preset.Name)) {
                            preset.IsDefault = PresetService.IsDefaultPreset(preset.Name);
                            bool isInstalled = existingPresets.Any(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
                            results.Add(new MarketplaceItem {
                                Id = discussionId,
                                Title = string.IsNullOrWhiteSpace(preset.Name) ? title : preset.Name,
                                Author = author,
                                DiscussionUrl = discussionUrl,
                                CategoryName = category,
                                UpvoteCount = upvotes,
                                CreatedAt = createdAt,
                                ItemType = MarketplaceItemType.Preset,
                                PresetPayload = preset,
                                IsInstalled = isInstalled
                            });
                        }
                    }
                    catch {
                        // Ignore malformed payloads
                    }
                }
                else if (rawJson.Contains("\"canvasColor\"") || rawJson.Contains("\"cardColor\"")) {
                    try {
                        ThemeDefinition? theme = JsonSerializer.Deserialize<ThemeDefinition>(rawJson, JsonOptions);
                        if (theme != null && !string.IsNullOrWhiteSpace(theme.DisplayName)) {
                            theme.IsDefault = ThemeService.IsDefaultTheme(theme.Id);
                            bool isInstalled = existingThemes.Any(t => t.Id.Equals(theme.Id, StringComparison.OrdinalIgnoreCase));
                            results.Add(new MarketplaceItem {
                                Id = discussionId,
                                Title = theme.DisplayName,
                                Author = author,
                                DiscussionUrl = discussionUrl,
                                CategoryName = category,
                                UpvoteCount = upvotes,
                                CreatedAt = createdAt,
                                ItemType = MarketplaceItemType.Theme,
                                ThemePayload = theme,
                                IsInstalled = isInstalled
                            });
                        }
                    }
                    catch {
                        // Ignore malformed payloads
                    }
                }
            }
        }

        return results;
    }

    public void InstallItem(MarketplaceItem item) {
        if (item.ItemType == MarketplaceItemType.Preset && item.PresetPayload != null) {
            item.PresetPayload.IsCustom = true;
            PresetService.SavePreset(item.PresetPayload);
            item.IsInstalled = true;
        }
        else if (item.ItemType == MarketplaceItemType.Theme && item.ThemePayload != null) {
            ThemeService.SaveTheme(item.ThemePayload);
            item.IsInstalled = true;
        }
    }

    public bool UninstallItem(MarketplaceItem item) {
        if (item.IsDefaultItem) {
            return false;
        }

        bool removed = false;
        if (item.ItemType == MarketplaceItemType.Preset && item.PresetPayload != null) {
            removed = PresetService.DeletePreset(item.PresetPayload.Name);
        }
        else if (item.ItemType == MarketplaceItemType.Theme && item.ThemePayload != null) {
            removed = ThemeService.DeleteTheme(item.ThemePayload);
        }

        if (removed) {
            item.IsInstalled = false;
        }

        return removed;
    }
}