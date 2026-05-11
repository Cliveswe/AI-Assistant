//Ignore Spelling: nomic json ollama
using System.Text;
using System.Text.Json;

public class EmbeddingService
{
    private readonly HttpClient _http = new();

    public async Task<float[]> GenerateEmbedding(string text)
    {
        var request = new
        {
            model = "nomic-embed-text",
            prompt = text
        };

        var json = JsonSerializer.Serialize(request);

        var response = await _http.PostAsync(
            "http://localhost:11434/api/embeddings",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        var result = await response.Content.ReadAsStringAsync();

        Console.WriteLine(result); // TEMP DEBUG

        using var doc = JsonDocument.Parse(result);

        //
        // New Ollama format compatibility
        //
        if (doc.RootElement.TryGetProperty("embedding", out var embeddingElement))
        {
            return embeddingElement
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();
        }

        if (doc.RootElement.TryGetProperty("embeddings", out var embeddingsElement))
        {
            return embeddingsElement[0]
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();
        }

        throw new Exception("No embedding field found in Ollama response.");
    }
}