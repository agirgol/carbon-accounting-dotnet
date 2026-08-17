using System;

namespace GhgAccounting.Units;

/// <summary>
/// Thrown when a conversion is requested between units that do not measure the
/// same physical quantity.
/// </summary>
/// <remarks>
/// This is deliberately an exception rather than a silent fallback. A cubic-metre
/// figure quietly treated as kilowatt hours understates a gas inventory by about
/// an order of magnitude, and nothing downstream would flag it.
/// </remarks>
public sealed class UnitConversionException : InvalidOperationException
{
    /// <summary>Initialises the exception for a specific unit pair.</summary>
    /// <param name="from">The source unit.</param>
    /// <param name="to">The requested target unit.</param>
    public UnitConversionException(Unit from, Unit to)
        : base(BuildMessage(from, to))
    {
        From = from;
        To = to;
    }

    /// <summary>The source unit of the rejected conversion.</summary>
    public Unit From { get; }

    /// <summary>The target unit of the rejected conversion.</summary>
    public Unit To { get; }

    private static string BuildMessage(Unit from, Unit to)
    {
        Dimension fromDimension = UnitConverter.GetDimension(from);
        Dimension toDimension = UnitConverter.GetDimension(to);

        return $"Cannot convert {from} ({fromDimension}) to {to} ({toDimension}). " +
               "Units are only convertible within a single dimension. Crossing dimensions requires " +
               "a substance property such as calorific value or density, which is a property of the " +
               "fuel and must come from an emission factor, not from the unit layer.";
    }
}
