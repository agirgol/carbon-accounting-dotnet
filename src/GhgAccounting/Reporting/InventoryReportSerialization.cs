using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GhgAccounting.Calculation;
using GhgAccounting.Units;

namespace GhgAccounting.Reporting;

public sealed partial class InventoryReport
{
    private const string TonneUnitLabel = "tCO2e";

    /// <summary>
    /// Renders the disclosure as JSON, in tonnes of CO<sub>2</sub>e.
    /// </summary>
    /// <returns>A single JSON object.</returns>
    /// <remarks>
    /// Written by hand rather than with a serializer, for the same reason the catalog is
    /// compiled rather than parsed: a reporting library should not oblige its consumers
    /// to take a dependency. The output is small and its shape is fixed, so the cost of
    /// writing it out is a few dozen lines.
    /// </remarks>
    public string ToJson()
    {
        var json = new StringBuilder();
        json.Append('{');

        json.Append("\"declarations\":{");
        Member(json, "gwpSet", GwpSet.ToString(), first: true);
        Member(json, "gwpTimeHorizonYears", 100);
        Member(json, "scope2Method", Scope2Method.ToString());
        Member(json, "unit", TonneUnitLabel);
        json.Append("},");

        json.Append("\"totals\":{");
        Member(json, "scope1", Tonnes(Scope1), first: true);
        Member(json, "scope2", Tonnes(Scope2));
        Member(json, "scope3", Tonnes(Scope3));
        Member(json, "total", Tonnes(Total));
        Member(json, "biogenicCarbonOutsideScopes", Tonnes(BiogenicCarbon));
        json.Append("},");

        json.Append("\"scope2\":{");
        Member(json, "locationBased", Tonnes(Inventory.Scope2.LocationBased), first: true);
        Member(json, "marketBased", Tonnes(Inventory.Scope2.MarketBased));
        Member(json, "hasLocationBased", Inventory.Scope2.HasLocationBased);
        Member(json, "hasMarketBased", Inventory.Scope2.HasMarketBased);
        json.Append("},");

        json.Append("\"scope3ByCategory\":[");
        bool firstCategory = true;
        foreach (Scope3CategoryTotal category in Scope3ByCategory)
        {
            if (!firstCategory)
            {
                json.Append(',');
            }

            firstCategory = false;
            json.Append('{');
            Member(json, "category", category.Category, first: true);
            Member(json, "co2e", Tonnes(category.Co2e));
            json.Append('}');
        }

        json.Append("],");

        Member(json, "scope3Uncategorised", Tonnes(Inventory.Scope3Uncategorised), first: true, leading: false);
        json.Append(',');

        json.Append("\"dataQuality\":[");
        bool firstTier = true;
        foreach (DataQualityShare share in DataQuality)
        {
            if (!firstTier)
            {
                json.Append(',');
            }

            firstTier = false;
            json.Append('{');
            Member(json, "tier", share.Quality.ToString(), first: true);
            Member(json, "share", share.Share);
            Member(json, "co2e", Tonnes(share.Co2e));
            json.Append('}');
        }

        json.Append("],");

        json.Append("\"uncertaintyPercent\":");
        json.Append(UncertaintyPercent is double spread ? Number(spread) : "null");
        json.Append(',');

        json.Append("\"sources\":[");
        bool firstSource = true;
        foreach (ReportSource source in Sources)
        {
            if (!firstSource)
            {
                json.Append(',');
            }

            firstSource = false;
            json.Append('{');
            Member(json, "id", source.Set.Id, first: true);
            Member(json, "share", source.Share);
            Member(json, "co2e", Tonnes(source.Co2e));
            Member(json, "factorCount", source.FactorCount);
            Member(json, "verification", source.Set.Verification.ToString());
            Member(json, "publisher", source.Set.Source.Publisher);
            Member(json, "publicationYear", source.Set.Source.PublicationYear);
            Member(json, "license", source.Set.Source.License);
            json.Append('}');
        }

        json.Append("],");

        json.Append("\"caveats\":[");
        bool firstCaveat = true;
        foreach (ReportCaveat caveat in Caveats)
        {
            if (!firstCaveat)
            {
                json.Append(',');
            }

            firstCaveat = false;
            json.Append('{');
            Member(json, "kind", caveat.Kind.ToString(), first: true);
            Member(json, "detail", caveat.Detail);
            json.Append('}');
        }

        json.Append(']');
        json.Append('}');
        return json.ToString();
    }

