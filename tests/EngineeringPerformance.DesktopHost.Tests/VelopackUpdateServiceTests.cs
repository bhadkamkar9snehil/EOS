using EngineeringPerformance.Application;
using EngineeringPerformance.DesktopHost.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringPerformance.DesktopHost.Tests;

public sealed class VelopackUpdateServiceTests
{
    [Fact]
    public void UnsupportedBuildStartsDisabled()
    {
        using var service = Create(new FakeBackend { IsInstalled = false });
        Assert.Equal(UpdatePhase.Unsupported, service.Status.Phase);
    }

    [Fact]
    public void PreparedUpdateIsRecoveredAtStartup()
    {
        using var service = Create(new FakeBackend { PendingVersion = "1.2.0" });
        Assert.Equal(UpdatePhase.ReadyToRestart, service.Status.Phase);
        Assert.Equal("1.2.0", service.Status.AvailableVersion);
    }

    [Fact]
    public async Task CheckReportsCurrentWhenNoUpdateExists()
    {
        using var service = Create(new FakeBackend());
        await service.CheckNowAsync();
        Assert.Equal(UpdatePhase.Current, service.Status.Phase);
        Assert.NotNull(service.Status.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task CheckThenDownloadReachesReadyToRestartWithProgress()
    {
        var backend = new FakeBackend { Candidate = new UpdateCandidate("1.2.0", new object()) };
        using var service = Create(backend);
        await service.CheckNowAsync();
        Assert.Equal(UpdatePhase.Available, service.Status.Phase);

        await service.DownloadAsync();
        Assert.Equal(UpdatePhase.ReadyToRestart, service.Status.Phase);
        Assert.Equal(100, service.Status.DownloadProgress);
    }

    [Fact]
    public async Task DownloadFailureRemainsAvailableForRetry()
    {
        var backend = new FakeBackend { Candidate = new UpdateCandidate("1.2.0", new object()), DownloadException = new IOException("offline") };
        using var service = Create(backend);
        await service.CheckNowAsync();
        await service.DownloadAsync();
        Assert.Equal(UpdatePhase.Available, service.Status.Phase);
        Assert.Equal("DownloadFailed", service.Status.FailureCode);
    }

    [Fact]
    public async Task RestartSchedulesApplyThenRequestsGracefulShutdown()
    {
        var backend = new FakeBackend { PendingVersion = "1.2.0" };
        var shutdown = false;
        using var service = Create(backend, () => shutdown = true);
        service.RestartAndApply();
        Assert.True(backend.ApplyScheduled);
        Assert.True(shutdown);
        await Task.CompletedTask;
    }

    private static VelopackUpdateService Create(FakeBackend backend, Action? shutdown = null) =>
        new(backend, shutdown ?? (() => { }), NullLogger<VelopackUpdateService>.Instance);

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public string CurrentVersion => "1.1.5";
        public string? PendingVersion { get; init; }
        public UpdateCandidate? Candidate { get; init; }
        public Exception? CheckException { get; init; }
        public Exception? DownloadException { get; init; }
        public bool ApplyScheduled { get; private set; }

        public Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken) =>
            CheckException is null ? Task.FromResult(Candidate) : Task.FromException<UpdateCandidate?>(CheckException);

        public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
        {
            if (DownloadException is not null) return Task.FromException(DownloadException);
            progress(25);
            progress(100);
            return Task.CompletedTask;
        }

        public void WaitExitThenApply() => ApplyScheduled = true;
    }
}
