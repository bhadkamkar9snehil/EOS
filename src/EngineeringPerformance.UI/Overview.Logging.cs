using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.UI;

/// <summary>
/// Logging-only partial for the large Overview component. The property name intentionally matches
/// the former static InteractionLog call site so the existing layout measurement now resolves to
/// an injected Microsoft.Extensions.Logging logger without keeping a second logging subsystem.
/// Remove the small compatibility extension when that call site is next touched for UI work.
/// </summary>
public partial class Overview
{
    [Inject]
    private ILogger<Overview> InteractionLog { get; set; } = NullLogger<Overview>.Instance;
}

internal static class OverviewLoggingExtensions
{
    public static void Write(
        this ILogger<Overview> logger,
        string eventName,
        string detail,
        Exception? exception = null)
    {
        if (exception is null)
        {
            logger.LogDebug(
                "Overview diagnostic event. EventName={EventName} Detail={Detail}",
                eventName,
                detail);
            return;
        }

        logger.LogError(
            exception,
            "Overview diagnostic event failed. EventName={EventName} Detail={Detail}",
            eventName,
            detail);
    }
}
