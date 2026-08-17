using System.Globalization;
using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Reporting;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class InventoryReportTests
{
    private const string NaturalGas = "defra-2026/fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string UkGrid = "defra-2026/uk-electricity/electricity-generated/electricity-uk/kwh/kwh";
    private const string UkWtt = "defra-2026/wtt-fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string TurkishGrid = "eurostat-grid-2023/tr/public-grid/mwh";
    private const string SyntheticBiomass = "example-value-chain/biomass/wood-pellets/kwh";
    private const string SyntheticMarketBased = "example-fuels/grid-electricity/market-based-residual/kwh";

    private static Inventory UkInventory() =>
        new EmissionCalculator(GwpSet.Ar5).CreateInventory()
            .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGas)
            .Add(new Quantity(400_000.0, Unit.KilowattHour), UkGrid)
            .Add(new Quantity(250_000.0, Unit.KilowattHour), UkWtt)
            .Build();

    [Fact]
    public void Report_StatesTheDeclarationsAStandardRequires()
    {
        InventoryReport report = InventoryReport.For(UkInventory(), Scope2Method.LocationBased);

        Assert.Equal(GwpSet.Ar5, report.GwpSet);
        Assert.Equal(Scope2Method.LocationBased, report.Scope2Method);
        Assert.True(report.Scope1.Value > 0);
        Assert.True(report.Scope2.Value > 0);
        Assert.True(report.Scope3.Value > 0);
    }

    [Fact]
    public void Total_AgreesWithTheInventoryItDescribes()
    {
        Inventory inventory = UkInventory();
        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        Assert.Equal(
            inventory.TotalWith(Scope2Method.LocationBased).Value,
            report.Total.Value,
            precision: 9);
    }

    [Fact]
    public void Sources_AreCitedWithTheirShareOfTheTotal()
    {
        InventoryReport report = InventoryReport.For(UkInventory(), Scope2Method.LocationBased);

        Assert.Single(report.Sources);
        Assert.Equal("defra-2026", report.Sources[0].Set.Id);
        Assert.Equal(3, report.Sources[0].FactorCount);
        Assert.Equal(1.0, report.Sources[0].Share, precision: 9);
    }

    [Fact]
    public void Sources_AreOrderedByContribution()
    {
        Inventory mixed = new EmissionCalculator(GwpSet.Ar5).CreateInventory()
            .Add(new Quantity(10.0, Unit.MegawattHour), TurkishGrid)
            .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGas)
            .Build();

        InventoryReport report = InventoryReport.For(mixed, Scope2Method.LocationBased);

        Assert.Equal(2, report.Sources.Count);
        Assert.True(report.Sources[0].Share >= report.Sources[1].Share);
        Assert.Equal(1.0, report.Sources.Sum(s => s.Share), precision: 9);
    }

    [Fact]
    public void Caveats_NameTheDerivedFactorSets()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar5).CreateInventory()
            .Add(new Quantity(10.0, Unit.MegawattHour), TurkishGrid)
            .Build();

        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        ReportCaveat derived = Assert.Single(
            report.Caveats, c => c.Kind == ReportCaveatKind.DerivedFactorComponents);

        Assert.Contains("eurostat-grid-2023", derived.Detail);
    }

    [Fact]
    public void Caveats_FlagUnverifiedData()
    {
        // The synthetic sets are placeholders and say so; a report built on them must
        // carry that forward rather than presenting the total as sound.
        Inventory inventory = new EmissionCalculator(GwpSet.Ar6).CreateInventory()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), SyntheticBiomass)
            .Build();

        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        Assert.Contains(report.Caveats, c => c.Kind == ReportCaveatKind.UnverifiedData);
    }

    [Fact]
    public void Caveats_FlagScope2ReportedUnderOnlyOneMethod()
    {
        InventoryReport report = InventoryReport.For(UkInventory(), Scope2Method.LocationBased);

        ReportCaveat caveat = Assert.Single(
            report.Caveats, c => c.Kind == ReportCaveatKind.Scope2NotDualReported);

        Assert.Contains("location-based", caveat.Detail);
    }

    [Fact]
    public void Caveats_AreAbsentWhenBothScope2MethodsArePresent()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar6).CreateInventory()
            .Add(new Quantity(10_000.0, Unit.KilowattHour), "example-fuels/grid-electricity/location-based/kwh")
            .Add(new Quantity(10_000.0, Unit.KilowattHour), SyntheticMarketBased)
            .Build();

        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        Assert.DoesNotContain(report.Caveats, c => c.Kind == ReportCaveatKind.Scope2NotDualReported);
    }

    [Fact]
    public void Caveats_FlagMixingRegions()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar5).CreateInventory()
            .Add(new Quantity(10.0, Unit.MegawattHour), TurkishGrid)
            .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGas)
            .Build();

        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        ReportCaveat caveat = Assert.Single(
            report.Caveats, c => c.Kind == ReportCaveatKind.RegionMismatch);

        Assert.Contains("TR", caveat.Detail);
        Assert.Contains("GB", caveat.Detail);
    }

    [Fact]
    public void BiogenicCarbon_IsDisclosedOutsideTheTotal()
    {
        Inventory inventory = new EmissionCalculator(GwpSet.Ar6).CreateInventory()
            .Add(new Quantity(1_000.0, Unit.KilowattHour), SyntheticBiomass)
            .Build();

        InventoryReport report = InventoryReport.For(inventory, Scope2Method.LocationBased);

        Assert.Equal(390.0, report.BiogenicCarbon.Value, precision: 6);
        Assert.True(report.Total.Value < 10.0);
    }

    [Fact]
    public void Markdown_CarriesTheDeclarationsFiguresAndCaveats()
    {
        string markdown = InventoryReport.For(UkInventory(), Scope2Method.LocationBased).ToMarkdown();

        Assert.Contains("# Greenhouse gas inventory", markdown);
        Assert.Contains("| Global warming potentials | Ar5, 100-year |", markdown);
        Assert.Contains("| Scope 2 method | LocationBased |", markdown);
        Assert.Contains("Biogenic CO2", markdown);
        Assert.Contains("## Factor sources", markdown);
        Assert.Contains("defra-2026", markdown);
        Assert.Contains("## Caveats", markdown);
        Assert.Contains("Scope2NotDualReported", markdown);
    }

    [Fact]
    public void Markdown_FormatsNumbersInvariantlyWhateverTheThreadCulture()
    {
        InventoryReport report = InventoryReport.For(UkInventory(), Scope2Method.LocationBased);
        string underInvariant = report.ToMarkdown();

        // Built by hand rather than by name, because the test project runs in
        // globalization-invariant mode and cannot load a real locale. Swapping the
        // separators is enough to catch a stray current-culture format.
        var swapped = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        swapped.NumberFormat.NumberDecimalSeparator = ",";
        swapped.NumberFormat.NumberGroupSeparator = ".";
        swapped.NumberFormat.PercentDecimalSeparator = ",";
        swapped.NumberFormat.PercentGroupSeparator = ".";

        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = swapped;

            // A report that renders "1.234" on one machine and "1,234" on another is a
            // reconciliation problem, and the reader cannot tell which they have.
            Assert.Equal(underInvariant, report.ToMarkdown());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Report_ForAMethodNeverReported_Throws()
    {
        Assert.Throws<Scope2MethodNotReportedException>(
            () => InventoryReport.For(UkInventory(), Scope2Method.MarketBased));
    }
}
