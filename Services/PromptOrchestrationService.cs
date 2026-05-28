namespace LocalAIClient.Services;

public class PromptOrchestrationService
{
    public string BuildPrompt(
        string userInput,
        string context,
        string conversationText,
        string summaryText,
        string activeProject,
        string model)
    {

        const int maxContextChars = 12000;

        if (context.Length > maxContextChars)
        {
            context = context.Substring(0, maxContextChars);
        }

        var modelRules = model switch
        {
            "codellama:13b" =>
        """
MODEL RULES:
- Prioritize code correctness
- Prefer implementation detail
- Explain code clearly
""",

            "mixtral" =>
        """
MODEL RULES:
- Prioritize reasoning depth
- Explain architectural tradeoffs
- Analyze carefully
""",

            _ =>
        """
MODEL RULES:
- Be concise and practical
"""
        };

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

{modelRules}
---

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