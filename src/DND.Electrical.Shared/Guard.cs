using System.Runtime.CompilerServices;

namespace DND.Electrical.Shared;

public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null) where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotEmpty(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value must not be empty.", name) : value;

    public static double Positive(double value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value > 0 && double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be a finite positive number.");

    public static double NonNegative(double value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value >= 0 && double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(name, value, "Value must be a finite non-negative number.");
}
