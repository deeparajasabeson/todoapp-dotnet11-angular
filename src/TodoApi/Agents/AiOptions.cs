namespace TodoApi.Agents;

/// <summary>
/// Connection details for the Azure AI Foundry resource that backs the chat assistant.
/// Endpoint and key come from user secrets or environment variables, never appsettings.json.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Foundry resource endpoint, e.g. https://&lt;name&gt;.services.ai.azure.com/.</summary>
    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Foundry project endpoint. Not needed for model calls; kept for reference.</summary>
    public string? ProjectEndpoint { get; set; }

    /// <summary>Name of the chat model *deployment*, which need not match the model name.</summary>
    public string ChatDeployment { get; set; } = "gpt-5-mini";

    /// <summary>Deployment used to embed the knowledge base and the user's question.</summary>
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

    /// <summary>How many knowledge-base passages to give the guide agent per question.</summary>
    public int RetrievedPassages { get; set; } = 4;

    /// <summary>The chat endpoint reports itself unavailable rather than failing when false.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}
