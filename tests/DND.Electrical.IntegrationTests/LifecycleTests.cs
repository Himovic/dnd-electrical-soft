using DND.Electrical.Application.Services;
using DND.Electrical.Domain;

namespace DND.Electrical.IntegrationTests;

public class LifecycleTests
{
    [Fact]
    public async Task Lifecycle_and_project_management_data_roundtrip()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var dev = p.Devices.First();
        p.Info.Phase = LifecyclePhase.Commissioning;
        p.Members.Add(new ProjectMember { Name = "A. Engineer", Role = ProjectRole.Engineer, Discipline = "Electrical" });
        p.Members.Add(new ProjectMember { Name = "B. Approver", Role = ProjectRole.Approver });
        p.Tasks.Add(new ProjectTask { Title = "Check terminal plan", Assignee = "A. Engineer", ObjectId = dev.Id, DueDate = DateTimeOffset.Parse("2026-12-01T00:00:00Z") });
        p.Milestones.Add(new Milestone { Name = "IFC issue", Phase = LifecyclePhase.DetailedEngineering, Due = DateTimeOffset.Parse("2026-12-15T00:00:00Z") });
        ApprovalService.RaiseIssue(p, "Missing earth", "PE not shown on sheet 3", IssueSeverity.High, "A. Engineer", dev.Id);
        var approval = ApprovalService.Request(p, "Sheet", p.Sheets[0].Id, p.Sheets[0].Title, "A. Engineer", "B. Approver");
        ApprovalService.Decide(p, approval, "B. Approver", true, "OK for construction");
        p.Comments.Add(new CommentRecord { TargetId = dev.Id, Author = "B. Approver", Text = "Verify rating" });
        p.DocumentRegister.Add(new DocumentRegisterEntry { Number = "DOC-001", Title = "Schematic", Kind = "Drawing" });
        p.Maintenance.Add(new MaintenanceRecord { ObjectId = dev.Id, Description = "Thermographic inspection", Technician = "C. Tech" });
        p.Commissioning.Add(new CommissioningRecord { ObjectId = dev.Id, Test = "Insulation resistance", Result = CommissioningResult.Passed, MeasuredValue = "> 500 MΩ", Date = DateTimeOffset.Now });

        var path = env.PathFor("life.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);

        Assert.Equal(LifecyclePhase.Commissioning, l.Info.Phase);
        Assert.Equal(new[] { "A. Engineer", "B. Approver" }, l.Members.Select(m => m.Name));
        Assert.Equal(ProjectRole.Approver, l.Members[1].Role);
        Assert.Equal(dev.Id, l.Tasks.Single().ObjectId);
        Assert.Equal("IFC issue", l.Milestones.Single().Name);
        Assert.Equal(1, l.Issues.Single().Number);
        Assert.Equal(IssueSeverity.High, l.Issues.Single().Severity);
        Assert.Equal(ApprovalStatus.Approved, l.Approvals.Single().Status);
        Assert.NotNull(l.Approvals.Single().DecidedAt);
        Assert.Equal("Verify rating", l.Comments.Single().Text);
        Assert.Equal("DOC-001", l.DocumentRegister.Single().Number);
        Assert.Equal(dev.Id, l.Maintenance.Single().ObjectId);
        Assert.Equal(CommissioningResult.Passed, l.Commissioning.Single().Result);
        Assert.Contains(l.AuditEvents, a => a.Action == "Approved");
    }

    [Fact]
    public void Approval_enforces_roles_and_four_eyes()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        p.Members.Add(new ProjectMember { Name = "Eng", Role = ProjectRole.Engineer });
        p.Members.Add(new ProjectMember { Name = "Lead", Role = ProjectRole.Approver });
        var a = ApprovalService.Request(p, "Sheet", p.Sheets[0].Id, "S1", "Lead", "Lead");
        Assert.Throws<InvalidOperationException>(() => ApprovalService.Request(p, "Sheet", p.Sheets[0].Id, "S1", "Lead", "Lead"));
        Assert.Throws<UnauthorizedAccessException>(() => ApprovalService.Decide(p, a, "Eng", true, ""));
        Assert.Throws<UnauthorizedAccessException>(() => ApprovalService.Decide(p, a, "Lead", true, ""));
        Assert.Throws<UnauthorizedAccessException>(() => ApprovalService.Decide(p, a, "Stranger", true, ""));
        p.Members.Add(new ProjectMember { Name = "PM", Role = ProjectRole.ProjectManager });
        ApprovalService.Decide(p, a, "PM", false, "Rework");
        Assert.Equal(ApprovalStatus.Rejected, a.Status);
        Assert.Throws<InvalidOperationException>(() => ApprovalService.Decide(p, a, "PM", true, ""));
    }

    [Fact]
    public void Engineering_object_resolves_all_representations()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var motor = p.Motors.First(m => m.DeviceId is not null);
        var dev = p.FindDevice(motor.DeviceId!.Value)!;
        var view = EngineeringObjectService.Describe(p, dev);
        Assert.Contains(view.Representations, r => r.Kind == RepresentationKind.SchematicSymbol);
        Assert.Contains(view.Representations, r => r.Kind == RepresentationKind.Motor && r.Id == motor.Id);

        var anyCabinetItem = p.Cabinets.SelectMany(c => c.Items).First(i => i.DeviceId is not null);
        var cabDev = p.FindDevice(anyCabinetItem.DeviceId!.Value)!;
        Assert.Contains(EngineeringObjectService.Describe(p, cabDev).Representations, r => r.Kind == RepresentationKind.CabinetItem);
    }

    [Fact]
    public void Conflicts_are_detected_and_only_synchronised_on_request()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        Assert.DoesNotContain(EngineeringObjectService.DetectAll(p), c => c.Field == "Tag");
        var motor = p.Motors.First(m => m.DeviceId is not null);
        var dev = p.FindDevice(motor.DeviceId!.Value)!;
        var originalTag = dev.Tag;
        motor.Tag = originalTag + "X";

        var conflict = Assert.Single(EngineeringObjectService.DetectAll(p), c => c.Field == "Tag" && c.ObjectId == dev.Id);
        // Detection alone changes nothing.
        Assert.Equal(originalTag, dev.Tag);
        Assert.Equal(originalTag + "X", motor.Tag);

        EngineeringObjectService.Synchronize(p, conflict, RepresentationKind.SchematicSymbol, "tester");
        Assert.Equal(originalTag, motor.Tag);
        Assert.DoesNotContain(EngineeringObjectService.DetectAll(p), c => c.Field == "Tag" && c.ObjectId == dev.Id);
        Assert.Contains(p.AuditEvents, a => a.Action == "Synchronized" && a.User == "tester");
    }

    [Fact]
    public void Power_rating_conflict_syncs_from_equipment()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-IND-001");
        var motor = p.Motors.First(m => m.DeviceId is not null);
        var dev = p.FindDevice(motor.DeviceId!.Value)!;
        dev.Properties[DeviceProperties.Rating] = "999 kW";
        var c = Assert.Single(EngineeringObjectService.DetectAll(p), c => c.Field == "Rated power (kW)" && c.ObjectId == dev.Id);
        EngineeringObjectService.Synchronize(p, c, RepresentationKind.Motor, "tester");
        Assert.Equal($"{motor.PowerKw.ToString(System.Globalization.CultureInfo.InvariantCulture)} kW", dev.Properties[DeviceProperties.Rating]);
    }
}
