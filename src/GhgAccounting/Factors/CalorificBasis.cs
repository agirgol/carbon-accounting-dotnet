namespace GhgAccounting.Factors;

/// <summary>
/// Which calorific value a fuel factor is expressed on.
/// </summary>
/// <remarks>
/// Gross (higher heating value) counts the latent heat of the water vapour in the
/// combustion products; net (lower heating value) does not. The two differ by roughly
/// 5% for natural gas and up to 10% for wet biomass, so pairing an activity figure
/// with a factor on the other basis misstates the result by that much — invisibly.
/// UK invoices are typically gross; IPCC and most continental European datasets are net.
/// </remarks>
public enum CalorificBasis
{
    /// <summary>The factor is not fuel-energy based, so no calorific basis applies.</summary>
    NotApplicable = 0,

    /// <summary>Gross calorific value, also called higher heating value (HHV).</summary>
    GrossCalorificValue = 1,

    /// <summary>Net calorific value, also called lower heating value (LHV).</summary>
    NetCalorificValue = 2,
}
