namespace GhgAccounting.Catalog;

/// <summary>
/// Provenance for a shipped catalog set: who published the values, in which
/// document, in which year, and under what redistribution terms.
/// </summary>
/// <remarks>
/// Provenance is part of the public API rather than a README footnote because
/// ISO 14064-1 and GHG Protocol assurance both require a reported figure to be
/// traceable to its factor source. A caller must be able to print the citation
/// next to the number.
/// </remarks>
public sealed class CatalogSource
{
    /// <summary>Creates a source citation.</summary>
    /// <param name="publisher">The publishing organisation, for example "IPCC" or "UK DESNZ".</param>
    /// <param name="title">The document and, where applicable, the exact table.</param>
    /// <param name="publicationYear">The year of publication.</param>
    /// <param name="url">A stable link to the document, if one exists.</param>
    /// <param name="license">The redistribution terms the values are shipped under.</param>
    public CatalogSource(string publisher, string title, int publicationYear, string? url, string? license)
    {
        Publisher = publisher;
        Title = title;
        PublicationYear = publicationYear;
        Url = url;
        License = license;
    }

    /// <summary>The publishing organisation.</summary>
    public string Publisher { get; }

    /// <summary>The document, and where applicable the exact table within it.</summary>
    public string Title { get; }

    /// <summary>The year the values were published.</summary>
    public int PublicationYear { get; }

    /// <summary>A stable link to the document, or <see langword="null"/> if none.</summary>
    public string? Url { get; }

    /// <summary>The redistribution terms, or <see langword="null"/> if not recorded.</summary>
    public string? License { get; }

    /// <summary>Renders the citation as a single line suitable for a report footer.</summary>
    /// <returns>A human-readable citation.</returns>
    public override string ToString() => $"{Publisher} ({PublicationYear}). {Title}";
}
