using CarbonAccounting.Units;

namespace CarbonAccounting.Calculation;

/// <summary>
/// Scope 3 emissions for one of the fifteen categories defined by the GHG Protocol
/// Scope 3 Standard.
/// </summary>
public readonly struct Scope3CategoryTotal
{
    internal Scope3CategoryTotal(int category, Quantity co2e)
    {
        Category = category;
        Co2e = co2e;
    }

    /// <summary>The category number, 1 to 15.</summary>
    public int Category { get; }

    /// <summary>Total carbon dioxide equivalent in this category.</summary>
    public Quantity Co2e { get; }

    /// <inheritdoc />
    public override string ToString() => $"Category {Category}: {Co2e} CO2e";
}

/// <summary>
/// The share of an inventory attributable to one data quality tier.
/// </summary>
/// <remarks>
/// ISO 14064-1 expects an inventory to be able to say how much of its total rests on
/// metered primary data and how much on published averages or estimates. Reporting a
/// single quality label for a whole inventory would hide exactly that.
/// </remarks>
public readonly struct DataQualityShare
{
    internal DataQualityShare(DataQuality quality, Quantity co2e, double share)
    {
        Quality = quality;
        Co2e = co2e;
        Share = share;
    }

    /// <summary>The data quality tier.</summary>
    public DataQuality Quality { get; }

    /// <summary>Carbon dioxide equivalent attributable to this tier.</summary>
    public Quantity Co2e { get; }

    /// <summary>This tier's fraction of the total, between 0 and 1.</summary>
    public double Share { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Quality}: {Share:P1} ({Co2e} CO2e)";
}