    /// <summary>
    /// Renders every line of the inventory as CSV, one row per calculated entry.
    /// </summary>
    /// <returns>A CSV document with a header row, CRLF line endings and UTF-8 text.</returns>
    /// <remarks>
    /// This is the audit trail rather than the summary. Each row carries the activity
    /// figure, the factor applied, the set it came from, its region and data quality,
    /// whether its gas split was derived, and what the publisher's own figure would have
    /// been. An assurer checking a total works down this, not down the disclosure.
    /// </remarks>
    public string ToCsv()
    {
        var csv = new StringBuilder();
        csv.Append("scope,scope3Category,scope2Method,activityValue,activityUnit,factorId,")
           .Append("factorSet,region,dataQuality,componentsAreDerived,co2eTonnes,")
           .Append("publishedCo2eTonnes,biogenicCarbonTonnes,uncertaintyPercent,sourceReference")
           .Append("\r\n");

        foreach (EmissionResult entry in Inventory.Entries)
        {
            var cells = new List<string>
            {
                entry.Scope.ToString(),
                entry.Scope3Category?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                entry.Scope2Method?.ToString() ?? string.Empty,
                Number(entry.Activity.Value),
                entry.Activity.Unit.ToString(),
                entry.Factor.Id,
                entry.Factor.Set.Id,
                entry.Factor.Region ?? string.Empty,
                entry.DataQuality.ToString(),
                entry.Factor.ComponentsAreDerived ? "true" : "false",
                Number(entry.Co2e.ConvertTo(Unit.Tonne).Value),
                entry.PublishedCo2e is Quantity published
                    ? Number(published.ConvertTo(Unit.Tonne).Value)
                    : string.Empty,
                Number(entry.BiogenicCarbon.ConvertTo(Unit.Tonne).Value),
                entry.UncertaintyPercent is double uncertainty
                    ? Number(uncertainty)
                    : string.Empty,
                entry.Factor.SourceReference ?? string.Empty,
            };

            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    csv.Append(',');
                }

                csv.Append(Escape(cells[i]));
            }

            csv.Append("\r\n");
        }

        return csv.ToString();
    }

    private static double Tonnes(Quantity value) => value.ConvertTo(Unit.Tonne).Value;

    // Round-trippable and culture-independent. A report that renders 1.234 on one machine
    // and 1,234 on another is a reconciliation problem the reader cannot detect.
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static void Member(StringBuilder json, string name, string? value, bool first = false, bool leading = true)
    {
        Separator(json, first, leading);
        json.Append('"').Append(name).Append("\":");
        AppendString(json, value);
    }

    private static void Member(StringBuilder json, string name, double value, bool first = false, bool leading = true)
    {
        Separator(json, first, leading);
        json.Append('"').Append(name).Append("\":").Append(Number(value));
    }

    private static void Member(StringBuilder json, string name, int value, bool first = false, bool leading = true)
    {
        Separator(json, first, leading);
        json.Append('"').Append(name).Append("\":")
            .Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Member(StringBuilder json, string name, bool value, bool first = false, bool leading = true)
    {
        Separator(json, first, leading);
        json.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
    }

    private static void Separator(StringBuilder json, bool first, bool leading)
    {
        if (!first && leading)
        {
            json.Append(',');
        }
    }

    private static void AppendString(StringBuilder json, string? value)
    {
        if (value is null)
        {
            json.Append("null");
            return;
        }

        json.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        json.Append(character);
                    }

                    break;
            }
        }

        json.Append('"');
    }

    private static string Escape(string cell)
    {
        // Factor notes carry commas and occasionally quotes, and a set's citation can run
        // to a sentence. Unescaped, either shifts every following column by one.
        if (cell.IndexOf(',') < 0 && cell.IndexOf('"') < 0
            && cell.IndexOf('\n') < 0 && cell.IndexOf('\r') < 0)
        {
            return cell;
        }

        return "\"" + cell.Replace("\"", "\"\"") + "\"";
    }
}
