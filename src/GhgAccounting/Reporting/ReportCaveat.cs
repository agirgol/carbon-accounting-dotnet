namespace GhgAccounting.Reporting;

/// <summary>
/// The kinds of thing a reader has to know before deciding how much weight a reported
/// total can carry.
/// </summary>
public enum ReportCaveatKind
{
    /// <summary>A contributing catalog set has not been checked against its cited source.</summary>
    UnverifiedData,

    /// <summary>A factor's per-gas split was reconstructed rather than published as gas masses.</summary>
    DerivedFactorComponents,

    /// <summary>A contributing factor is a proxy or an estimate rather than measured or published for the activity.</summary>
    WeakDataQuality,

    /// <summary>The inventory holds no Scope 2 data under the other method, so dual reporting is incomplete.</summary>
    Scope2NotDualReported,

    /// <summary>No combined uncertainty could be computed because not every contributing factor declares one.</summary>
    UncertaintyUnavailable,

    /// <summary>Scope 3 emissions exist that no category could be assigned to.</summary>
    UncategorisedScope3,

    /// <summary>A factor was applied outside the region its set declares it valid for.</summary>
    RegionMismatch,
}

/// <summary>
/// One thing about this inventory that a reader is entitled to know.
/// </summary>
/// <remarks>
/// Caveats are computed from the inventory rather than written by the reporter, so an
/// inconvenient one cannot be left out by forgetting to mention it.
/// </remarks>
public sealed class ReportCaveat
{
    internal ReportCaveat(ReportCaveatKind kind, string detail)
    {
        Kind = kind;
        Detail = detail;
    }

    /// <summary>What kind of caveat this is.</summary>
    public ReportCaveatKind Kind { get; }

    /// <summary>What specifically triggered it, naming the sets or factors involved.</summary>
    public string Detail { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Detail}";
}
