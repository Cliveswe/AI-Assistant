
namespace LocalAiClient.Models;

public class CategorizedMemory
{
    public string Category { get; set; } = "";

    public List<ConversationMessage> Messages { get; set; } = [];
}