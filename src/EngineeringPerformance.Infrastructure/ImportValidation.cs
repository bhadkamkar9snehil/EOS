namespace EngineeringPerformance.Infrastructure;

public sealed record ImportSkipReason(string FileName, string Reason);

internal static class ImportSkipLog
{
    public static void Record(List<ImportSkipReason> sink, string fileName, string reason) =>
        sink.Add(new ImportSkipReason(fileName, reason));
}
