using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using CarbonAccounting.Generators.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CarbonAccounting.Generators;

/// <summary>
/// Turns the versioned JSON catalog under <c>data/</c> into compiled C# lookup tables.
/// </summary>
/// <remarks>
/// <para>
/// The catalog is authored as JSON because that is what makes it reviewable: a factor
/// change shows up as a readable diff in a pull request, next to its citation. It is
/// compiled to C# because that is what makes it cheap: consumers get static arrays,
/// with no JSON parser, no embedded resource, no start-up cost, and no package
/// dependency added to their graph.
/// </para>
/// <para>
/// Enum-valued fields are matched to enum members <em>by name</em> and emitted as
/// direct member references. A typo in the data therefore fails the build at the
/// generated call site instead of silently producing a wrong number.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CatalogGenerator : IIncrementalGenerator
{
    private const string GwpKind = "gwp";
    private const string FactorsKind = "factors";
    private const string KindMetadataKey = "build_metadata.AdditionalFiles.CarbonCatalogKind";
    private const string StrictPropertyKey = "build_property.CarbonRequireVerifiedCatalog";

    private const string Ns = "global::CarbonAccounting";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<bool> requireVerified = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(StrictPropertyKey, out string? value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        IncrementalValuesProvider<CatalogFile> files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, token) => Classify(pair.Left, pair.Right, token))
            .Where(static file => file.Kind.Length > 0);

        context.RegisterSourceOutput(
            files.Where(static f => f.Kind == GwpKind).Collect().Combine(requireVerified),
            static (production, input) => EmitGwpTables(production, input.Left, input.Right));

        context.RegisterSourceOutput(
            files.Where(static f => f.Kind == FactorsKind).Collect().Combine(requireVerified),
            static (production, input) => EmitFactorSets(production, input.Left, input.Right));
    }

    private static CatalogFile Classify(AdditionalText file, AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        if (!options.GetOptions(file).TryGetValue(KindMetadataKey, out string? kind) || string.IsNullOrEmpty(kind))
        {
            return CatalogFile.Ignored;
        }

        SourceText? text = file.GetText(token);
        return text is null ? CatalogFile.Ignored : new CatalogFile(kind!, file.Path, text.ToString());
    }

    // ---------------------------------------------------------------- GWP sets

    private static void EmitGwpTables(SourceProductionContext production, ImmutableArray<CatalogFile> files, bool requireVerified)
    {
        var entries = new List<string>();

        foreach (CatalogFile file in Ordered(files))
        {
            JsonObject? root = ParseRoot(production, file);
            if (root is null)
            {
                continue;
            }

            string? id = RequireIdentifier(production, file, root, "id", "GwpSet");
            string? name = RequireString(production, file, root, "name");
            double? horizon = RequireNumber(production, file, root, "timeHorizonYears");
            string? source = BuildSource(production, file, root);
            string? verification = BuildVerification(production, file, root, id, requireVerified);
            JsonArray? values = root.Array("values");

            if (values is null)
            {
                Report(production, CatalogDiagnostics.MissingField, file, root, "values", file.FileName);
            }

            if (id is null || name is null || horizon is null || source is null || verification is null || values is null)
            {
                continue;
            }

            var gases = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonNode item in values.Items)
            {
                if (item is not JsonObject entry)
                {
                    Report(production, CatalogDiagnostics.MissingField, file, item, "gas", file.FileName);
                    continue;
                }

                string? gas = RequireIdentifier(production, file, entry, "gas", "GreenhouseGas");
                double? gwp = RequireNumber(production, file, entry, "gwp");

                if (gas is null || gwp is null)
                {
                    continue;
                }

                if (!seen.Add(gas))
                {
                    Report(production, CatalogDiagnostics.DuplicateId, file, entry, gas);
                    continue;
                }

                gases.Add(
                    $"new {Ns}.GwpValue({Ns}.GreenhouseGas.{gas}, {Number(gwp.Value)}, " +
                    $"{Literal(entry.String("formula"))}, {Literal(entry.String("sourceTable"))})");
            }

            bool feedback = root.Boolean("includesClimateCarbonFeedback") ?? false;

            var builder = new StringBuilder();
            builder.Append("                new ").Append(Ns).AppendLine(".GwpTable(");
            builder.Append("                    ").Append(Ns).Append(".GwpSet.").Append(id).AppendLine(",");
            builder.Append("                    ").Append(Literal(name)).AppendLine(",");
            builder.Append("                    ").Append((int)horizon.Value).AppendLine(",");
            builder.Append("                    ").Append(feedback ? "true" : "false").AppendLine(",");
            builder.Append("                    ").Append(source).AppendLine(",");
            builder.Append("                    ").Append(verification).AppendLine(",");
            builder.Append("                    new ").Append(Ns).AppendLine(".GwpValue[]");
            builder.AppendLine("                    {");
            foreach (string gas in gases)
            {
                builder.Append("                        ").Append(gas).AppendLine(",");
            }

            builder.AppendLine("                    })");

            entries.Add(builder.ToString().TrimEnd('\r', '\n'));
        }

        var output = new StringBuilder();
        AppendHeader(output);
        output.AppendLine("namespace CarbonAccounting");
        output.AppendLine("{");
        output.AppendLine("    public sealed partial class GwpTable");
        output.AppendLine("    {");
        // Held by a nested type rather than a plain static field: static initialisers of a
        // partial class run in file order, which is not something a generated file controls.
        // A nested holder is initialised on first access instead, so the hand-written half
        // can never observe this as null no matter how the compiler orders the sources.
        output.AppendLine("        internal static GwpTable[] GeneratedTables => CatalogHolder.Tables;");
        output.AppendLine();
        output.AppendLine("        private static class CatalogHolder");
        output.AppendLine("        {");
        output.AppendLine("            internal static readonly GwpTable[] Tables = CreateGeneratedTables();");
        output.AppendLine("        }");
        output.AppendLine();
        output.AppendLine("        private static GwpTable[] CreateGeneratedTables()");
        output.AppendLine("        {");
        output.Append("            return new ").Append(Ns).AppendLine(".GwpTable[]");
        output.AppendLine("            {");
        foreach (string entry in entries)
        {
            output.Append(entry).AppendLine(",");
        }

        output.AppendLine("            };");
        output.AppendLine("        }");
        output.AppendLine("    }");
        output.AppendLine("}");

        production.AddSource("GwpTable.Catalog.g.cs", SourceText.From(output.ToString(), Encoding.UTF8));
    }

    // ------------------------------------------------------------- Factor sets

    private static void EmitFactorSets(SourceProductionContext production, ImmutableArray<CatalogFile> files, bool requireVerified)
    {
        var entries = new List<string>();
        var factorIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (CatalogFile file in Ordered(files))
        {
            JsonObject? root = ParseRoot(production, file);
            if (root is null)
            {
                continue;
            }

            string? setId = RequireString(production, file, root, "id");
            string? name = RequireString(production, file, root, "name");
            string? source = BuildSource(production, file, root);
            string? verification = BuildVerification(production, file, root, setId, requireVerified);
            JsonArray? factors = root.Array("factors");

            if (factors is null)
            {
                Report(production, CatalogDiagnostics.MissingField, file, root, "factors", file.FileName);
            }

            if (setId is null || name is null || source is null || verification is null || factors is null)
            {
                continue;
            }

            var emitted = new List<string>();

            foreach (JsonNode item in factors.Items)
            {
                if (item is not JsonObject factor)
                {
                    Report(production, CatalogDiagnostics.MissingField, file, item, "id", file.FileName);
                    continue;
                }

                string? id = RequireString(production, file, factor, "id");
                string? activity = RequireString(production, file, factor, "activity");
                string? scope = RequireIdentifier(production, file, factor, "scope", "Scope");
                string? unit = RequireIdentifier(production, file, factor, "unit", "Unit");
                string? quality = RequireIdentifier(production, file, factor, "dataQuality", "DataQuality");
                // A factor may carry a gas breakdown, a published CO2e figure, or both.
                // Value-chain datasets almost always publish only the aggregate, and
                // there is no split behind it to recover.
                JsonObject? components = factor.Object("components");
                double? publishedCo2e = factor.Number("publishedCo2eKgPerUnit");
                string? publishedBasis = OptionalIdentifier(production, file, factor, "publishedGwpBasis", "GwpSet");

                if (components is null && (publishedCo2e is null || publishedBasis is null))
                {
                    Report(production, CatalogDiagnostics.FactorHasNoValue, file, factor, id ?? file.FileName);
                    continue;
                }

                if (id is null || activity is null || scope is null || unit is null || quality is null)
                {
                    continue;
                }

                if (!factorIds.Add(id))
                {
                    Report(production, CatalogDiagnostics.DuplicateId, file, factor, id);
                    continue;
                }

                var componentExpressions = new List<string>();
                IEnumerable<KeyValuePair<string, JsonNode>> members = components is null
                    ? Array.Empty<KeyValuePair<string, JsonNode>>()
                    : components.Members.OrderBy(m => m.Key, StringComparer.Ordinal);

                foreach (KeyValuePair<string, JsonNode> member in members)
                {
                    if (!IsIdentifier(member.Key))
                    {
                        Report(production, CatalogDiagnostics.InvalidEnumName, file, member.Value, member.Key, "GreenhouseGas");
                        continue;
                    }

                    if (member.Value is not JsonNumber number)
                    {
                        Report(production, CatalogDiagnostics.MissingField, file, member.Value, member.Key, file.FileName);
                        continue;
                    }

                    componentExpressions.Add(
                        $"new {Ns}.Factors.GasComponent({Ns}.GreenhouseGas.{member.Key}, {Number(number.Value)})");
                }

                string basis = OptionalIdentifier(production, file, factor, "basis", "CalorificBasis") ?? "NotApplicable";
                double? category = factor.Number("scope3Category");
                string? method = OptionalIdentifier(production, file, factor, "scope2Method", "Scope2Method");
                double biogenic = factor.Number("biogenicCarbonKg") ?? 0.0;
                double? uncertainty = factor.Number("uncertaintyPercent");

                var builder = new StringBuilder();
                builder.Append("                        new ").Append(Ns).AppendLine(".Factors.EmissionFactor(");
                builder.Append("                            ").Append(Literal(id)).AppendLine(",");
                builder.Append("                            ").Append(Literal(activity)).AppendLine(",");
                builder.Append("                            ").Append(Ns).Append(".Scope.").Append(scope).AppendLine(",");
                builder.Append("                            ")
                    .Append(category is null ? "null" : ((int)category.Value).ToString(CultureInfo.InvariantCulture))
                    .AppendLine(",");
                builder.Append("                            ")
                    .Append(method is null ? "null" : $"{Ns}.Scope2Method.{method}")
                    .AppendLine(",");
                builder.Append("                            ").Append(Ns).Append(".Units.Unit.").Append(unit).AppendLine(",");
                builder.Append("                            ").Append(Ns).Append(".Factors.CalorificBasis.").Append(basis).AppendLine(",");
                builder.Append("                            new ").Append(Ns).Append(".Factors.GasComponent[] { ")
                    .Append(string.Join(", ", componentExpressions)).AppendLine(" },");
                builder.Append("                            ")
                    .Append((factor.Boolean("componentsAreDerived") ?? false) ? "true" : "false")
                    .AppendLine(",");
                builder.Append("                            ")
                    .Append(publishedCo2e is null ? "null" : Number(publishedCo2e.Value))
                    .AppendLine(",");
                builder.Append("                            ")
                    .Append(publishedBasis is null ? "null" : $"{Ns}.GwpSet.{publishedBasis}")
                    .AppendLine(",");
                builder.Append("                            ").Append(Number(biogenic)).AppendLine(",");
                builder.Append("                            ").Append(Ns).Append(".DataQuality.").Append(quality).AppendLine(",");
                builder.Append("                            ")
                    .Append(uncertainty is null ? "null" : Number(uncertainty.Value))
                    .AppendLine(",");
                builder.Append("                            ").Append(Literal(factor.String("note"))).AppendLine(",");
                builder.Append("                            ").Append(Literal(factor.String("sourceReference"))).Append(')');

                emitted.Add(builder.ToString());
            }

            var setBuilder = new StringBuilder();
            setBuilder.Append("                new ").Append(Ns).AppendLine(".Factors.FactorSet(");
            setBuilder.Append("                    ").Append(Literal(setId)).AppendLine(",");
            setBuilder.Append("                    ").Append(Literal(name)).AppendLine(",");
            setBuilder.Append("                    ").Append(Literal(root.String("region"))).AppendLine(",");
            setBuilder.Append("                    ").Append(Literal(root.String("validFrom"))).AppendLine(",");
            setBuilder.Append("                    ").Append(Literal(root.String("validTo"))).AppendLine(",");
            setBuilder.Append("                    ").Append(source).AppendLine(",");
            setBuilder.Append("                    ").Append(verification).AppendLine(",");
            setBuilder.Append("                    new ").Append(Ns).AppendLine(".Factors.EmissionFactor[]");
            setBuilder.AppendLine("                    {");
            foreach (string factor in emitted)
            {
                setBuilder.Append(factor).AppendLine(",");
            }

            setBuilder.AppendLine("                    })");

            entries.Add(setBuilder.ToString().TrimEnd('\r', '\n'));
        }

        var output = new StringBuilder();
        AppendHeader(output);
        output.AppendLine("namespace CarbonAccounting.Factors");
        output.AppendLine("{");
        output.AppendLine("    public static partial class FactorCatalog");
        output.AppendLine("    {");
        // See the GWP emitter: a nested holder removes the dependency on the order the
        // compiler happens to place the hand-written and generated halves of the partial.
        output.AppendLine("        internal static FactorSet[] GeneratedSets => CatalogHolder.Sets;");
        output.AppendLine();
        output.AppendLine("        private static class CatalogHolder");
        output.AppendLine("        {");
        output.AppendLine("            internal static readonly FactorSet[] Sets = CreateGeneratedSets();");
        output.AppendLine("        }");
        output.AppendLine();
        output.AppendLine("        private static FactorSet[] CreateGeneratedSets()");
        output.AppendLine("        {");
        output.Append("            return new ").Append(Ns).AppendLine(".Factors.FactorSet[]");
        output.AppendLine("            {");
        foreach (string entry in entries)
        {
            output.Append(entry).AppendLine(",");
        }

        output.AppendLine("            };");
        output.AppendLine("        }");
        output.AppendLine("    }");
        output.AppendLine("}");

        production.AddSource("FactorCatalog.Catalog.g.cs", SourceText.From(output.ToString(), Encoding.UTF8));
    }

    // ------------------------------------------------------------------ shared

    private static IEnumerable<CatalogFile> Ordered(ImmutableArray<CatalogFile> files) =>
        files.OrderBy(f => f.Path, StringComparer.Ordinal);

    private static JsonObject? ParseRoot(SourceProductionContext production, CatalogFile file)
    {
        try
        {
            if (JsonParser.Parse(file.Text) is JsonObject root)
            {
                return root;
            }

            production.ReportDiagnostic(Diagnostic.Create(
                CatalogDiagnostics.MalformedJson, Location.None, file.FileName, "the top-level value is not an object"));
            return null;
        }
        catch (JsonParseException exception)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                CatalogDiagnostics.MalformedJson,
                CreateLocation(file, exception.Position, 1),
                file.FileName,
                exception.Message));
            return null;
        }
    }

    private static string? BuildSource(SourceProductionContext production, CatalogFile file, JsonObject root)
    {
        JsonObject? source = root.Object("source");
        if (source is null)
        {
            Report(production, CatalogDiagnostics.MissingField, file, root, "source", file.FileName);
            return null;
        }

        string? publisher = RequireString(production, file, source, "publisher");
        string? title = RequireString(production, file, source, "title");
        double? year = RequireNumber(production, file, source, "publicationYear");

        if (publisher is null || title is null || year is null)
        {
            return null;
        }

        return $"new {Ns}.Catalog.CatalogSource({Literal(publisher)}, {Literal(title)}, " +
               $"{(int)year.Value}, {Literal(source.String("url"))}, {Literal(source.String("license"))})";
    }

    private static string? BuildVerification(
        SourceProductionContext production,
        CatalogFile file,
        JsonObject root,
        string? setId,
        bool requireVerified)
    {
        JsonObject? verification = root.Object("verification");
        if (verification is null)
        {
            Report(production, CatalogDiagnostics.MissingField, file, root, "verification", file.FileName);
            return null;
        }

        string? status = RequireString(production, file, verification, "status");
        if (status is null)
        {
            return null;
        }

        string member = status switch
        {
            "verified" => "Verified",
            "needs-review" => "NeedsReview",
            "placeholder" => "Placeholder",
            _ => string.Empty,
        };

        if (member.Length == 0)
        {
            Report(production, CatalogDiagnostics.InvalidEnumName, file, verification, status, "VerificationStatus");
            return null;
        }

        if (member != "Verified")
        {
            DiagnosticDescriptor descriptor = requireVerified
                ? CatalogDiagnostics.UnverifiedInStrictBuild
                : CatalogDiagnostics.UnverifiedSet;

            production.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                CreateLocation(file, verification.Start, verification.Length),
                setId ?? file.FileName,
                status));
        }

        return $"{Ns}.VerificationStatus.{member}";
    }

    private static string? RequireString(SourceProductionContext production, CatalogFile file, JsonObject owner, string field)
    {
        string? value = owner.String(field);
        if (string.IsNullOrEmpty(value))
        {
            Report(production, CatalogDiagnostics.MissingField, file, owner, field, file.FileName);
            return null;
        }

        return value;
    }

    private static double? RequireNumber(SourceProductionContext production, CatalogFile file, JsonObject owner, string field)
    {
        double? value = owner.Number(field);
        if (value is null)
        {
            Report(production, CatalogDiagnostics.MissingField, file, owner, field, file.FileName);
        }

        return value;
    }

    private static string? RequireIdentifier(
        SourceProductionContext production,
        CatalogFile file,
        JsonObject owner,
        string field,
        string enumName)
    {
        string? value = RequireString(production, file, owner, field);
        if (value is null)
        {
            return null;
        }

        if (!IsIdentifier(value))
        {
            Report(production, CatalogDiagnostics.InvalidEnumName, file, owner, value, enumName);
            return null;
        }

        return value;
    }

    private static string? OptionalIdentifier(
        SourceProductionContext production,
        CatalogFile file,
        JsonObject owner,
        string field,
        string enumName)
    {
        string? value = owner.String(field);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!IsIdentifier(value!))
        {
            Report(production, CatalogDiagnostics.InvalidEnumName, file, owner, value!, enumName);
            return null;
        }

        return value;
    }

    private static void Report(
        SourceProductionContext production,
        DiagnosticDescriptor descriptor,
        CatalogFile file,
        JsonNode node,
        params object?[] arguments) =>
        production.ReportDiagnostic(Diagnostic.Create(
            descriptor, CreateLocation(file, node.Start, node.Length), arguments));

    private static Location CreateLocation(CatalogFile file, int start, int length)
    {
        if (start < 0 || start > file.Text.Length)
        {
            return Location.None;
        }

        int safeLength = Math.Max(1, Math.Min(length, file.Text.Length - start));
        var span = new TextSpan(start, safeLength);
        SourceText text = SourceText.From(file.Text);

        return Location.Create(file.Path, span, text.Lines.GetLinePositionSpan(span));
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Number(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture) + "D";

    private static string Literal(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder output)
    {
        output.AppendLine("// <auto-generated/>");
        output.AppendLine("// Generated by CarbonAccounting.Generators from the JSON catalog under data/.");
        output.AppendLine("// Do not edit: change the JSON and rebuild.");
        output.AppendLine("#nullable enable");
        output.AppendLine();
    }

    /// <summary>
    /// A catalog file reduced to plain strings so that the incremental pipeline can
    /// compare values structurally and skip regeneration when nothing changed.
    /// </summary>
    private readonly struct CatalogFile : IEquatable<CatalogFile>
    {
        private static readonly char[] s_pathSeparators = { '/', '\\' };

        public CatalogFile(string kind, string path, string text)
        {
            Kind = kind;
            Path = path;
            Text = text;
        }

        public static CatalogFile Ignored => new CatalogFile(string.Empty, string.Empty, string.Empty);

        public string Kind { get; }

        public string Path { get; }

        public string Text { get; }

        public string FileName
        {
            get
            {
                int slash = Path.LastIndexOfAny(s_pathSeparators);
                return slash >= 0 ? Path.Substring(slash + 1) : Path;
            }
        }

        public bool Equals(CatalogFile other) =>
            string.Equals(Kind, other.Kind, StringComparison.Ordinal) &&
            string.Equals(Path, other.Path, StringComparison.Ordinal) &&
            string.Equals(Text, other.Text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CatalogFile other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Kind.GetHashCode();
                hash = (hash * 397) ^ Path.GetHashCode();
                return (hash * 397) ^ Text.GetHashCode();
            }
        }
    }
}
