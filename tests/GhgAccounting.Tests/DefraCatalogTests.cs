using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class DefraCatalogTests
{
    private const string SetId = "defra-2026-secr";
    private const string NaturalGasGross = "defra-2026/fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string NaturalGasWtt = "defra-2026/wtt-fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string UkElectricity = "defra-2026/uk-electricity/electricity-generated/electricity-uk/kwh/kwh";
    private const string UkElectricityTd = "defra-2026/transmission-and-distribution/t-and-d-uk-electricity/electricity-uk/kwh/kwh";
    private const string DieselBlendLitres = "defra-2026/fuels/liquid-fuels/diesel-average-biofuel-blend/litres";

    private static FactorSet Defra => FactorCatalog.GetSet(SetId);

    [Fact]
    public void Set_IsVerifiedAndAttributed()
    {
        FactorSet set = Defra;

        Assert.Equal(VerificationStatus.Verified, set.Verification);
        Assert.Equal("GB", set.Region);
        Assert.Equal(2026, set.Source.PublicationYear);
        Assert.Contains("Open Government Licence", set.Source.License);
    }

    [Fact]
    public void Set_CoversAllThreeScopes()
    {
        var scopes = Defra.Factors.Select(f => f.Scope).Distinct().OrderBy(s => s).ToList();

        Assert.Equal(new[] { Scope.Scope1, Scope.Scope2, Scope.Scope3 }, scopes);
    }

    [Theory]
    [InlineData(NaturalGasGross, 0.18231)]
    [InlineData(UkElectricity, 0.13096)]
    [InlineData(UkElectricityTd, 0.01299)]
    [InlineData(NaturalGasWtt, 0.03021)]
    public void PublishedFigures_MatchTheDesnzFlatFile(string id, double expected)
    {
        Assert.Equal(expected, FactorCatalog.Get(id).PublishedCo2eKgPerUnit!.Value, precision: 8);
    }

    [Fact]
    public void UkElectricity_IsScope2LocationBased()
    {
        EmissionFactor factor = FactorCatalog.Get(UkElectricity);

        // A national dataset can only give the grid average. A market-based factor
        // depends on the contracts a particular company holds.
        Assert.Equal(Scope.Scope2, factor.Scope);
        Assert.Equal(Scope2Method.LocationBased, factor.Scope2Method);
    }

    [Fact]
    public void FuelEnergyFactors_DeclareTheirCalorificBasis()
    {
        Assert.Equal(CalorificBasis.GrossCalorificValue, FactorCatalog.Get(NaturalGasGross).Basis);

        EmissionFactor net = FactorCatalog.Get("defra-2026/fuels/gaseous-fuels/natural-gas/kwh-net-cv");
        Assert.Equal(CalorificBasis.NetCalorificValue, net.Basis);

        // The two bases differ by roughly 10% for gas; pairing activity data with the
        // wrong one is a silent error of that size.
        Assert.True(net.PublishedCo2eKgPerUnit > FactorCatalog.Get(NaturalGasGross).PublishedCo2eKgPerUnit);
    }

    [Fact]
    public void GasBreakdowns_AreMarkedAsDerived()
    {
        EmissionFactor factor = FactorCatalog.Get(NaturalGasGross);

        // DESNZ publishes the split already multiplied by its own GWPs, so the masses
        // here were divided back out rather than read off the page.
        Assert.True(factor.ComponentsAreDerived);
        Assert.Equal(GwpSet.Ar5, factor.PublishedGwpBasis);
        Assert.Equal(3, factor.Components.Count);
    }

    [Fact]
    public void WellToTankFactors_HaveNoBreakdownToReAggregate()
    {
        EmissionFactor factor = FactorCatalog.Get(NaturalGasWtt);

        Assert.Empty(factor.Components);
        Assert.NotNull(factor.PublishedCo2eKgPerUnit);
        Assert.Equal(3, factor.Scope3Category);
    }

    [Fact]
    public void WellToTankFactors_RefuseToBeUsedUnderAnotherGwpSet()
    {
        var activity = new Quantity(1_000.0, Unit.KilowattHour);

        // Works under the basis it was published on.
        Assert.Equal(30.21, new EmissionCalculator(GwpSet.Ar5).Calculate(activity, NaturalGasWtt).Co2e.Value, precision: 6);

        // Refuses under any other: there is no split left to re-aggregate, and
        // converting the aggregate would mean inventing one.
        GwpBasisMismatchException exception = Assert.Throws<GwpBasisMismatchException>(
            () => new EmissionCalculator(GwpSet.Ar6).Calculate(activity, NaturalGasWtt));

        Assert.Equal(GwpSet.Ar5, exception.PublishedBasis);
        Assert.Equal(GwpSet.Ar6, exception.Requested);
    }

    [Fact]
    public void FactorsWithABreakdown_CanBeReAggregatedUnderAr6()
    {
        var activity = new Quantity(10_000.0, Unit.KilowattHour);

        double ar5 = new EmissionCalculator(GwpSet.Ar5).Calculate(activity, NaturalGasGross).Co2e.Value;
        double ar6 = new EmissionCalculator(GwpSet.Ar6).Calculate(activity, NaturalGasGross).Co2e.Value;

        // This is the whole reason the split is recovered rather than the aggregate
        // simply being stored: the same DESNZ factor can be reported under either set.
        Assert.NotEqual(ar5, ar6);
    }

    [Fact]
    public void RecomputedTotals_TrackThePublishedOnesClosely()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar5);
        var activity = new Quantity(1_000.0, Unit.KilowattHour);

        EmissionResult result = calculator.Calculate(activity, NaturalGasGross);

        Assert.NotNull(result.PublishedCo2e);

        double published = result.PublishedCo2e!.Value.Value;
        double recomputed = result.Co2e.Value;
        double relative = (recomputed - published) / published;

        // Recomputing lands just above DESNZ's own figure, because DESNZ applies the
        // non-fossil methane potential of 28 to fossil fuels while this library applies
        // the fossil value of 30 that AR5 publishes for them. The gap is real, tiny, and
        // documented — which is why both numbers are kept side by side.
        Assert.True(relative > 0, "Applying the fossil methane potential should raise the total.");
        Assert.True(relative < 0.001, $"Divergence from the published figure was {relative:P4}, which is larger than expected.");
    }

    [Fact]
    public void BlendedForecourtFuels_CarryTheirBiogenicCarbonSeparately()
    {
        EmissionFactor factor = FactorCatalog.Get(DieselBlendLitres);

        // DESNZ reports the biofuel fraction's CO2 outside the scopes, exactly as the
        // GHG Protocol requires. It rides along with the factor but never enters a total.
        Assert.Equal(0.14, factor.BiogenicCarbonKg, precision: 6);

        Inventory inventory = new EmissionCalculator(GwpSet.Ar5)
            .CreateInventory()
            .Add(new Quantity(1_000.0, Unit.Litre), DieselBlendLitres)
            .Build();

        Assert.Equal(140.0, inventory.BiogenicCarbon.Value, precision: 6);
        Assert.DoesNotContain("biogenic", inventory.TotalWith(Scope2Method.LocationBased).ToString());
        Assert.True(inventory.TotalWith(Scope2Method.LocationBased).Value < 140.0 + inventory.Scope1.Value);
    }

    [Fact]
    public void EveryFactor_IsTraceableToARowInTheSourceFile()
    {
        Assert.All(Defra.Factors, f => Assert.StartsWith("DESNZ 2026 flat file", f.SourceReference));
    }

    [Fact]
    public void AnEndToEndSecrInventory_ReportsBothScope2Methods()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar5)
            .CreateInventory()
            .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGasGross)   // Scope 1
            .Add(new Quantity(400_000.0, Unit.KilowattHour), UkElectricity)     // Scope 2
            .Add(new Quantity(400_000.0, Unit.KilowattHour), UkElectricityTd)   // Scope 3 cat 3
            .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGasWtt)     // Scope 3 cat 3
            .Build();

        Assert.True(inventory.Scope1.Value > 0);
        Assert.True(inventory.Scope2.HasLocationBased);
        Assert.False(inventory.Scope2.HasMarketBased);
        Assert.Equal(new[] { 3 }, inventory.Scope3ByCategory.Select(c => c.Category));

        // No market-based factor was supplied, so asking for that total is an error
        // rather than a quietly smaller number.
        Assert.Throws<Scope2MethodNotReportedException>(() => inventory.TotalWith(Scope2Method.MarketBased));

        Quantity total = inventory.TotalWith(Scope2Method.LocationBased).ConvertTo(Unit.Tonne);
        Assert.True(total.Value > 0);
    }
}
