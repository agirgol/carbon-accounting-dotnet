namespace GhgAccounting.Units;

/// <summary>
/// Units accepted as the denominator of an emission factor, and therefore as the
/// unit of incoming activity data.
/// </summary>
/// <remarks>
/// Members may be appended but never renumbered: the numeric values appear in
/// generated catalog code and in callers' persisted data.
/// </remarks>
public enum Unit
{
    // --- Energy (base: kWh) ---

    /// <summary>Kilowatt hour. Base unit of <see cref="Dimension.Energy"/>.</summary>
    KilowattHour = 0,

    /// <summary>Megawatt hour.</summary>
    MegawattHour = 1,

    /// <summary>Gigawatt hour.</summary>
    GigawattHour = 2,

    /// <summary>Megajoule.</summary>
    Megajoule = 3,

    /// <summary>Gigajoule.</summary>
    Gigajoule = 4,

    /// <summary>Therm (ISO/UK definition, 105.505585 MJ). Common on UK gas invoices.</summary>
    Therm = 5,

    /// <summary>One million British thermal units (MMBtu). Common on US energy invoices.</summary>
    MillionBtu = 6,

    // --- Volume (base: litre) ---

    /// <summary>Litre. Base unit of <see cref="Dimension.Volume"/>.</summary>
    Litre = 100,

    /// <summary>Cubic metre.</summary>
    CubicMetre = 101,

    /// <summary>US liquid gallon.</summary>
    UsGallon = 102,

    /// <summary>Imperial gallon.</summary>
    ImperialGallon = 103,

    // --- Mass (base: kilogram) ---

    /// <summary>Kilogram. Base unit of <see cref="Dimension.Mass"/>.</summary>
    Kilogram = 200,

    /// <summary>Gram.</summary>
    Gram = 201,

    /// <summary>Metric tonne (1 000 kg).</summary>
    Tonne = 202,

    /// <summary>Avoirdupois pound.</summary>
    Pound = 203,

    /// <summary>US short ton (2 000 lb).</summary>
    ShortTon = 204,

    /// <summary>Imperial long ton (2 240 lb).</summary>
    LongTon = 205,

    // --- Distance (base: kilometre) ---

    /// <summary>Kilometre. Base unit of <see cref="Dimension.Distance"/>.</summary>
    Kilometre = 300,

    /// <summary>Metre.</summary>
    Metre = 301,

    /// <summary>Statute mile.</summary>
    Mile = 302,

    /// <summary>Nautical mile.</summary>
    NauticalMile = 303,

    // --- Freight activity (base: tonne-kilometre) ---

    /// <summary>Tonne-kilometre. Base unit of <see cref="Dimension.FreightTransport"/>.</summary>
    TonneKilometre = 400,

    /// <summary>Tonne-mile.</summary>
    TonneMile = 401,

    /// <summary>Kilogram-kilometre.</summary>
    KilogramKilometre = 402,

    // --- Passenger activity (base: passenger-kilometre) ---

    /// <summary>Passenger-kilometre. Base unit of <see cref="Dimension.PassengerTransport"/>.</summary>
    PassengerKilometre = 500,

    /// <summary>Passenger-mile.</summary>
    PassengerMile = 501,
}
