//Ignore Spelling: nomic json ollama
using System.Text;
using System.Text.Json;

public class EmbeddingService
{
    //private readonly HttpClient _http = new();
    private readonly HttpClient  _http = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public async Task<float[]> GenerateEmbedding(string text)
    {
        var request = new
        {
            model = "nomic-embed-text",
            prompt = text
        };

        string json = JsonSerializer.Serialize(request);

        HttpResponseMessage response = await _http.PostAsync(
            "http://localhost:11434/api/embeddings",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        string result = await response.Content.ReadAsStringAsync();

        Console.WriteLine(result); // TEMP DEBUG

        using JsonDocument doc = JsonDocument.Parse(result);

        //
        // Detect Ollama errors FIRST
        //
        if (doc.RootElement.TryGetProperty("error", out JsonElement error))
        {
            throw new Exception(
                $"Ollama embedding error: {error.GetString()}");
        }

        //
        // New Ollama format compatibility
        //
        if (doc.RootElement.TryGetProperty("embedding", out JsonElement embeddingElement))
        {
            return embeddingElement
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();
        }

        if (doc.RootElement.TryGetProperty("embeddings", out JsonElement embeddingsElement))
        {
            return embeddingsElement[0]
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();
        }

        throw new Exception("No embedding field found in Ollama response.");
    }
}