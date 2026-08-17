using System;
using System.Linq;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class GwpTableTests
{
    [Theory]
    [InlineData(GwpSet.Ar5)]
    [InlineData(GwpSet.Ar6)]
    public void For_ReturnsACompiledTable(GwpSet set)
    {
        GwpTable table = GwpTable.For(set);

        Assert.Equal(set, table.Set);
        Assert.Equal(100, table.TimeHorizonYears);
        Assert.NotEmpty(table.Values);
    }

    [Theory]
    [InlineData(GwpSet.Ar5)]
    [InlineData(GwpSet.Ar6)]
    public void CarbonDioxide_IsAlwaysTheReferenceGas(GwpSet set)
    {
        Assert.Equal(1.0, GwpTable.For(set).GetGwp(GreenhouseGas.CarbonDioxide));
    }

    [Fact]
    public void Ar5AndAr6_DisagreeOnEveryNonReferenceGas()
    {
        GwpTable ar5 = GwpTable.For(GwpSet.Ar5);
        GwpTable ar6 = GwpTable.For(GwpSet.Ar6);

        // The whole point of making the set an explicit caller choice: the same
        // inventory reports differently depending on which one is disclosed.
        foreach (GwpValue value in ar5.Values.Where(v => v.Gas != GreenhouseGas.CarbonDioxide))
        {
            Assert.True(
                ar6.TryGetGwp(value.Gas, out double ar6Gwp),
                $"AR6 should cover {value.Gas} if AR5 does.");

            Assert.NotEqual(value.Gwp, ar6Gwp);
        }
    }

    [Fact]
    public void FossilMethane_OutweighsBiogenicMethane_InBothSets()
    {
        foreach (GwpSet set in new[] { GwpSet.Ar5, GwpSet.Ar6 })
        {
            GwpTable table = GwpTable.For(set);

            Assert.True(
                table.GetGwp(GreenhouseGas.MethaneFossil) > table.GetGwp(GreenhouseGas.MethaneBiogenic),
                $"{set}: fossil methane oxidises to fossil CO2, so it must carry the higher potential.");
        }
    }

    [Fact]
    public void SameActivityData_ProducesDifferentTotals_UnderDifferentSets()
    {
        // One tonne of fugitive fossil methane from a gas network.
        var leak = new Quantity(1.0, Unit.Tonne);

        double underAr5 = GwpTable.For(GwpSet.Ar5).ToCo2e(leak, GreenhouseGas.MethaneFossil).Value;
        double underAr6 = GwpTable.For(GwpSet.Ar6).ToCo2e(leak, GreenhouseGas.MethaneFossil).Value;

        Assert.NotEqual(underAr5, underAr6);
        Assert.True(Math.Abs(underAr5 - underAr6) / underAr5 > 0.005);
    }

    [Fact]
    public void ToCo2e_KeepsTheInputUnit()
    {
        var mass = new Quantity(2.0, Unit.Kilogram);

        Quantity result = GwpTable.For(GwpSet.Ar6).ToCo2e(mass, GreenhouseGas.CarbonDioxide);

        Assert.Equal(Unit.Kilogram, result.Unit);
        Assert.Equal(2.0, result.Value);
    }

    [Fact]
    public void ToCo2e_RejectsANonMassQuantity()
    {
        var energy = new Quantity(100.0, Unit.KilowattHour);

        Assert.Throws<ArgumentException>(
            () => GwpTable.For(GwpSet.Ar6).ToCo2e(energy, GreenhouseGas.CarbonDioxide));
    }

    [Fact]
    public void GetGwp_ForAnUncoveredGas_ThrowsRatherThanReturningZero()
    {
        GwpTable table = GwpTable.For(GwpSet.Ar6);
        var uncovered = (GreenhouseGas)9_999;

        GasNotCoveredException exception =
            Assert.Throws<GasNotCoveredException>(() => table.GetGwp(uncovered));

        Assert.Equal(GwpSet.Ar6, exception.Set);
    }

    [Fact]
    public void TryGetGwp_ForAnUncoveredGas_ReturnsFalse()
    {
        Assert.False(GwpTable.For(GwpSet.Ar6).TryGetGwp((GreenhouseGas)9_999, out double gwp));
        Assert.Equal(0.0, gwp);
    }

    [Fact]
    public void EverySet_CarriesACitationAndAVerificationStatus()
    {
        Assert.NotEmpty(GwpTable.All);

        foreach (GwpTable table in GwpTable.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(table.Source.Publisher));
            Assert.False(string.IsNullOrWhiteSpace(table.Source.Title));
            Assert.True(table.Source.PublicationYear > 1990);
            Assert.True(Enum.IsDefined(table.Verification));
        }
    }

    [Theory]
    [InlineData(GwpSet.Ar5, false)]
    [InlineData(GwpSet.Ar6, true)]
    public void ClimateCarbonFeedback_MatchesWhatTheReportActuallyPublishes(GwpSet set, bool expected)
    {
        // AR5 Table 8.A.1 includes feedbacks for CO2 only — the non-CO2 values with
        // feedbacks live in Table 8.SM.16 and are not what ships here. AR6 changed
        // approach and includes carbon cycle responses in its headline metrics.
        // Getting this flag wrong misdescribes the numbers without changing them.
        Assert.Equal(expected, GwpTable.For(set).IncludesClimateCarbonFeedback);
    }

    [Fact]
    public void EveryValue_RecordsTheTableItWasReadFrom()
    {
        // AR6 draws on two tables, so a set-level citation is not enough to say where
        // any individual number came from.
        foreach (GwpTable table in GwpTable.All)
        {
            Assert.All(table.Values, value => Assert.False(string.IsNullOrWhiteSpace(value.SourceTable)));
        }
    }

    // Regression guard on data that has been signed off against the primary source.
    // A silent edit to the catalog should fail here, not in someone's disclosure.
    [Theory]
    [InlineData(GwpSet.Ar5, GreenhouseGas.CarbonDioxide, 1)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.MethaneFossil, 30)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.MethaneBiogenic, 28)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.NitrousOxide, 265)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.SulfurHexafluoride, 23_500)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.NitrogenTrifluoride, 16_100)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc23, 12_400)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc32, 677)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc125, 3_170)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc134a, 1_300)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc143a, 4_800)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Hfc152a, 138)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Pfc14, 6_630)]
    [InlineData(GwpSet.Ar5, GreenhouseGas.Pfc116, 11_100)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.CarbonDioxide, 1)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.MethaneFossil, 29.8)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.MethaneBiogenic, 27.0)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.NitrousOxide, 273)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.SulfurHexafluoride, 24_300)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.NitrogenTrifluoride, 17_400)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc23, 14_600)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc32, 771)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc125, 3_740)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc134a, 1_530)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc143a, 5_810)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Hfc152a, 164)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Pfc14, 7_380)]
    [InlineData(GwpSet.Ar6, GreenhouseGas.Pfc116, 12_400)]
    public void VerifiedValues_MatchTheCitedIpccTable(GwpSet set, GreenhouseGas gas, double expected)
    {
        Assert.Equal(expected, GwpTable.For(set).GetGwp(gas));
    }
}
