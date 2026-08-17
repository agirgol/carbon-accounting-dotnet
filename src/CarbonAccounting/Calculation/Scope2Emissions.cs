using CarbonAccounting.Units;

namespace CarbonAccounting.Calculation;

/// <summary>
/// Scope 2 emissions under both accounting methods, kept side by side.
/// </summary>
/// <remarks>
/// The GHG Protocol Scope 2 Guidance requires dual reporting: a company with any
/// contractual electricity instruments publishes both figures. They are not alternatives
/// and they must never be added together, so there is deliberately no combined total on
/// this type.
/// </remarks>
public readonly struct Scope2Emissions
{
    internal Scope2Emissions(Quantity locationBased, Quantity marketBased, bool hasLocationBased, bool hasMarketBased)
    {
        LocationBased = locationBased;
        MarketBased = marketBased;
        HasLocationBased = hasLocationBased;
        HasMarketBased = hasMarketBased;
    }

    /// <summary>Emissions from the average intensity of the grid the consumption physically occurred on.</summary>
    public Quantity LocationBased { get; }

    /// <summary>Emissions reflecting contractual instruments, with a residual mix for unclaimed consumption.</summary>
    public Quantity MarketBased { get; }

    /// <summary>Whether any location-based figure was actually recorded, as opposed to being zero.</summary>
    public bool HasLocationBased { get; }

    /// <summary>Whether any market-based figure was actually recorded, as opposed to being zero.</summary>
    public bool HasMarketBased { get; }

    /// <summary>Whether the inventory contains no Scope 2 data at all under either method.</summary>
    public bool IsEmpty => !HasLocationBased && !HasMarketBased;

    /// <inheritdoc />
    public override string ToString() =>
        $"location-based {LocationBased}, market-based {MarketBased}";
}
