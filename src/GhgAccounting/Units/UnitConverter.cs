using System;

namespace GhgAccounting.Units;

/// <summary>
/// Converts activity data between units of the same <see cref="Dimension"/>.
/// </summary>
/// <remarks>
/// Every ratio below is an exact definitional constant, not a measurement, so the
/// conversion is lossless up to double-precision rounding. Values are routed through
/// the dimension's base unit; converting a unit to itself is an identity and does not
/// round-trip through a multiply.
/// </remarks>
public static class UnitConverter
{
    // Exact definitional constants.
    private const double MegajoulesPerKilowattHour = 3.6;
    private const double MegajoulesPerTherm = 105.505585262;     // ISO/UK therm
    private const double MegajoulesPerMillionBtu = 1055.05585262; // BTU(IT) basis
    private const double LitresPerUsGallon = 3.785411784;
    private const double LitresPerImperialGallon = 4.54609;
    private const double KilogramsPerPound = 0.45359237;
    private const double KilometresPerMile = 1.609344;
    private const double KilometresPerNauticalMile = 1.852;

    /// <summary>
    /// Returns the physical quantity <paramref name="unit"/> measures.
    /// </summary>
    /// <param name="unit">The unit to classify.</param>
    /// <returns>The unit's dimension.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined member.</exception>
    public static Dimension GetDimension(Unit unit) => unit switch
    {
        >= Unit.KilowattHour and <= Unit.MillionBtu => Dimension.Energy,
        >= Unit.Litre and <= Unit.ImperialGallon => Dimension.Volume,
        >= Unit.Kilogram and <= Unit.LongTon => Dimension.Mass,
        >= Unit.Kilometre and <= Unit.NauticalMile => Dimension.Distance,
        >= Unit.TonneKilometre and <= Unit.KilogramKilometre => Dimension.FreightTransport,
        >= Unit.PassengerKilometre and <= Unit.PassengerMile => Dimension.PassengerTransport,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Not a defined Unit member."),
    };

    /// <summary>
    /// Returns how many of the dimension's base units one <paramref name="unit"/> equals.
    /// </summary>
    /// <param name="unit">The unit to measure.</param>
    /// <returns>The multiplier onto the base unit of the unit's dimension.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unit"/> is not a defined member.</exception>
    public static double GetBaseUnitFactor(Unit unit) => unit switch
    {
        // Energy, base kWh
        Unit.KilowattHour => 1.0,
        Unit.MegawattHour => 1_000.0,
        Unit.GigawattHour => 1_000_000.0,
        Unit.Megajoule => 1.0 / MegajoulesPerKilowattHour,
        Unit.Gigajoule => 1_000.0 / MegajoulesPerKilowattHour,
        Unit.Therm => MegajoulesPerTherm / MegajoulesPerKilowattHour,
        Unit.MillionBtu => MegajoulesPerMillionBtu / MegajoulesPerKilowattHour,

        // Volume, base litre
        Unit.Litre => 1.0,
        Unit.CubicMetre => 1_000.0,
        Unit.UsGallon => LitresPerUsGallon,
        Unit.ImperialGallon => LitresPerImperialGallon,

        // Mass, base kilogram
        Unit.Kilogram => 1.0,
        Unit.Gram => 0.001,
        Unit.Tonne => 1_000.0,
        Unit.Pound => KilogramsPerPound,
        Unit.ShortTon => 2_000.0 * KilogramsPerPound,
        Unit.LongTon => 2_240.0 * KilogramsPerPound,

        // Distance, base kilometre
        Unit.Kilometre => 1.0,
        Unit.Metre => 0.001,
        Unit.Mile => KilometresPerMile,
        Unit.NauticalMile => KilometresPerNauticalMile,

        // Freight activity, base tonne-kilometre
        Unit.TonneKilometre => 1.0,
        Unit.TonneMile => KilometresPerMile,
        Unit.KilogramKilometre => 0.001,

        // Passenger activity, base passenger-kilometre
        Unit.PassengerKilometre => 1.0,
        Unit.PassengerMile => KilometresPerMile,

        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Not a defined Unit member."),
    };

    /// <summary>
    /// Converts <paramref name="value"/> from one unit to another within the same dimension.
    /// </summary>
    /// <param name="value">The magnitude to convert.</param>
    /// <param name="from">The unit <paramref name="value"/> is expressed in.</param>
    /// <param name="to">The unit to express the result in.</param>
    /// <returns>The converted magnitude.</returns>
    /// <exception cref="UnitConversionException"><paramref name="from"/> and <paramref name="to"/> belong to different dimensions.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either unit is not a defined member.</exception>
    public static double Convert(double value, Unit from, Unit to)
    {
        if (from == to)
        {
            return value;
        }

        if (GetDimension(from) != GetDimension(to))
        {
            throw new UnitConversionException(from, to);
        }

        return value * GetBaseUnitFactor(from) / GetBaseUnitFactor(to);
    }

    /// <summary>
    /// Attempts a conversion, returning <see langword="false"/> instead of throwing
    /// when the units are dimensionally incompatible.
    /// </summary>
    /// <param name="value">The magnitude to convert.</param>
    /// <param name="from">The unit <paramref name="value"/> is expressed in.</param>
    /// <param name="to">The unit to express the result in.</param>
    /// <param name="result">The converted magnitude, or <c>0</c> when the conversion is not defined.</param>
    /// <returns><see langword="true"/> if the conversion was performed.</returns>
    public static bool TryConvert(double value, Unit from, Unit to, out double result)
    {
        if (!IsDefined(from) || !IsDefined(to) || GetDimension(from) != GetDimension(to))
        {
            result = 0.0;
            return false;
        }

        result = from == to ? value : value * GetBaseUnitFactor(from) / GetBaseUnitFactor(to);
        return true;
    }

    /// <summary>
    /// Reports whether <paramref name="unit"/> is a member this converter understands.
    /// </summary>
    /// <param name="unit">The unit to test.</param>
    /// <returns><see langword="true"/> if the unit is defined.</returns>
    public static bool IsDefined(Unit unit)
    {
        switch (unit)
        {
            case >= Unit.KilowattHour and <= Unit.MillionBtu:
            case >= Unit.Litre and <= Unit.ImperialGallon:
            case >= Unit.Kilogram and <= Unit.LongTon:
            case >= Unit.Kilometre and <= Unit.NauticalMile:
            case >= Unit.TonneKilometre and <= Unit.KilogramKilometre:
            case >= Unit.PassengerKilometre and <= Unit.PassengerMile:
                return true;
            default:
                return false;
        }
    }
}
