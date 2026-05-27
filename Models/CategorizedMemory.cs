
namespace LocalAiClient.Models;

//Create memory category model to organised conversational knowledge.
public class CategorizedMemory
{
    public string Category { get; set; } = "";

    public List<ConversationMessage> Messages { get; set; } = [];
}