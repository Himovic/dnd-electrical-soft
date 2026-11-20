using DND.Electrical.Domain;
using DND.Electrical.Licensing;
using DND.Electrical.Twin;

namespace DND.Electrical.Application.Services;

/// <summary>TWIN module: live data binding of engineering objects (Modbus TCP, MQTT, simulator). Requires the TWIN licence module.</summary>
public sealed class TwinService(IEntitlements entitlements)
{
    public TwinEngine CreateEngine(ElectricalProject p, Func<TwinPoint, ITwinDataSource?>? factory = null)
    {
        entitlements.Demand(EngineeringModule.TWIN);
        return new TwinEngine(p, factory);
    }

    /// <summary>Creates simulator points for analog instruments that have no twin point yet (range taken from the instrument).</summary>
    public int CreateSimulatedPoints(ElectricalProject p, string? user = null)
    {
        entitlements.Demand(EngineeringModule.TWIN);
        var n = 0;
        foreach (var i in p.Instruments.Where(i => i.IsAnalog && i.RangeMax > i.RangeMin && p.TwinPoints.All(t => t.InstrumentId != i.Id)))
        {
            var span = i.RangeMax - i.RangeMin;
            p.TwinPoints.Add(new TwinPoint
            {
                Tag = i.Tag, Description = $"{i.Description} (simulated)", InstrumentId = i.Id, DeviceId = i.DeviceId, Source = TwinSourceKind.Simulator, Unit = i.Unit,
                SimMin = i.RangeMin + 0.1 * span, SimMax = i.RangeMax - 0.05 * span, SimPeriodS = 30 + 7 * n,
            });
            n++;
        }
        if (n > 0) p.Audit("Twin points", $"{n} simulated point(s) created from instruments", user);
        return n;
    }

    public static IEnumerable<Representation> Representations(ElectricalProject p, Device d) =>
        p.TwinPoints.Where(t => t.DeviceId == d.Id || (t.InstrumentId is { } i && p.Instruments.Any(x => x.Id == i && x.DeviceId == d.Id)))
            .Select(t => new Representation(RepresentationKind.TwinPoint, t.Id, $"{t.Tag} {t.Address} {t.Unit}".Trim(), $"{t.Source} {t.Endpoint}".Trim()));
}
