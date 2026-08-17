using System;
using System.Collections.Generic;
using System.Linq;
using GhgAccounting.Factors;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class FactorCatalogTests
{
    [Fact]
    public void Catalog_IsCompiledIn()
    {
        Assert.NotEmpty(FactorCatalog.Sets);
        Assert.NotEmpty(FactorCatalog.Factors);
    }

    [Fact]
    public void FactorIds_AreUnique()
    {
        List<string> duplicates = FactorCatalog.Factors
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryFactor_KnowsWhichPublishedSetItCameFrom()
    {
        foreach (EmissionFactor factor in FactorCatalog.Factors)
        {
            Assert.NotNull(factor.Set);
            Assert.False(string.IsNullOrWhiteSpace(factor.Set.Source.Publisher));
            Assert.Contains(factor, factor.Set.Factors);
        }
    }

    [Fact]
    public void EveryFactor_CarriesSomethingCalculable()
    {
        // Either a gas breakdown, which can be re-aggregated under any GWP set, or a
        // published CO2e figure that states the set it was aggregated under. A factor
        // with neither cannot produce a number and must not compile.
        Assert.All(
            FactorCatalog.Factors,
            factor => Assert.True(
                factor.Components.Count > 0 ||
                (factor.PublishedCo2eKgPerUnit is not null && factor.PublishedGwpBasis is not null),
                $"{factor.Id} has no gas breakdown and no published CO2e basis."));
    }

    [Fact]
    public void EveryFactor_UsesADefinedUnit()
    {
        Assert.All(FactorCatalog.Factors, factor => Assert.True(UnitConverter.IsDefined(factor.Unit)));
    }

    [Fact]
    public void Scope2Factors_DeclareWhichMethodTheyServe()
    {
        // The Scope 2 Guidance mandates dual reporting; a factor that does not say
        // which side it belongs to cannot be placed in either column.
        foreach (EmissionFactor factor in FactorCatalog.Factors.Where(f => f.Scope == Scope.Scope2))
        {
            Assert.NotNull(factor.Scope2Method);
        }
    }

    [Fact]
    public void NonScope2Factors_DoNotDeclareAScope2Method()
    {
        foreach (EmissionFactor factor in FactorCatalog.Factors.Where(f => f.Scope != Scope.Scope2))
        {
            Assert.Null(factor.Scope2Method);
        }
    }

    [Fact]
    public void Scope3Categories_AreWithinTheFifteenDefinedByTheStandard()
    {
        foreach (EmissionFactor factor in FactorCatalog.Factors.Where(f => f.Scope3Category is not null))
        {
            Assert.InRange(factor.Scope3Category!.Value, 1, 15);
        }
    }

    [Fact]
    public void LocationBasedAndMarketBasedGridFactors_AreBothAvailable()
    {
        List<EmissionFactor> grid = FactorCatalog.Factors
            .Where(f => f.Scope == Scope.Scope2)
            .ToList();

        Assert.Contains(grid, f => f.Scope2Method == Scope2Method.LocationBased);
        Assert.Contains(grid, f => f.Scope2Method == Scope2Method.MarketBased);
    }

    [Fact]
    public void Get_ByUnknownId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => FactorCatalog.Get("no-such-factor"));
    }

    [Fact]
    public void TryGet_ByKnownId_ReturnsTheFactor()
    {
        string id = FactorCatalog.Factors[0].Id;

        Assert.True(FactorCatalog.TryGet(id, out EmissionFactor? factor));
        Assert.NotNull(factor);
        Assert.Equal(id, factor!.Id);
    }

    [Fact]
    public void UnverifiedSets_AreVisibleThroughTheApi()
    {
        // A caller building a compliance report must be able to refuse to use data
        // that has not been checked against its source. That means the status has to
        // be readable at run time, not just recorded in the repository.
        Assert.All(
            FactorCatalog.Sets,
            set => Assert.True(Enum.IsDefined(set.Verification)));
    }
}
