using System.Text.Json.Serialization;

namespace RepoPacker.UI;

public sealed class PackingPreset {
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("language")]
	public string Language { get; set; } = string.Empty;

	[JsonPropertyName("excludedDirectories")]
	public string ExcludedDirectoriesText { get; set; } = string.Empty;

	[JsonPropertyName("excludedExtensions")]
	public string ExcludedExtensionsText { get; set; } = string.Empty;

	[JsonPropertyName("isCustom")]
	public bool IsCustom { get; set; }

	[JsonPropertyName("isDefault")]
	public bool IsDefault { get; set; }

	public PackingPreset() { }

	public PackingPreset(string name, string language, string directories, string extensions, bool isCustom = false, bool isDefault = false) {
		Name = name;
		Language = language;
		ExcludedDirectoriesText = directories;
		ExcludedExtensionsText = extensions;
		IsCustom = isCustom;
		IsDefault = isDefault;
	}

	public override string ToString() => Name;
}