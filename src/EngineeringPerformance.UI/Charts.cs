using System.Text;
using EngineeringPerformance.Application;
using Microsoft.AspNetCore.Components;

namespace EngineeringPerformance.UI;

/// <summary>
/// Builds the Overview's SVG charts as markup. Razor reserves the &lt;text&gt;
/// element, so SVG that carries axis labels is assembled here rather than in
/// component markup.
/// </summary>
public static class Charts
{
    private const string Blue = "#2a78d6";     // categorical slot 1
    private const string Orange = "#eb6834";   // categorical slot 2
    private const string Aqua = "#1baf7a";     // categorical slot 3
    private const string Grid = "#e1e0d9";
    private const string Axis = "#c3c2b7";
    private const string Muted = "#898781";

    private static string N(double value) => Analytics.Svg(value);

    private static string Escape(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void Text(StringBuilder sb, double x, double y, string content, string anchor = "middle", string cls = "tick") =>
        sb.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" text-anchor=\"{anchor}\" class=\"{cls}\">{Escape(content)}</text>");

    /// <summary>Utilization against average daily punch hours, split into workload quadrants.</summary>
    public static MarkupString Scatter(IReadOnlyList<MonthlyPerformanceItem> items)
    {
        const double left = 46, right = 410, top = 14, bottom = 212, maxHours = 12, maxUtil = 130;
        double X(double hours) => left + Math.Clamp(hours, 0, maxHours) / maxHours * (right - left);
        double Y(double utilization) => bottom - Math.Clamp(utilization, 0, maxUtil) / maxUtil * (bottom - top);

        var sb = new StringBuilder();
        sb.Append("<svg class=\"chart\" viewBox=\"0 0 420 250\" role=\"img\" aria-label=\"Utilization against daily workload\">");

        foreach (var line in new[] { 0, 25, 50, 75, 100, 125 })
        {
            sb.Append($"<line x1=\"{N(left)}\" y1=\"{N(Y(line))}\" x2=\"{N(right)}\" y2=\"{N(Y(line))}\" stroke=\"{Grid}\" stroke-width=\"1\" />");
            Text(sb, left - 6, Y(line) + 3.5, $"{line}%", "end");
        }

        var target = Y((double)Analytics.UtilizationTarget);
        sb.Append($"<line x1=\"{N(left)}\" y1=\"{N(target)}\" x2=\"{N(right)}\" y2=\"{N(target)}\" stroke=\"{Muted}\" stroke-width=\"1\" stroke-dasharray=\"4 3\" />");

        var ceiling = X((double)Analytics.WorkloadCeiling);
        sb.Append($"<line x1=\"{N(ceiling)}\" y1=\"{N(top)}\" x2=\"{N(ceiling)}\" y2=\"{N(bottom)}\" stroke=\"{Muted}\" stroke-width=\"1\" stroke-dasharray=\"4 3\" />");
        Text(sb, ceiling + 3, top + 10, $"{Analytics.Format(Analytics.WorkloadCeiling, "0")} h/day", "start");

        sb.Append($"<line x1=\"{N(left)}\" y1=\"{N(bottom)}\" x2=\"{N(right)}\" y2=\"{N(bottom)}\" stroke=\"{Axis}\" stroke-width=\"1\" />");
        sb.Append($"<line x1=\"{N(left)}\" y1=\"{N(top)}\" x2=\"{N(left)}\" y2=\"{N(bottom)}\" stroke=\"{Axis}\" stroke-width=\"1\" />");

        foreach (var hours in new[] { 0, 3, 6, 9, 12 }) Text(sb, X(hours), bottom + 16, hours.ToString());
        Text(sb, (left + right) / 2, 245, "Average punch hours per accountable day", "middle", "axis-title");

        foreach (var item in items)
        {
            var color = item.WorkloadBand() switch { "optimal" => Aqua, "high" => Orange, _ => Blue };
            sb.Append($"<circle cx=\"{N(X((double)item.Workload()))}\" cy=\"{N(Y((double)item.Utilization))}\" r=\"6\" fill=\"{color}\" stroke=\"#ffffff\" stroke-width=\"2\">");
            sb.Append($"<title>{Escape(item.EmployeeName)} — {Analytics.Format(item.Utilization)}% utilization, {Analytics.Format(item.Workload())} h/day</title>");
            sb.Append("</circle>");
        }

        sb.Append("</svg>");
        return new MarkupString(sb.ToString());
    }

    /// <summary>Team average against top and bottom quartile across the operational dimensions.</summary>
    public static MarkupString Radar(IReadOnlyList<MonthlyPerformanceItem> items)
    {
        var axes = Analytics.Radar(items);
        if (axes.Count == 0) return new MarkupString(string.Empty);
        const double cx = 150, cy = 118, radius = 84;

        (double X, double Y) Point(int index, double r)
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * index / axes.Count;
            return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }

