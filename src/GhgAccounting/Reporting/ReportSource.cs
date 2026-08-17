using GhgAccounting.Factors;
using GhgAccounting.Units;

namespace GhgAccounting.Reporting;

/// <summary>
/// One published dataset that fed the inventory, with how much of the total came from
/// it.
/// </summary>
/// <remarks>
/// The GHG Protocol expects a disclosure to say where its factors came from. Listing the
/// sets with their contribution answers the follow-up question too: a reader can tell at
/// a glance whether an unverified or proxy-heavy set is carrying the number or barely
/// touching it.
/// </remarks>
public sealed class ReportSource
{
    internal ReportSource(FactorSet set, int factorCount, Quantity co2e, double share)
    {
        Set = set;
        FactorCount = factorCount;
        Co2e = co2e;
        Share = share;
    }

    /// <summary>The set, carrying its citation and verification status.</summary>
    public FactorSet Set { get; }

    /// <summary>How many distinct factors from this set were applied.</summary>
    public int FactorCount { get; }

    /// <summary>Carbon dioxide equivalent attributable to this set.</summary>
    public Quantity Co2e { get; }

    /// <summary>This set's fraction of the reported total, between 0 and 1.</summary>
    public double Share { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Set.Id}: {Share:P1} of the total, {FactorCount} factors, {Set.Verification}";
}
