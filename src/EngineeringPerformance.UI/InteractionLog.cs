namespace EngineeringPerformance.UI;

internal static class InteractionLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EngineeringPerformance");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "interaction.log");

    public static void Write(string eventName, string detail, Exception? exception = null)
    {
        try
        {
            var suffix = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
            var line = $"{DateTimeOffset.Now:O} | {eventName} | {detail}{suffix}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // Diagnostic logging must never break an interaction.
        }
    }
}
