using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class EgridCatalogTests
{
    private const string SetId = "egrid-2023";
    private const string California = "egrid-2023/subregion/camx/mwh";
    private const string UpstateNewYork = "egrid-2023/subregion/nyup/mwh";
    private const string Alaska = "egrid-2023/subregion/akgd/mwh";

    private static FactorSet Egrid => FactorCatalog.GetSet(SetId);

    [Fact]
    public void Set_IsVerifiedAndInThePublicDomain()
    {
        FactorSet set = Egrid;

        Assert.Equal(VerificationStatus.Verified, set.Verification);
        Assert.Equal("US", set.Region);
        Assert.Contains("public domain", set.Source.License);
    }

    [Fact]
    public void Set_CoversEveryEgridSubregion()
    {
        Assert.Equal(27, Egrid.Factors.Count);
        Assert.All(Egrid.Factors, f => Assert.Equal(Scope.Scope2, f.Scope));
        Assert.All(Egrid.Factors, f => Assert.Equal(Scope2Method.LocationBased, f.Scope2Method));
    }

    [Theory]
    [InlineData(California, 194.3512704)]
    [InlineData(UpstateNewYork, 109.8115704)]
    [InlineData(Alaska, 408.0735288)]
    public void CarbonDioxideRates_MatchThePublishedWorkbook(string id, double expected)
    {
        GasComponent co2 = FactorCatalog.Get(id).Components.Single(c => c.Gas == GreenhouseGas.CarbonDioxide);

        Assert.Equal(expected, co2.KilogramsPerUnit, precision: 6);
    }

    [Fact]
    public void GasMasses_ArePublishedRatherThanDerived()
    {
        EmissionFactor factor = FactorCatalog.Get(California);

        // eGRID publishes CO2, CH4 and N2O as separate masses per MWh, so unlike the
        // DESNZ set nothing had to be divided back out of a CO2e figure.
        Assert.False(factor.ComponentsAreDerived);
        Assert.Null(factor.PublishedCo2eKgPerUnit);
        Assert.Null(factor.PublishedGwpBasis);
        Assert.Equal(3, factor.Components.Count);
    }

    [Fact]
    public void Factors_CanBeAggregatedUnderEitherAssessmentReport()
    {
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double ar5 = new EmissionCalculator(GwpSet.Ar5).Calculate(consumption, California).Co2e.Value;
        double ar6 = new EmissionCalculator(GwpSet.Ar6).Calculate(consumption, California).Co2e.Value;

        // Having the gas split is what makes this possible; a CO2e-only factor would
        // have thrown for one of the two.
        Assert.NotEqual(ar5, ar6);
        Assert.True(ar5 > 194_000.0, "CO2 alone already exceeds this, before CH4 and N2O.");
    }

    [Fact]
    public void MeterReadingsInKilowattHours_ConvertToTheFactorsUnit()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);

        double fromMegawattHours = calculator.Calculate(new Quantity(1.0, Unit.MegawattHour), California).Co2e.Value;
        double fromKilowattHours = calculator.Calculate(new Quantity(1_000.0, Unit.KilowattHour), California).Co2e.Value;

        Assert.Equal(fromMegawattHours, fromKilowattHours, precision: 9);
    }

    [Fact]
    public void SubregionsDiffer_WhichIsTheWholePointOfUsingThem()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        var consumption = new Quantity(10_000.0, Unit.MegawattHour);

        double upstateNewYork = calculator.Calculate(consumption, UpstateNewYork).Co2e.Value;
        double alaska = calculator.Calculate(consumption, Alaska).Co2e.Value;

        // The same consumption is nearly four times the emissions on the Alaska grid.
        // A single national average would erase that.
        Assert.True(alaska > upstateNewYork * 3.0);
    }

    [Fact]
    public void EveryFactor_IsTraceableToItsSubregionRow()
    {
        Assert.All(Egrid.Factors, f => Assert.StartsWith("eGRID2023 sheet SRL23", f.SourceReference));
    }

    [Fact]
    public void AUsInventory_ReportsLocationBasedOnly()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar6)
            .CreateInventory()
            .Add(new Quantity(5_000.0, Unit.MegawattHour), California)
            .Build();

        Assert.True(inventory.Scope2.HasLocationBased);
        Assert.False(inventory.Scope2.HasMarketBased);

        // eGRID cannot supply a market-based figure: that depends on the contracts a
        // particular company holds, not on the grid it sits on.
        Assert.Throws<Scope2MethodNotReportedException>(() => inventory.TotalWith(Scope2Method.MarketBased));
    }
}
