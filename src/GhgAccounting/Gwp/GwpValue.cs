namespace GhgAccounting;

/// <summary>
/// One gas's global warming potential within a single <see cref="GwpTable"/>.
/// </summary>
public readonly struct GwpValue
{
    /// <summary>Creates a GWP entry.</summary>
    /// <param name="gas">The gas.</param>
    /// <param name="gwp">Its warming potential relative to CO<sub>2</sub>.</param>
    /// <param name="formula">The chemical formula, for display.</param>
    /// <param name="sourceTable">The exact table the value was read from.</param>
    public GwpValue(GreenhouseGas gas, double gwp, string? formula, string? sourceTable)
    {
        Gas = gas;
        Gwp = gwp;
        Formula = formula;
        SourceTable = sourceTable;
    }

    /// <summary>The gas.</summary>
    public GreenhouseGas Gas { get; }

    /// <summary>The warming potential relative to CO<sub>2</sub> over the table's time horizon.</summary>
    public double Gwp { get; }

    /// <summary>The chemical formula, or <see langword="null"/> if not recorded.</summary>
    public string? Formula { get; }

    /// <summary>
    /// The exact table this value was read from, or <see langword="null"/> if the set
    /// draws on a single table and the citation on <see cref="GwpTable.Source"/> is
    /// sufficient.
    /// </summary>
    /// <remarks>
    /// AR6 needs this: its headline metrics table covers only a handful of species, and
    /// its methane values differ from the supplementary table's because they account for
    /// the carbon content of the methane. A single set-level citation would misstate
    /// where half the numbers came from.
    /// </remarks>
    public string? SourceTable { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Gas} = {Gwp}";
}
