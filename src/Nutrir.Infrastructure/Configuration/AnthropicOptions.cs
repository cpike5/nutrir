namespace Nutrir.Infrastructure.Configuration;

public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-haiku-4-5";
    public int MaxTokens { get; set; } = 4096;
}
