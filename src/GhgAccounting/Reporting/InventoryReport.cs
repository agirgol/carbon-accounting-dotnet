using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GhgAccounting.Calculation;
using GhgAccounting.Factors;
using GhgAccounting.Units;

namespace GhgAccounting.Reporting;

/// <summary>
/// An inventory arranged as a disclosure: the figures a report has to state, the
/// declarations that make them interpretable, and the caveats that stop them looking
/// more certain than they are.
/// </summary>
/// <remarks>
/// <para>
/// The GHG Protocol Corporate Standard and ISO 14064-1 both require more than a number.
/// A disclosure has to name the GWP set, report Scope 2 under both methods, hold
/// biogenic CO<sub>2</sub> outside the totals, cite its factor sources and describe data
/// quality. Those are all present on <see cref="Inventory"/>, but scattered; this type
/// gathers them into the shape a reader needs and computes the caveats rather than
/// leaving them to be remembered.
/// </para>
/// <para>
/// It renders nothing regulatory. CBAM and CSRD formats change on their own schedule and
/// are deliberately out of scope; this is the standard's own disclosure content.
/// </para>
/// </remarks>
public sealed partial class InventoryReport
{
    private InventoryReport(
        Inventory inventory,
        Scope2Method method,
        ReportSource[] sources,
        ReportCaveat[] caveats)
    {
        Inventory = inventory;
        Scope2Method = method;
        Sources = sources;
        Caveats = caveats;
        Total = inventory.TotalWith(method);
        Scope2 = method == Scope2Method.LocationBased
            ? inventory.Scope2.LocationBased
            : inventory.Scope2.MarketBased;
        UncertaintyPercent = inventory.UncertaintyPercentFor(method);
        DataQuality = inventory.DataQualityBreakdownFor(method);
    }

    /// <summary>
    /// Arranges an inventory as a disclosure under one Scope 2 method.
    /// </summary>
    /// <param name="inventory">The completed inventory.</param>
    /// <param name="method">Which Scope 2 figure this disclosure leads with.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inventory"/> is <see langword="null"/>.</exception>
    /// <exception cref="Scope2MethodNotReportedException">
    /// The inventory holds Scope 2 data, but none under <paramref name="method"/>.
    /// </exception>
    public static InventoryReport For(Inventory inventory, Scope2Method method)
    {
        if (inventory is null)
        {
            throw new ArgumentNullException(nameof(inventory));
        }

        EmissionResult[] contributing = inventory.Entries
            .Where(e => e.Scope != Scope.Scope2 || e.Scope2Method == method)
            .ToArray();

        double total = contributing.Sum(e => e.Co2e.Value);

        var sources = contributing
            .GroupBy(e => e.Factor.Set, ReferenceEqualityComparer.Instance)
            .Select(g => new ReportSource(
                (FactorSet)g.Key,
                g.Select(e => e.Factor.Id).Distinct(StringComparer.Ordinal).Count(),
                new Quantity(g.Sum(e => e.Co2e.Value), Unit.Kilogram),
                total == 0.0 ? 0.0 : g.Sum(e => e.Co2e.Value) / total))
            .OrderByDescending(s => s.Share)
            .ToArray();

        return new InventoryReport(inventory, method, sources, Caveat(inventory, contributing, method));
    }

    /// <summary>The inventory this report describes.</summary>
    public Inventory Inventory { get; }

    /// <summary>The assessment report the figures were aggregated with. A disclosure must state this.</summary>
    public GwpSet GwpSet => Inventory.GwpSet;

    /// <summary>Which Scope 2 method this disclosure leads with.</summary>
    public Scope2Method Scope2Method { get; }

    /// <summary>Direct emissions from owned or controlled sources.</summary>
    public Quantity Scope1 => Inventory.Scope1;

    /// <summary>Purchased energy emissions under <see cref="Scope2Method"/>.</summary>
    public Quantity Scope2 { get; }

    /// <summary>Value chain emissions.</summary>
    public Quantity Scope3 => Inventory.Scope3;

