using System;
using System.Collections.Generic;

namespace CarbonAccounting.Factors;

/// <summary>
/// Every emission factor compiled into this build, indexed by id.
/// </summary>
/// <remarks>
/// The catalog is generated from <c>data/factors/*.json</c> at compile time, so the
/// lookup tables are ordinary static arrays. Nothing is read from disk, nothing is
/// parsed at start-up, and the shipped package pulls in no dependency to do it.
/// </remarks>
public static partial class FactorCatalog
{
    private static readonly EmissionFactor[] s_allFactors = FlattenFactors();
    private static readonly Dictionary<string, EmissionFactor> s_byId = IndexById(s_allFactors);

    /// <summary>Every published set compiled into this build.</summary>
    public static IReadOnlyList<FactorSet> Sets => GeneratedSets;

    /// <summary>Every factor across every set, in set order.</summary>
    public static IReadOnlyList<EmissionFactor> Factors => s_allFactors;

    /// <summary>
    /// Returns the factor with the given id.
    /// </summary>
    /// <param name="id">The factor id.</param>
    /// <returns>The matching factor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No factor with that id is compiled into this build.</exception>
    public static EmissionFactor Get(string id)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        if (!s_byId.TryGetValue(id, out EmissionFactor? factor))
        {
            throw new KeyNotFoundException($"No emission factor with id '{id}' is compiled into this build.");
        }

        return factor;
    }

    /// <summary>
    /// Attempts to find the factor with the given id.
    /// </summary>
    /// <param name="id">The factor id.</param>
    /// <param name="factor">The matching factor, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if a factor was found.</returns>
    public static bool TryGet(string id, out EmissionFactor? factor)
    {
        if (id is null)
        {
            factor = null;
            return false;
        }

        return s_byId.TryGetValue(id, out factor);
    }

    /// <summary>
    /// Returns the set with the given id.
    /// </summary>
    /// <param name="id">The set id.</param>
    /// <returns>The matching set.</returns>
    /// <exception cref="KeyNotFoundException">No set with that id is compiled into this build.</exception>
    public static FactorSet GetSet(string id)
    {
        FactorSet[] sets = GeneratedSets;
        for (int i = 0; i < sets.Length; i++)
        {
            if (string.Equals(sets[i].Id, id, StringComparison.Ordinal))
            {
                return sets[i];
            }
        }

        throw new KeyNotFoundException($"No factor set with id '{id}' is compiled into this build.");
    }

    private static EmissionFactor[] FlattenFactors()
    {
        FactorSet[] sets = GeneratedSets;

        int count = 0;
        for (int i = 0; i < sets.Length; i++)
        {
            count += sets[i].Factors.Count;
        }

        var all = new EmissionFactor[count];
        int next = 0;
        for (int i = 0; i < sets.Length; i++)
        {
            IReadOnlyList<EmissionFactor> factors = sets[i].Factors;
            for (int j = 0; j < factors.Count; j++)
            {
                all[next++] = factors[j];
            }
        }

        return all;
    }

    private static Dictionary<string, EmissionFactor> IndexById(EmissionFactor[] all)
    {
        var index = new Dictionary<string, EmissionFactor>(all.Length, StringComparer.Ordinal);
        for (int i = 0; i < all.Length; i++)
        {
            // The generator rejects duplicate ids at build time; this is a belt-and-braces guard.
            index[all[i].Id] = all[i];
        }

        return index;
    }
}
