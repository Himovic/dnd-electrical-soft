using System.Text.Json.Serialization;

namespace DND.Electrical.Domain;

public enum LifecyclePhase
{
    ConceptualDesign, PreliminaryEngineering, DetailedEngineering, Verification, Procurement, Construction, Commissioning, Operation, Maintenance,
}

public enum ProjectRole { Viewer, Engineer, LeadEngineer, Approver, ProjectManager, Administrator }

public enum WorkStatus { Open, InProgress, Blocked, Done, Cancelled }

public enum IssueSeverity { Low, Medium, High, Critical }

public enum ApprovalStatus { Pending, Approved, Rejected, Withdrawn }

public enum CommissioningResult { NotTested, Passed, Failed, PassedWithRemarks }

public sealed class ProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Discipline { get; set; } = string.Empty;
    public ProjectRole Role { get; set; } = ProjectRole.Engineer;
}

public sealed class ProjectTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public string Discipline { get; set; } = string.Empty;
    public LifecyclePhase Phase { get; set; } = LifecyclePhase.DetailedEngineering;
    public DateTimeOffset? DueDate { get; set; }
    public WorkStatus Status { get; set; } = WorkStatus.Open;
    /// <summary>Engineering object (device identity) the task concerns, if any.</summary>
    public Guid? ObjectId { get; set; }
}

public sealed class Milestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public LifecyclePhase Phase { get; set; }
    public DateTimeOffset Due { get; set; }
    public DateTimeOffset? Completed { get; set; }
}

public sealed class EngineeringIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; } = IssueSeverity.Medium;
    public WorkStatus Status { get; set; } = WorkStatus.Open;
    public string RaisedBy { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public Guid? ObjectId { get; set; }
    public Guid? SheetId { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? Resolved { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>Approval of a document, sheet, calculation or revision. Decisions are immutable records.</summary>
public sealed class ApprovalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SubjectKind { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public string SubjectTitle { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Approver { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class CommentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TargetId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
}

public sealed class DocumentRegisterEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Revision { get; set; } = "A";
    public string Status { get; set; } = "Draft";
    public Guid? AttachmentId { get; set; }
    public DateTimeOffset? Issued { get; set; }
}

public sealed class MaintenanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Engineering object identity (device id).</summary>
    public Guid ObjectId { get; set; }
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "Preventive";
    public string Description { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public DateTimeOffset? NextDue { get; set; }
}

public sealed class CommissioningRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }
    public string Test { get; set; } = string.Empty;
    public CommissioningResult Result { get; set; } = CommissioningResult.NotTested;
    public string MeasuredValue { get; set; } = string.Empty;
    public string By { get; set; } = string.Empty;
    public DateTimeOffset? Date { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

/// <summary>
/// Stored 3D layout (cabinet, room, plant area or tray network). The scene itself is serialised by the 3D module;
/// objects reference engineering objects by device identity.
/// </summary>
public sealed class Layout3DDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "3D layout";
    public string Kind { get; set; } = "Cabinet";
    public Guid? CabinetId { get; set; }
    public string SceneJson { get; set; } = "{}";
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
}

/// <summary>Stored D&amp;D PowerStudio network (serialised by the POWER module) with the time of the last study.</summary>
public sealed class PowerNetworkDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Network";
    public string NetworkJson { get; set; } = "{}";
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Audit record of an AI assistant interaction: provider, model, whether project data was shared, the proposals
/// returned and the user's decision on each. The project context sent to an external provider is not stored.
/// </summary>
public sealed class AiInteractionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string User { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool ProjectDataShared { get; set; }
    public string Question { get; set; } = string.Empty;
    public string AnswerExcerpt { get; set; } = string.Empty;
    /// <summary>Proposal titles, one per line.</summary>
    public string Proposals { get; set; } = string.Empty;
    /// <summary>Decisions as "title → Accepted|Rejected", one per line.</summary>
    public string Decisions { get; set; } = string.Empty;
}
