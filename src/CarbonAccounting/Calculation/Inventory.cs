using System;
using System.Collections.Generic;
using CarbonAccounting.Units;

namespace CarbonAccounting.Calculation;

/// <summary>
/// A completed greenhouse gas inventory: emissions grouped by scope, with Scope 2
/// reported under both methods and biogenic carbon kept outside the totals.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no single <c>Total</c> property. A corporate inventory does not
/// have one — it has a location-based total and a market-based total, and which one a
/// company leads with is a disclosure decision. <see cref="TotalWith(Scope2Method)"/>
/// forces that choice to be made explicitly at the call site.
/// </para>
/// <para>
/// All emission figures are CO<sub>2</sub>e masses in kilograms; use
/// <see cref="Quantity.ConvertTo(Unit)"/> to report in tonnes.
/// </para>
/// </remarks>
public sealed class Inventory
{
    private readonly EmissionResult[] _entries;

    internal Inventory(GwpSet gwpSet, EmissionResult[] entries)
    {
        GwpSet = gwpSet;
        _entries = entries;

        double scope1 = 0.0;
        double scope2Location = 0.0;
        double scope2Market = 0.0;
        double scope3 = 0.0;
        double scope3Uncategorised = 0.0;
        double biogenic = 0.0;
        bool hasLocation = false;
        bool hasMarket = false;

        var byCategory = new SortedDictionary<int, double>();

        foreach (EmissionResult entry in entries)
        {
            double co2e = entry.Co2e.Value;
            biogenic += entry.BiogenicCarbon.Value;

            switch (entry.Scope)
            {
                case Scope.Scope1:
                    scope1 += co2e;
                    break;

                case Scope.Scope2 when entry.Scope2Method == Scope2Method.LocationBased:
                    scope2Location += co2e;
                    hasLocation = true;
                    break;

                case Scope.Scope2:
                    scope2Market += co2e;
                    hasMarket = true;
                    break;

                default:
                    scope3 += co2e;
                    if (entry.Scope3Category is int category)
                    {
                        byCategory.TryGetValue(category, out double running);
                        byCategory[category] = running + co2e;
                    }
                    else
                    {
                        scope3Uncategorised += co2e;
                    }

                    break;
            }
        }

        Scope1 = Kilograms(scope1);
        Scope3 = Kilograms(scope3);
        Scope3Uncategorised = Kilograms(scope3Uncategorised);
        BiogenicCarbon = Kilograms(biogenic);

        Scope2 = new Scope2Emissions(
            Kilograms(scope2Location),
            Kilograms(scope2Market),
            hasLocation,
            hasMarket);

        var categories = new Scope3CategoryTotal[byCategory.Count];
        int next = 0;
        foreach (KeyValuePair<int, double> pair in byCategory)
        {
            categories[next++] = new Scope3CategoryTotal(pair.Key, Kilograms(pair.Value));
        }

        Scope3ByCategory = categories;
    }

    /// <summary>The assessment report every figure in this inventory was aggregated with.</summary>
    public GwpSet GwpSet { get; }

    /// <summary>Every calculated line in the inventory, in the order it was added.</summary>
    public IReadOnlyList<EmissionResult> Entries => _entries;

    /// <summary>Direct emissions from owned or controlled sources.</summary>
    public Quantity Scope1 { get; }

    /// <summary>Purchased energy emissions under both accounting methods.</summary>
    public Scope2Emissions Scope2 { get; }

    /// <summary>All value chain emissions, categorised or not.</summary>
    public Quantity Scope3 { get; }

    /// <summary>
    /// The part of <see cref="Scope3"/> whose factors name no category. Non-zero here
    /// means the inventory cannot be laid out against the standard's fifteen-category
    /// reporting table without further classification.
    /// </summary>
    public Quantity Scope3Uncategorised { get; }

    /// <summary>Scope 3 broken down by category, ascending, omitting categories with no data.</summary>
    public IReadOnlyList<Scope3CategoryTotal> Scope3ByCategory { get; }

    /// <summary>
    /// Biogenic CO<sub>2</sub> across the inventory. Disclosed alongside the scope
    /// totals and never inside them, so this is not a component of
    /// <see cref="TotalWith(Scope2Method)"/>.
    /// </summary>
    public Quantity BiogenicCarbon { get; }