        string Ring(double r) => string.Join(" ", Enumerable.Range(0, axes.Count).Select(i =>
        {
            var (x, y) = Point(i, r);
            return $"{N(x)},{N(y)}";
        }));

        string Series(Func<RadarAxis, decimal> selector) => string.Join(" ", axes.Select((axis, i) =>
        {
            var (x, y) = Point(i, radius * (double)selector(axis) / 100d);
            return $"{N(x)},{N(y)}";
        }));

        var sb = new StringBuilder();
        sb.Append("<svg class=\"chart\" viewBox=\"0 0 300 240\" role=\"img\" aria-label=\"Category performance radar\">");
        foreach (var ring in new[] { 0.25, 0.5, 0.75, 1.0 })
            sb.Append($"<polygon points=\"{Ring(radius * ring)}\" fill=\"none\" stroke=\"{Grid}\" stroke-width=\"1\" />");

        for (var i = 0; i < axes.Count; i++)
        {
            var (x, y) = Point(i, radius);
            var (lx, ly) = Point(i, radius + 20);
            sb.Append($"<line x1=\"{N(cx)}\" y1=\"{N(cy)}\" x2=\"{N(x)}\" y2=\"{N(y)}\" stroke=\"{Grid}\" stroke-width=\"1\" />");
            Text(sb, lx, ly + 3, axes[i].Label);
        }

        sb.Append($"<polygon points=\"{Series(a => a.BottomQuartile)}\" fill=\"{Aqua}\" fill-opacity=\"0.12\" stroke=\"{Aqua}\" stroke-width=\"2\" />");
        sb.Append($"<polygon points=\"{Series(a => a.TopQuartile)}\" fill=\"{Orange}\" fill-opacity=\"0.12\" stroke=\"{Orange}\" stroke-width=\"2\" />");
        sb.Append($"<polygon points=\"{Series(a => a.TeamAverage)}\" fill=\"{Blue}\" fill-opacity=\"0.16\" stroke=\"{Blue}\" stroke-width=\"2\" />");
        sb.Append("</svg>");
        return new MarkupString(sb.ToString());
    }

    /// <summary>Monthly team average with a least-squares projection of the next month.</summary>
    public static MarkupString Trend(IReadOnlyList<DateTime> months, IReadOnlyList<decimal> series)
    {
        if (series.Count == 0) return new MarkupString(string.Empty);
        var projected = Analytics.Forecast(series);
        var span = Math.Max(1, series.Count - 1 + (projected is null ? 0 : 1));
        const double left = 60, right = 880, top = 20, bottom = 180;
        double X(int i) => left + (double)i / span * (right - left);
        double Y(decimal score) => bottom - (double)score / 100d * (bottom - top);

        var sb = new StringBuilder();
        sb.Append("<svg class=\"chart trend\" viewBox=\"0 0 900 220\" role=\"img\" aria-label=\"Team score trend and forecast\">");
        foreach (var line in new[] { 0, 25, 50, 75, 100 })
        {
            sb.Append($"<line x1=\"{N(left)}\" y1=\"{N(Y(line))}\" x2=\"{N(right)}\" y2=\"{N(Y(line))}\" stroke=\"{Grid}\" stroke-width=\"1\" />");
            Text(sb, left - 8, Y(line) + 4, line.ToString(), "end");
        }

        var path = string.Join(" ", series.Select((s, i) => $"{(i == 0 ? "M" : "L")}{N(X(i))},{N(Y(s))}"));
        sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{Blue}\" stroke-width=\"2\" stroke-linejoin=\"round\" />");

        if (projected is not null)
        {
            sb.Append($"<path d=\"M{N(X(series.Count - 1))},{N(Y(series[^1]))} L{N(X(series.Count))},{N(Y(projected.Value))}\" fill=\"none\" stroke=\"{Blue}\" stroke-width=\"2\" stroke-dasharray=\"6 4\" />");
            sb.Append($"<circle cx=\"{N(X(series.Count))}\" cy=\"{N(Y(projected.Value))}\" r=\"6\" fill=\"#ffffff\" stroke=\"{Blue}\" stroke-width=\"2\" />");
            Text(sb, X(series.Count), Y(projected.Value) - 13, Analytics.Format(projected.Value), "middle", "point-label");
            Text(sb, X(series.Count), 202, "Forecast");
        }

        for (var i = 0; i < series.Count; i++)
        {
            sb.Append($"<circle cx=\"{N(X(i))}\" cy=\"{N(Y(series[i]))}\" r=\"6\" fill=\"{Blue}\" stroke=\"#ffffff\" stroke-width=\"2\">");
            sb.Append($"<title>{Escape(months[i].ToString("MMMM yyyy"))}: {Analytics.Format(series[i])}</title>");
            sb.Append("</circle>");
            Text(sb, X(i), 202, months[i].ToString("MMM yy"));
        }

        sb.Append("</svg>");
        return new MarkupString(sb.ToString());
    }
}
