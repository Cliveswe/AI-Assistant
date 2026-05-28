//Ignore Spelling: ollama json codellama mixtral yyyy dev codebase

using LocalAiClient.Models;
using LocalAiClient.Services;
using LocalAIClient.Services;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace LocalAiClient
{
    public static class Program
    {

        private static RetrievalService retrievalService = new RetrievalService();

        //Active project variable to maintain context across the application.
        //This allows us to keep separate indexes, memories, and summaries for
        //different codebases, improving relevance and organization.
        private static string? activeProject;

        private static string repoPath = "";
        private static string indexPath = "";
        private static string vectorPath = "";
        private static string memoryPath = "";
        private static string summaryPath = "";
        private static readonly HttpClient http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        private static LoggingService loggingService = new LoggingService();

        private static async Task Main()
        {

            Console.WriteLine("Select mode:");
            Console.WriteLine("1 = Chat");
            Console.WriteLine("2 = Index code");
            Console.Write("Choice: ");
            string? mode = Console.ReadLine();

            //Add active project selection. This allows us to maintain
            //separate indexes and memories for different codebases, improving
            //relevance and organisation.
            Console.Write("Repository path: ");
            repoPath = Console.ReadLine()!;

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                Console.WriteLine("Invalid repository path.");
                return;
            }

            // Automatically derive project name
            activeProject = new DirectoryInfo(repoPath).Name;

            indexPath = $@"I:\AI\indexes\{activeProject}";
            vectorPath = $@"I:\AI\indexes\vectors\{activeProject}";
            memoryPath = $@"I:\AI\memory\{activeProject}\conversation.json";
            summaryPath = $@"I:\AI\memory\{activeProject}\summaries.json";

            Console.WriteLine($"Active project: {activeProject}");
            Console.WriteLine($"RepoPath: {repoPath}");
            Console.WriteLine($"IndexPath: {indexPath}");
            Console.WriteLine($"VectorPath: {vectorPath}");
            Console.WriteLine($"MemoryPath: {memoryPath}");
            Console.WriteLine($"SummaryPath: {summaryPath}");

            //Checks for saved memory. Reloads previous session. Restores conversation continuity.
            List<ConversationMessage> conversationHistory;

            if (File.Exists(memoryPath))
            {
                string memoryJson =
                    File.ReadAllText(memoryPath);

                conversationHistory =
                JsonSerializer.Deserialize<
                    List<ConversationMessage>>
                    (memoryJson)
                ?? new List<ConversationMessage>();

                Console.WriteLine(
                    $"Loaded memory: {conversationHistory.Count} messages");
            }
            else
            {
                conversationHistory =
                    new List<ConversationMessage>();
            }

            if (mode == "1")
            {
                Console.WriteLine("Local AI Client");
                Console.WriteLine("Models: mistral | codellama:13b | mixtral | qwen2.5:3b");
                Console.WriteLine("Type 'exit' to quit\n");

                while (true)
                {
                    Console.Write("Prompt: ");
                    var userInput = Console.ReadLine();

                    if (userInput == "exit") break;

                    var suggestedModel = SuggestModel(userInput);

                    Console.WriteLine($"Suggested model: {suggestedModel}");

                    Console.Write("Model (press Enter to accept suggestion): ");
                    var modelInput = Console.ReadLine();

                    if (modelInput == "exit") break;

                    //model fallback
                    var model = string.IsNullOrWhiteSpace(modelInput)
                        ? suggestedModel
                        : modelInput;

                    Console.WriteLine($"Using model: {model}");

                    //Save user messages.
                    conversationHistory.Add(new ConversationMessage
                    {
                        Role = "user",
                        Content = userInput
                    });
                    var chunks = await RetrieveHybridChunks(userInput, indexPath, vectorPath);

                    // DEBUG OUTPUT (Phase 3.6.4.2)
                    Console.WriteLine("\n--- EXPANDED CONTEXT ---");

                    foreach (var chunk in chunks)
                    {
                        Console.WriteLine(chunk.Substring(0, Math.Min(150, chunk.Length)));
                        Console.WriteLine("------");
                    }

                    int indexedFiles = Directory.Exists(indexPath)
                        ? Directory.GetFiles(indexPath, "*.txt").Length
                        : 0;

                    Console.WriteLine($"Files indexed: {indexedFiles}");

                    //This prevents worst case hallucination.
                    if (chunks.Count == 0)
                    {
                        Console.WriteLine("\n--- RESPONSE ---");
                        Console.WriteLine("No relevant context found in codebase.");
                        Console.WriteLine();
                        continue;
                    }

                    string context = BuildSmartContext(chunks);
                    Console.WriteLine($"\nContext size: {context.Length} chars");
                    Console.WriteLine($"Chunks used: {chunks.Count}");

                    /*
                     * Once we introduce RAG:
                     * The RAG prompt becomes the real prompt
                     * So fullPrompt is simply:
                     * RAG wrapper + your user request
                     */
                    string basePrompt = userInput;

                    //Build conversation history text.
                    string conversationText = BuildConversationHistory(conversationHistory, userInput);

                    Console.WriteLine($"Memory category: {CategorizeMessage(userInput)}");//category diagnostics
                    Console.WriteLine($"\nMemory context size: {conversationText.Length}");//memory diagnostics

                    //Inject summaries into prompt.
                    string summaryText = LoadRecentSummaries(summaryPath);

                    string fullPrompt = $"""
You are a senior software engineering assistant.

STRICT RULES:
- Answer ONLY using the provided CONTEXT
- Reference filenames when possible
- If the answer is not explicitly in the context, reply: "I don't know based on the provided code"
- Do NOT guess
- Do NOT infer missing details
- Be precise and concise

ACTIVE PROJECT:
{activeProject}

---

LONG-TERM MEMORY:
{summaryText}

---
CONVERSATION HISTORY:
{conversationText}

---

CONTEXT:
{context}

---

User request:
{basePrompt}
""";

                    var request = new
                    {
                        model = model,
                        prompt = fullPrompt,
                        stream = false
                    };

                    string json = JsonSerializer.Serialize(request);

                    HttpResponseMessage response = await http.PostAsync(
                        "http://localhost:11434/api/generate",
                        new StringContent(json, Encoding.UTF8, "application/json")
                    );

                    string result = await response.Content.ReadAsStringAsync();

                    JsonNode? jsonNode = JsonNode.Parse(result);
                    string? responseText = jsonNode?["response"]?.ToString();

                    //Save assistant responses.
                    conversationHistory.Add(new ConversationMessage
                    {
                        Role = "assistant",
                        Content = responseText ?? ""
                    });

                    SaveConversationHistory(conversationHistory, memoryPath);//Save memory after each assistant response.

                    //Auto-generate summaries periodically.
                    //Every 10 messages: generates compressed summary, persists summary to disk,
                    //creates long-term conversational abstraction.
                    if (conversationHistory.Count % 10 == 0)
                    {
                        string summary =
                            GenerateConversationSummary(
                                conversationHistory);

                        SaveConversationSummary(
                            summary,
                            summaryPath);

                        Console.WriteLine(
                            "\nConversation summary created.");
                    }

                    Console.WriteLine("\n--- RESPONSE ---");
                    Console.WriteLine(responseText);

                    //Hallucination warning
                    if (LooksLikeHallucination(responseText))
                    {
                        Console.WriteLine("⚠️ Warning: response may contain speculation.");
                    }
                    Console.WriteLine();
                    loggingService.Log(activeProject, model, fullPrompt, responseText ?? "");
                }
            }


            if (mode == "2")
            {

                if (Directory.Exists(indexPath))
                {
                    Directory.Delete(indexPath, true);
                }

                if (Directory.Exists(vectorPath))
                {
                    Directory.Delete(vectorPath, true);
                }

                List<string> files = GetCodeFiles(repoPath);

                int fileCounter = 0;

                Console.WriteLine($"Indexing project: {activeProject}");

                foreach (var file in files)
                {
                    var content = File.ReadAllText(file);
                    var chunks = ChunkFile(content);

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        var chunkContent = chunks[i];

                        //
                        // Save raw chunk
                        //
                        var fileName = $"file{fileCounter}_{i}.txt";
                        var fullPath = Path.Combine(indexPath, fileName);

                        //Add embedding generation to indexing mode
                        Directory.CreateDirectory(indexPath);
                        var enrichedChunk = $"""
FILE: {Path.GetFileName(file)}
PATH: {file}


{chunkContent}
""";

                        File.WriteAllText(fullPath, enrichedChunk);

                        //
                        // Generate embedding
                        //
                        var embeddingService = new EmbeddingService();

                        var embedding = await embeddingService.GenerateEmbedding(chunkContent);
                        Console.WriteLine($"Embedding size: {embedding.Length}");
                        //
                        // Create vector record, indexing pipeline.
                        //
                        var record = new EmbeddingRecord
                        {
                            FileName = Path.GetFileName(file),

                            FilePath = file,

                            ProjectName = new DirectoryInfo(repoPath).Name,

                            ChunkIndex = i,

                            Content = chunkContent,

                            Embedding = embedding
                        };

                        Directory.CreateDirectory(vectorPath);

                        var jsonPath = Path.Combine(
                            vectorPath,
                            $"{Path.GetFileNameWithoutExtension(fileName)}.json"
                        );

                        var recordJson = JsonSerializer.Serialize(
                            record,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });

                        File.WriteAllText(jsonPath, recordJson);

                        Console.WriteLine($"Indexed chunk: {fileName}");
                    }

                    fileCounter++;
                }

                Console.WriteLine("Indexing complete.");
            }
        }

        // A suggestion function.
        private static string SuggestModel(string userInput)
        {
            var input = userInput.ToLower();

            if (input.Contains("c#") || input.Contains("code") || input.Contains("class"))
                return "codellama:13b";

            if (input.Contains("design") || input.Contains("architecture") || input.Contains("analysis"))
                return "mixtral";

            return "mistral";
        }

        //Build simple file scanner.
        private static List<string> GetCodeFiles(string rootPath)
        {
            var extensions = new[]
            {
        ".cs",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".html",
        ".css",
        ".cpp",
        ".h",
        ".hpp",
        ".py",
        ".java"
    };

            var ignoredFolders = new[]
            {
        "\\bin\\",
        "\\obj\\",
        "\\node_modules\\",
        "\\dist\\",
        "\\build\\",
        "\\packages\\",
        "\\wwwroot\\lib\\",
        "\\.git\\",
        "\\coverage\\",
        "\\vendor\\"
    };

            return Directory
                .GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                    extensions.Contains(
                        Path.GetExtension(f),
                        StringComparer.OrdinalIgnoreCase))
                .Where(f =>
                    !ignoredFolders.Any(ignore =>
                        f.Contains(ignore,
                            StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        //Read and chunk files.
        private static List<string> ChunkFile(string content, int maxChunkSize = 1500)
        {
            List<string> chunks = new List<string>();

            string[] lines = content.Split('\n');

            StringBuilder current = new StringBuilder();

            foreach (var line in lines)
            {
                //
                // Hard-stop huge lines
                //
                var safeLine = line.Length > 300
                    ? line.Substring(0, 300)
                    : line;

                //
                // If next line exceeds chunk size,
                // finalize current chunk
                //
                if (current.Length + safeLine.Length > maxChunkSize)
                {
                    if (current.Length > 0)
                    {
                        chunks.Add(current.ToString());
                        current.Clear();
                    }
                }

                current.AppendLine(safeLine);
            }

            //
            // Final chunk
            //
            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
            }

            return chunks;
        }

        //Basic hallucination detection (lightweight)
        private static bool LooksLikeHallucination(string response)
        {
            if (response == null) return true;

            var text = response.ToLower();

            return text.Contains("likely") ||
                   text.Contains("probably") ||
                   text.Contains("it seems") ||
                   text.Contains("typically");
        }

        //Extract symbols from retrieved chunks
        private static List<string> ExtractSymbols(List<string> chunks)
        {
            var symbols = new HashSet<string>();

            foreach (var chunk in chunks)
            {
                var lines = chunk.Split('\n');

                foreach (var line in lines)
                {
                    // class names
                    if (line.Contains("class "))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var index = Array.IndexOf(parts, "class");
                        if (index >= 0 && index + 1 < parts.Length)
                            symbols.Add(parts[index + 1]);
                    }

                    // service/interface hints
                    if (line.Contains("Service") || line.Contains("Controller"))
                    {
                        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var w in words)
                        {
                            if (w.EndsWith("Service") || w.EndsWith("Controller"))
                                symbols.Add(w);
                        }
                    }
                }
            }

            return symbols.ToList();
        }

        //Second-pass retrieval
        private static List<string> ExpandContext(List<string> initialChunks, string indexPath, int maxExpanded = 5)
        {
            var symbols = ExtractSymbols(initialChunks);

            var files = Directory.GetFiles(indexPath, "*.txt");

            var scored = new List<(string content, int score)>();

            foreach (var file in files)
            {
                var content = File.ReadAllText(file);

                int score = 0;

                foreach (var symbol in symbols)
                {
                    // direct symbol match
                    if (content.Contains(symbol, StringComparison.OrdinalIgnoreCase))
                        score += 2;

                    // stronger boost for class definitions
                    if (content.Contains($"class {symbol}", StringComparison.OrdinalIgnoreCase))
                        score += 4;

                    // service usage
                    if (content.Contains($"{symbol}(", StringComparison.OrdinalIgnoreCase))
                        score += 1;
                }

                if (score > 0)
                {
                    scored.Add((content, score));
                }
            }

            List<string> selected = scored
                .OrderByDescending(x => x.score)
                .Take(maxExpanded)
                .Select(x => x.content)
                .ToList();

            // ensure original chunks always survive
            foreach (var chunk in initialChunks)
            {
                if (!selected.Contains(chunk))
                    selected.Insert(0, chunk);
            }

            return selected.Distinct().ToList();
        }

        //Cosine similarity function. Measures how similar two meanings are.
        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0;
            double magA = 0;
            double magB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }

        //Create semantic retrieval method.
        private static async Task<List<string>> RetrieveSemanticChunks(
            string query,
            string vectorPath,
            int maxChunks = 3)
        {
            var embeddingService = new EmbeddingService();

            //
            // Generate query embedding
            //
            var queryEmbedding =
                await embeddingService.GenerateEmbedding(query);

            if (!Directory.Exists(vectorPath))
            {
                return new List<string>();
            }

            //
            // Load vector files
            //
            var files = Directory.GetFiles(vectorPath, "*.json");

            //semantic retrieval output
            var scored = new List<(EmbeddingRecord record, double score)>();

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);

                var record = JsonSerializer.Deserialize<EmbeddingRecord>(json);

                if (record == null || record.Embedding == null)
                    continue;

                //
                // Compare vector similarity
                //
                var similarity =
                    CosineSimilarity(queryEmbedding, record.Embedding);

                scored.Add((record, similarity));
            }

            //
            // Highest similarity first
            //
            var results = scored
             .OrderByDescending(x => x.score)
             .Take(maxChunks)
             .Select(x =>
         $"""
FILE: {x.record.FileName}
PROJECT: {x.record.ProjectName}
CHUNK: {x.record.ChunkIndex}

{x.record.Content}
""")
             .ToList();

            //
            // DEBUG
            //
            Console.WriteLine("\n--- SEMANTIC MATCHES ---");

            foreach (var result in results)
            {
                Console.WriteLine(
                    result.Substring(0, Math.Min(200, result.Length)));

                Console.WriteLine("------");
            }

            return results;
        }

        //Create hybrid retrieval method.
        private static async Task<List<string>> RetrieveHybridChunks(
            string query,
            string indexPath,
            string vectorPath,
            int maxChunks = 25)
        {
            if (query.Contains("all controllers", StringComparison.OrdinalIgnoreCase))
            {
                maxChunks = 50;
            }

            if (query.Contains("list", StringComparison.OrdinalIgnoreCase))
            {
                maxChunks = 40;
            }

            //
            // Keyword retrieval
            //
            var keywordChunks = retrievalService
                .RetrieveRelevantChunks(query, indexPath, maxChunks);

            //
            // Semantic retrieval
            //
            var semanticChunks =
                await RetrieveSemanticChunks(query, vectorPath, maxChunks);

            //
            // Merge
            //
            var merged = keywordChunks
                .Concat(semanticChunks)
                .Distinct()
                .ToList();

            //
            // Simple re-ranking:
            // prefer chunks containing query terms
            //
            var reranked = merged
                .Select(chunk =>
                {
                    int score = 0;

                    var words = query
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var word in words)
                    {
                        if (chunk.Contains(
                            word,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            score++;
                        }
                    }

                    return new
                    {
                        Content = chunk,
                        Score = score
                    };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Content.Length)
                .Take(maxChunks)
                .Select(x => x.Content)
                .ToList();

            //
            // DEBUG
            //
            Console.WriteLine("\n--- HYBRID RETRIEVAL ---");

            foreach (var chunk in reranked)
            {
                Console.WriteLine(
                    chunk.Substring(
                        0,
                        Math.Min(200, chunk.Length)));

                Console.WriteLine("------");
            }

            return reranked;
        }

        //Chunk compression helper.
        private static string CompressChunk(string content, int maxLength = 1200)
        {
            if (content.Length <= maxLength)
                return content;

            return content.Substring(0, maxLength)
                + "\n\n...[TRUNCATED]";
        }

        private static string BuildSmartContext(List<string> chunks, int maxContextLength = 12000)
        {
            var builder = new StringBuilder();

            var usedFiles = new HashSet<string>();

            foreach (var chunk in chunks)
            {
                //
                // Avoid duplicate files dominating context
                //
                var lines = chunk.Split('\n');

                var fileLine = lines
                    .FirstOrDefault(l => l.StartsWith("FILE:"));

                if (fileLine != null)
                {
                    if (usedFiles.Contains(fileLine))
                        continue;

                    usedFiles.Add(fileLine);
                }

                //
                // Compress large chunks
                //
                var compressed = CompressChunk(chunk);

                //
                // Respect context budget
                //
                if (builder.Length + compressed.Length
                    > maxContextLength)
                {
                    break;
                }

                builder.AppendLine(compressed);
                builder.AppendLine("\n---\n");
            }

            return builder.ToString();
        }

        //Conversation history builder. This constructs a relevant subset of past
        //conversation to inject into the prompt, providing context and continuity
        //while respecting token limits. It uses relevance scoring to select the most
        //pertinent messages based on the current user query.
        private static string BuildConversationHistory(
            List<ConversationMessage> history,
            string currentQuery,
            int maxMessages = 6)
        {
            List<ConversationMessage> relevantMessages = GetRelevantCategoryMemory(currentQuery, history, maxMessages);

            StringBuilder builder = new StringBuilder();

            foreach (var message in relevantMessages)
            {
                builder.AppendLine($"{message.Role.ToUpper()}:");

                builder.AppendLine(message.Content);

                builder.AppendLine();
            }

            //Prevent oversized memory injection
            const int maxMemoryChars = 4000;

            if (builder.Length > maxMemoryChars)
            {
                return builder.ToString()
                    .Substring(0, maxMemoryChars);
            }

            return builder.ToString();
        }

        //Create memory save helper.
        private static void SaveConversationHistory(
            List<ConversationMessage> history,
            string memoryPath)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(memoryPath)!);

            const int maxMessages = 50;
            //IMPORTANT: memory will eventually grow too large (memory trimming).
            if (history.Count > maxMessages)
            {
                history = history
                    .TakeLast(maxMessages)
                    .ToList();
            }

            var json = JsonSerializer.Serialize(
                history,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(memoryPath, json);
        }

        //Memory relevance scorer.
        private static int ScoreMemoryRelevance(
            string query,
            ConversationMessage message)
        {
            int score = 0;

            var queryWords = query
                .ToLower()
                .Split(' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2)
                .Distinct();

            foreach (var word in queryWords)
            {
                if (message.Content.Contains(
                    word,
                    StringComparison.OrdinalIgnoreCase))
                {
                    score++;
                }
            }

            return score;
        }


        //Create summary generation helper. This creates lightweight conversational abstraction instead of storing only raw chat logs.
        public static string GenerateConversationSummary(List<ConversationMessage> history, int maxMessages = 20)
        {
            List<ConversationMessage> recent = history
                .TakeLast(maxMessages)
                .ToList();

            StringBuilder builder = new StringBuilder();

            builder.AppendLine(
                "Conversation topics discussed:");

            foreach (ConversationMessage message in recent)
            {
                if (message.Role == "user")
                {
                    builder.AppendLine(
                        $"- {message.Content}");
                }
            }

            return builder.ToString();
        }

        //Create summary persistence helper. This allows us to maintain a high-level
        //overview of past conversations without needing to read through entire chat logs.
        public static void SaveConversationSummary(string summary, string summaryPath)
        {
            List<ConversationSummary> summaries;

            if (File.Exists(summaryPath))
            {
                string json =
                    File.ReadAllText(summaryPath);

                summaries =
                    JsonSerializer.Deserialize<
                        List<ConversationSummary>>(json)
                    ?? new();
            }
            else
            {
                summaries = new();
            }

            summaries.Add(
                new ConversationSummary
                {
                    CreatedAt = DateTime.Now,
                    Summary = summary
                });

            string output =
                JsonSerializer.Serialize(
                    summaries,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

            File.WriteAllText(summaryPath, output);
        }

        //Load summaries into prompt. This provides a way to inject high-level abstractions
        //of past conversations, improving long-term coherence without overwhelming the model
        //with raw chat logs.
        public static string LoadRecentSummaries(string summaryPath, int maxSummaries = 3)
        {
            if (!File.Exists(summaryPath))
                return "";

            string json =
                File.ReadAllText(summaryPath);

            List<ConversationSummary>? summaries =
                JsonSerializer.Deserialize<
                    List<ConversationSummary>>(json);

            if (summaries == null)
                return "";

            IEnumerable<ConversationSummary> recent = summaries
                .TakeLast(maxSummaries);

            StringBuilder builder = new StringBuilder();

            foreach (ConversationSummary summary in recent)
            {
                builder.AppendLine(summary.Summary);
                builder.AppendLine("\n---\n");
            }

            return builder.ToString();
        }

        //Create memory categorization helper. This allows us to
        //organise conversational knowledge into categories, enabling
        //more targeted retrieval and context injection based on
        //the user's current needs. This creates: topic routing for memory organisation.
        public static string CategorizeMessage(string text)
        {
            string input = text.ToLower();

            if (input.Contains("controller") ||
                input.Contains("route") ||
                input.Contains("endpoint"))
            {
                return "web";
            }

            if (input.Contains("service") ||
                input.Contains("dependency injection") ||
                input.Contains("singleton"))
            {
                return "services";
            }

            if (input.Contains("authentication") ||
                input.Contains("authorization") ||
                input.Contains("jwt"))
            {
                return "security";
            }

            if (input.Contains("database") ||
                input.Contains("sql") ||
                input.Contains("entity framework"))
            {
                return "database";
            }

            if (input.Contains("architecture") ||
                input.Contains("design") ||
                input.Contains("pattern"))
            {
                return "architecture";
            }

            return "general";
        }

        //Create categorised memory builder. This structures conversational memory into categories, allowing
        //for more efficient retrieval and context management based on the user's current focus or needs.
        public static Dictionary<string, List<ConversationMessage>> BuildCategorizedMemory(List<ConversationMessage> history)
        {
            var categorized = new Dictionary<string, List<ConversationMessage>>();

            foreach (ConversationMessage message in history)
            {
                string category = CategorizeMessage(message.Content);

                if (!categorized.ContainsKey(category))
                {
                    categorized[category] = new List<ConversationMessage>();
                }

                categorized[category]
                    .Add(message);
            }

            return categorized;
        }

        //Create relevant category selector. This allows us to dynamically select and
        //inject the most relevant subset of conversational memory based on the user's
        //current query, improving context relevance and response quality.
        public static List<ConversationMessage> GetRelevantCategoryMemory(
           string query, List<ConversationMessage> history, int maxMessages = 6)
        {
            string targetCategory =
                CategorizeMessage(query);

            Dictionary<string, List<ConversationMessage>> categorized =
                BuildCategorizedMemory(history);

            if (!categorized.ContainsKey(targetCategory))
            {
                return new();
            }

            return categorized[targetCategory]
                .TakeLast(maxMessages)
                .ToList();
        }
    }
}