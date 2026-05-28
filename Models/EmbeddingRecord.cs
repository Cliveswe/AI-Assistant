namespace LocalAIClient.Models;

public class EmbeddingRecord
{
    public string FileName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public string ProjectName { get; set; } = "";

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = "";

    public float[] Embedding { get; set; } = [];
}