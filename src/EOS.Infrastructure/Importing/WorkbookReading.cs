using ClosedXML.Excel;

namespace EOS.Infrastructure.Importing;

/// <summary>Small helpers shared by the workbook importers. Reads only; never writes a source file.</summary>
internal static class WorkbookReading
{
    /// <summary>
    /// Opens a workbook even when Excel has it open (shared read). The file is copied into memory and closed at once,
    /// so a source file is never left locked by an import (important for a long-running server).
    /// </summary>
    public static XLWorkbook Open(string path)
    {
        var buffer = new MemoryStream();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            file.CopyTo(buffer);
        buffer.Position = 0;
        return new XLWorkbook(buffer);
    }

    public static IXLWorksheet? Sheet(this XLWorkbook wb, string name) =>
        wb.Worksheets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string Text(this IXLWorksheet ws, int row, int col) =>
        ws.Cell(row, col).GetString().Replace(' ', ' ').Trim();

    public static decimal? Number(this IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<double>(out var d)) return (decimal)d;
        var s = cell.GetString().Trim().TrimEnd('%');
        return decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? (cell.GetString().Contains('%') ? v / 100m : v) : null;
    }

    public static DateOnly? Date(this IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<DateTime>(out var dt)) return DateOnly.FromDateTime(dt);
        return ParseDateText(cell.GetString());
    }

    private static readonly string[] DateFormats =
    [
        "dd-MMM-yy", "dd-MMM-yyyy", "d-MMM-yy", "d-MMM-yyyy", "dd-MMMM-yy", "dd-MMMM-yyyy", "d-MMMM-yy", "d-MMMM-yyyy",
        "dd MMM yyyy", "d MMM yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "dd.MM.yyyy",
    ];

    /// <summary>Tolerant text date reader: "05-Sept-26", "17-July-26", "14-09-2026", "2026-09-14". Returns null for 0, "-" and junk.</summary>
    public static DateOnly? ParseDateText(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length < 6) return null;                                  // "0", "-", "NA"
        s = System.Text.RegularExpressions.Regex.Replace(s, "(?i)sept(?!ember)", "Sep");
        return DateOnly.TryParseExact(s, DateFormats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>First row (within the first <paramref name="scan"/> rows) whose column A equals <paramref name="header"/>.</summary>
    public static int? FindHeaderRow(this IXLWorksheet ws, string header, int scan = 15)
    {
        for (var r = 1; r <= scan; r++)
            if (string.Equals(ws.Text(r, 1), header, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    public static int LastColumn(this IXLWorksheet ws, int headerRow) =>
        ws.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0;
}
