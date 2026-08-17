using System.Linq;
using CarbonAccounting.Calculation;
using CarbonAccounting.Factors;
using CarbonAccounting.Units;
using Xunit;

namespace CarbonAccounting.Tests;

public class EmissionCalculatorTests
{
    private const string NaturalGas = "example-fuels/natural-gas/gcv/kwh";
    private const string GridLocationBased = "example-fuels/grid-electricity/location-based/kwh";
    private const string WoodPellets = "example-value-chain/biomass/wood-pellets/kwh";
    private const string AirTravel = "example-value-chain/business-travel/air-short-haul/passenger-km";

    [Fact]
    public void Calculate_MultipliesEachGasComponentByTheActivity()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        EmissionResult result = calculator.Calculate(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas);

        // 1000 kWh x (0.18 kg CO2 + 0.0003 kg CH4 + 0.0001 kg N2O) per kWh,
        // aggregated with AR6 (CH4 fossil 29.8, N2O 273):
        //   180 + 0.3 x 29.8 + 0.1 x 273 = 216.24 kg CO2e
        Assert.Equal(216.24, result.Co2e.Value, precision: 6);
        Assert.Equal(Unit.Kilogram, result.Co2e.Unit);
    }

    [Fact]
    public void Calculate_UnderAr5_ProducesADifferentTotalFromAr6()
    {
        var activity = new Quantity(1_000.0, Unit.KilowattHour);

        double ar5 = new EmissionCalculator(GwpSet.Ar5).Calculate(activity, NaturalGas).Co2e.Value;
        double ar6 = new EmissionCalculator(GwpSet.Ar6).Calculate(activity, NaturalGas).Co2e.Value;

        Assert.Equal(215.5, ar5, precision: 6);
        Assert.Equal(216.24, ar6, precision: 6);
    }

    [Fact]
    public void Calculate_PerGasFiguresSumToTheTotal()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        EmissionResult result = calculator.Calculate(new Quantity(5_000.0, Unit.PassengerKilometre), AirTravel);

        double summed = result.Gases.Sum(g => g.Co2e.Value);

        Assert.Equal(result.Co2e.Value, summed, precision: 9);
        Assert.Equal(3, result.Gases.Count);
    }

    [Fact]
    public void Calculate_ConvertsActivityIntoTheFactorsUnit()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        // The factor is per kWh; the meter reads in MWh.
        double fromMegawattHours = calculator.Calculate(new Quantity(1.0, Unit.MegawattHour), NaturalGas).Co2e.Value;
        double fromKilowattHours = calculator.Calculate(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas).Co2e.Value;

        Assert.Equal(fromKilowattHours, fromMegawattHours, precision: 9);
    }

    [Fact]
    public void Calculate_WithAnActivityFromAnotherDimension_Throws()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        // Cubic metres of gas against a per-kWh factor. Off by roughly an order of
        // magnitude, and invisible if the library were to convert it anyway.
        Assert.Throws<UnitConversionException>(
            () => calculator.Calculate(new Quantity(100.0, Unit.CubicMetre), NaturalGas));
    }

    [Fact]
    public void Calculate_KeepsBiogenicCarbonOutOfTheCo2eTotal()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        EmissionResult result = calculator.Calculate(new Quantity(1_000.0, Unit.KilowattHour), WoodPellets);

        // Only CH4 and N2O count towards the scope total for biomass combustion.
        //   0.02 kg CH4 (biogenic, 27.0) + 0.006 kg N2O (273) = 2.178 kg CO2e
        Assert.Equal(2.178, result.Co2e.Value, precision: 6);

        // The 390 kg of biogenic CO2 is reported, but separately.
        Assert.Equal(390.0, result.BiogenicCarbon.Value, precision: 6);
    }

    [Fact]
    public void Calculate_CarriesTheProvenanceOfTheFigure()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        var activity = new Quantity(10_000.0, Unit.KilowattHour);

        EmissionResult result = calculator.Calculate(activity, GridLocationBased);

        Assert.Equal(activity, result.Activity);
        Assert.Equal(GwpSet.Ar6, result.GwpSet);
        Assert.Equal(Scope.Scope2, result.Scope);
        Assert.Equal(Scope2Method.LocationBased, result.Scope2Method);
        Assert.Equal(GridLocationBased, result.Factor.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Factor.Set.Source.Publisher));
    }

    [Fact]
    public void Calculate_WithAnUnknownFactorId_Throws()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => calculator.Calculate(new Quantity(1.0, Unit.KilowattHour), "no-such-factor"));
    }

    [Fact]
    public void Calculate_WithZeroActivity_ProducesZeroEmissions()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        EmissionResult result = calculator.Calculate(new Quantity(0.0, Unit.KilowattHour), NaturalGas);

        Assert.Equal(0.0, result.Co2e.Value);
        Assert.All(result.Gases, g => Assert.Equal(0.0, g.Mass.Value));
    }

    [Fact]
    public void Calculate_AcceptsAFactorInstanceDirectly()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        EmissionFactor factor = FactorCatalog.Get(NaturalGas);

        EmissionResult result = calculator.Calculate(new Quantity(1_000.0, Unit.KilowattHour), factor);

        Assert.Same(factor, result.Factor);
    }
}
