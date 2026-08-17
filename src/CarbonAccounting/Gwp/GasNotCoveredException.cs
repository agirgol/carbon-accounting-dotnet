using System;

namespace CarbonAccounting;

/// <summary>
/// Thrown when a GWP set publishes no potential for the requested gas.
/// </summary>
/// <remarks>
/// Treated as an error rather than a zero: a gas silently valued at zero drops out
/// of the CO<sub>2</sub>e total without leaving a trace anywhere in the report.
/// </remarks>
public sealed class GasNotCoveredException : InvalidOperationException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="gas">The gas that was requested.</param>
    /// <param name="set">The set that does not cover it.</param>
    public GasNotCoveredException(GreenhouseGas gas, GwpSet set)
        : base($"The {set} GWP set publishes no value for {gas}. " +
               "Supply the potential explicitly, or use a set that covers this gas — " +
               "do not treat the gas as zero.")
    {
        Gas = gas;
        Set = set;
    }

    /// <summary>The gas that was requested.</summary>
    public GreenhouseGas Gas { get; }

    /// <summary>The set that does not cover it.</summary>
    public GwpSet Set { get; }
}
