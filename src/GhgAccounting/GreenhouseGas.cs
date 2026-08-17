namespace GhgAccounting;

/// <summary>
/// The greenhouse gases covered by the GHG Protocol Corporate Standard and
/// ISO 14064-1, identified individually so that a caller — not the catalog —
/// chooses which <see cref="GwpSet"/> converts them to CO<sub>2</sub>e.
/// </summary>
/// <remarks>
/// Values are contiguous from zero so that lookup tables can be flat arrays.
/// Members may be appended but never renumbered: the numeric values appear in
/// generated code and in callers' persisted data.
/// </remarks>
public enum GreenhouseGas
{
    /// <summary>Carbon dioxide (CO<sub>2</sub>). The reference gas, GWP 1 by definition.</summary>
    CarbonDioxide = 0,

    /// <summary>
    /// Methane (CH<sub>4</sub>) of fossil origin. Carries a higher GWP than biogenic
    /// methane because its atmospheric oxidation adds fossil CO<sub>2</sub>.
    /// </summary>
    MethaneFossil = 1,

    /// <summary>
    /// Methane (CH<sub>4</sub>) of biogenic origin — landfill, wastewater, enteric
    /// fermentation, anaerobic digestion.
    /// </summary>
    MethaneBiogenic = 2,

    /// <summary>Nitrous oxide (N<sub>2</sub>O).</summary>
    NitrousOxide = 3,

    /// <summary>Sulfur hexafluoride (SF<sub>6</sub>). Dominant in electrical switchgear inventories.</summary>
    SulfurHexafluoride = 4,

    /// <summary>Nitrogen trifluoride (NF<sub>3</sub>). Added to the Kyoto basket by the Doha Amendment.</summary>
    NitrogenTrifluoride = 5,

    /// <summary>HFC-23 (trifluoromethane, CHF<sub>3</sub>).</summary>
    Hfc23 = 6,

    /// <summary>HFC-32 (difluoromethane, CH<sub>2</sub>F<sub>2</sub>).</summary>
    Hfc32 = 7,

    /// <summary>HFC-125 (pentafluoroethane).</summary>
    Hfc125 = 8,

    /// <summary>HFC-134a (1,1,1,2-tetrafluoroethane). The most common mobile air-conditioning refrigerant.</summary>
    Hfc134a = 9,

    /// <summary>HFC-143a (1,1,1-trifluoroethane).</summary>
    Hfc143a = 10,

    /// <summary>HFC-152a (1,1-difluoroethane).</summary>
    Hfc152a = 11,

    /// <summary>PFC-14 (tetrafluoromethane, CF<sub>4</sub>). Aluminium smelting and semiconductor etch.</summary>
    Pfc14 = 12,

    /// <summary>PFC-116 (hexafluoroethane, C<sub>2</sub>F<sub>6</sub>).</summary>
    Pfc116 = 13,
}
