using System.Linq;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class EurostatGridTests
{
    private const string SetId = "eurostat-grid-2023";
    private const string Turkiye = "eurostat-grid-2023/tr/public-grid/mwh";
    private const string France = "eurostat-grid-2023/fr/public-grid/mwh";
    private const string Norway = "eurostat-grid-2023/no/public-grid/mwh";
    private const string California = "egrid-2023/subregion/camx/mwh";

    private static FactorSet Set => FactorCatalog.GetSet(SetId);

    [Fact]
    public void Set_IsVerifiedAndDerivedFromEurostat()
    {
        Assert.Equal(VerificationStatus.Verified, Set.Verification);
        Assert.Contains("Eurostat", Set.Source.Publisher);
        Assert.Contains("free reuse", Set.Source.License);
    }

    [Fact]
    public void EveryFactor_CarriesItsOwnCountry()
    {
        // One published dataset covers many jurisdictions, so the region belongs on the
        // factor rather than on the set. Applying one country's grid factor to another's
        // consumption is the most common inventory error there is.
        Assert.All(Set.Factors, f => Assert.Equal(2, f.Region!.Length));
        Assert.Equal(Set.Factors.Count, Set.Factors.Select(f => f.Region).Distinct().Count());
    }

    [Fact]
    public void Factors_AreMarkedDerived()
    {
        // No authority publishes these. They are computed from two Eurostat series, and
        // the flag says so rather than letting them pass as published.
        Assert.All(Set.Factors, f => Assert.True(f.ComponentsAreDerived));
        Assert.All(Set.Factors, f => Assert.Null(f.PublishedGwpBasis));
        Assert.All(Set.Factors, f => Assert.Equal(Scope2Method.LocationBased, f.Scope2Method));
    }

    [Theory]
    [InlineData(Turkiye, 474.1)]
    [InlineData(France, 49.8)]
    [InlineData(Norway, 6.1)]
    public void Intensities_MatchTheDerivation(string id, double expected)
    {
        double actual = new EmissionCalculator(GwpSet.Ar5)
            .Calculate(new Quantity(1.0, Unit.MegawattHour), id).Co2e.Value;

        Assert.Equal(expected, actual, precision: 1);
    }

    [Fact]
    public void NuclearAndHydroGrids_ComeOutOrdersOfMagnitudeApartFromFossilOnes()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double norway = calculator.Calculate(consumption, Norway).Co2e.Value;
        double france = calculator.Calculate(consumption, France).Co2e.Value;
        double turkiye = calculator.Calculate(consumption, Turkiye).Co2e.Value;

        // Hydro, then nuclear, then a fossil-heavy grid. Roughly two orders of magnitude
        // between the ends, which is the whole reason a regional factor is not optional.
        Assert.True(norway < france);
        Assert.True(france < turkiye);
        Assert.True(turkiye > norway * 50.0);
    }

    [Fact]
    public void CountriesWhereTheAllocationDrivesTheAnswer_AreNotPublished()
    {
        var published = Set.Factors.Select(f => f.Region).ToHashSet();

        // Denmark, Lithuania and Latvia run large district heating networks, so most of
        // their CRF 1.A.1.a emissions sit against heat and the grid factor would come out
        // of the allocation convention rather than out of the data. The importer measures
        // that and declines to publish rather than shipping a citable guess.
        Assert.DoesNotContain("DK", published);
        Assert.DoesNotContain("LT", published);
        Assert.DoesNotContain("LV", published);
    }

    [Fact]
    public void FactorsSensitiveToTheAllocation_AreDowngradedToProxy()
    {
        // Everything published is within the tolerance, but the ones nearer the edge say
        // so through their data quality rather than looking as solid as the rest.
        Assert.Contains(Set.Factors, f => f.DataQuality == DataQuality.Proxy);
        Assert.Contains(Set.Factors, f => f.DataQuality == DataQuality.Secondary);
        Assert.All(Set.Factors, f => Assert.Contains("would move the result by", f.Note));
    }

    [Fact]
    public void GridFactorsAcrossSources_AreComparable()
    {
        var calculator = new EmissionCalculator(GwpSet.Ar6);
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double turkiye = calculator.Calculate(consumption, Turkiye).Co2e.Value;
        double california = calculator.Calculate(consumption, California).Co2e.Value;

        // Different publishers, different derivations, same unit and same basis, so the
        // two can sit in one inventory without the caller reconciling anything.
        Assert.True(turkiye > california);
    }

    [Fact]
    public void ChoiceOfGwpSet_BarelyMovesAGridFactor()
    {
        var consumption = new Quantity(1_000.0, Unit.MegawattHour);

        double ar5 = new EmissionCalculator(GwpSet.Ar5).Calculate(consumption, Turkiye).Co2e.Value;
        double ar6 = new EmissionCalculator(GwpSet.Ar6).Calculate(consumption, Turkiye).Co2e.Value;

        // Worth pinning: the AR5/AR6 choice matters enormously for fugitive methane and
        // hardly at all for grid electricity. A library that hid the choice would leave a
        // user unable to tell which case they were in.
        Assert.NotEqual(ar5, ar6);
        Assert.True(System.Math.Abs(ar6 - ar5) / ar5 < 0.001);
    }
}
