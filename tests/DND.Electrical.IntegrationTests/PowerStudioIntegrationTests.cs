using DND.Electrical.Application.Services;
using DND.Electrical.Licensing;
using DND.Electrical.Power;

namespace DND.Electrical.IntegrationTests;

public class PowerStudioIntegrationTests
{
    [Fact]
    public async Task Networks_persist_in_the_project_and_give_identical_results()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-PWR-003");
        var svc = new PowerStudioService(env.Licensing);
        var net = SampleNetworks.DemoIndustrial();
        svc.Save(p, net);
        var path = env.PathFor("pwr.dndx");
        await env.Store.SaveAsync(p, path);
        var l = await env.Store.LoadAsync(path);
        var back = PowerStudioService.Load(Assert.Single(l.PowerNetworks));
        Assert.Equal(svc.LoadFlow(net).LossesMw, svc.LoadFlow(back).LossesMw, 12);
        Assert.Equal(svc.ShortCircuit(net).At("MCC1").Ik1Ka, svc.ShortCircuit(back).At("MCC1").Ik1Ka, 12);
    }

    [Fact]
    public void Network_built_from_the_project_load_list_solves_and_lists_placeholders()
    {
        using var env = new TestEnvironment();
        var p = env.Demos.Create("DEMO-PWR-003");
        Assert.NotEmpty(p.Loads);
        var svc = new PowerStudioService(env.Licensing);
        var (n, notes) = svc.BuildFromProject(p, 1000);
        Assert.Empty(n.Validate());
        Assert.Contains(notes, x => x.Contains("placeholder"));
        var lf = svc.LoadFlow(n);
        Assert.True(lf.Converged);
        var demand = p.Loads.Sum(x => x.PowerKw * x.DemandFactor) + p.Motors.Sum(m => m.PowerKw / m.Efficiency);
        Assert.Equal(demand / 1000 + lf.LossesMw, lf.Buses.Sum(b => b.PGenMw), 6);
        Assert.All(svc.ShortCircuit(n).Faults, f => Assert.True(f.Ik3MaxKa > 0));
        Assert.Contains("Newton", PowerStudioService.LoadFlowReport(n, lf));
    }

    [Fact]
    public void Studies_require_the_power_module()
    {
        using var env = new TestEnvironment();
        var limited = DND.Electrical.TestSupport.TestLicensing.Create(env.PathFor("lim"), EngineeringModule.ELEC);
        var svc = new PowerStudioService(limited);
        var n = SampleNetworks.Ieee14();
        Assert.Equal(EngineeringModule.POWER, Assert.Throws<LicenseException>(() => svc.LoadFlow(n)).Module);
        Assert.Throws<LicenseException>(() => svc.ShortCircuit(n));
        Assert.Throws<LicenseException>(() => svc.Harmonics(n));
        Assert.Throws<LicenseException>(() => svc.Save(env.Demos.Create("DEMO-PWR-003"), n));
    }
}
