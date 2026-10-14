namespace EOS.Domain.Hosting;

/// <summary>
/// A physical server or VM. There are deliberately NO credential fields anywhere in this model (spec §2.4, §24.9).
/// </summary>
public sealed class Host
{
    public int Id { get; set; }

    /// <summary>"physical" or "vm".</summary>
    public string Kind { get; set; } = "vm";
    public int? ParentHostId { get; set; }
    public string? Ip { get; set; }
    public string Name { get; set; } = "";
    public string? Purpose { get; set; }
    public string State { get; set; } = "Unknown";
    public decimal? RamGb { get; set; }
    public int? CpuCores { get; set; }
    public int? LogicalProcessors { get; set; }
    public string? OwnerName { get; set; }
    public bool? NeedBackup { get; set; }
    public bool? MarkedForDeletion { get; set; }
    public bool Removed { get; set; }
    public DateOnly? LastBackupOn { get; set; }
    public string? Notes { get; set; }
}

public sealed class HostDisk
{
    public int Id { get; set; }
    public int HostId { get; set; }
    public string Drive { get; set; } = "";
    public decimal FreeGb { get; set; }
    public decimal TotalGb { get; set; }
    public DateOnly CapturedOn { get; set; }
}

public sealed class ProjectHost
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int HostId { get; set; }

    /// <summary>application, database, publish, test, demo…</summary>
    public string Role { get; set; } = "application";
    public string? Notes { get; set; }
}

/// <summary>A published or demo system (URLs only, never credentials).</summary>
public sealed class HostedSystem
{
    public int Id { get; set; }
    public int? HostId { get; set; }
    public int? ProjectId { get; set; }
    public string? Product { get; set; }
    public string? Customer { get; set; }
    public string? SystemName { get; set; }
    public string? InternalUrl { get; set; }
    public string? ExternalUrl { get; set; }
    public string? Purpose { get; set; }
}
