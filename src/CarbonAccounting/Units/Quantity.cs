using System;
using System.Globalization;

namespace CarbonAccounting.Units;

/// <summary>
/// A magnitude paired with the unit it is expressed in.
/// </summary>
/// <remarks>
/// Activity data arriving as a bare <see cref="double"/> is the most common source
/// of silent inventory errors, because the unit lives only in a column header or a
/// developer's assumption. Carrying the unit in the type makes a mismatch a compile-
/// or run-time failure rather than a wrong number in a published report.
/// </remarks>
public readonly struct Quantity : IEquatable<Quantity>
{
    /// <summary>Creates a quantity.</summary>
    /// <param name="value">The magnitude.</param>
    /// <param name="unit">The unit the magnitude is expressed in.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined member, or <paramref name="value"/> is not a finite number.</exception>
    public Quantity(double value, Unit unit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Activity data must be a finite number.");
        }

        if (!UnitConverter.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Not a defined Unit member.");
        }

        Value = value;
        Unit = unit;
    }

    /// <summary>The magnitude.</summary>
    public double Value { get; }

    /// <summary>The unit <see cref="Value"/> is expressed in.</summary>
    public Unit Unit { get; }

    /// <summary>The physical quantity this measures.</summary>
    public Dimension Dimension => UnitConverter.GetDimension(Unit);

    /// <summary>
    /// Returns the same physical amount expressed in <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The unit to convert to.</param>
    /// <returns>An equivalent quantity in the target unit.</returns>
    /// <exception cref="UnitConversionException"><paramref name="target"/> is in a different dimension.</exception>
    public Quantity ConvertTo(Unit target) =>
        new Quantity(UnitConverter.Convert(Value, Unit, target), target);

    /// <summary>
    /// Adds another quantity, converting it into this quantity's unit first.
    /// </summary>
    /// <param name="other">The quantity to add.</param>
    /// <returns>The sum, expressed in this quantity's unit.</returns>
    /// <exception cref="UnitConversionException"><paramref name="other"/> is in a different dimension.</exception>
    public Quantity Add(Quantity other) =>
        new Quantity(Value + UnitConverter.Convert(other.Value, other.Unit, Unit), Unit);

    /// <inheritdoc />
    public bool Equals(Quantity other) => Value.Equals(other.Value) && Unit == other.Unit;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Quantity other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (Value.GetHashCode() * 397) ^ (int)Unit;
        }
    }

    /// <summary>Compares two quantities for exact equality of both magnitude and unit.</summary>
    /// <param name="left">The first quantity.</param>
    /// <param name="right">The second quantity.</param>
    /// <returns><see langword="true"/> if both are identical.</returns>
    public static bool operator ==(Quantity left, Quantity right) => left.Equals(right);

    /// <summary>Compares two quantities for inequality.</summary>
    /// <param name="left">The first quantity.</param>
    /// <param name="right">The second quantity.</param>
    /// <returns><see langword="true"/> if they differ.</returns>
    public static bool operator !=(Quantity left, Quantity right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() =>
        Value.ToString("G17", CultureInfo.InvariantCulture) + " " + Unit;
}
