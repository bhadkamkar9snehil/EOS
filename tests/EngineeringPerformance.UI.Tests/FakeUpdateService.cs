using EngineeringPerformance.Application;

namespace EngineeringPerformance.UI.Tests;

internal sealed class FakeUpdateService(UpdateStatus status) : IUpdateService
{
    public UpdateStatus Status { get; private set; } = status;
    public event Action? Changed;
    public int CheckCalls { get; private set; }
    public int DownloadCalls { get; private set; }
    public int RestartCalls { get; private set; }

    public Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        CheckCalls++;
        return Task.CompletedTask;
    }

    public Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        DownloadCalls++;
        return Task.CompletedTask;
    }

    public void RestartAndApply() => RestartCalls++;

    public void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
