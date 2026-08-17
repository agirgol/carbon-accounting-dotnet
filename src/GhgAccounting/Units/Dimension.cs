namespace GhgAccounting.Units;

/// <summary>
/// The physical quantity a <see cref="Unit"/> measures.
/// </summary>
/// <remarks>
/// Conversion is only ever defined <em>within</em> a dimension. Crossing dimensions
/// — cubic metres of gas to kilowatt hours, litres of diesel to kilograms — needs a
/// substance property (calorific value, density) that varies by fuel, supplier and
/// season. Those belong in a factor, not in a unit table, so this library refuses
/// to do them implicitly.
/// </remarks>
public enum Dimension
{
    /// <summary>Energy content. Base unit: <see cref="Unit.KilowattHour"/>.</summary>
    Energy = 0,

    /// <summary>Volume. Base unit: <see cref="Unit.Litre"/>.</summary>
    Volume = 1,

    /// <summary>Mass. Base unit: <see cref="Unit.Kilogram"/>.</summary>
    Mass = 2,

    /// <summary>Distance. Base unit: <see cref="Unit.Kilometre"/>.</summary>
    Distance = 3,

    /// <summary>Freight activity (mass × distance). Base unit: <see cref="Unit.TonneKilometre"/>.</summary>
    FreightTransport = 4,

    /// <summary>Passenger activity (passengers × distance). Base unit: <see cref="Unit.PassengerKilometre"/>.</summary>
    PassengerTransport = 5,
}
