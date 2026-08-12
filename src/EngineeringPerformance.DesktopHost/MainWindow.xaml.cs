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

    /// <summary>
    /// Renders a deterministic set of real EOS routes through the WPF-hosted BlazorWebView and
    /// captures the WebView2 surface to PNG. This is intentionally inside the desktop host rather
    /// than a mock HTML harness: it exercises the exact Tailwind output, Blazor routing, chart JS,
    /// WebView2 runtime and desktop-host asset wiring that users actually run.
    /// </summary>
    public async Task<bool> CaptureVisualEvidenceAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var records = new List<VisualCaptureRecord>();

        try
        {
            await BlazorHost.WebView.EnsureCoreWebView2Async();
            var core = BlazorHost.WebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 did not initialize.");

            await InstallDiagnosticsAsync(core);
            await WaitForRouteAsync(core, "overview", TimeSpan.FromSeconds(30));

            var viewports = new[]
            {
                new Viewport(1536, 1024),
                new Viewport(1280, 800)
            };
            var routes = new[]
            {
                new CaptureRoute("overview", "/overview"),
                new CaptureRoute("employee", "/employee/Asha%20Nair"),
                new CaptureRoute("timesheets", "/timesheets"),
                new CaptureRoute("peer-insights", "/peer-insights")
            };

            foreach (var viewport in viewports)
            {
                foreach (var route in routes)
                {
                    records.Add(await CaptureAsync(core, outputDirectory, route, viewport, "light"));
                }
            }

            // Dark-mode coverage is concentrated on the two densest visual routes; all tokens are
            // still shared, while these screenshots catch the failure modes that matter most:
            // surface separation, semantic contrast, charts, dense labels and focus hierarchy.
            var large = viewports[0];
            records.Add(await CaptureAsync(core, outputDirectory, routes[0], large, "dark"));
            records.Add(await CaptureAsync(core, outputDirectory, routes[1], large, "dark"));

            var reportPath = Path.Combine(outputDirectory, "visual-report.json");
            await File.WriteAllTextAsync(
                reportPath,
                JsonSerializer.Serialize(new
                {
                    generatedUtc = DateTime.UtcNow,
                    machine = Environment.MachineName,
                    user = Environment.UserName,
                    records
                }, new JsonSerializerOptions { WriteIndented = true }));

            return records.All(x => x.Errors.Length == 0 && !x.HorizontalOverflow && x.ClippedPlateCount == 0);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "capture-failure.txt"),
                $"{DateTime.UtcNow:O}{Environment.NewLine}{exception}");
            return false;
        }
    }

    private async Task<VisualCaptureRecord> CaptureAsync(
        CoreWebView2 core,
        string outputDirectory,
        CaptureRoute route,
        Viewport viewport,
        string theme)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            WindowState = WindowState.Normal;
            Width = viewport.Width;
            Height = viewport.Height;
            Left = 0;
            Top = 0;
            UpdateLayout();
        });

        var themeJson = JsonSerializer.Serialize(theme);
        await core.ExecuteScriptAsync($"""
            (() => {{
              if (window.epaTheme && typeof window.epaTheme.set === 'function') window.epaTheme.set({themeJson});
              else {{ document.documentElement.dataset.theme = {themeJson}; localStorage.setItem('epa-theme', {themeJson}); }}
              window.__eosVisualErrors = [];
              return true;
            }})()
            """);

        var pathJson = JsonSerializer.Serialize(route.Path);
        await core.ExecuteScriptAsync($"""
            (() => {{
              if (location.pathname !== {pathJson}) {{
                history.pushState({{}}, '', {pathJson});
                window.dispatchEvent(new PopStateEvent('popstate'));
              }}
              return location.pathname;
            }})()
            """);

        await WaitForRouteAsync(core, route.Slug, TimeSpan.FromSeconds(20));
        await WaitForRenderSettleAsync(core);

        var fileName = $"{route.Slug}-{theme}-{viewport.Width}x{viewport.Height}.png";
        var screenshotPath = Path.Combine(outputDirectory, fileName);
        await using (var stream = File.Create(screenshotPath))
        {
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }

        var diagnosticsEncoded = await core.ExecuteScriptAsync("""
            (() => {
              const root = document.documentElement;
              const rootStyle = getComputedStyle(root);
              const visible = el => {
                const r = el.getBoundingClientRect();
                const s = getComputedStyle(el);
                return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && parseFloat(s.opacity || '1') > 0.05;
              };
              const plates = [...document.querySelectorAll('.plate')].filter(visible);
              const clipped = plates.filter(el => {
                const r = el.getBoundingClientRect();
                return r.left < -1 || r.right > innerWidth + 1;
              }).length;

              const parseColor = value => {
                if (!value || value === 'transparent') return null;
                const values = value.match(/[\d.]+/g)?.map(Number) || [];
                if (values.length < 3) return null;
                return { r: values[0], g: values[1], b: values[2], a: values.length >= 4 ? values[3] : 1 };
              };
              const channel = value => {
                const x = value / 255;
                return x <= 0.04045 ? x / 12.92 : Math.pow((x + 0.055) / 1.055, 2.4);
              };
              const luminance = color => 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
              const contrast = (left, right) => {
                const a = luminance(left), b = luminance(right);
                return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
              };
              const opaqueBackground = element => {
                let current = element;
                while (current) {
                  const color = parseColor(getComputedStyle(current).backgroundColor);
                  if (color && color.a >= 0.98) return color;
                  current = current.parentElement;
                }
                return parseColor(getComputedStyle(document.body).backgroundColor) || { r: 255, g: 255, b: 255, a: 1 };
              };
              const ownText = el => [...el.childNodes]
                .filter(node => node.nodeType === Node.TEXT_NODE)
                .map(node => node.textContent || '')
                .join(' ')
                .replace(/\s+/g, ' ')
                .trim();
              const sample = (el, text, fontSize, ratio) => ({
                Text: text.slice(0, 120),
                FontSize: Math.round(fontSize * 10) / 10,
                Ratio: ratio == null ? null : Math.round(ratio * 100) / 100,
                Tag: el.tagName.toLowerCase(),
                ClassName: String(el.className || '').slice(0, 220)
              });

              let tinyText = 0;
              let lowContrastText = 0;
              const tinyTextSamples = [];
              const lowContrastSamples = [];
              for (const el of document.querySelectorAll('body *')) {
                if (!visible(el)) continue;
                const text = ownText(el);
                if (!text) continue;
                const s = getComputedStyle(el);
                const fontSize = parseFloat(s.fontSize || '0');
                if (fontSize > 0 && fontSize < 11) {
                  tinyText++;
                  if (tinyTextSamples.length < 40) tinyTextSamples.push(sample(el, text, fontSize, null));
                }

                const foreground = parseColor(s.color);
                const background = opaqueBackground(el.parentElement || el);
                if (!foreground || foreground.a < 0.98 || !background) continue;
                const ratio = contrast(foreground, background);
                const weight = Number.parseInt(s.fontWeight, 10) || 400;
                const large = fontSize >= 24 || (fontSize >= 18.66 && weight >= 700);
                const required = large ? 3 : 4.5;
                if (ratio + 0.01 < required) {
                  lowContrastText++;
                  if (lowContrastSamples.length < 40) lowContrastSamples.push(sample(el, text, fontSize, ratio));
                }
              }

              return JSON.stringify({
                InnerWidth: innerWidth,
                InnerHeight: innerHeight,
                HorizontalOverflow: root.scrollWidth > innerWidth + 1,
                ClippedPlateCount: clipped,
                CanvasCount: document.querySelectorAll('canvas').length,
                SvgCount: document.querySelectorAll('svg').length,
                TinyTextCount: tinyText,
                TinyTextSamples: tinyTextSamples,
                LowContrastTextCount: lowContrastText,
                LowContrastSamples: lowContrastSamples,
                Errors: window.__eosVisualErrors || [],
                Tokens: {
                  canvas: rootStyle.getPropertyValue('--color-canvas').trim(),
                  chassis: rootStyle.getPropertyValue('--color-chassis').trim(),
                  surface: rootStyle.getPropertyValue('--color-surface').trim(),
                  ink: rootStyle.getPropertyValue('--color-ink').trim(),
                  muted: rootStyle.getPropertyValue('--color-muted').trim(),
                  uiMuted: rootStyle.getPropertyValue('--ui-muted').trim(),
                  primary: rootStyle.getPropertyValue('--color-primary').trim(),
                  line: rootStyle.getPropertyValue('--color-line').trim()
                }
              });
            })()
            """);

        var diagnosticsJson = JsonSerializer.Deserialize<string>(diagnosticsEncoded) ?? "{}";
        var diagnostics = JsonSerializer.Deserialize<BrowserDiagnostics>(diagnosticsJson)
            ?? new BrowserDiagnostics();

        return new VisualCaptureRecord(
            route.Slug,
            route.Path,
            theme,
            viewport.Width,
            viewport.Height,
            diagnostics.InnerWidth,
            diagnostics.InnerHeight,
            fileName,
            diagnostics.HorizontalOverflow,
            diagnostics.ClippedPlateCount,
            diagnostics.CanvasCount,
            diagnostics.SvgCount,
            diagnostics.TinyTextCount,
            diagnostics.TinyTextSamples ?? [],
            diagnostics.LowContrastTextCount,
            diagnostics.LowContrastSamples ?? [],
            diagnostics.Errors ?? [],
            diagnostics.Tokens ?? new Dictionary<string, string>());
    }

    private static async Task InstallDiagnosticsAsync(CoreWebView2 core)
    {
        await core.ExecuteScriptAsync("""
            (() => {
              if (window.__eosVisualHooksInstalled) return true;
              window.__eosVisualHooksInstalled = true;
              window.__eosVisualErrors = [];
              const stringify = value => {
                try { return typeof value === 'string' ? value : JSON.stringify(value); }
                catch { return String(value); }
              };
              window.addEventListener('error', event => {
                window.__eosVisualErrors.push(`window.error: ${event.message || 'unknown error'}`);
              });
              window.addEventListener('unhandledrejection', event => {
                window.__eosVisualErrors.push(`unhandledrejection: ${stringify(event.reason)}`);
              });
              const originalError = console.error.bind(console);
              console.error = (...args) => {
                window.__eosVisualErrors.push(`console.error: ${args.map(stringify).join(' ')}`);
                originalError(...args);
              };
              return true;
            })()
            """);
    }

    private static async Task WaitForRouteAsync(CoreWebView2 core, string slug, TimeSpan timeout)
    {
        var selector = JsonSerializer.Serialize($".route-{slug}");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await core.ExecuteScriptAsync($"document.querySelector({selector}) !== null");
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase)) return;
            await Task.Delay(150);
        }
        throw new TimeoutException($"Route '{slug}' did not render within {timeout.TotalSeconds:0} seconds.");
    }

    private static async Task WaitForRenderSettleAsync(CoreWebView2 core)
    {
        // Fonts + two animation frames cover layout; the short additional delay lets the local
        // lazy-loaded ECharts bundle paint its canvas before CapturePreviewAsync runs.
        await core.ExecuteScriptAsync("""
            (async () => {
              if (document.fonts?.ready) await document.fonts.ready;
              await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
              return true;
            })()
            """);
        await Task.Delay(1100);
    }

    private sealed record CaptureRoute(string Slug, string Path);
    private sealed record Viewport(int Width, int Height);

    private sealed record VisualCaptureRecord(
        string Route,
        string Path,
        string Theme,
        int RequestedWidth,
        int RequestedHeight,
        int ActualInnerWidth,
        int ActualInnerHeight,
        string Screenshot,
        bool HorizontalOverflow,
        int ClippedPlateCount,
        int CanvasCount,
        int SvgCount,
        int TinyTextCount,
        IReadOnlyList<TextAuditSample> TinyTextSamples,
        int LowContrastTextCount,
        IReadOnlyList<TextAuditSample> LowContrastSamples,
        string[] Errors,
        IReadOnlyDictionary<string, string> Tokens);

    private sealed record TextAuditSample(string Text, decimal FontSize, decimal? Ratio, string Tag, string ClassName);

    private sealed class BrowserDiagnostics
    {
        public int InnerWidth { get; init; }
        public int InnerHeight { get; init; }
        public bool HorizontalOverflow { get; init; }
        public int ClippedPlateCount { get; init; }
        public int CanvasCount { get; init; }
        public int SvgCount { get; init; }
        public int TinyTextCount { get; init; }
        public TextAuditSample[]? TinyTextSamples { get; init; }
        public int LowContrastTextCount { get; init; }
        public TextAuditSample[]? LowContrastSamples { get; init; }
        public string[]? Errors { get; init; }
        public Dictionary<string, string>? Tokens { get; init; }
    }
}
