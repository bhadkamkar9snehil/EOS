using System.Text.RegularExpressions;
using EOS.Domain.Hosting;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Importing;

public sealed partial class InfrastructureImporter(EosDbContext db)
{
    private static readonly string[] CredentialHints = ["user", "login", "password", "pwd", "secret", "credential", "token", "passwd", " sa"];

    public Task<ImportReport> ImportAsync(string path, bool commit, string actor, CancellationToken ct = default) =>
        ImportRunner.RunAsync(db, "server and vm inventory", path, commit, actor, report => WorkAsync(path, report, ct), ct);

    private async Task WorkAsync(string path, ImportReport report, CancellationToken ct)
    {
        using var wb = WorkbookReading.Open(path);
        var hosts = await db.Hosts.ToListAsync(ct);

        ImportPhysical(wb.Sheet("New Physical Server Details") ?? wb.Sheet("Physical Server Details (2)"), hosts, report);
        await db.SaveChangesAsync(ct);

        ImportVmList(wb.Sheet("Sheet1"), hosts, report);
        await db.SaveChangesAsync(ct);

        await LinkProjectsAsync(hosts, report, ct);
        ImportSystems(wb.Sheet("ALL Demos"), hosts, report);
        await db.SaveChangesAsync(ct);
    }

    private void ImportPhysical(ClosedXML.Excel.IXLWorksheet? ws, List<Host> hosts, ImportReport report)
    {
        if (ws is null) { report.Warnings.Add("Physical server sheet not found."); return; }
        var cols = Whitelist(ws, 1, "server type", "ip address", "used for", "c drive remaining/total", "d drive remaining/total",
            "e drive remaining/total", "f drive remaining/total", "total ram", "cpu cores", "logical processors");
        if (!cols.ContainsKey("ip address")) { report.Warnings.Add("Physical sheet: 'IP Address' not found."); return; }

        var today = DateOnly.FromDateTime(DateTime.Today);
        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var ip = Cell(ws, r, cols, "ip address");
            if (!IpPattern().IsMatch(ip)) continue;                               // "Not Access" and blank rows
            var kind = Cell(ws, r, cols, "server type");
            var isVm = kind.StartsWith("VM", StringComparison.OrdinalIgnoreCase);
            var purpose = Nullable(Cell(ws, r, cols, "used for"));

            var host = hosts.FirstOrDefault(h => h.Ip == ip);
            if (host is null)
            {
                host = new Host { Ip = ip, Name = purpose ?? ip, Kind = isVm ? "vm" : "physical" };
                db.Hosts.Add(host);
                hosts.Add(host);
                report.Added(isVm ? "VM" : "Host");
            }
            else report.Unchanged(isVm ? "VM" : "Host");
            host.Purpose ??= purpose;
            if (isVm && Regex.Match(kind, @"\d+\.\d+\.\d+\.\d+") is { Success: true } parent)
            {
                var parentHost = hosts.FirstOrDefault(h => h.Ip == parent.Value);
                if (parentHost is not null && host.ParentHostId != parentHost.Id && parentHost.Id != 0) host.ParentHostId = parentHost.Id;
            }
            if (!isVm)
            {
                host.RamGb = ParseGb(Cell(ws, r, cols, "total ram")) ?? host.RamGb;
                host.CpuCores = (int?)ParseGb(Cell(ws, r, cols, "cpu cores")) ?? host.CpuCores;
                host.LogicalProcessors = (int?)ParseGb(Cell(ws, r, cols, "logical processors")) ?? host.LogicalProcessors;
            }
        }
        db.SaveChanges();

        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var ip = Cell(ws, r, cols, "ip address");
            var host = hosts.FirstOrDefault(h => h.Ip == ip && h.Kind == "physical");
            if (host is null) continue;
            foreach (var (header, drive) in new[] { ("c drive remaining/total", "C"), ("d drive remaining/total", "D"), ("e drive remaining/total", "E"), ("f drive remaining/total", "F") })
            {
                var m = DiskPattern().Match(Cell(ws, r, cols, header));
                if (!m.Success) continue;
                var free = decimal.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var total = decimal.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var current = db.HostDisks.Where(d => d.HostId == host.Id && d.Drive == drive).OrderByDescending(d => d.CapturedOn).FirstOrDefault();
                if (current is not null && current.FreeGb == free && current.TotalGb == total) { report.Unchanged("Disk"); continue; }
                db.HostDisks.Add(new HostDisk { HostId = host.Id, Drive = drive, FreeGb = free, TotalGb = total, CapturedOn = today });
                report.Added("Disk");
            }
        }
    }

    private void ImportVmList(ClosedXML.Excel.IXLWorksheet? ws, List<Host> hosts, ImportReport report)
    {
        if (ws is null) { report.Warnings.Add("VM list sheet ('Sheet1') not found."); return; }
        var cols = Whitelist(ws, 1, "host ip", "vm ip", "virtual machine", "state", "project / purpose", "assigned to", "need backup", "isdelete vm", "vm removed");
        if (!cols.ContainsKey("virtual machine")) { report.Warnings.Add("VM list: 'Virtual Machine' not found."); return; }
        var seenVms = new HashSet<Host>();

        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var name = Cell(ws, r, cols, "virtual machine");
            if (name.Length == 0) continue;
            var hostIp = Cell(ws, r, cols, "host ip");
            var vmIp = Cell(ws, r, cols, "vm ip");
            var parent = hosts.FirstOrDefault(h => h.Ip == hostIp && h.Kind == "physical");
            if (parent is null && IpPattern().IsMatch(hostIp))
            {
                parent = new Host { Kind = "physical", Ip = hostIp, Name = hostIp };
                db.Hosts.Add(parent);
                db.SaveChanges();
                hosts.Add(parent);
                report.Added("Host");
            }

            var vm = hosts.FirstOrDefault(h => h.Kind == "vm" && (IpPattern().IsMatch(vmIp) && h.Ip == vmIp || h.Name == name));
            if (vm is not null && !seenVms.Add(vm))
            {
                report.Warnings.Add($"VM list row {r}: '{name}' appears more than once; the later row wins.");
            }
            var isNew = vm is null;
            if (isNew)
            {
                vm = new Host { Kind = "vm", Name = name };
                db.Hosts.Add(vm);
                hosts.Add(vm);
                seenVms.Add(vm);
            }
            vm!.Name = name;
            if (IpPattern().IsMatch(vmIp)) vm.Ip = vmIp;
            if (parent is not null) vm.ParentHostId = parent.Id;
            vm.State = NormaliseState(Cell(ws, r, cols, "state"));
            vm.Purpose = Nullable(Cell(ws, r, cols, "project / purpose")) ?? vm.Purpose;
            vm.OwnerName = Nullable(Cell(ws, r, cols, "assigned to")) ?? vm.OwnerName;
            vm.NeedBackup = YesNo(Cell(ws, r, cols, "need backup")) ?? vm.NeedBackup;
            vm.MarkedForDeletion = YesNo(Cell(ws, r, cols, "isdelete vm")) ?? vm.MarkedForDeletion;
            vm.Removed = Cell(ws, r, cols, "vm removed") is "1" or "True" or "true" or "Yes";

            if (isNew) report.Added("VM");
            else
            {
                db.ChangeTracker.DetectChanges();
                if (db.Entry(vm).State == EntityState.Modified) report.Updated("VM"); else report.Unchanged("VM");
            }
        }
    }

    private async Task LinkProjectsAsync(List<Host> hosts, ImportReport report, CancellationToken ct)
    {
        var projects = await db.Projects.AsNoTracking().Where(p => p.ProjectNo != "").ToListAsync(ct);
        var links = await db.ProjectHosts.ToListAsync(ct);
        foreach (var vm in hosts.Where(h => h.Kind == "vm" && h.Id != 0))
        {
            foreach (var p in projects.Where(p => vm.Name.Contains(p.ProjectNo, StringComparison.OrdinalIgnoreCase)))
            {
                var role = vm.Name.Contains("Database", StringComparison.OrdinalIgnoreCase) ? "database"
                    : vm.Name.Contains("Publish", StringComparison.OrdinalIgnoreCase) ? "publish" : "application";
                if (links.Any(l => l.ProjectId == p.Id && l.HostId == vm.Id && l.Role == role)) { report.Unchanged("Project link"); continue; }
                var link = new ProjectHost { ProjectId = p.Id, HostId = vm.Id, Role = role };
                db.ProjectHosts.Add(link);
                links.Add(link);
                report.Added("Project link");
            }
        }
    }

    private void ImportSystems(ClosedXML.Excel.IXLWorksheet? ws, List<Host> hosts, ImportReport report)
    {
        if (ws is null) return;
        var headerRow = Enumerable.Range(1, 20).FirstOrDefault(r => ws.Text(r, 2).StartsWith("Product", StringComparison.OrdinalIgnoreCase));
        if (headerRow == 0) { report.Warnings.Add("ALL Demos: header row not found; skipped."); return; }
        var cols = Whitelist(ws, headerRow, "product / module", "customer / demo name", "server / vm ip", "system", "internal url", "external url");
        var existing = db.HostedSystems.ToList();
        for (var r = headerRow + 1; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            var product = Cell(ws, r, cols, "product / module");
            var customer = Cell(ws, r, cols, "customer / demo name");
            if (product.Length == 0 && customer.Length == 0) continue;
            var ip = Cell(ws, r, cols, "server / vm ip");
            var internalUrl = Nullable(Cell(ws, r, cols, "internal url"));
            var found = existing.FirstOrDefault(s => s.InternalUrl == internalUrl && s.Customer == customer && s.Product == product);
            if (found is not null) { report.Unchanged("Hosted system"); continue; }
            var system = new HostedSystem
            {
                Product = Nullable(product), Customer = Nullable(customer), SystemName = Nullable(Cell(ws, r, cols, "system")),
                InternalUrl = internalUrl, ExternalUrl = Nullable(Cell(ws, r, cols, "external url")), Purpose = "Demo",
                HostId = hosts.FirstOrDefault(h => h.Ip == ip)?.Id,
            };
            db.HostedSystems.Add(system);
            existing.Add(system);
            report.Added("Hosted system");
        }
    }

    private static Dictionary<string, int> Whitelist(ClosedXML.Excel.IXLWorksheet ws, int headerRow, params string[] wanted)
    {
        foreach (var w in wanted)
            if (CredentialHints.Any(h => w.Contains(h.Trim(), StringComparison.OrdinalIgnoreCase)) && !w.Contains("used for", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to read a credential-like column: '{w}'.");

        var all = ErpProjectImporter.HeaderMap(ws, headerRow);
        return wanted.Where(all.ContainsKey).ToDictionary(w => w, w => all[w], StringComparer.OrdinalIgnoreCase);
    }

    private static string Cell(ClosedXML.Excel.IXLWorksheet ws, int row, Dictionary<string, int> cols, string header) =>
        cols.TryGetValue(header, out var c) ? ws.Text(row, c) : "";

    private static string NormaliseState(string s) => s.Trim().ToLowerInvariant() switch
    {
        "running" => "Running",
        "off" => "Off",
        "" => "Unknown",
        var x when x.Contains("access") => "No access",
        _ => "Unknown",
    };

    private static bool? YesNo(string s) => s.Trim().ToLowerInvariant() switch { "yes" or "y" or "true" or "1" => true, "no" or "n" or "false" or "0" => false, _ => null };

    private static decimal? ParseGb(string s)
    {
        var m = Regex.Match(s, @"[\d.]+");
        return m.Success && decimal.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? Nullable(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,3}){3}$")]
    private static partial Regex IpPattern();

    [GeneratedRegex(@"([\d.]+)\s*/\s*([\d.]+)\s*GB", RegexOptions.IgnoreCase)]
    private static partial Regex DiskPattern();
}
