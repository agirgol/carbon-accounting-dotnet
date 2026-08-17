using GhgAccounting.Units;

namespace GhgAccounting.Calculation;

/// <summary>
/// The mass of one gas released by a single activity, alongside what that mass
/// amounts to in CO<sub>2</sub>e under the calculation's chosen GWP set.
/// </summary>
/// <remarks>
/// Both figures are kept because a report needs them for different purposes: the raw
/// mass is what an inventory discloses per gas, and the CO<sub>2</sub>e is what
/// aggregates. Deriving one from the other after the fact would require the caller to
/// remember which GWP set was used.
/// </remarks>
public readonly struct GasEmission
{
    /// <summary>Creates a gas emission line.</summary>
    /// <param name="gas">The gas released.</param>
    /// <param name="mass">Mass of that gas.</param>
    /// <param name="co2e">The same mass expressed as carbon dioxide equivalent.</param>
    public GasEmission(GreenhouseGas gas, Quantity mass, Quantity co2e)
    {
        Gas = gas;
        Mass = mass;
        Co2e = co2e;
    }

    /// <summary>The gas released.</summary>
    public GreenhouseGas Gas { get; }

    /// <summary>Mass of <see cref="Gas"/> released.</summary>
    public Quantity Mass { get; }

    /// <summary>The same mass expressed as CO<sub>2</sub>e.</summary>
    public Quantity Co2e { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Gas}: {Mass} ({Co2e} CO2e)";
}
