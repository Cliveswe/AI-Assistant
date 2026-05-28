namespace LocalAiClient.Services;

public class LoggingService
{
    public void Log(
        string activeProject,
        string model,
        string prompt,
        string response)
    {
        var logPath =
            Path.Combine("logs", "chatlog.txt");

        Directory.CreateDirectory("logs");

        var entry = $@"
TIMESTAMP: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
PROJECT: {activeProject}
MODEL: {model}

PROMPT:
{prompt}

RESPONSE:
{response}

----------------------------------------
";

        File.AppendAllText(logPath, entry);
    }
}