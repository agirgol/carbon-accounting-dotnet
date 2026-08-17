using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class EurostatGridTests
{
    private const string SetId = "eurostat-grid-tr-2023";
    private const string Turkiye = "eurostat-grid-tr-2023/public-grid/mwh";

    // UK and US comparators, for the regional spread test.
    private const string UnitedKingdom = "defra-2026/uk-electricity/electricity-generated/electricity-uk/kwh/kwh";
    private const string California = "egrid-2023/subregion/camx/mwh";

    private static FactorSet Set => FactorCatalog.GetSet(SetId);

    [Fact]
    public void Set_IsVerifiedAndScopedToTurkiye()
    {
        Assert.Equal(VerificationStatus.Verified, Set.Verification);
        Assert.Equal("TR", Set.Region);
        Assert.Contains("Eurostat", Set.Source.Publisher);
    }

    [Fact]
    public void Factor_IsScope2LocationBased()
    {
        EmissionFactor factor = FactorCatalog.Get(Turkiye);

        Assert.Equal(Scope.Scope2, factor.Scope);
        Assert.Equal(Scope2Method.LocationBased, factor.Scope2Method);
        Assert.Equal(Unit.MegawattHour, factor.Unit);
    }

    [Fact]
    public void GasMasses_AreMarkedDerived()
    {
        EmissionFactor factor = FactorCatalog.Get(Turkiye);

        // No authority publishes this number. It is computed here from two Eurostat
        // series, and the flag says so rather than letting it pass as published.
        Assert.True(factor.ComponentsAreDerived);
        Assert.Equal(3, factor.Components.Count);

        // Derived, but from gas masses rather than a CO2e aggregate, so no GWP set is
        // baked in and the factor carries no published basis to be bound to.
        Assert.Null(factor.PublishedGwpBasis);
    }

    [Theory]
    [InlineData(GwpSet.Ar5, 474.08)]
    [InlineData(GwpSet.Ar6, 474.19)]
    public void Intensity_MatchesTheDerivation(GwpSet set, double expected)
    {
        var calculator = new EmissionCalculator(set);

        EmissionResult result = calculator.Calculate(new Quantity(1.0, Unit.MegawattHour), Turkiye);

        Assert.Equal(expected, result.Co2e.Value, precision: 2);
    }

    [Fact]
    public void Intensity_IsCarriedByCarbonDioxide()
    {
        EmissionResult result = new EmissionCalculator(GwpSet.Ar6)
            .Calculate(new Quantity(1.0, Unit.MegawattHour), Turkiye);

        GasEmission co2 = result.Gases.Single(g => g.Gas == GreenhouseGas.CarbonDioxide);

        // Over 99% of a fossil-heavy grid's CO2e is the CO2 itself; the CH4 and N2O
        // shares are why the choice of GWP set barely moves this particular number.
        Assert.True(co2.Co2e.Value / result.Co2e.Value > 0.99);
    }

    [Fact]
    public void ChoiceOfGwpSet_BarelyMovesAGridFactor()
    {
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double ar5 = new EmissionCalculator(GwpSet.Ar5).Calculate(consumption, Turkiye).Co2e.Value;
        double ar6 = new EmissionCalculator(GwpSet.Ar6).Calculate(consumption, Turkiye).Co2e.Value;

        // Worth pinning: the AR5/AR6 choice matters enormously for fugitive methane and
        // hardly at all for grid electricity. A library that hid the choice would leave
        // a user unable to tell which case they were in.
        Assert.NotEqual(ar5, ar6);
        Assert.True(System.Math.Abs(ar6 - ar5) / ar5 < 0.001);
    }

    [Fact]
    public void GridsDifferByRegion_WhichIsWhyTheFactorIsRegional()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double turkiye = calculator.Calculate(consumption, Turkiye).Co2e.Value;
        double california = calculator.Calculate(consumption, California).Co2e.Value;
        double unitedKingdom = new EmissionCalculator(GwpSet.Ar5)
            .Calculate(new Quantity(1_000_000.0, Unit.KilowattHour), UnitedKingdom).Co2e.Value;

        // The same megawatt hour is roughly three and a half times the emissions in
        // Türkiye that it is on the UK grid. Applying one country's factor to another's
        // consumption is the most common inventory error there is.
        Assert.True(turkiye > california);
        Assert.True(turkiye > unitedKingdom * 3.0);
    }

    [Fact]
    public void Factor_IsTraceableToItsEurostatSeries()
    {
        Assert.Contains("env_air_gge", FactorCatalog.Get(Turkiye).SourceReference);
        Assert.Contains("nrg_bal_c", FactorCatalog.Get(Turkiye).SourceReference);
    }
}