    /// <summary>Scope 1 + Scope 2 under the chosen method + Scope 3.</summary>
    public Quantity Total { get; }

    /// <summary>
    /// Biogenic CO<sub>2</sub>, disclosed alongside the total and deliberately not
    /// inside it.
    /// </summary>
    public Quantity BiogenicCarbon => Inventory.BiogenicCarbon;

    /// <summary>Scope 3 broken down by the standard's fifteen categories.</summary>
    public IReadOnlyList<Scope3CategoryTotal> Scope3ByCategory => Inventory.Scope3ByCategory;

    /// <summary>How the total splits across data quality tiers.</summary>
    public IReadOnlyList<DataQualityShare> DataQuality { get; }

    /// <summary>Combined relative standard uncertainty, or <see langword="null"/> if it could not be computed.</summary>
    public double? UncertaintyPercent { get; }

    /// <summary>The catalog sets that fed this total, largest contribution first.</summary>
    public IReadOnlyList<ReportSource> Sources { get; }

    /// <summary>
    /// Everything about this inventory that qualifies the total, computed rather than
    /// remembered.
    /// </summary>
    public IReadOnlyList<ReportCaveat> Caveats { get; }

    private static ReportCaveat[] Caveat(
        Inventory inventory, EmissionResult[] contributing, Scope2Method method)
    {
        var caveats = new List<ReportCaveat>();

        string[] unverified = contributing
            .Where(e => e.Factor.Set.Verification != VerificationStatus.Verified)
            .Select(e => e.Factor.Set.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (unverified.Length > 0)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.UnverifiedData,
                "Nobody has checked these sets against their cited sources: " + string.Join(", ", unverified)));
        }

        string[] derived = contributing
            .Where(e => e.Factor.ComponentsAreDerived)
            .Select(e => e.Factor.Set.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (derived.Length > 0)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.DerivedFactorComponents,
                "Per-gas splits were reconstructed rather than published as gas masses in: "
                + string.Join(", ", derived)));
        }

        double weak = contributing
            .Where(e => e.DataQuality == GhgAccounting.DataQuality.Proxy
                     || e.DataQuality == GhgAccounting.DataQuality.Estimated)
            .Sum(e => e.Co2e.Value);

        double total = contributing.Sum(e => e.Co2e.Value);
        if (weak > 0.0 && total > 0.0)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.WeakDataQuality,
                (weak / total).ToString("P1", CultureInfo.InvariantCulture)
                + " of the total rests on proxy or estimated factors."));
        }

        if (!inventory.Scope2.IsEmpty
            && !(inventory.Scope2.HasLocationBased && inventory.Scope2.HasMarketBased))
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.Scope2NotDualReported,
                "The GHG Protocol Scope 2 Guidance expects both methods; only "
                + (inventory.Scope2.HasLocationBased ? "location-based" : "market-based")
                + " data is present."));
        }

        if (inventory.UncertaintyPercentFor(method) is null && total > 0.0)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.UncertaintyUnavailable,
                "Not every contributing factor publishes an uncertainty, so no combined figure is stated."));
        }

        if (inventory.Scope3Uncategorised.Value > 0.0)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.UncategorisedScope3,
                $"{inventory.Scope3Uncategorised} of Scope 3 could not be placed in one of the fifteen categories."));
        }

        string[] regions = contributing
            .Select(e => e.Factor.Region)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray()!;

        if (regions.Length > 1)
        {
            caveats.Add(new ReportCaveat(
                ReportCaveatKind.RegionMismatch,
                "Factors from more than one region were applied: " + string.Join(", ", regions)
                + ". Confirm each was used for activity in its own region."));
        }

        return caveats.ToArray();
    }

    /// <summary>
    /// Renders the disclosure as Markdown, in tonnes of CO<sub>2</sub>e.
    /// </summary>
    /// <returns>A self-contained report body.</returns>
    /// <remarks>
    /// Every number is formatted with the invariant culture. A report that renders
    /// "1.234" on one machine and "1,234" on another is a reconciliation problem waiting
    /// to happen, and the reader has no way to tell which they are looking at.
    /// </remarks>
    public string ToMarkdown()
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        string Tonnes(Quantity value) => value.ConvertTo(Unit.Tonne).Value.ToString("N2", culture);
        string Pct(double value) => value.ToString("P1", culture);

        text.AppendLine("# Greenhouse gas inventory");
        text.AppendLine();
        text.AppendLine("| Declaration | Value |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| Global warming potentials | {GwpSet}, 100-year |");
        text.AppendLine($"| Scope 2 method | {Scope2Method} |");
        text.AppendLine("| Reported in | tonnes CO2e |");
        text.AppendLine();

        text.AppendLine("## Emissions by scope");
        text.AppendLine();
        text.AppendLine("| Scope | tCO2e |");
        text.AppendLine("|---|---:|");
        text.AppendLine($"| Scope 1 | {Tonnes(Scope1)} |");
        text.AppendLine($"| Scope 2 ({Scope2Method}) | {Tonnes(Scope2)} |");
        text.AppendLine($"| Scope 3 | {Tonnes(Scope3)} |");
        text.AppendLine($"| **Total** | **{Tonnes(Total)}** |");
        text.AppendLine();
        text.AppendLine(
            $"Biogenic CO2 of {Tonnes(BiogenicCarbon)} tCO2e is reported separately and is "
            + "not included in any figure above.");
        text.AppendLine();

        if (Inventory.Scope2.HasLocationBased && Inventory.Scope2.HasMarketBased)
        {
            text.AppendLine("Both Scope 2 methods are reported:");
            text.AppendLine();
            text.AppendLine("| Method | tCO2e |");
            text.AppendLine("|---|---:|");
            text.AppendLine($"| Location-based | {Tonnes(Inventory.Scope2.LocationBased)} |");
            text.AppendLine($"| Market-based | {Tonnes(Inventory.Scope2.MarketBased)} |");
            text.AppendLine();
        }

        if (Scope3ByCategory.Count > 0)
        {
            text.AppendLine("## Scope 3 by category");
            text.AppendLine();
            text.AppendLine("| Category | tCO2e |");
            text.AppendLine("|---|---:|");
            foreach (Scope3CategoryTotal category in Scope3ByCategory)
            {
                text.AppendLine($"| {category.Category} | {Tonnes(category.Co2e)} |");
            }

            text.AppendLine();
        }

        text.AppendLine("## Data quality");
        text.AppendLine();
        text.AppendLine("| Tier | Share | tCO2e |");
        text.AppendLine("|---|---:|---:|");
        foreach (DataQualityShare share in DataQuality)
        {
            text.AppendLine($"| {share.Quality} | {Pct(share.Share)} | {Tonnes(share.Co2e)} |");
        }

        text.AppendLine();
        text.AppendLine(UncertaintyPercent is double spread
            ? $"Combined relative standard uncertainty: {spread.ToString("N1", culture)}%."
            : "No combined uncertainty is stated, because not every contributing factor publishes one.");
        text.AppendLine();

        text.AppendLine("## Factor sources");
        text.AppendLine();
        text.AppendLine("| Set | Share | Factors | Verification | Citation |");
        text.AppendLine("|---|---:|---:|---|---|");
        foreach (ReportSource source in Sources)
        {
            text.AppendLine(
                $"| {source.Set.Id} | {Pct(source.Share)} | {source.FactorCount} | "
                + $"{source.Set.Verification} | {source.Set.Source.Publisher}, "
                + $"{source.Set.Source.PublicationYear.ToString(culture)} |");
        }

        text.AppendLine();

        text.AppendLine("## Caveats");
        text.AppendLine();
        if (Caveats.Count == 0)
        {
            text.AppendLine("None. Every contributing set is verified, every factor publishes an "
                + "uncertainty, and Scope 2 is reported under both methods.");
        }
        else
        {
            foreach (ReportCaveat caveat in Caveats)
            {
                text.AppendLine($"- **{caveat.Kind}** — {caveat.Detail}");
            }
        }

        return text.ToString();
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Total} CO2e total, {GwpSet}, {Scope2Method}, {Caveats.Count} caveats";

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
