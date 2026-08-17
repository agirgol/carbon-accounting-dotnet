using System.Collections.Generic;
using GhgAccounting.Catalog;

namespace GhgAccounting.Factors;

/// <summary>
/// A versioned group of emission factors from one publisher and one publication year.
/// </summary>
/// <remarks>
/// Sets are the unit of provenance and of versioning. Factors are never mixed across
/// sets implicitly, because a region, a year and a calorific basis are properties of
/// the publication, not of the individual row.
/// </remarks>
public sealed class FactorSet
{
    internal FactorSet(
        string id,
        string name,
        string? region,
        string? validFrom,
        string? validTo,
        CatalogSource source,
        VerificationStatus verification,
        EmissionFactor[] factors)
    {
        Id = id;
        Name = name;
        Region = region;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Source = source;
        Verification = verification;
        Factors = factors;

        for (int i = 0; i < factors.Length; i++)
        {
            factors[i].Set = this;
        }
    }

    /// <summary>Stable kebab-case identifier, including the publication year.</summary>
    public string Id { get; }

    /// <summary>Display name of the published set.</summary>
    public string Name { get; }

    /// <summary>
    /// ISO 3166-1 alpha-2 code, or <c>GLOBAL</c>. Applying a regional factor outside
    /// its region is one of the most common inventory errors, so the region travels
    /// with the data rather than living in documentation.
    /// </summary>
    public string? Region { get; }

    /// <summary>ISO-8601 date the set becomes applicable, or <see langword="null"/> if open-ended.</summary>
    public string? ValidFrom { get; }

    /// <summary>ISO-8601 date the set stops being applicable, or <see langword="null"/> if open-ended.</summary>
    public string? ValidTo { get; }

    /// <summary>Where the values came from.</summary>
    public CatalogSource Source { get; }

    /// <summary>Whether the shipped values have been checked against <see cref="Source"/>.</summary>
    public VerificationStatus Verification { get; }

    /// <summary>The factors in this set.</summary>
    public IReadOnlyList<EmissionFactor> Factors { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({Factors.Count} factors, {Verification})";
}
