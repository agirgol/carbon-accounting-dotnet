using System;
using System.Linq;
using System.Text.Json;
using GhgAccounting.Calculation;
using GhgAccounting.Reporting;
using GhgAccounting.Units;
using Xunit;

namespace GhgAccounting.Tests;

public class ReportSerializationTests
{
    private const string NaturalGas = "defra-2026/fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string UkGrid = "defra-2026/uk-electricity/electricity-generated/electricity-uk/kwh/kwh";
    private const string UkWtt = "defra-2026/wtt-fuels/gaseous-fuels/natural-gas/kwh-gross-cv";
    private const string TurkishGrid = "eurostat-grid-2023/tr/public-grid/mwh";
    private const string BiofuelDiesel = "defra-2026/fuels/liquid-fuels/diesel-average-biofuel-blend/litres";

    private static InventoryReport Report() =>
        InventoryReport.For(
            new EmissionCalculator(GwpSet.Ar5).CreateInventory()
                .Add(new Quantity(250_000.0, Unit.KilowattHour), NaturalGas)
                .Add(new Quantity(400_000.0, Unit.KilowattHour), UkGrid)
                .Add(new Quantity(250_000.0, Unit.KilowattHour), UkWtt)
                .Add(new Quantity(12_000.0, Unit.Litre), BiofuelDiesel)
                .Add(new Quantity(500.0, Unit.MegawattHour), TurkishGrid)
                .Build(),
            Scope2Method.LocationBased);

    [Fact]
    public void Json_Parses()
    {
        // The writer is hand-rolled so the library keeps no dependency. The test project
        // has no such constraint, so it parses the output properly rather than matching
        // substrings and hoping.
        using JsonDocument document = JsonDocument.Parse(Report().ToJson());

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [Fact]
    public void Json_CarriesTheDeclarations()
    {
        using JsonDocument document = JsonDocument.Parse(Report().ToJson());
        JsonElement declarations = document.RootElement.GetProperty("declarations");

        Assert.Equal("Ar5", declarations.GetProperty("gwpSet").GetString());
        Assert.Equal(100, declarations.GetProperty("gwpTimeHorizonYears").GetInt32());
        Assert.Equal("LocationBased", declarations.GetProperty("scope2Method").GetString());
        Assert.Equal("tCO2e", declarations.GetProperty("unit").GetString());
    }

    [Fact]
    public void Json_TotalsAgreeWithTheReport()
    {
        InventoryReport report = Report();
        using JsonDocument document = JsonDocument.Parse(report.ToJson());
        JsonElement totals = document.RootElement.GetProperty("totals");

        Assert.Equal(
            report.Total.ConvertTo(Unit.Tonne).Value,
            totals.GetProperty("total").GetDouble(),
            precision: 9);

        // Biogenic carbon appears under a name that says where it sits.
        Assert.True(totals.GetProperty("biogenicCarbonOutsideScopes").GetDouble() > 0);
    }

    [Fact]
    public void Json_ListsSourcesAndCaveats()
    {
        using JsonDocument document = JsonDocument.Parse(Report().ToJson());

        JsonElement sources = document.RootElement.GetProperty("sources");
        Assert.Equal(2, sources.GetArrayLength());
        Assert.All(
            sources.EnumerateArray(),
            s => Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("publisher").GetString())));

        JsonElement caveats = document.RootElement.GetProperty("caveats");
        Assert.True(caveats.GetArrayLength() > 0);
        Assert.All(
            caveats.EnumerateArray(),
            c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("detail").GetString())));
    }

    [Fact]
    public void Json_EscapesTextThatWouldOtherwiseBreakIt()
    {
        // Set citations run to whole sentences with quotes and punctuation in them, and a
        // caveat lists set ids separated by commas.
        string json = Report().ToJson();

        using JsonDocument document = JsonDocument.Parse(json);
        string license = document.RootElement.GetProperty("sources")[0]
            .GetProperty("license").GetString()!;

        Assert.Contains(" ", license);
    }

    [Fact]
    public void Json_UsesInvariantNumbersWhateverTheThreadCulture()
    {
        InventoryReport report = Report();
        string underInvariant = report.ToJson();

        var swapped = (System.Globalization.CultureInfo)
            System.Globalization.CultureInfo.InvariantCulture.Clone();
        swapped.NumberFormat.NumberDecimalSeparator = ",";

        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = swapped;
            Assert.Equal(underInvariant, report.ToJson());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Csv_HasOneRowPerInventoryEntry()
    {
        InventoryReport report = Report();

        string[] lines = report.ToCsv()
            .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(report.Inventory.Entries.Count + 1, lines.Length);
        Assert.StartsWith("scope,scope3Category,scope2Method,activityValue", lines[0]);
    }

    [Fact]
    public void Csv_CarriesTheProvenanceOfEachLine()
    {
        string csv = Report().ToCsv();

        Assert.Contains("defra-2026", csv);
        Assert.Contains("eurostat-grid-2023", csv);
        Assert.Contains("DESNZ 2026 flat file", csv);
        Assert.Contains("TR", csv);
    }

    [Fact]
    public void Csv_QuotesCellsContainingSeparators()
    {
        string csv = Report().ToCsv();

        // Source references carry commas. Unescaped, every column after them shifts by
        // one and a spreadsheet silently misreads the file.
        Assert.Contains("\"Eurostat env_air_gge CRF1A1A and nrg_bal_c, TR 2023\"", csv);
    }

    [Fact]
    public void Csv_EveryRowHasTheSameColumnCount()
    {
        string csv = Report().ToCsv();
        string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        int expected = lines[0].Split(',').Length;

        foreach (string line in lines.Skip(1))
        {
            Assert.Equal(expected, CountCells(line));
        }
    }

    [Fact]
    public void Csv_LeavesPublishedFiguresBlankWhereThereIsNone()
    {
        // The Eurostat factors are derived and carry no publisher CO2e, so that column
        // has to be empty rather than zero: zero would read as "the publisher said none".
        string csv = Report().ToCsv();
        string line = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.Contains("eurostat-grid-2023/tr"));

        string[] cells = SplitCells(line).ToArray();
        Assert.Equal(string.Empty, cells[11]);
    }

    private static int CountCells(string line) => SplitCells(line).Count();

    private static System.Collections.Generic.IEnumerable<string> SplitCells(string line)
    {
        var cell = new System.Text.StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                yield return cell.ToString();
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        yield return cell.ToString();
    }
}
