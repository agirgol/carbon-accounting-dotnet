namespace CarbonAccounting.Factors;

/// <summary>
/// The mass of one gas released per single unit of activity.
/// </summary>
/// <remarks>
/// Factors are stored per gas rather than pre-aggregated to CO<sub>2</sub>e so that
/// the <see cref="GwpSet"/> stays a caller decision. A catalog that ships only
/// CO<sub>2</sub>e has already baked in an assessment report, and the choice can
/// never be revisited without new source data.
/// </remarks>
public readonly struct GasComponent
{
    /// <summary>Creates a component.</summary>
    /// <param name="gas">The gas released.</param>
    /// <param name="kilogramsPerUnit">Kilograms of that gas per one unit of activity.</param>
    public GasComponent(GreenhouseGas gas, double kilogramsPerUnit)
    {
        Gas = gas;
        KilogramsPerUnit = kilogramsPerUnit;
    }

    /// <summary>The gas released.</summary>
    public GreenhouseGas Gas { get; }

    /// <summary>Kilograms of <see cref="Gas"/> per one unit of activity.</summary>
    public double KilogramsPerUnit { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Gas}: {KilogramsPerUnit} kg/unit";
}
