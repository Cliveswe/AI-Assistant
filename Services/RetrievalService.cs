namespace LocalAIClient.Services;

//Create simple retrieval function.
public class RetrievalService
{
    public List<string> RetrieveRelevantChunks(string query, string indexPath, int maxChunks = 3)
    {
        if (!Directory.Exists(indexPath))
        {
            return new List<string>();
        }

        string[] files = Directory.GetFiles(indexPath, "*.txt");

        List<(string content, int score)> results = new List<(string content, int score)>();

        var words = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => w.Length > 1)
            .SelectMany(w => new[]
            {
                w,
                w.ToLower(),
                w.Replace(".", ""),
                w.Replace("()", "")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        
        //for debugging
        Console.WriteLine($"QUERY: {query}");
        Console.WriteLine($"WORDS: {string.Join(", ", words)}");
        
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);

            int score = 0;


            foreach (var word in words)
            {
                if (content.Contains(word, StringComparison.OrdinalIgnoreCase))
                    score += Math.Min(10, word.Length);

                // 3.5.2 improvement: boost class-level relevance
                if (content.Contains("class " + word, StringComparison.OrdinalIgnoreCase))
                    score += 20;
            }



            //Penalize overly large chunks
            if (content.Length > 3000)
            {
                score -= 1; // slight penalty
            }

            if (score > 0)
            {
                results.Add((content, score));
            }
        }

        //Add retrieval fallback Prevents, “No relevant context found” from ever appearing when index exists.
        if (results.Count == 0)
        {
            results = files
                .Take(5)
                .Select(f => (File.ReadAllText(f), 1))
                .ToList();
        }

        //Increase candidate pool, then trim
        var candidates = results
        .OrderByDescending(r => r.score)
        .ThenByDescending(r => r.content.Length) // Phase 3.5.3 improvement
        .Take(10)
        .ToList();

        // Remove near-duplicates
        var selected = new List<string>();

        foreach (var item in candidates)
        {
            var preview = item.content.Substring(0, Math.Min(100, item.content.Length));

            if (!selected.Any(s => s.StartsWith(preview)))
            {
                selected.Add(item.content);
            }

            if (selected.Count >= maxChunks)
                break;
        }

        // DEBUG OUTPUT (3.4.5)
        Console.WriteLine("\n--- RETRIEVED CHUNKS ---");
        foreach (var chunk in selected)
        {
            Console.WriteLine(chunk.Substring(0, Math.Min(200, chunk.Length)));
            Console.WriteLine("------");
        }

        return selected;
    }
}