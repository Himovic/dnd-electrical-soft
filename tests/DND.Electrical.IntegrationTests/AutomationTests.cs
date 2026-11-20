using DND.Electrical.Application.Demo;
using DND.Electrical.Application.Services;
using DND.Electrical.Application.Templates;
using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Power;
using DND.Electrical.Reports;
using DND.Electrical.Validation;

namespace DND.Electrical.IntegrationTests;

public class AutomationTests
{
    private static IReadOnlyList<ValidationIssue> Issues(TestEnvironment env, ElectricalProject p, string prefix) =>
        new ValidationEngine().Run(p, env.Schematic.Binder.BindWithConnectivity(p)).Issues.Where(i => i.RuleId.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    [Fact]
    public void Oil_and_gas_demo_has_consistent_automation_data()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-OG-009");
        Assert.Equal(9, p.Instruments.Count);
        Assert.All(p.Instruments, i => Assert.NotNull(i.PlcChannelId));
        Assert.All(p.Instruments.Where(i => i.HazardousZone.Length > 0), i => Assert.Contains("demo record", i.ExMarking));
        Assert.Equal(4, p.Loops.Count);
        var ce = Assert.Single(p.CauseEffects);
        Assert.Empty(Issues(env, p, "AUT").Where(i => i.Severity != Severity.Information));
        // Gas detected trips everything; low suction pressure does not open the blowdown valve.
        var gas = ce.Causes.Single(c => c.Tag.StartsWith("GD-310"));
        Assert.Equal(4, AutomationService.Evaluate(ce, new[] { gas.Id }).Count);
        var lowP = ce.Causes.Single(c => c.Tag.StartsWith("PT-301"));
        Assert.DoesNotContain(AutomationService.Evaluate(ce, new[] { lowP.Id }), e => e.Tag == "XV-306");
    }

