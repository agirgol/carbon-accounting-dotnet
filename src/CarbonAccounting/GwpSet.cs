namespace CarbonAccounting;

/// <summary>
/// The IPCC assessment report whose 100-year global warming potentials are used
/// to aggregate individual gases into CO<sub>2</sub>e.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberate, explicit choice rather than a library default. The same
/// activity data produces materially different totals under different sets — a
/// methane-heavy inventory moves by roughly 6% between
/// <see cref="Ar5"/> and <see cref="Ar6"/> — and the GHG Protocol requires the
/// chosen set to be disclosed alongside the result.
/// </para>
/// <para>
/// Which set applies is a reporting-regime question, not a "newest is best"
/// question: several national inventory regimes still mandate <see cref="Ar5"/>.
/// </para>
/// </remarks>
public enum GwpSet
{
    /// <summary>
    /// IPCC Fourth Assessment Report (2007), GWP-100.
    /// </summary>
    /// <remarks>
    /// Present so that a factor published on an AR4 basis can say so — several
    /// categories of every major dataset are still aggregated this way. No AR4 table
    /// ships, so <see cref="GwpTable.For(GwpSet)"/> rejects it: this member labels
    /// legacy data, it does not enable calculation with it.
    /// </remarks>
    Ar4 = 4,

    /// <summary>IPCC Fifth Assessment Report (2013), GWP-100, without climate-carbon feedback.</summary>
    Ar5 = 5,

    /// <summary>IPCC Sixth Assessment Report (2021), GWP-100, without climate-carbon feedback.</summary>
    Ar6 = 6,
}
