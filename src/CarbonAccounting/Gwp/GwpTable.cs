using System;
using System.Collections.Generic;
using CarbonAccounting.Catalog;

namespace CarbonAccounting;

/// <summary>
/// One IPCC assessment report's global warming potentials, as a flat lookup.
/// </summary>
/// <remarks>
/// Instances are generated at compile time from <c>data/gwp/*.json</c>; there is no
/// parsing, no I/O and no allocation on the lookup path. See
/// <see cref="GwpSet"/> for why the choice of table is the caller's.
/// </remarks>
public sealed partial class GwpTable
{
    private readonly double[] _byGas;

    internal GwpTable(
        GwpSet set,
        string name,
        int timeHorizonYears,
        bool includesClimateCarbonFeedback,
        CatalogSource source,
        VerificationStatus verification,
        GwpValue[] values)
    {
        Set = set;
        Name = name;
        TimeHorizonYears = timeHorizonYears;
        IncludesClimateCarbonFeedback = includesClimateCarbonFeedback;
        Source = source;
        Verification = verification;
        Values = values;

        int size = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int index = (int)values[i].Gas;
            if (index >= size)
            {
                size = index + 1;
            }
        }

        _byGas = new double[size];
        for (int i = 0; i < _byGas.Length; i++)
        {
            _byGas[i] = double.NaN;
        }

        for (int i = 0; i < values.Length; i++)
        {
            _byGas[(int)values[i].Gas] = values[i].Gwp;
        }
    }

    /// <summary>Which assessment report this table comes from.</summary>
    public GwpSet Set { get; }

    /// <summary>The set's display name.</summary>
    public string Name { get; }

    /// <summary>The time horizon the potentials are integrated over, in years. Corporate reporting uses 100.</summary>
    public int TimeHorizonYears { get; }

    /// <summary>
    /// Whether these are the values that include climate-carbon feedback. GHG
    /// Protocol inventories use the values <em>without</em> feedback.
    /// </summary>
    public bool IncludesClimateCarbonFeedback { get; }

    /// <summary>Where the values came from.</summary>
    public CatalogSource Source { get; }

    /// <summary>Whether the shipped values have been checked against <see cref="Source"/>.</summary>
    public VerificationStatus Verification { get; }

    /// <summary>Every gas this table covers, in enum order.</summary>
    public IReadOnlyList<GwpValue> Values { get; }

    /// <summary>
    /// Returns the global warming potential of <paramref name="gas"/>.
    /// </summary>
    /// <param name="gas">The gas to look up.</param>
    /// <returns>The GWP, relative to CO<sub>2</sub>.</returns>
    /// <exception cref="GasNotCoveredException">This table publishes no value for the gas.</exception>
    public double GetGwp(GreenhouseGas gas)
    {
        if (!TryGetGwp(gas, out double gwp))
        {
            throw new GasNotCoveredException(gas, Set);
        }

        return gwp;
    }

    /// <summary>
    /// Attempts to look up the global warming potential of <paramref name="gas"/>.
    /// </summary>
    /// <param name="gas">The gas to look up.</param>
    /// <param name="gwp">The GWP, or <c>0</c> when the table does not cover the gas.</param>
    /// <returns><see langword="true"/> if the table covers the gas.</returns>
    public bool TryGetGwp(GreenhouseGas gas, out double gwp)
    {
        int index = (int)gas;
        if ((uint)index >= (uint)_byGas.Length)
        {
            gwp = 0.0;
            return false;
        }

        double value = _byGas[index];
        if (double.IsNaN(value))
        {
            gwp = 0.0;
            return false;
        }

        gwp = value;
        return true;
    }

    /// <summary>
    /// Converts a mass of a single gas into carbon dioxide equivalent.
    /// </summary>
    /// <param name="mass">Mass of the gas. Any <see cref="Units.Dimension.Mass"/> unit.</param>
    /// <param name="gas">The gas the mass is of.</param>
    /// <returns>The CO<sub>2</sub>e mass, in the same unit as <paramref name="mass"/>.</returns>
    /// <exception cref="GasNotCoveredException">This table publishes no value for the gas.</exception>
    /// <exception cref="ArgumentException"><paramref name="mass"/> is not a mass quantity.</exception>
    public Units.Quantity ToCo2e(Units.Quantity mass, GreenhouseGas gas)
    {
        if (mass.Dimension != Units.Dimension.Mass)
        {
            throw new ArgumentException(
                $"CO2e conversion needs a mass, but {mass.Unit} measures {mass.Dimension}.",
                nameof(mass));
        }

        return new Units.Quantity(mass.Value * GetGwp(gas), mass.Unit);
    }

    /// <summary>Every GWP set compiled into this build.</summary>
    public static IReadOnlyList<GwpTable> All => GeneratedTables;

    /// <summary>
    /// Returns the table for <paramref name="set"/>.
    /// </summary>
    /// <param name="set">The assessment report to use.</param>
    /// <returns>The matching table.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No table for that set is compiled into this build.</exception>
    public static GwpTable For(GwpSet set)
    {
        GwpTable[] tables = GeneratedTables;
        for (int i = 0; i < tables.Length; i++)
        {
            if (tables[i].Set == set)
            {
                return tables[i];
            }
        }

        throw new ArgumentOutOfRangeException(nameof(set), set, "No GWP table for this set is compiled into this build.");
    }

    /// <inheritdoc />
    public override string ToString() => $"{Set} (GWP-{TimeHorizonYears}, {Values.Count} gases)";
}
