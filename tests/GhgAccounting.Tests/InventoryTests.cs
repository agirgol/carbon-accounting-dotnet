using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class InventoryTests
{
    private const string NaturalGas = "example-fuels/natural-gas/gcv/kwh";
    private const string GridLocationBased = "example-fuels/grid-electricity/location-based/kwh";
    private const string GridMarketBased = "example-fuels/grid-electricity/market-based-residual/kwh";
    private const string WoodPellets = "example-value-chain/biomass/wood-pellets/kwh";
    private const string AirTravel = "example-value-chain/business-travel/air-short-haul/passenger-km";
    private const string RoadFreight = "example-value-chain/upstream-transport/road-freight/tonne-km";

    private static InventoryBuilder Builder(GwpSet set = GwpSet.Ar6) =>
        new EmissionCalculator(set).CreateInventory();

    [Fact]
    public void Build_GroupsEmissionsByScope()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased)
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridMarketBased)
            .Add(new Quantity(5_000.0, Unit.PassengerKilometre), AirTravel)
            .Build();

        Assert.Equal(216.24, inventory.Scope1.Value, precision: 6);
        Assert.Equal(4_000.0, inventory.Scope2.LocationBased.Value, precision: 6);
        Assert.Equal(5_200.0, inventory.Scope2.MarketBased.Value, precision: 6);
        Assert.True(inventory.Scope3.Value > 0.0);
    }

    [Fact]
    public void TotalWith_DiffersBetweenTheTwoScope2Methods()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased)
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridMarketBased)
            .Build();

        double location = inventory.TotalWith(Scope2Method.LocationBased).Value;
        double market = inventory.TotalWith(Scope2Method.MarketBased).Value;

        Assert.Equal(216.24 + 4_000.0, location, precision: 6);
        Assert.Equal(216.24 + 5_200.0, market, precision: 6);

        // The two are never summed: a company reports both, and each total counts its
        // purchased electricity exactly once.
        Assert.NotEqual(location, market);
    }

    [Fact]
    public void TotalWith_ForAMethodThatWasNeverReported_Throws()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased)
            .Build();

        Scope2MethodNotReportedException exception = Assert.Throws<Scope2MethodNotReportedException>(
            () => inventory.TotalWith(Scope2Method.MarketBased));

        Assert.Equal(Scope2Method.MarketBased, exception.Requested);
    }

    [Fact]
    public void TotalWith_OnAnInventoryWithNoScope2_ReturnsTheOtherScopes()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)
            .Build();

        // No purchased electricity at all is a legitimate zero, not missing data.
        Assert.Equal(216.24, inventory.TotalWith(Scope2Method.LocationBased).Value, precision: 6);
        Assert.Equal(216.24, inventory.TotalWith(Scope2Method.MarketBased).Value, precision: 6);
        Assert.True(inventory.Scope2.IsEmpty);
    }

    [Fact]
    public void BiogenicCarbon_IsReportedButExcludedFromEveryTotal()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), WoodPellets)
            .Build();

        Assert.Equal(390.0, inventory.BiogenicCarbon.Value, precision: 6);

        // Only the CH4 and N2O from the same combustion reach the scope total.
        Assert.Equal(2.178, inventory.Scope1.Value, precision: 6);
        Assert.Equal(2.178, inventory.TotalWith(Scope2Method.LocationBased).Value, precision: 6);
    }

    [Fact]
    public void Scope3_IsBrokenDownByCategoryInAscendingOrder()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(20_000.0, Unit.TonneKilometre), RoadFreight)  // category 4
            .Add(new Quantity(5_000.0, Unit.PassengerKilometre), AirTravel) // category 6
            .Build();

        Assert.Equal(new[] { 4, 6 }, inventory.Scope3ByCategory.Select(c => c.Category));
        Assert.Equal(
            inventory.Scope3.Value,
            inventory.Scope3ByCategory.Sum(c => c.Co2e.Value),
            precision: 9);
        Assert.Equal(0.0, inventory.Scope3Uncategorised.Value);
    }

    [Fact]
    public void Add_AResultFromAnotherGwpSet_Throws()
    {
        EmissionResult underAr5 = new EmissionCalculator(GwpSet.Ar5)
            .Calculate(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas);

        GwpSetMismatchException exception = Assert.Throws<GwpSetMismatchException>(
            () => Builder(GwpSet.Ar6).Add(underAr5));

        Assert.Equal(GwpSet.Ar6, exception.Expected);
        Assert.Equal(GwpSet.Ar5, exception.Actual);
    }

    [Fact]
    public void UncertaintyPercentFor_CombinesComponentsInQuadrature()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)         // 216.24 kg, 5%
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased) // 4000 kg, 10%
            .Build();

        double? combined = inventory.UncertaintyPercentFor(Scope2Method.LocationBased);

        // sqrt((216.24 x 0.05)^2 + (4000 x 0.10)^2) / 4216.24 = 9.4907%
        Assert.NotNull(combined);
        Assert.Equal(9.49, combined!.Value, precision: 2);

        // Combining in quadrature must not exceed the largest single component.
        Assert.True(combined.Value < 10.0);
    }

    [Fact]
    public void UncertaintyPercentFor_AnEmptyInventory_IsNull()
    {
        Assert.Null(Builder().Build().UncertaintyPercentFor(Scope2Method.LocationBased));
    }

    [Fact]
    public void DataQualityBreakdown_SplitsTheTotalAcrossTiers()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)      // Secondary
            .Add(new Quantity(20_000.0, Unit.TonneKilometre), RoadFreight)  // Proxy
            .Build();

        var breakdown = inventory.DataQualityBreakdownFor(Scope2Method.LocationBased);

        Assert.Equal(new[] { DataQuality.Secondary, DataQuality.Proxy }, breakdown.Select(b => b.Quality));
        Assert.Equal(1.0, breakdown.Sum(b => b.Share), precision: 9);
        Assert.Equal(
            inventory.TotalWith(Scope2Method.LocationBased).Value,
            breakdown.Sum(b => b.Co2e.Value),
            precision: 9);
    }

    [Fact]
    public void Entries_ArePreservedForAudit()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), NaturalGas)
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased)
            .Build();

        Assert.Equal(2, inventory.Entries.Count);
        Assert.All(inventory.Entries, e => Assert.Equal(GwpSet.Ar6, e.GwpSet));
        Assert.All(inventory.Entries, e => Assert.NotNull(e.Factor.Set.Source));
    }

    [Fact]
    public void Totals_CanBeReportedInTonnes()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridLocationBased)
            .Build();

        Quantity tonnes = inventory.TotalWith(Scope2Method.LocationBased).ConvertTo(Unit.Tonne);

        Assert.Equal(4.0, tonnes.Value, precision: 9);
        Assert.Equal(Unit.Tonne, tonnes.Unit);
    }

    [Fact]
    public void ToString_DoesNotThrowWhenOnlyMarketBasedDataExists()
    {
        Inventory inventory = Builder()
            .Add(new Quantity(10_000.0, Unit.KilowattHour), GridMarketBased)
            .Build();

        Assert.Contains("market-based", inventory.ToString());
    }
}
