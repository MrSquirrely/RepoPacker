namespace RepoPacker.UI;

public sealed class ContextBudgetProfile {
	public string DisplayName { get; init; } = string.Empty;
	public int MaxTokens { get; init; }
	public string TokenizerModel { get; init; } = "gpt-4o";

	public ContextBudgetProfile(string displayName, int maxTokens, string tokenizerModel = "gpt-4o") {
		DisplayName = displayName;
		MaxTokens = maxTokens;
		TokenizerModel = tokenizerModel;
	}

	public override string ToString() => DisplayName;
}