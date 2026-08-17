using System;
using System.Collections.Generic;
using CarbonAccounting.Factors;
using CarbonAccounting.Units;

namespace CarbonAccounting.Calculation;

/// <summary>
/// Applies emission factors to activity data under one fixed GWP set.
/// </summary>
/// <remarks>
/// The GWP set is fixed for the lifetime of the calculator rather than passed per call.
/// An inventory that mixes assessment reports is not a valid disclosure, and making the
/// set an instance-level decision means it cannot vary line by line without the caller
/// noticing they built a second calculator.
/// </remarks>
public sealed class EmissionCalculator
{
    /// <summary>Creates a calculator bound to one GWP set.</summary>
    /// <param name="gwpSet">The assessment report to aggregate gases with.</param>
    /// <exception cref="ArgumentOutOfRangeException">No table for that set is compiled into this build.</exception>
    public EmissionCalculator(GwpSet gwpSet)
    {
        Gwp = GwpTable.For(gwpSet);
    }

    /// <summary>The GWP table every calculation on this instance uses.</summary>
    public GwpTable Gwp { get; }

    /// <summary>The assessment report this calculator aggregates with.</summary>
    public GwpSet GwpSet => Gwp.Set;

    /// <summary>
    /// Converts activity data into emissions using <paramref name="factor"/>.
    /// </summary>
    /// <param name="activity">
    /// The activity figure. Any unit in the same <see cref="Dimension"/> as the factor's
    /// denominator is accepted and converted; a unit from another dimension is rejected.
    /// </param>
    /// <param name="factor">The factor to apply.</param>
    /// <returns>The emissions, with the inputs and provenance attached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factor"/> is <see langword="null"/>.</exception>
    /// <exception cref="UnitConversionException">The activity unit measures a different physical quantity than the factor's denominator.</exception>
    /// <exception cref="GasNotCoveredException">The factor names a gas this GWP set publishes no potential for.</exception>
    public EmissionResult Calculate(Quantity activity, EmissionFactor factor)
    {
        if (factor is null)
        {
            throw new ArgumentNullException(nameof(factor));
        }

        // Throws rather than guessing when the dimensions do not match: applying a
        // per-kWh factor to a cubic-metre reading is an order-of-magnitude error that
        // nothing downstream would catch.
        double amount = UnitConverter.Convert(activity.Value, activity.Unit, factor.Unit);

        IReadOnlyList<GasComponent> components = factor.Components;
        var gases = new GasEmission[components.Count];
        double co2eKilograms = 0.0;

        for (int i = 0; i < components.Count; i++)
        {
            GasComponent component = components[i];
            double massKilograms = amount * component.KilogramsPerUnit;
            double gasCo2e = massKilograms * Gwp.GetGwp(component.Gas);

            gases[i] = new GasEmission(
                component.Gas,
                new Quantity(massKilograms, Unit.Kilogram),
                new Quantity(gasCo2e, Unit.Kilogram));

            co2eKilograms += gasCo2e;
        }

        if (components.Count == 0)
        {
            // No breakdown to re-aggregate, so the publisher's own figure is the only
            // thing available — and it is only valid under the set it was made with.
            if (factor.PublishedGwpBasis != GwpSet || factor.PublishedCo2eKgPerUnit is not double rate)
            {
                throw new GwpBasisMismatchException(factor.Id, factor.PublishedGwpBasis, GwpSet);
            }

            co2eKilograms = amount * rate;
        }

        Quantity? published = factor.PublishedCo2eKgPerUnit is double perUnit
            ? new Quantity(amount * perUnit, Unit.Kilogram)
            : (Quantity?)null;

        return new EmissionResult(
            activity,
            factor,
            GwpSet,
            gases,
            new Quantity(co2eKilograms, Unit.Kilogram),
            new Quantity(amount * factor.BiogenicCarbonKg, Unit.Kilogram),
            published);
    }

    /// <summary>
    /// Converts activity data using the catalog factor with the given id.
    /// </summary>
    /// <param name="activity">The activity figure.</param>
    /// <param name="factorId">Identifier of a factor compiled into this build.</param>
    /// <returns>The emissions, with the inputs and provenance attached.</returns>
    /// <exception cref="KeyNotFoundException">No factor with that id is compiled into this build.</exception>
    public EmissionResult Calculate(Quantity activity, string factorId) =>
        Calculate(activity, FactorCatalog.Get(factorId));

    /// <summary>
    /// Starts an inventory that aggregates results from this calculator.
    /// </summary>
    /// <returns>A builder bound to this calculator's GWP set.</returns>
    public InventoryBuilder CreateInventory() => new InventoryBuilder(this);
}
