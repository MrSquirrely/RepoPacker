using System.Text.Json.Serialization;

namespace RepoPacker.UI;

public sealed class ThemeDefinition {
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = string.Empty;

	[JsonPropertyName("isDefault")]
	public bool IsDefault { get; set; }

	[JsonPropertyName("canvasColor")]
	public string CanvasColor { get; set; } = "#0D1117";

	[JsonPropertyName("cardColor")]
	public string CardColor { get; set; } = "#161B22";

	[JsonPropertyName("titleBarColor")]
	public string TitleBarColor { get; set; } = "#161B22";

	[JsonPropertyName("borderColor")]
	public string BorderColor { get; set; } = "#30363D";

	[JsonPropertyName("textPrimaryColor")]
	public string TextPrimaryColor { get; set; } = "#E6EDF3";

	[JsonPropertyName("textSecondaryColor")]
	public string TextSecondaryColor { get; set; } = "#8B949E";

	[JsonPropertyName("inputBgColor")]
	public string InputBgColor { get; set; } = "#010409";

	[JsonPropertyName("btnSecondaryBgColor")]
	public string BtnSecondaryBgColor { get; set; } = "#21262D";

	[JsonPropertyName("btnSecondaryHoverColor")]
	public string BtnSecondaryHoverColor { get; set; } = "#30363D";

	[JsonPropertyName("itemHoverColor")]
	public string ItemHoverColor { get; set; } = "#21262D";

	[JsonPropertyName("itemSelectedColor")]
	public string ItemSelectedColor { get; set; } = "#1F6FEB";

	[JsonPropertyName("titleBtnHoverColor")]
	public string TitleBtnHoverColor { get; set; } = "#30363D";

	public override string ToString() => DisplayName;
}