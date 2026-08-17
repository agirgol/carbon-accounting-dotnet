using System.Diagnostics.CodeAnalysis;

namespace GhgAccounting;

/// <summary>
/// The GHG Protocol Corporate Standard emission scopes.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1712:Do not prefix enum values with type name",
    Justification = "\"Scope 1\", \"Scope 2\" and \"Scope 3\" are the GHG Protocol's own terms and appear verbatim in every report, standard and audit that consumes this library. Renaming them to One/Two/Three to satisfy a general naming rule would make the API harder to map onto the standard it implements.")]
public enum Scope
{
    /// <summary>Direct emissions from sources owned or controlled by the reporting company.</summary>
    Scope1 = 1,

    /// <summary>Indirect emissions from purchased electricity, steam, heating and cooling.</summary>
    Scope2 = 2,

    /// <summary>All other indirect emissions in the value chain, across 15 categories.</summary>
    Scope3 = 3,
}

/// <summary>
/// The two Scope 2 accounting methods defined by the GHG Protocol Scope 2 Guidance (2015).
/// </summary>
/// <remarks>
/// The guidance mandates <em>dual reporting</em>: a company with any contractual
/// electricity instruments must publish both figures. They are not alternatives,
/// and a single factor is only ever valid for one of them.
/// </remarks>
public enum Scope2Method
{
    /// <summary>
    /// Average emission intensity of the grid the consumption physically occurs on.
    /// Ignores contractual instruments entirely.
    /// </summary>
    LocationBased = 0,

    /// <summary>
    /// Reflects contractual instruments — power purchase agreements, guarantees of
    /// origin, renewable energy certificates — falling back to a residual mix factor
    /// for unclaimed consumption.
    /// </summary>
    MarketBased = 1,
}

/// <summary>
/// Data quality tier for an activity value or emission factor, used for the
/// uncertainty assessment ISO 14064-1 requires.
/// </summary>
public enum DataQuality
{
    /// <summary>Metered or invoiced at the reporting entity. Lowest uncertainty.</summary>
    Primary = 0,

    /// <summary>Published average from a recognised dataset for the relevant activity and region.</summary>
    Secondary = 1,

    /// <summary>A published value for a different but related activity, region or year.</summary>
    Proxy = 2,

    /// <summary>Derived by extrapolation, spend-based modelling or engineering judgement. Highest uncertainty.</summary>
    Estimated = 3,
}

/// <summary>
/// Whether a shipped catalog set has been checked against its primary source.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Invented or illustrative values. Never valid for reporting.</summary>
    Placeholder = 0,

    /// <summary>Transcribed from a real source but not yet checked cell-by-cell.</summary>
    NeedsReview = 1,

    /// <summary>Checked against the cited primary source by a named reviewer on a recorded date.</summary>
    Verified = 2,
}
