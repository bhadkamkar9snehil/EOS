using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace EngineeringPerformance.DesktopHost;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        Services = services;
        InitializeComponent();
        DataContext = this;
    }

    public IServiceProvider Services { get; }

    internal void ConfigureVisualCapture(VisualCaptureOptions options)
    {
        Width = options.Width;
        Height = options.Height;
        BlazorView.StartPath = options.Route;
    }

    internal async Task CaptureVisualAsync(VisualCaptureOptions options, CancellationToken cancellationToken = default)
    {
        await BlazorView.WebView.EnsureCoreWebView2Async();
        var core = BlazorView.WebView.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 initialized without a CoreWebView2 instance.");

        await WaitForRenderedRouteAsync(core, options.Route, cancellationToken);

        if (options.Theme is "light" or "dark")
        {
            var theme = JsonSerializer.Serialize(options.Theme);
            await core.ExecuteScriptAsync($"window.epaTheme?.set({theme});");
            await Task.Delay(500, cancellationToken);
        }

        var outputDirectory = Path.GetDirectoryName(options.OutputFile);
        if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        await using (var stream = File.Create(options.OutputFile))
        {
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }

        var diagnostics = await CaptureDomDiagnosticsAsync(core);
        await File.WriteAllTextAsync(Path.ChangeExtension(options.OutputFile, ".json"), diagnostics, cancellationToken);
    }

    private static async Task WaitForRenderedRouteAsync(CoreWebView2 core, string route, CancellationToken cancellationToken)
    {
        var firstSegment = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "overview";
        var expectedClass = JsonSerializer.Serialize($"route-{firstSegment}");
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ready = await core.ExecuteScriptAsync($$"""
                    (() => {
                      const main = document.querySelector('main');
                      return document.readyState === 'complete' &&
                             !!main &&
                             main.classList.contains({{expectedClass}}) &&
                             document.body.innerText.length > 100;
                    })()
                    """);
                if (string.Equals(ready, "true", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(700, cancellationToken);
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // WebView exists but its first document has not completed loading yet.
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for visual route '{route}' to render.");
    }

    private static async Task<string> CaptureDomDiagnosticsAsync(CoreWebView2 core)
    {
        var raw = await core.ExecuteScriptAsync("""
            (() => JSON.stringify({
              title: document.title,
              href: location.href,
              theme: document.documentElement.dataset.theme || 'system',
              viewport: {
                width: window.innerWidth,
                height: window.innerHeight,
                devicePixelRatio: window.devicePixelRatio
              },
              document: {
                clientWidth: document.documentElement.clientWidth,
                scrollWidth: document.documentElement.scrollWidth,
                clientHeight: document.documentElement.clientHeight,
                scrollHeight: document.documentElement.scrollHeight
              },
              main: (() => {
                const el = document.querySelector('main');
                return el ? {
                  clientWidth: el.clientWidth,
                  scrollWidth: el.scrollWidth,
                  clientHeight: el.clientHeight,
                  scrollHeight: el.scrollHeight
                } : null;
              })(),
              overflowCandidates: [...document.querySelectorAll('main *')]
                .filter(el => el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 2)
                .slice(0, 80)
                .map(el => ({
                  tag: el.tagName.toLowerCase(),
                  classes: typeof el.className === 'string' ? el.className : '',
                  clientWidth: el.clientWidth,
                  scrollWidth: el.scrollWidth,
                  text: (el.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 120)
                }))
            }))()
            """);

        return JsonSerializer.Deserialize<string>(raw) ?? raw;
    }
}
