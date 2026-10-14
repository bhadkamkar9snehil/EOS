using EOS.Domain.Audit;
using EOS.Domain.Hosting;
using EOS.Infrastructure.Configuration;
using EOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EOS.Infrastructure.Hosting;

public sealed class HostException(string message) : Exception(message);

public sealed record DiskView(string Drive, decimal FreeGb, decimal TotalGb, decimal FreeFraction, DateOnly CapturedOn);

public sealed record HostRow(
    int Id, string Kind, int? ParentHostId, string? ParentIp, string? Ip, string Name, string? Purpose, string State, decimal? RamGb, int? CpuCores,
    string? OwnerName, bool? NeedBackup, bool? MarkedForDeletion, bool Removed, DateOnly? LastBackupOn,
    IReadOnlyList<DiskView> Disks, IReadOnlyList<(int ProjectId, string ProjectNo, string Role)> Projects, int VmCount);

public sealed record HostPatch
{
    public string? Name { get; init; }
    public string? Purpose { get; init; }
    public string? State { get; init; }
    public string? OwnerName { get; init; }
    public bool? NeedBackup { get; init; }
    public bool? MarkedForDeletion { get; init; }
    public bool? Removed { get; init; }
    public DateOnly? LastBackupOn { get; init; }
    public string? Notes { get; init; }
}

/// <summary>Servers and VMs per project (spec §18). No credentials exist anywhere in this model.</summary>
public sealed class HostService(EosDbContext db)
{
    public async Task<IReadOnlyList<HostRow>> ListAsync(int? projectId = null, bool includeRemoved = false, CancellationToken ct = default)
    {
        var hosts = await db.Hosts.AsNoTracking().Where(h => includeRemoved || !h.Removed).OrderBy(h => h.Kind).ThenBy(h => h.Ip).ThenBy(h => h.Name).ToListAsync(ct);
        var disks = await db.HostDisks.AsNoTracking().ToListAsync(ct);
        var links = await db.ProjectHosts.AsNoTracking().ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.ProjectNo, ct);
        var byId = hosts.ToDictionary(h => h.Id);

        var rows = hosts.Select(h => new HostRow(h.Id, h.Kind, h.ParentHostId, h.ParentHostId is { } pid && byId.TryGetValue(pid, out var p) ? p.Ip : null, h.Ip, h.Name, h.Purpose, h.State, h.RamGb, h.CpuCores,
            h.OwnerName, h.NeedBackup, h.MarkedForDeletion, h.Removed, h.LastBackupOn,
            disks.Where(d => d.HostId == h.Id).GroupBy(d => d.Drive).Select(g => g.OrderByDescending(d => d.CapturedOn).First())
                 .Select(d => new DiskView(d.Drive, d.FreeGb, d.TotalGb, d.TotalGb == 0 ? 0 : Math.Round(d.FreeGb / d.TotalGb, 4), d.CapturedOn)).OrderBy(d => d.Drive).ToList(),
            links.Where(l => l.HostId == h.Id).Select(l => (l.ProjectId, projects.GetValueOrDefault(l.ProjectId, "?"), l.Role)).ToList(),
            hosts.Count(x => x.ParentHostId == h.Id))).ToList();
        return projectId is { } id ? rows.Where(r => r.Projects.Any(p => p.ProjectId == id)).ToList() : rows;
    }

    /// <summary>Off, unknown purpose and/or marked for deletion: candidates to reclaim (spec §18.4).</summary>
    public async Task<IReadOnlyList<HostRow>> ReclaimListAsync(CancellationToken ct = default) =>
        (await ListAsync(ct: ct)).Where(h => h.Kind == "vm" && h.State == "Off" && (h.MarkedForDeletion == true || string.Equals(h.Purpose, "Unknown", StringComparison.OrdinalIgnoreCase)) && h.Projects.Count == 0).ToList();

    public async Task UpdateAsync(int id, HostPatch patch, string actor, CancellationToken ct = default)
    {
        var h = await db.Hosts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new HostException($"Host {id} not found.");
        if (patch.Name is not null) h.Name = patch.Name;
        if (patch.Purpose is not null)
        {
            if (!await ConfigurationService.IsValidAsync(db, EOS.Domain.Configuration.Vocabularies.HostPurpose, patch.Purpose, ct)) throw new HostException($"'{patch.Purpose}' is not an allowed purpose.");
            h.Purpose = patch.Purpose;
        }
        if (patch.State is not null)
        {
            if (!await ConfigurationService.IsValidAsync(db, EOS.Domain.Configuration.Vocabularies.HostState, patch.State, ct)) throw new HostException($"'{patch.State}' is not an allowed state.");
            h.State = patch.State;
        }
        if (patch.OwnerName is not null) h.OwnerName = patch.OwnerName.Length == 0 ? null : patch.OwnerName;
        if (patch.NeedBackup is { } nb) h.NeedBackup = nb;
        if (patch.MarkedForDeletion is { } md) h.MarkedForDeletion = md;
        if (patch.Removed is { } rm) h.Removed = rm;
        if (patch.LastBackupOn is { } lb) h.LastBackupOn = lb;
        if (patch.Notes is not null) h.Notes = patch.Notes;
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "host.update", SubjectKind = "host", SubjectId = h.Name, Detail = System.Text.Json.JsonSerializer.Serialize(patch) });
        await db.SaveChangesAsync(ct);
    }

    public async Task LinkAsync(int projectId, int hostId, string role, string actor, CancellationToken ct = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new HostException($"Project {projectId} not found.");
        if (!await db.Hosts.AnyAsync(h => h.Id == hostId, ct)) throw new HostException($"Host {hostId} not found.");
        if (await db.ProjectHosts.AnyAsync(l => l.ProjectId == projectId && l.HostId == hostId && l.Role == role, ct)) return;
        db.ProjectHosts.Add(new ProjectHost { ProjectId = projectId, HostId = hostId, Role = role });
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "host.link", SubjectKind = "host", SubjectId = hostId.ToString(), Detail = $"project {projectId} as {role}" });
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlinkAsync(int projectId, int hostId, string role, string actor, CancellationToken ct = default)
    {
        var link = await db.ProjectHosts.SingleOrDefaultAsync(l => l.ProjectId == projectId && l.HostId == hostId && l.Role == role, ct);
        if (link is null) return;
        db.ProjectHosts.Remove(link);
        db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = actor, Action = "host.unlink", SubjectKind = "host", SubjectId = hostId.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HostedSystem>> SystemsAsync(int? projectId = null, CancellationToken ct = default) =>
        await db.HostedSystems.AsNoTracking().Where(s => projectId == null || s.ProjectId == projectId).OrderBy(s => s.Customer).ToListAsync(ct);
}
