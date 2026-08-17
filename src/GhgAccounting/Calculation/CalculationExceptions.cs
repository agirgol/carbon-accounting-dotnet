using System;

namespace GhgAccounting.Calculation;

/// <summary>
/// Thrown when a result computed under one GWP set is added to an inventory built on
/// another.
/// </summary>
/// <remarks>
/// An inventory that mixes assessment reports has no meaningful total and cannot be
/// disclosed, because the standard requires a single stated set for the reporting year.
/// </remarks>
public sealed class GwpSetMismatchException : InvalidOperationException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="expected">The set the inventory is built on.</param>
    /// <param name="actual">The set the offending result was computed under.</param>
    public GwpSetMismatchException(GwpSet expected, GwpSet actual)
        : base($"This inventory aggregates {expected} results, but the result being added was computed under {actual}. " +
               "An inventory must state a single GWP set; recalculate the result with the inventory's set instead.")
    {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The set the inventory is built on.</summary>
    public GwpSet Expected { get; }

    /// <summary>The set the offending result was computed under.</summary>
    public GwpSet Actual { get; }
}

/// <summary>
/// Thrown when a factor that publishes only an aggregated CO<sub>2</sub>e figure is used
/// under a different GWP set than the one it was aggregated with.
/// </summary>
/// <remarks>
/// A factor with a gas breakdown can be re-aggregated under any set. One without a
/// breakdown cannot: the publisher's GWP choice is baked into the number and there is
/// nothing left to recompute from. Converting it would mean guessing the split.
/// </remarks>
public sealed class GwpBasisMismatchException : InvalidOperationException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="factorId">The factor that could not be used.</param>
    /// <param name="publishedBasis">The set the factor's figure was aggregated under.</param>
    /// <param name="requested">The set the calculation is running under.</param>
    public GwpBasisMismatchException(string factorId, GwpSet? publishedBasis, GwpSet requested)
        : base($"Factor '{factorId}' publishes no gas breakdown, only a CO2e figure aggregated under " +
               $"{publishedBasis?.ToString() ?? "an unstated set"}, so it cannot be used in a {requested} " +
               "calculation. Re-aggregating would mean inventing the split the publisher never gave.")
    {
        FactorId = factorId;
        PublishedBasis = publishedBasis;
        Requested = requested;
    }

    /// <summary>The factor that could not be used.</summary>
    public string FactorId { get; }

    /// <summary>The set the factor's figure was aggregated under.</summary>
    public GwpSet? PublishedBasis { get; }

    /// <summary>The set the calculation is running under.</summary>
    public GwpSet Requested { get; }
}

/// <summary>
/// Thrown when a total is requested under a Scope 2 method the inventory holds no data
/// for, while it does hold data under the other method.
/// </summary>
/// <remarks>
/// Returning the other method's Scope 2 figure would be wrong, and returning zero would
/// silently understate the total by the whole of purchased electricity. Neither is
/// something a caller could detect, so this is an error instead.
/// </remarks>
public sealed class Scope2MethodNotReportedException : InvalidOperationException
{
    /// <summary>Initialises the exception.</summary>
    /// <param name="requested">The method that was asked for.</param>
    public Scope2MethodNotReportedException(Scope2Method requested)
        : base($"This inventory holds Scope 2 data, but none of it is {requested}. " +
               "The GHG Protocol Scope 2 Guidance requires dual reporting: add the missing " +
               $"{requested} factors before asking for a total under that method.")
    {
        Requested = requested;
    }

    /// <summary>The method that was asked for.</summary>
    public Scope2Method Requested { get; }
}
