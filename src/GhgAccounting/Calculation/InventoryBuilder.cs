using System;
using System.Collections.Generic;
using GhgAccounting.Factors;
using GhgAccounting.Units;

namespace GhgAccounting.Calculation;

/// <summary>
/// Accumulates calculated lines into an <see cref="Inventory"/>.
/// </summary>
/// <remarks>
/// Obtained from <see cref="EmissionCalculator.CreateInventory"/> so that the inventory
/// and the calculations that feed it cannot end up on different GWP sets by accident.
/// Results computed elsewhere may still be added, but only if their set matches.
/// </remarks>
public sealed class InventoryBuilder
{
    private readonly EmissionCalculator _calculator;
    private readonly List<EmissionResult> _entries = new List<EmissionResult>();

    internal InventoryBuilder(EmissionCalculator calculator)
    {
        _calculator = calculator;
    }

    /// <summary>The assessment report this inventory is built on.</summary>
    public GwpSet GwpSet => _calculator.GwpSet;

    /// <summary>How many lines have been added so far.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Calculates <paramref name="activity"/> against <paramref name="factor"/> and adds
    /// the result.
    /// </summary>
    /// <param name="activity">The activity figure.</param>
    /// <param name="factor">The factor to apply.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="UnitConversionException">The activity unit measures a different physical quantity than the factor's denominator.</exception>
    public InventoryBuilder Add(Quantity activity, EmissionFactor factor) =>
        Add(_calculator.Calculate(activity, factor));

    /// <summary>
    /// Calculates <paramref name="activity"/> against the catalog factor with the given
    /// id and adds the result.
    /// </summary>
    /// <param name="activity">The activity figure.</param>
    /// <param name="factorId">Identifier of a factor compiled into this build.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="KeyNotFoundException">No factor with that id is compiled into this build.</exception>
    public InventoryBuilder Add(Quantity activity, string factorId) =>
        Add(_calculator.Calculate(activity, factorId));

    /// <summary>
    /// Adds an already calculated result.
    /// </summary>
    /// <param name="result">The result to add.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="GwpSetMismatchException"><paramref name="result"/> was computed under a different GWP set.</exception>
    public InventoryBuilder Add(EmissionResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.GwpSet != GwpSet)
        {
            throw new GwpSetMismatchException(GwpSet, result.GwpSet);
        }

        _entries.Add(result);
        return this;
    }

    /// <summary>
    /// Produces the inventory from everything added so far.
    /// </summary>
    /// <returns>The completed inventory.</returns>
    /// <remarks>
    /// The builder stays usable afterwards; calling this twice produces two independent
    /// snapshots rather than sharing state with the first.
    /// </remarks>
    public Inventory Build() => new Inventory(GwpSet, _entries.ToArray());
}