    /// <summary>
    /// Total emissions taking Scope 2 from the requested method.
    /// </summary>
    /// <param name="method">Which Scope 2 figure to include.</param>
    /// <returns>Scope 1 + Scope 2 under <paramref name="method"/> + Scope 3, as CO<sub>2</sub>e.</returns>
    /// <exception cref="Scope2MethodNotReportedException">
    /// The inventory holds Scope 2 data, but none under <paramref name="method"/>.
    /// </exception>
    public Quantity TotalWith(Scope2Method method)
    {
        GuardScope2(method);

        double scope2 = method == Scope2Method.LocationBased
            ? Scope2.LocationBased.Value
            : Scope2.MarketBased.Value;

        return Kilograms(Scope1.Value + scope2 + Scope3.Value);
    }

    /// <summary>
    /// Combined relative standard uncertainty of <see cref="TotalWith(Scope2Method)"/>,
    /// as a percentage.
    /// </summary>
    /// <param name="method">Which Scope 2 figure to include.</param>
    /// <returns>
    /// The combined uncertainty, or <see langword="null"/> when any contributing factor
    /// publishes no uncertainty, or when the total is zero.
    /// </returns>
    /// <remarks>
    /// Uses the IPCC error propagation approach for sums: uncertainties are combined in
    /// quadrature, weighted by each line's contribution. Returning
    /// <see langword="null"/> rather than a partial figure is deliberate — an
    /// uncertainty computed from only the lines that happened to declare one understates
    /// the real spread, and nothing in the output would reveal that.
    /// </remarks>
    /// <exception cref="Scope2MethodNotReportedException">
    /// The inventory holds Scope 2 data, but none under <paramref name="method"/>.
    /// </exception>
    public double? UncertaintyPercentFor(Scope2Method method)
    {
        GuardScope2(method);

        double sumOfSquares = 0.0;
        double total = 0.0;

        foreach (EmissionResult entry in _entries)
        {
            if (!Contributes(entry, method))
            {
                continue;
            }

            if (entry.UncertaintyPercent is not double uncertainty)
            {
                return null;
            }

            double contribution = entry.Co2e.Value;
            double absolute = contribution * (uncertainty / 100.0);

            sumOfSquares += absolute * absolute;
            total += contribution;
        }

        if (total == 0.0)
        {
            return null;
        }

        return Math.Sqrt(sumOfSquares) / Math.Abs(total) * 100.0;
    }

    /// <summary>
    /// How the total under <paramref name="method"/> splits across data quality tiers.
    /// </summary>
    /// <param name="method">Which Scope 2 figure to include.</param>
    /// <returns>One entry per tier present, ordered best quality first.</returns>
    /// <exception cref="Scope2MethodNotReportedException">
    /// The inventory holds Scope 2 data, but none under <paramref name="method"/>.
    /// </exception>
    public IReadOnlyList<DataQualityShare> DataQualityBreakdownFor(Scope2Method method)
    {
        GuardScope2(method);

        var byTier = new SortedDictionary<DataQuality, double>();
        double total = 0.0;

        foreach (EmissionResult entry in _entries)
        {
            if (!Contributes(entry, method))
            {
                continue;
            }

            byTier.TryGetValue(entry.DataQuality, out double running);
            byTier[entry.DataQuality] = running + entry.Co2e.Value;
            total += entry.Co2e.Value;
        }

        var shares = new DataQualityShare[byTier.Count];
        int next = 0;
        foreach (KeyValuePair<DataQuality, double> pair in byTier)
        {
            double share = total == 0.0 ? 0.0 : pair.Value / total;
            shares[next++] = new DataQualityShare(pair.Key, Kilograms(pair.Value), share);
        }

        return shares;
    }

    private void GuardScope2(Scope2Method method)
    {
        if (Scope2.IsEmpty)
        {
            return;
        }

        bool reported = method == Scope2Method.LocationBased
            ? Scope2.HasLocationBased
            : Scope2.HasMarketBased;

        if (!reported)
        {
            throw new Scope2MethodNotReportedException(method);
        }
    }

    private static bool Contributes(EmissionResult entry, Scope2Method method) =>
        entry.Scope != Scope.Scope2 || entry.Scope2Method == method;

    private static Quantity Kilograms(double value) => new Quantity(value, Unit.Kilogram);

    /// <inheritdoc />
    public override string ToString()
    {
        // TotalWith throws when the requested method was never reported, and ToString
        // must not. Lead with location-based, falling back to whichever method exists.
        Scope2Method method = Scope2.IsEmpty || Scope2.HasLocationBased
            ? Scope2Method.LocationBased
            : Scope2Method.MarketBased;

        string label = method == Scope2Method.LocationBased ? "location-based" : "market-based";

        return $"{_entries.Length} entries, {GwpSet}, {label} total {TotalWith(method)} CO2e";
    }
}
