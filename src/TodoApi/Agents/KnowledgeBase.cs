using Microsoft.Extensions.AI;

namespace TodoApi.Agents;

public sealed record Passage(string Source, string Heading, string Text)
{
    public override string ToString() => $"[{Source} - {Heading}]\n{Text}";
}

/// <summary>
/// Retrieval over the Markdown files in Knowledge/. The corpus is small and static, so the
/// index is a list of embedded passages held in memory and built once on first use - no
/// vector database to run, and no extra Azure resource to pay for. Swap the search method
/// for Azure AI Search if the corpus ever outgrows this.
/// </summary>
public sealed class KnowledgeBase(
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IHostEnvironment environment,
    ILogger<KnowledgeBase> logger)
{
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private IReadOnlyList<(Passage Passage, ReadOnlyMemory<float> Vector)>? _index;

    public async Task<IReadOnlyList<Passage>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(cancellationToken);
        if (index.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var queryVector = (await embeddings.GenerateAsync([query], cancellationToken: cancellationToken))[0].Vector;

        return index
            .Select(entry => (entry.Passage, Score: CosineSimilarity(queryVector.Span, entry.Vector.Span)))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.Passage)
            .ToList();
    }

    private async Task<IReadOnlyList<(Passage Passage, ReadOnlyMemory<float> Vector)>> GetIndexAsync(
        CancellationToken cancellationToken)
    {
        if (_index is { } built)
        {
            return built;
        }

        await _buildLock.WaitAsync(cancellationToken);
        try
        {
            if (_index is { } raced)
            {
                return raced;
            }

            var passages = LoadPassages();
            if (passages.Count == 0)
            {
                logger.LogWarning("Knowledge base is empty; the guide agent will have nothing to cite.");
                return _index = [];
            }

            var vectors = await embeddings.GenerateAsync(
                passages.Select(p => p.ToString()).ToList(),
                cancellationToken: cancellationToken);

            logger.LogInformation("Embedded {Count} knowledge passages.", passages.Count);

            return _index = passages
                .Zip(vectors, (passage, embedding) => (passage, embedding.Vector))
                .ToList();
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <summary>One passage per "## " heading, which keeps each chunk on a single topic.</summary>
    private List<Passage> LoadPassages()
    {
        var directory = Path.Combine(environment.ContentRootPath, "Knowledge");
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Knowledge directory not found at {Directory}.", directory);
            return [];
        }

        var passages = new List<Passage>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories))
        {
            var source = Path.GetFileNameWithoutExtension(file);

            foreach (var section in File.ReadAllText(file).Split("\n## ", StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = section.TrimStart('#', ' ', '\n', '\r');
                var newline = trimmed.IndexOf('\n');
                if (newline < 0)
                {
                    continue;
                }

                var heading = trimmed[..newline].Trim();
                var body = trimmed[(newline + 1)..].Trim();

                if (body.Length > 0)
                {
                    passages.Add(new Passage(source, heading, body));
                }
            }
        }

        return passages;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, magnitudeA = 0, magnitudeB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