    [Fact]
    public void Automation_rules_detect_inconsistent_data()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-OG-009");
        var pt = p.Instruments.Single(i => i.Tag == "PT-301");
        p.Alarms.Single(a => a.Tag == "PT-301-HH").SetPoint = 50;                  // outside 0…40 barg
        p.Alarms.Single(a => a.Tag == "PT-301-L").SetPoint = 5;                    // below LL = 8
        p.Instruments.Add(new Instrument { Tag = "PT-301", RangeMin = 0, RangeMax = 10, Unit = "bar" });  // duplicate
        p.Instruments.Single(i => i.Tag == "TT-303").ExMarking = "";               // zone 1 without Ex record
        p.ScadaNodes[2].IpAddress = p.ScadaNodes[1].IpAddress;                      // duplicate IP on the same network
        p.ScadaNodes.Add(new ScadaNode { Name = "Bad", IpAddress = "300.1.1.1" });
        var gasEffect = p.CauseEffects[0].Effects.First(e => e.DeviceId is not null);
        p.Devices.Remove(p.FindDevice(gasEffect.DeviceId!.Value)!);
        var issues = Issues(env, p, "AUT");
        Assert.Contains(issues, i => i.RuleId == "AUT001" && i.Message.Contains("PT-301 is used 2 times"));
        Assert.Contains(issues, i => i.RuleId == "AUT002" && i.Message.Contains("outside the range"));
        Assert.Contains(issues, i => i.RuleId == "AUT002" && i.Message.Contains("not ordered"));
        Assert.Contains(issues, i => i.RuleId == "AUT005" && i.Message.Contains("deleted device"));
        Assert.Contains(issues, i => i.RuleId == "AUT006" && i.Message.Contains("is used by"));
        Assert.Contains(issues, i => i.RuleId == "AUT006" && i.Message.Contains("not a valid IPv4"));
        Assert.Contains(issues, i => i.RuleId == "AUT007" && i.Message.Contains("TT-303"));
        _ = pt;
    }

    [Fact]
    public void Io_assignment_checks_signal_kind_direction_and_occupancy()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-MFG-006");
        var svc = new AutomationService(env.Licensing);
        var st = p.PlcStations.Single(s => s.Tag == "-A10");
        var spareDi = st.Channels.First(c => c.Kind == PlcSignalKind.DigitalInput && c.IsSpare);
        var spareAi = st.Channels.First(c => c.Kind == PlcSignalKind.AnalogInput && c.IsSpare);
        var aoUsed = st.Channels.First(c => c.Kind == PlcSignalKind.AnalogOutput && !c.IsSpare);
        var ft = new Instrument { Tag = "FT-205", Description = "Glue flow", Variable = MeasuredVariable.Flow, RangeMax = 20, Unit = "l/min" };
        p.Instruments.Add(ft);
        Assert.Contains("cannot be wired", svc.AssignToChannel(p, ft, spareDi));
        Assert.Contains("needs an input", svc.AssignToChannel(p, ft, aoUsed));
        Assert.Null(svc.AssignToChannel(p, ft, spareAi));
        Assert.Equal("FT-205", spareAi.Tag);
        Assert.Equal("4–20 mA", spareAi.SignalType);
        var other = new Instrument { Tag = "FT-206", RangeMax = 1, Unit = "x" };
        p.Instruments.Add(other);
        Assert.Contains("already used", svc.AssignToChannel(p, other, spareAi));
        // Reassignment frees the old channel (add a module: the demo's AI card is now full)
        DND.Electrical.PLC.PlcService.AddModule(st, st.Racks[0], "GEN-AI4");
        var spareAi2 = st.Channels.First(c => c.Kind == PlcSignalKind.AnalogInput && c.IsSpare);
        Assert.Null(svc.AssignToChannel(p, ft, spareAi2));
        Assert.True(spareAi.IsSpare);
    }

    [Fact]
    public void Alarm_proposals_use_the_span_and_require_analog_instruments()
    {
        using var env = new TestEnvironment();
        var svc = new AutomationService(env.Licensing);
        var i = new Instrument { Tag = "LT-1", RangeMin = 200, RangeMax = 1200, Unit = "mm" };
        var a = svc.ProposeAlarms(i, new Dictionary<AlarmType, double> { [AlarmType.LowLow] = 0.05, [AlarmType.High] = 0.9 });
        Assert.Equal(250, a.Single(x => x.Type == AlarmType.LowLow).SetPoint);
        Assert.Equal(1100, a.Single(x => x.Type == AlarmType.High).SetPoint);
        Assert.Equal(AlarmPriority.High, a.Single(x => x.Type == AlarmType.LowLow).Priority);
        Assert.Throws<InvalidOperationException>(() => svc.ProposeAlarms(new Instrument { Tag = "XS-1", Signal = InstrumentSignal.Digital24VDC }, new Dictionary<AlarmType, double> { [AlarmType.High] = 0.9 }));
    }

    [Fact]
    public async Task Automation_data_roundtrip_reports_and_loop_diagram()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-OG-009");
        var sheets = p.Sheets.Count;
        var sheet = new AutomationService(env.Licensing).GenerateLoopDiagram(p, p.Loops.Single(l => l.Tag == "PIC-301"));
        Assert.Equal(sheets + 1, p.Sheets.Count);
        Assert.Contains(sheet.Drawing.Entities.OfType<DND.Electrical.CAD.Entities.TextEntity>(), t => t.Value.Contains("PT-301") || t.Value == "PT");
        new AutomationService(env.Licensing).GenerateLoopDiagram(p, p.Loops.Single(l => l.Tag == "PIC-301"));
        Assert.Equal(sheets + 1, p.Sheets.Count); // regenerated in place

        var path = env.PathFor("og.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);
        Assert.Equal(p.Instruments.Select(i => i.Tag), l.Instruments.Select(i => i.Tag));
        Assert.Equal(p.Alarms.Count, l.Alarms.Count);
        Assert.Equal(p.CauseEffects[0].Links.Count, l.CauseEffects[0].Links.Count);
        Assert.Equal(p.ScadaNodes.Select(n => n.ParentId), l.ScadaNodes.Select(n => n.ParentId));
        Assert.Equal(InstrumentSignal.Hart, l.Instruments.Single(i => i.Tag == "PT-301").Signal);

        var reports = new ProjectReports(l, env.Schematic.Binder.BindWithConnectivity(l));
        Assert.Equal(9, reports.InstrumentIndex().Rows.Count);
        Assert.Equal(5, reports.CauseEffect().Rows.Count);
        Assert.Equal(6, reports.CauseEffect().Columns.Count);
        Assert.Contains(reports.AlarmList().Rows, r => r[0]?.ToString() == "GD-310-HH");
        Assert.Contains(ProjectReports.Catalogue("fr"), c => c.id == "causeeffect" && c.title.StartsWith("Matrice"));
    }

    [Fact]
    public void Automation_operations_require_the_auto_module()
    {
        using var env = new TestEnvironment();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(env.PathFor("lim"), EngineeringModule.ELEC);
        var p = env.Demos.Create("DEMO-OG-009");
        var svc = new AutomationService(limited);
        Assert.Equal(EngineeringModule.AUTO, Assert.Throws<LicenseException>(() => svc.GenerateLoopDiagram(p, p.Loops[0])).Module);
        Assert.Throws<LicenseException>(() => svc.ImportPlcStations(p));
    }

    public static IEnumerable<object[]> NewDemos => new[] { "DEMO-MFG-006", "DEMO-RAIL-007", "DEMO-DC-008", "DEMO-OG-009", "DEMO-PV-010" }.Select(d => new object[] { d });

    [Theory]
    [MemberData(nameof(NewDemos))]
    public void Industry_demos_carry_networks_architecture_and_lifecycle(string number)
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create(number);
        var n = PowerStudioService.Load(Assert.Single(p.PowerNetworks));
        Assert.Contains("demo", n.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(LoadFlowSolver.Solve(n).Converged);
        Assert.All(ShortCircuitSolver.Solve(n).Faults, f => Assert.True(f.Ik3MaxKa > 0));
        Assert.NotEmpty(p.ScadaNodes);
        Assert.NotEmpty(p.Members);
        Assert.NotEmpty(p.Tasks);
        Assert.NotEmpty(p.Calculations);
        Assert.Empty(Issues(env, p, "AUT").Where(i => i.Severity == Severity.Error));
    }

    [Theory]
    [InlineData("datacenter", ProjectIndustry.DataCenter)]
    [InlineData("solar", ProjectIndustry.SolarPv)]
    [InlineData("railway", ProjectIndustry.Railway)]
    [InlineData("oilgas", ProjectIndustry.OilAndGas)]
    public void Industry_templates_create_projects(string id, ProjectIndustry industry)
    {
        using var env = new TestEnvironment();
        var t = ProjectTemplate.Get(id);
        Assert.Equal(id, t.Id);
        var p = t.Create(new ProjectInfo { Number = "T-1", Name = "Template test" }, env.Schematic);
        Assert.Equal(industry, p.Info.Industry);
        Assert.True(p.Sheets.Count >= 4);
    }
}
