using System;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class QuantityTests
{
    [Fact]
    public void ConvertTo_ReturnsTheSamePhysicalAmount()
    {
        var energy = new Quantity(2.5, Unit.MegawattHour);

        Quantity converted = energy.ConvertTo(Unit.KilowattHour);

        Assert.Equal(2_500.0, converted.Value, precision: 10);
        Assert.Equal(Unit.KilowattHour, converted.Unit);
    }

    [Fact]
    public void Add_NormalisesTheOtherOperandIntoThisUnit()
    {
        var a = new Quantity(1.0, Unit.Tonne);
        var b = new Quantity(500.0, Unit.Kilogram);

        Quantity sum = a.Add(b);

        Assert.Equal(1.5, sum.Value, precision: 10);
        Assert.Equal(Unit.Tonne, sum.Unit);
    }

    [Fact]
    public void Add_AcrossDimensions_Throws()
    {
        var mass = new Quantity(1.0, Unit.Kilogram);
        var distance = new Quantity(1.0, Unit.Kilometre);

        Assert.Throws<UnitConversionException>(() => mass.Add(distance));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Constructor_RejectsNonFiniteActivityData(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quantity(value, Unit.KilowattHour));
    }

    [Fact]
    public void Constructor_RejectsAnUndefinedUnit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quantity(1.0, (Unit)9_999));
    }

    [Fact]
    public void Equality_ComparesMagnitudeAndUnit()
    {
        var a = new Quantity(1.0, Unit.Tonne);
        var b = new Quantity(1.0, Unit.Tonne);
        var c = new Quantity(1_000.0, Unit.Kilogram);

        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        // Deliberately not equal: the same amount in a different unit is a different
        // value, and silently equating them would hide unit bugs rather than expose them.
        Assert.True(a != c);
    }

    [Fact]
    public void Dimension_ReflectsTheUnit()
    {
        Assert.Equal(Dimension.Volume, new Quantity(1.0, Unit.Litre).Dimension);
        Assert.Equal(Dimension.FreightTransport, new Quantity(1.0, Unit.TonneKilometre).Dimension);
    }
}
