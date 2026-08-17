using Microsoft.CodeAnalysis;

namespace CarbonAccounting.Generators;

/// <summary>
/// Build-time diagnostics for the catalog. Data problems surface as compiler errors
/// on the offending JSON line rather than as exceptions at run time.
/// </summary>
internal static class CatalogDiagnostics
{
    private const string Category = "CarbonAccounting.Catalog";

    public static readonly DiagnosticDescriptor MalformedJson = new DiagnosticDescriptor(
        id: "CARB001",
        title: "Catalog file is not well-formed JSON",
        messageFormat: "Catalog file '{0}' could not be parsed: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingField = new DiagnosticDescriptor(
        id: "CARB002",
        title: "Catalog entry is missing a required field",
        messageFormat: "'{0}' is required by the catalog schema but is missing or of the wrong type in '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateId = new DiagnosticDescriptor(
        id: "CARB003",
        title: "Duplicate catalog identifier",
        messageFormat: "The identifier '{0}' is declared more than once. Catalog ids must be unique and are never reused with a changed value.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidEnumName = new DiagnosticDescriptor(
        id: "CARB004",
        title: "Catalog value is not a usable enum member name",
        messageFormat: "'{0}' is not a valid {1} member name. Catalog values map onto enum members by name.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnverifiedInStrictBuild = new DiagnosticDescriptor(
        id: "CARB005",
        title: "Unverified catalog set in a verified-only build",
        messageFormat: "Catalog set '{0}' has verification status '{1}'. This build requires every set to be 'verified' before it can ship.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Set CarbonRequireVerifiedCatalog=false for local development. The release pipeline sets it to true so that unchecked numbers can never reach a published package.");

    public static readonly DiagnosticDescriptor FactorHasNoValue = new DiagnosticDescriptor(
        id: "CARB007",
        title: "Emission factor carries no usable value",
        messageFormat: "Factor '{0}' declares neither a gas breakdown nor a published CO2e figure with its GWP basis, so nothing can be calculated from it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnverifiedSet = new DiagnosticDescriptor(
        id: "CARB006",
        title: "Catalog set has not been verified against its source",
        messageFormat: "Catalog set '{0}' has verification status '{1}'. Its values must not be used for reporting until checked against the cited source.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
