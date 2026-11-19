using System.Globalization;
using System.Text;
using DND.Electrical.Shared;

namespace DND.Electrical.Calculations;

public enum ResultStatus { Pass, Warning, Fail, Informational }

public readonly record struct Quantity(double Value, string Unit, int Decimals = 2)
{
    public override string ToString() =>
        Value.ToString("F" + Decimals, CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit);
}

public sealed record CalculationStep(string Description, string Formula, string Substitution, string Result);

/// <summary>
/// Transparent result of an engineering calculation. Results are preliminary engineering aids and
/// never constitute certification or standards compliance approval.
/// </summary>
public sealed class CalculationResult
{
    public const string Disclaimer =
        "Preliminary engineering calculation. Results must be verified by a qualified engineer against the applicable standards and manufacturer data. This is not a certification of compliance.";

    public string CalculationType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string StandardProfile { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public List<(string Name, Quantity Value)> Inputs { get; } = new();
    public List<CalculationStep> Steps { get; } = new();
    public List<(string Name, Quantity Value)> Outputs { get; } = new();
    public List<string> Assumptions { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Checks { get; } = new();
    public ResultStatus Status { get; set; } = ResultStatus.Informational;
    public string Summary { get; set; } = string.Empty;

    public double Output(string name) => Outputs.First(o => o.Name == name).Value.Value;

    public CalculationResult Input(string name, double value, string unit, int decimals = 2)
    {
        Inputs.Add((name, new Quantity(value, unit, decimals)));
        return this;
    }

    public CalculationResult Out(string name, double value, string unit, int decimals = 2)
    {
        Outputs.Add((name, new Quantity(value, unit, decimals)));
        return this;
    }

    public CalculationResult Step(string description, string formula, string substitution, double result, string unit, int decimals = 3)
    {
        Steps.Add(new CalculationStep(description, formula, substitution, new Quantity(result, unit, decimals).ToString()));
        return this;
    }

    /// <summary>Records a pass/fail check and degrades the status accordingly.</summary>
    public bool Check(bool ok, string description, bool failIsWarning = false)
    {
        Checks.Add((ok ? "✔ " : failIsWarning ? "⚠ " : "✖ ") + description);
        if (!ok)
        {
            if (failIsWarning) { if (Status != ResultStatus.Fail) Status = ResultStatus.Warning; }
            else Status = ResultStatus.Fail;
        }
        else if (Status == ResultStatus.Informational) Status = ResultStatus.Pass;
        return ok;
    }

    public void Warn(string message)
    {
        Warnings.Add(message);
        if (Status is ResultStatus.Pass or ResultStatus.Informational) Status = ResultStatus.Warning;
    }

    public string ToReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Title);
        sb.AppendLine(new string('=', Title.Length));
        sb.AppendLine($"Standard / profile : {StandardProfile}");
        if (Method.Length > 0) sb.AppendLine($"Method             : {Method}");
        sb.AppendLine($"Status             : {Status}");
        sb.AppendLine();
        sb.AppendLine("Inputs");
        foreach (var (n, v) in Inputs) sb.AppendLine($"  {n,-34} {v}");
        sb.AppendLine();
        sb.AppendLine("Calculation steps");
        var i = 1;
        foreach (var s in Steps)
        {
            sb.AppendLine($"  {i++}. {s.Description}");
            sb.AppendLine($"     {s.Formula}");
            if (s.Substitution.Length > 0) sb.AppendLine($"     = {s.Substitution}");
            sb.AppendLine($"     = {s.Result}");
        }
        sb.AppendLine();
        sb.AppendLine("Results");
        foreach (var (n, v) in Outputs) sb.AppendLine($"  {n,-34} {v}");
        if (Checks.Count > 0) { sb.AppendLine(); sb.AppendLine("Checks"); foreach (var c in Checks) sb.AppendLine("  " + c); }
        if (Assumptions.Count > 0) { sb.AppendLine(); sb.AppendLine("Assumptions"); foreach (var a in Assumptions) sb.AppendLine("  - " + a); }
        if (Warnings.Count > 0) { sb.AppendLine(); sb.AppendLine("Warnings"); foreach (var w in Warnings) sb.AppendLine("  - " + w); }
        sb.AppendLine();
        sb.AppendLine(Disclaimer);
        sb.AppendLine($"{ProductInfo.ProductName} — {ProductInfo.PoweredBy}");
        return sb.ToString();
    }

    internal static string F(double v, int d = 3) => v.ToString("F" + d, CultureInfo.InvariantCulture);
}

public enum SupplySystem { SinglePhase, ThreePhase }

public enum Conductor { Copper, Aluminium }

public static class ConductorData
{
    /// <summary>Resistivity at 20 °C in Ω·mm²/m (IEC 60228 basis): Cu 0.017241, Al 0.028264.</summary>
    public static double Rho20(Conductor c) => c == Conductor.Copper ? 0.017241 : 0.028264;

    /// <summary>
    /// Resistivity used for voltage drop at normal service temperature: 1.25 × ρ20
    /// (IEC 60364-5-52 Annex G: ρ1 = 0.0225 Ω·mm²/m Cu, 0.036 Ω·mm²/m Al).
    /// </summary>
    public static double RhoService(Conductor c) => c == Conductor.Copper ? 0.0225 : 0.036;

    /// <summary>Reactance per unit length used when unknown: 0.08 mΩ/m (IEC 60364-5-52 Annex G).</summary>
    public const double ReactancePerMetre = 0.08e-3;

    public static readonly double[] StandardSections = { 1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300 };

    /// <summary>Common standard protective device ratings (A).</summary>
    public static readonly double[] StandardRatings = { 6, 10, 13, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600 };

    public static readonly double[] StandardTransformerKva = { 50, 100, 160, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150 };
}
