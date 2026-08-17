using System.Collections.Generic;
using GhgAccounting.Factors;
using GhgAccounting.Units;

namespace GhgAccounting.Calculation;

/// <summary>
/// One activity figure converted into emissions, carrying everything needed to defend
/// the number: the input, the factor, the GWP set, and the resulting mass per gas.
/// </summary>
/// <remarks>
/// A result is deliberately not a bare <see cref="double"/>. Under assurance, a reported
/// figure has to be traceable to the activity data and the published factor that produced
/// it, and that trail is easiest to keep if it never gets separated from the number.
/// </remarks>
public sealed class EmissionResult
{
    internal EmissionResult(
        Quantity activity,
        EmissionFactor factor,
        GwpSet gwpSet,
        GasEmission[] gases,
        Quantity co2e,
        Quantity biogenicCarbon,
        Quantity? publishedCo2e)
    {
        Activity = activity;
        Factor = factor;
        GwpSet = gwpSet;
        Gases = gases;
        Co2e = co2e;
        BiogenicCarbon = biogenicCarbon;
        PublishedCo2e = publishedCo2e;
    }

    /// <summary>The activity data as supplied by the caller, in the caller's unit.</summary>
    public Quantity Activity { get; }

    /// <summary>The factor applied, including its source and verification status.</summary>
    public EmissionFactor Factor { get; }

    /// <summary>The assessment report whose potentials produced <see cref="Co2e"/>.</summary>
    public GwpSet GwpSet { get; }

    /// <summary>The scope these emissions belong to.</summary>
    public Scope Scope => Factor.Scope;

    /// <summary>Which Scope 2 method this result serves, or <see langword="null"/> outside Scope 2.</summary>
    public Scope2Method? Scope2Method => Factor.Scope2Method;

    /// <summary>The Scope 3 category, or <see langword="null"/> outside Scope 3.</summary>
    public int? Scope3Category => Factor.Scope3Category;

    /// <summary>Mass released per gas, with each gas's CO<sub>2</sub>e contribution.</summary>
    public IReadOnlyList<GasEmission> Gases { get; }

    /// <summary>
    /// Total carbon dioxide equivalent for this activity, aggregated under
    /// <see cref="GwpSet"/>.
    /// </summary>
    public Quantity Co2e { get; }

    /// <summary>
    /// What the factor's publisher would have reported for this activity, using their
    /// own CO<sub>2</sub>e figure, or <see langword="null"/> if they published none.
    /// </summary>
    /// <remarks>
    /// This differs from <see cref="Co2e"/> whenever the publisher aggregated on a
    /// different basis than the calculation — DESNZ, for instance, applies the non-fossil
    /// methane potential to fossil fuels. A compliance filing usually has to reproduce
    /// the publisher's number, so both are kept rather than one silently replacing the
    /// other.
    /// </remarks>
    public Quantity? PublishedCo2e { get; }

    /// <summary>
    /// Biogenic CO<sub>2</sub> released by this activity. Reported alongside the
    /// inventory rather than inside any scope total, and therefore never included in
    /// <see cref="Co2e"/>.
    /// </summary>
    public Quantity BiogenicCarbon { get; }

    /// <summary>The data quality tier of the factor applied.</summary>
    public DataQuality DataQuality => Factor.DataQuality;

    /// <summary>Relative standard uncertainty of the factor applied, or <see langword="null"/> if the publisher stated none.</summary>
    public double? UncertaintyPercent => Factor.UncertaintyPercent;

    /// <inheritdoc />
    public override string ToString() => $"{Factor.Id}: {Co2e} CO2e ({Scope}, {GwpSet})";
}
