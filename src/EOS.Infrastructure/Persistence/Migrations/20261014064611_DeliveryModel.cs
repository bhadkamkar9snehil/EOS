using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Allocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    DemandId = table.Column<int>(type: "INTEGER", nullable: true),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Hours = table.Column<decimal>(type: "TEXT", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AmcTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectNo = table.Column<string>(type: "TEXT", nullable: true),
                    Product = table.Column<string>(type: "TEXT", nullable: true),
                    InAmc = table.Column<bool>(type: "INTEGER", nullable: false),
                    RaisedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RaisedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Issue = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AssignedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ResolvedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AssignedEngineers = table.Column<string>(type: "TEXT", nullable: true),
                    RootCause = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmcTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    HoursPerDay = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    WorkDays = table.Column<string>(type: "TEXT", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeGapLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    DemandId = table.Column<int>(type: "INTEGER", nullable: true),
                    RaisedBy = table.Column<string>(type: "TEXT", nullable: false),
                    RaisedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    GapType = table.Column<string>(type: "TEXT", nullable: true),
                    ImpactHours = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    ImpactDays = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", nullable: true),
                    Approver = table.Column<string>(type: "TEXT", nullable: true),
                    DecisionOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeGapLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityKind = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Options = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityKind = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Demands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    OpportunityId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkillId = table.Column<int>(type: "INTEGER", nullable: false),
                    MinLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    HoursPerWeek = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: true),
                    StatusOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostDisks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Drive = table.Column<string>(type: "TEXT", nullable: false),
                    FreeGb = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    TotalGb = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    CapturedOn = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostDisks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostedSystems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    Product = table.Column<string>(type: "TEXT", nullable: true),
                    Customer = table.Column<string>(type: "TEXT", nullable: true),
                    SystemName = table.Column<string>(type: "TEXT", nullable: true),
                    InternalUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedSystems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Hosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ParentHostId = table.Column<int>(type: "INTEGER", nullable: true),
                    Ip = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    RamGb = table.Column<decimal>(type: "TEXT", precision: 8, scale: 2, nullable: true),
                    CpuCores = table.Column<int>(type: "INTEGER", nullable: true),
                    LogicalProcessors = table.Column<int>(type: "INTEGER", nullable: true),
                    OwnerName = table.Column<string>(type: "TEXT", nullable: true),
                    NeedBackup = table.Column<bool>(type: "INTEGER", nullable: true),
                    MarkedForDeletion = table.Column<bool>(type: "INTEGER", nullable: true),
                    Removed = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastBackupOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leaves", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    ReplacementPersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    ScenarioId = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ApprovedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Opportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LeadName = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedValueLakh = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    ExpectedHours = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    LikelyBookingMonth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DurationMonths = table.Column<int>(type: "INTEGER", nullable: true),
                    Stage = table.Column<string>(type: "TEXT", nullable: true),
                    WinProbability = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredSkills = table.Column<string>(type: "TEXT", nullable: true),
                    ConvertedProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSample = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectHosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectHosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectNo = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Customer = table.Column<string>(type: "TEXT", nullable: true),
                    ProductLine = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectType = table.Column<string>(type: "TEXT", nullable: true),
                    ErpStatus = table.Column<string>(type: "TEXT", nullable: true),
                    TrackerStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectManager = table.Column<string>(type: "TEXT", nullable: true),
                    TechnicalManager = table.Column<string>(type: "TEXT", nullable: true),
                    TechnicalManagerPersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    OrderValue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MarginPercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true),
                    PoStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PoEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ContractualEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PlannedEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SiteLocation = table.Column<string>(type: "TEXT", nullable: true),
                    ParentProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    CalendarId = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthOverride = table.Column<string>(type: "TEXT", nullable: true),
                    HealthOverrideReason = table.Column<string>(type: "TEXT", nullable: true),
                    ProgressOverride = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: true),
                    LastStatusUpdate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSample = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: true),
                    Owner = table.Column<string>(type: "TEXT", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Mitigation = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MovesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    DecisionBy = table.Column<string>(type: "TEXT", nullable: true),
                    DecisionUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vocabulary",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vocabulary", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalendarExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CalendarId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsWorking = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarExceptions_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Baselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Baselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Baselines_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Module = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    WorkHours = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    WorkIsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    FixedDurationDays = table.Column<int>(type: "INTEGER", nullable: true),
                    StartNoEarlierThan = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PercentComplete = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalKey = table.Column<string>(type: "TEXT", nullable: true),
                    LockedFields = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PlannedFinish = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ActualStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ActualFinish = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ScheduledStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ScheduledFinish = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TotalFloatDays = table.Column<int>(type: "INTEGER", nullable: true),
                    IsCritical = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Tasks_Tasks_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BaselineTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaselineId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    WbsCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Start = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Finish = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WorkHours = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PercentComplete = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaselineTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaselineTasks_Baselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "Baselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    Units = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PlaceholderSkillId = table.Column<int>(type: "INTEGER", nullable: true),
                    PlaceholderMinLevel = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    PredecessorId = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    LagDays = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLinks_Tasks_PredecessorId",
                        column: x => x.PredecessorId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskLinks_Tasks_SuccessorId",
                        column: x => x.SuccessorId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_PersonId_ProjectId_Month_Status",
                table: "Allocations",
                columns: new[] { "PersonId", "ProjectId", "Month", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AmcTickets_ProjectNo",
                table: "AmcTickets",
                column: "ProjectNo");

            migrationBuilder.CreateIndex(
                name: "IX_Baselines_ProjectId_Number",
                table: "Baselines",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaselineTasks_BaselineId",
                table: "BaselineTasks",
                column: "BaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarExceptions_CalendarId_Date",
                table: "CalendarExceptions",
                columns: new[] { "CalendarId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_Name",
                table: "Calendars",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinitions_EntityKind_Key",
                table: "CustomFieldDefinitions",
                columns: new[] { "EntityKind", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_EntityKind_EntityId_Key",
                table: "CustomFieldValues",
                columns: new[] { "EntityKind", "EntityId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Demands_ProjectId",
                table: "Demands",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_HostDisks_HostId_Drive_CapturedOn",
                table: "HostDisks",
                columns: new[] { "HostId", "Drive", "CapturedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedSystems_ProjectId",
                table: "HostedSystems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_Ip",
                table: "Hosts",
                column: "Ip");

            migrationBuilder.CreateIndex(
                name: "IX_Leaves_PersonId_FromDate",
                table: "Leaves",
                columns: new[] { "PersonId", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Movements_PersonId_EffectiveDate",
                table: "Movements",
                columns: new[] { "PersonId", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Code",
                table: "Opportunities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectHosts_ProjectId_HostId_Role",
                table: "ProjectHosts",
                columns: new[] { "ProjectId", "HostId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ProjectNo",
                table: "Projects",
                column: "ProjectNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskIssues_ProjectId",
                table: "RiskIssues",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_CreatedUtc",
                table: "Scenarios",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignments_TaskId",
                table: "TaskAssignments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_PredecessorId",
                table: "TaskLinks",
                column: "PredecessorId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_ProjectId_PredecessorId_SuccessorId_Type",
                table: "TaskLinks",
                columns: new[] { "ProjectId", "PredecessorId", "SuccessorId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskLinks_SuccessorId",
                table: "TaskLinks",
                column: "SuccessorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentId",
                table: "Tasks",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_ExternalKey",
                table: "Tasks",
                columns: new[] { "ProjectId", "ExternalKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_SortOrder",
                table: "Tasks",
                columns: new[] { "ProjectId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Vocabulary_Kind_Value",
                table: "Vocabulary",
                columns: new[] { "Kind", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Allocations");

            migrationBuilder.DropTable(
                name: "AmcTickets");

            migrationBuilder.DropTable(
                name: "BaselineTasks");

            migrationBuilder.DropTable(
                name: "CalendarExceptions");

            migrationBuilder.DropTable(
                name: "ChangeGapLog");

            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions");

            migrationBuilder.DropTable(
                name: "CustomFieldValues");

            migrationBuilder.DropTable(
                name: "Demands");

            migrationBuilder.DropTable(
                name: "HostDisks");

            migrationBuilder.DropTable(
                name: "HostedSystems");

            migrationBuilder.DropTable(
                name: "Hosts");

            migrationBuilder.DropTable(
                name: "Leaves");

            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "Opportunities");

            migrationBuilder.DropTable(
                name: "ProjectHosts");

            migrationBuilder.DropTable(
                name: "RiskIssues");

            migrationBuilder.DropTable(
                name: "Scenarios");

            migrationBuilder.DropTable(
                name: "TaskAssignments");

            migrationBuilder.DropTable(
                name: "TaskLinks");

            migrationBuilder.DropTable(
                name: "Vocabulary");

            migrationBuilder.DropTable(
                name: "Baselines");

            migrationBuilder.DropTable(
                name: "Calendars");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
