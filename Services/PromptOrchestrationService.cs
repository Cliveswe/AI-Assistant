namespace LocalAIClient.Services;

public class PromptOrchestrationService
{
    public string BuildPrompt(
        string userInput,
        string context,
        string conversationText,
        string summaryText,
        string activeProject)
    {

        const int maxContextChars = 12000;

        if (context.Length > maxContextChars)
        {
            context =
                context.Substring(0, maxContextChars);
        }

        return $"""
You are a senior software engineering assistant.

STRICT RULES:
- Answer ONLY using the provided CONTEXT
- If the answer is not explicitly in the context, reply:
  "I don't know based on the provided code"
- Do NOT guess
- Do NOT infer missing details
- Be precise and concise
- Reference filenames when possible

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
{userInput}
""";
    }
}