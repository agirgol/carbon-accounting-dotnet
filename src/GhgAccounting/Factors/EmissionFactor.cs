using System.Collections.Generic;
using GhgAccounting.Units;

namespace GhgAccounting.Factors;

/// <summary>
/// A single published conversion from one unit of activity to a set of gas masses,
/// a CO<sub>2</sub>e figure, or both.
/// </summary>
/// <remarks>
/// Two shapes exist because published datasets come in two shapes. A factor with a gas
/// breakdown is GWP-agnostic and can be aggregated under whichever set the caller
/// chooses. A factor with only <see cref="PublishedCo2eKgPerUnit"/> has already been
/// aggregated by its publisher and is bound to <see cref="PublishedGwpBasis"/> — most
/// value-chain factors are published this way and there is no data behind them to
/// recover a split from.
/// </remarks>
public sealed class EmissionFactor
{
    private readonly string? _region;

    internal EmissionFactor(
        string id,
        string activity,
        Scope scope,
        int? scope3Category,
        Scope2Method? scope2Method,
        Unit unit,
        CalorificBasis basis,
        GasComponent[] components,
        bool componentsAreDerived,
        double? publishedCo2eKgPerUnit,
        GwpSet? publishedGwpBasis,
        double biogenicCarbonKg,
        DataQuality dataQuality,
        double? uncertaintyPercent,
        string? note,
        string? sourceReference,
        string? region)
    {
        _region = region;
        Id = id;
        Activity = activity;
        Scope = scope;
        Scope3Category = scope3Category;
        Scope2Method = scope2Method;
        Unit = unit;
        Basis = basis;
        Components = components;
        ComponentsAreDerived = componentsAreDerived;
        PublishedCo2eKgPerUnit = publishedCo2eKgPerUnit;
        PublishedGwpBasis = publishedGwpBasis;
        BiogenicCarbonKg = biogenicCarbonKg;
        DataQuality = dataQuality;
        UncertaintyPercent = uncertaintyPercent;
        Note = note;
        SourceReference = sourceReference;
        Set = null!; // Assigned by the owning FactorSet during construction.
    }

    /// <summary>
    /// Stable, globally unique identifier. Never reused with a changed value: a
    /// corrected factor ships under a new id so that a restated inventory is
    /// distinguishable from an unchanged one.
    /// </summary>
    public string Id { get; }

    /// <summary>The activity this factor applies to, as the publisher names it.</summary>
    public string Activity { get; }

    /// <summary>The GHG Protocol scope the resulting emissions belong to.</summary>
    public Scope Scope { get; }

    /// <summary>The Scope 3 category (1-15), or <see langword="null"/> outside Scope 3.</summary>
    public int? Scope3Category { get; }

    /// <summary>
    /// Which Scope 2 method this factor serves, or <see langword="null"/> outside Scope 2.
    /// A factor is only ever valid for one method.
    /// </summary>
    public Scope2Method? Scope2Method { get; }

    /// <summary>The unit of activity data this factor's denominator is expressed in.</summary>
    public Unit Unit { get; }

    /// <summary>The calorific basis, for fuel-energy factors.</summary>
    public CalorificBasis Basis { get; }

    /// <summary>
    /// Kilograms of each gas released per one <see cref="Unit"/> of activity. Empty when
    /// the publisher gives no breakdown, in which case <see cref="PublishedCo2eKgPerUnit"/>
    /// carries the figure.
    /// </summary>
    public IReadOnlyList<GasComponent> Components { get; }

    /// <summary>
    /// Whether <see cref="Components"/> was reconstructed rather than published as gas
    /// masses.
    /// </summary>
    /// <remarks>
    /// Publishers normally give the split already multiplied by their own GWPs, so
    /// recovering masses means dividing those back out. The arithmetic is exact, but the
    /// inputs carry the publisher's rounding, and a reader is entitled to know which
    /// numbers came off the page and which were computed.
    /// </remarks>
    public bool ComponentsAreDerived { get; }

    /// <summary>
    /// The CO<sub>2</sub>e figure the publisher printed, per unit of activity, or
    /// <see langword="null"/> if none was given.
    /// </summary>
    /// <remarks>
    /// Kept even when <see cref="Components"/> is present, so a caller can reproduce the
    /// publisher's own total exactly — which is what a compliance filing usually needs —
    /// rather than only the value this library recomputes.
    /// </remarks>
    public double? PublishedCo2eKgPerUnit { get; }

    /// <summary>
    /// The assessment report <see cref="PublishedCo2eKgPerUnit"/> was aggregated under.
    /// </summary>
    /// <remarks>
    /// A CO<sub>2</sub>e figure is meaningless without it, which is why a factor with no
    /// gas breakdown cannot be used in an inventory built on a different set.
    /// </remarks>
    public GwpSet? PublishedGwpBasis { get; }

    /// <summary>
    /// Biogenic CO<sub>2</sub> released per unit of activity, in kilograms. Reported
    /// separately under the GHG Protocol and never added into the scope totals.
    /// </summary>
    public double BiogenicCarbonKg { get; }

    /// <summary>The data quality tier of this factor.</summary>
    public DataQuality DataQuality { get; }

    /// <summary>Relative standard uncertainty as published, in percent, or <see langword="null"/> if not stated.</summary>
    public double? UncertaintyPercent { get; }

    /// <summary>Free-text caveat from the publisher, if any.</summary>
    public string? Note { get; }

    /// <summary>The publisher's own identifier for this row, so it can be found in the original file.</summary>
    public string? SourceReference { get; }

    /// <summary>
    /// Where this factor is valid, falling back to the set's region when the factor
    /// names none.
    /// </summary>
    /// <remarks>
    /// Electricity datasets routinely cover many jurisdictions or grid areas in one
    /// publication, and applying a regional factor outside its region is the single most
    /// common inventory error. Keeping the region on the factor means the answer is
    /// always one property away, whatever shape the source came in.
    /// </remarks>
    public string? Region => _region ?? Set.Region;

    /// <summary>The published set this factor belongs to. Carries the citation and verification status.</summary>
    public FactorSet Set { get; internal set; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({Scope}, per {Unit})";
}
