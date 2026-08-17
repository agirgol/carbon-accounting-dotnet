using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class UnitConverterTests
{
    [Theory]
    [InlineData(1.0, Unit.MegawattHour, Unit.KilowattHour, 1_000.0)]
    [InlineData(1.0, Unit.GigawattHour, Unit.MegawattHour, 1_000.0)]
    [InlineData(3.6, Unit.Megajoule, Unit.KilowattHour, 1.0)]
    [InlineData(1.0, Unit.Gigajoule, Unit.Megajoule, 1_000.0)]
    [InlineData(1.0, Unit.CubicMetre, Unit.Litre, 1_000.0)]
    [InlineData(1.0, Unit.Tonne, Unit.Kilogram, 1_000.0)]
    [InlineData(1.0, Unit.Mile, Unit.Kilometre, 1.609344)]
    [InlineData(1.0, Unit.TonneMile, Unit.TonneKilometre, 1.609344)]
    public void Convert_WithinDimension_ReturnsDefinitionalRatio(double value, Unit from, Unit to, double expected)
    {
        double actual = UnitConverter.Convert(value, from, to);

        Assert.Equal(expected, actual, precision: 10);
    }

    [Theory]
    [InlineData(Unit.KilowattHour)]
    [InlineData(Unit.Therm)]
    [InlineData(Unit.ImperialGallon)]
    [InlineData(Unit.ShortTon)]
    [InlineData(Unit.NauticalMile)]
    public void Convert_ToItself_IsExactIdentity(Unit unit)
    {
        const double Value = 1234.56789;

        // Not just "close enough": routing a value through a multiply and a divide it
        // does not need is how reported totals drift between two runs of the same data.
        Assert.Equal(Value, UnitConverter.Convert(Value, unit, unit));
    }

    [Theory]
    [InlineData(Unit.Litre, Unit.Kilogram)]
    [InlineData(Unit.CubicMetre, Unit.KilowattHour)]
    [InlineData(Unit.Kilometre, Unit.TonneKilometre)]
    public void Convert_AcrossDimensions_Throws(Unit from, Unit to)
    {
        UnitConversionException exception =
            Assert.Throws<UnitConversionException>(() => UnitConverter.Convert(1.0, from, to));

        Assert.Equal(from, exception.From);
        Assert.Equal(to, exception.To);
    }

    [Fact]
    public void Convert_CubicMetresOfGasToKilowattHours_IsRefused()
    {
        // The single most common gas-inventory error. It is a calorific-value question,
        // not a unit question, so the unit layer must not answer it.
        Assert.Throws<UnitConversionException>(
            () => UnitConverter.Convert(1_000.0, Unit.CubicMetre, Unit.KilowattHour));
    }

    [Fact]
    public void TryConvert_AcrossDimensions_ReturnsFalse()
    {
        Assert.False(UnitConverter.TryConvert(1.0, Unit.Litre, Unit.Kilogram, out double result));
        Assert.Equal(0.0, result);
    }

    [Theory]
    [InlineData(Unit.KilowattHour, Dimension.Energy)]
    [InlineData(Unit.MillionBtu, Dimension.Energy)]
    [InlineData(Unit.UsGallon, Dimension.Volume)]
    [InlineData(Unit.LongTon, Dimension.Mass)]
    [InlineData(Unit.NauticalMile, Dimension.Distance)]
    [InlineData(Unit.KilogramKilometre, Dimension.FreightTransport)]
    [InlineData(Unit.PassengerMile, Dimension.PassengerTransport)]
    public void GetDimension_ClassifiesEveryUnit(Unit unit, Dimension expected)
    {
        Assert.Equal(expected, UnitConverter.GetDimension(unit));
    }

    [Fact]
    public void RoundTrip_ThroughEveryUnitInADimension_PreservesValue()
    {
        const double Original = 42.0;

        foreach (Unit unit in new[] { Unit.MegawattHour, Unit.Megajoule, Unit.Gigajoule, Unit.Therm, Unit.MillionBtu })
        {
            double there = UnitConverter.Convert(Original, Unit.KilowattHour, unit);
            double back = UnitConverter.Convert(there, unit, Unit.KilowattHour);

            Assert.Equal(Original, back, precision: 9);
        }
    }
}
