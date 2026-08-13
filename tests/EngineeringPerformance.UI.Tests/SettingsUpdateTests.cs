using Bunit;
using EngineeringPerformance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace EngineeringPerformance.UI.Tests;

public sealed class SettingsUpdateTests : BunitContext
{
    [Fact]
    public void DevelopmentBuildShowsStructuredUnsupportedState()
    {
        var page = RenderPage(new UpdateStatus(UpdatePhase.Unsupported, "1.1.5"), out _);
        Assert.Contains("Development build", page.Markup);
        Assert.Contains("1.1.5", page.Markup);
        Assert.DoesNotContain("Check for updates", page.Markup);
    }

    [Fact]
    public void AvailableUpdateUsesExplicitDownloadAction()
    {
        var page = RenderPage(new UpdateStatus(UpdatePhase.Available, "1.1.5", "1.2.0"), out var updates);
        var button = page.FindAll("button").Single(x => x.TextContent.Contains("Download v1.2.0"));
        button.Click();
        Assert.Equal(1, updates.DownloadCalls);
    }

    [Fact]
    public void ReadyUpdateUsesExplicitRestartAction()
    {
        var page = RenderPage(new UpdateStatus(UpdatePhase.ReadyToRestart, "1.1.5", "1.2.0", 100), out var updates);
        var button = page.FindAll("button").Single(x => x.TextContent.Contains("Restart · v1.2.0"));
        button.Click();
        Assert.Equal(1, updates.RestartCalls);
    }

    private IRenderedComponent<SettingsPage> RenderPage(UpdateStatus status, out FakeUpdateService updates)
    {
        var database = new FakeApplicationDatabase();
        updates = new FakeUpdateService(status);
        Services.AddSingleton<IApplicationDatabase>(database);
        Services.AddSingleton(new AppState(database));
        Services.AddSingleton<IUpdateService>(updates);
        return Render<SettingsPage>();
    }
}
