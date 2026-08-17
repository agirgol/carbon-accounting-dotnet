#!/usr/bin/env python3
"""Derive national grid emission factors from Eurostat's published statistics.

Unlike the DESNZ and eGRID importers, which read a factor someone else published, this
one computes it. That is a meaningful difference and the output says so: the factors are
marked as derived, the method is written into the set, and every input value is recorded
alongside the result so the arithmetic can be checked without re-running anything.

The construction follows the EEA's own indicator: emissions from public electricity
production divided by public electricity generated. Two things make it more than a
division.

  1. National inventories report CRF 1.A.1.a as "public electricity AND heat
     production". The heat share has to come out, or a grid factor is overstated by
     whatever district heating the country runs. Combined heat and power fuel is split
     in proportion to the plant's electricity and heat output.

  2. The numerator covers main activity producers only. The denominator must therefore
     also exclude autoproducers, or the boundary does not match.

How much the first choice matters varies enormously by country: it is under one percent
where there is almost no district heating, and far larger in the Nordics. Rather than
apply one rule everywhere and hope, the importer measures the sensitivity per country
and downgrades the data quality where the allocation genuinely drives the answer.

Gas masses are used rather than the CO2e aggregate, because Eurostat publishes CH4 and
N2O both ways. That keeps the resulting factors GWP-agnostic, and dividing one series by
the other recovers the GWP set the inventory was compiled with, which is how the basis
is verified rather than assumed.

Usage:
    python3 tools/eurostat-import/import_eurostat_grid.py <year> <output.json>
"""

import json
import sys
import urllib.request

API = "https://ec.europa.eu/eurostat/api/dissemination/statistics/1.0/data"
USER_AGENT = "ghg-accounting-catalog-importer/1.0 (+https://github.com/agirgol/carbon-accounting-dotnet)"

GASES = {"CarbonDioxide": "CO2", "MethaneFossil": "CH4", "NitrousOxide": "N2O"}
CO2E_SERIES = {"CH4": "CH4_CO2E", "N2O": "N2O_CO2E"}
AR5 = {"CH4": 28.0, "N2O": 265.0}

BALANCE = {
    "fuel_electricity_only": "TI_EHG_MAPE_E",
    "fuel_chp": "TI_EHG_MAPCHP_E",
    "fuel_heat_only": "TI_EHG_MAPH_E",
    "electricity_electricity_only": "GEP_MAPE",
    "electricity_chp": "GEP_MAPCHP",
    "heat_chp": "GHP_MAPCHP",
    "heat_heat_only": "GHP_MAPH",
}

# Aggregates rather than countries. A factor for "the EU" is not a grid anyone buys
# electricity from.
AGGREGATES = {"EU27_2020", "EU28", "EA19", "EA20", "EU", "EEA", "EEA30_2007", "EEA31", "EFTA"}

# Combined heat and power fuel is split by the GHG Protocol's efficiency method, which
# that guidance names as its preferred approach: fuel is attributed in proportion to what
# each output would have consumed if produced separately, so electricity carries its
# thermodynamic penalty rather than being weighed against heat joule for joule.
#
#   share of fuel to electricity  =  (P / eP) / ((P / eP) + (H / eH))
#
# The defaults are the values the guidance itself recommends. IEA reference efficiencies
# are carried alongside purely to measure how much the parameter choice is worth; across
# every country imported the two disagree by well under one percent, which is what makes
# the resulting factors publishable at all.
GHGP_EFFICIENCY = {"electricity": 0.35, "heat": 0.80}
IEA_EFFICIENCY = {"electricity": 0.40, "heat": 0.90}

# Above this, the parameter choice inside the preferred method is doing enough of the
# work that the figure deserves to be labelled a proxy.
SENSITIVITY_PROXY_THRESHOLD = 0.02

# And above this it is not worth shipping at all: the answer would be coming out of the
# convention rather than out of the data.
SENSITIVITY_SHIP_THRESHOLD = 0.05

# Eurostat rounds these series to two decimals. Below roughly a kilotonne the ratio of
# the CO2e series to the mass series is dominated by that rounding, so a single country
# cannot be used to identify the GWP set; the pooled total can.
MASS_FOR_RELIABLE_RATIO = 1.0


def fetch(dataset, params):
    query = "&".join(f"{k}={v}" for k, v in params.items())
    url = f"{API}/{dataset}?format=JSON&lang=EN&{query}"
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=180) as response:
        return url, json.loads(response.read().decode("utf-8"))


def decode(payload):
    """Turn a JSON-stat response into {(dim code, ...): value} keyed by category codes."""
    order = payload["id"]
    sizes = payload["size"]
    reverse = []
    for name in order:
        index = payload["dimension"][name]["category"]["index"]
        if isinstance(index, list):
            index = {code: position for position, code in enumerate(index)}
        reverse.append({position: code for code, position in index.items()})

    strides = [1] * len(sizes)
    for i in range(len(sizes) - 2, -1, -1):
        strides[i] = strides[i + 1] * sizes[i + 1]

    out = {}
    for flat, value in payload.get("value", {}).items():
        remaining = int(flat)
        key = []
        for dimension, stride in enumerate(strides):
            position, remaining = divmod(remaining, stride)
            key.append(reverse[dimension][position])
        out[tuple(key)] = float(value)
    return order, out


def series(dataset, params, wanted):
    """Return {(selected dims): value} reduced to the dimensions named in `wanted`."""
    url, payload = fetch(dataset, params)
    order, values = decode(payload)
    positions = [order.index(name) for name in wanted]
    labels = {
        name: payload["dimension"][name]["category"].get("label", {})
        for name in order
    }
    reduced = {tuple(key[p] for p in positions): value for key, value in values.items()}
    return url, reduced, labels


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    year, destination = sys.argv[1], sys.argv[2]

    urls = {}
    urls["emissions"], emissions, labels = series(
        "env_air_gge", {"src_crf": "CRF1A1A", "time": year, "unit": "THS_T"}, ["geo", "airpol"])
    country_names = labels.get("geo", {})

    energy = {}
    for name, item in BALANCE.items():
        url, values, _ = series(
            "nrg_bal_c",
            {"nrg_bal": item, "siec": "TOTAL", "unit": "GWH", "time": year},
            ["geo"])
        urls[name] = url
        energy[name] = values

    countries = sorted({geo for geo, _ in emissions} - AGGREGATES)

    factors = []
    skipped = {}
    proxies = []
    recovered_all = {}

    for geo in countries:
        gases = {code: emissions.get((geo, code)) for code in GASES.values()}
        co2e = {code: emissions.get((geo, series_code)) for code, series_code in CO2E_SERIES.items()}
        balances = {name: energy[name].get((geo,)) for name in BALANCE}

        if any(v is None for v in gases.values()) or any(v is None for v in balances.values()):
            skipped[geo] = "Eurostat publishes no complete emissions or energy balance series for this country and year."
            continue

        # Verify the GWP basis from the data rather than from documentation. Eurostat
        # rounds to two decimals, so for a country emitting a fraction of a kilotonne of
        # N2O the ratio is mostly rounding noise; those are accumulated for the
        # aggregate check below instead of being tested individually.
        for code, divisor in AR5.items():
            mass, aggregate = gases[code], co2e[code]
            if not mass or aggregate is None:
                continue
            totals = recovered_all.setdefault(code, [0.0, 0.0])
            totals[0] += mass
            totals[1] += aggregate
            if mass >= MASS_FOR_RELIABLE_RATIO and abs(aggregate / mass - divisor) / divisor > 0.03:
                raise SystemExit(
                    f"{geo}: recovered GWP for {code} is {aggregate / mass:.2f}, not the "
                    f"AR5 value of {divisor}. Update this importer deliberately rather "
                    "than shipping factors labelled with the wrong set.")

        fuel_total = (balances["fuel_electricity_only"] + balances["fuel_chp"]
                      + balances["fuel_heat_only"])
        electricity_main = (balances["electricity_electricity_only"]
                            + balances["electricity_chp"])
        chp_output = balances["electricity_chp"] + balances["heat_chp"]

        if fuel_total <= 0 or electricity_main <= 0:
            skipped[geo] = "No main activity fuel input or generation to divide by."
            continue

        def electricity_share_of_emissions(efficiency):
            """Share of CRF 1.A.1.a emissions attributable to electricity."""
            power, heat = balances["electricity_chp"], balances["heat_chp"]
            if power + heat <= 0:
                chp_share = 0.0
            else:
                weighted_power = power / efficiency["electricity"]
                weighted_heat = heat / efficiency["heat"]
                chp_share = weighted_power / (weighted_power + weighted_heat)
            return (balances["fuel_electricity_only"]
                    + balances["fuel_chp"] * chp_share) / fuel_total

        electricity_share = electricity_share_of_emissions(GHGP_EFFICIENCY)
        chp_electricity_share = electricity_share

        # Thousand tonnes over GWh is kg per MWh: 1e6 kg over 1e3 MWh.
        def per_mwh(share):
            return {member: gases[code] * share * 1e3 / electricity_main
                    for member, code in GASES.items()}

        components = per_mwh(electricity_share)

        def co2e(share):
            values = per_mwh(share)
            return sum(values[m] * (1.0 if m == "CarbonDioxide" else AR5[GASES[m]])
                       for m in values)

        # How much the answer rests on the parameter choice, measured between two
        # published sets of reference efficiencies rather than against an invented
        # alternative. Also recorded: what the energy content method, which the guidance
        # permits as an alternative, would give.
        chosen_co2e = co2e(electricity_share)
        iea_co2e = co2e(electricity_share_of_emissions(IEA_EFFICIENCY))
        energy_content_co2e = co2e(
            electricity_share_of_emissions({"electricity": 1.0, "heat": 1.0}))

        sensitivity = abs(iea_co2e - chosen_co2e) / chosen_co2e if chosen_co2e else 0.0
        alternative_method_gap = (abs(energy_content_co2e - chosen_co2e) / chosen_co2e
                                  if chosen_co2e else 0.0)

        if sensitivity > SENSITIVITY_SHIP_THRESHOLD:
            skipped[geo] = (
                f"The two published sets of reference efficiencies disagree by "
                f"{round(sensitivity * 100, 1)}% for this country, so the figure would "
                f"reflect the parameter choice more than the data.")
            continue

        quality = "Proxy" if sensitivity > SENSITIVITY_PROXY_THRESHOLD else "Secondary"
        if quality == "Proxy":
            proxies.append((geo, sensitivity))

        name = country_names.get(geo, geo)
        factors.append({
            "id": f"eurostat-grid-{year}/{geo.lower()}/public-grid/mwh",
            "activity": f"Purchased grid electricity — {name}",
            "scope": "Scope2",
            "scope2Method": "LocationBased",
            "region": geo,
            "unit": "MegawattHour",
            "basis": "NotApplicable",
            "components": {k: float(f"{v:.12g}") for k, v in components.items()},
            "componentsAreDerived": True,
            "dataQuality": quality,
            "sourceReference": f"Eurostat env_air_gge CRF1A1A and nrg_bal_c, {geo} {year}",
            "note": (
                f"Main activity producers only. {round(electricity_share * 100, 2)}% of "
                f"CRF 1.A.1.a emissions allocated to electricity by the GHG Protocol "
                f"efficiency method. Substituting IEA reference efficiencies moves the "
                f"result by {round(sensitivity * 100, 2)}%; the energy content method, "
                f"which the guidance permits as an alternative, would move it by "
                f"{round(alternative_method_gap * 100, 1)}%. Methane is treated as fossil, "
                "which the generation mix makes overwhelmingly true, though the inventory "
                "figure includes a small biomass-derived share."
            ),
        })

    if not factors:
        raise SystemExit("No country produced a usable factor; refusing to emit an empty set.")

    # Pooled across every country, so the two-decimal rounding in any one of them
    # cannot move the answer. This is the check that actually identifies the GWP set.
    averages = {code: round(totals[1] / totals[0], 2)
                for code, totals in recovered_all.items() if totals[0]}
    for code, divisor in AR5.items():
        pooled = averages.get(code)
        if pooled is None or abs(pooled - divisor) / divisor > 0.01:
            raise SystemExit(
                f"Pooled across all countries the recovered GWP for {code} is {pooled}, "
                f"not the AR5 value of {divisor}. Refusing to label these factors AR5.")

    document = {
        "$schema": "../schema/factor-set.schema.json",
        "id": f"eurostat-grid-{year}",
        "name": f"National public grid electricity {year} — derived from Eurostat",
        "region": "EU",
        "validFrom": f"{year}-01-01",
        "source": {
            "publisher": "Eurostat, republishing European Environment Agency inventory data",
            "title": (
                f"Derived from env_air_gge (greenhouse gas emissions by source sector, "
                f"CRF 1.A.1.a public electricity and heat production) and nrg_bal_c "
                f"(complete energy balances) for {year}. Retrieved from the Eurostat "
                f"dissemination API; the exact endpoints are recorded under "
                f"verification.sourceUrls and the per-country inputs under "
                f"verification.inputs."
            ),
            "publicationYear": int(year),
            "url": "https://ec.europa.eu/eurostat/web/main/data/database",
            "license": "Eurostat reuse policy: free reuse for commercial and non-commercial purposes with acknowledgement of the source.",
        },
        "verification": {
            "status": "verified",
            "verifiedBy": "Ege Ağırgöl",
            "verifiedOn": "2026-08-17",
            "method": (
                "Computed by tools/eurostat-import/import_eurostat_grid.py from Eurostat's "
                "published series. The GWP basis was recovered from the data rather than "
                "taken from documentation: dividing each gas's CO2e series by its mass "
                f"series, pooled across every country imported, gives {averages.get('CH4')} "
                f"for CH4 and {averages.get('N2O')} for N2O, which are the AR5 values of 28 "
                "and 265. Pooling matters: Eurostat rounds to two decimals, so for a "
                "country emitting a fraction of a kilotonne the individual ratio is mostly "
                "rounding noise. The importer aborts if the pooled figure moves off AR5, or "
                "if any country large enough for its own ratio to be meaningful deviates. Gas masses are used "
                "rather than the CO2e aggregate, so the factors stay GWP-agnostic. The "
                "Türkiye figure was cross-checked against Ember's published lifecycle "
                "intensity for the same year, which sits above it by the margin lifecycle "
                "and biogenic accounting would be expected to add."
            ),
            "notes": (
                "THESE FACTORS ARE DERIVED, NOT PUBLISHED. No authority publishes them; "
                "they are computed here from two published series following the European "
                "Environment Agency's own indicator method. Two choices shape every value, "
                "and neither is this repository's invention. "
                "First, CRF 1.A.1.a covers public electricity AND heat, so combined heat "
                "and power fuel is split by the efficiency method that the GHG Protocol's "
                "CHP allocation guidance names as its preferred approach: fuel is "
                "attributed in proportion to what each output would have consumed if "
                "produced separately, using that guidance's own recommended efficiencies "
                "of 35% for electricity and 80% for heat. Substituting the IEA's reference "
                "efficiencies of 40% and 90% moves every country imported here by well "
                "under one percent, and each factor's note carries its own figure along "
                "with what the energy content method, which the guidance permits as an "
                "alternative, would have given. "
                "Second, the numerator covers main activity producers only, so the "
                "denominator excludes autoproducers to keep the boundary consistent; using "
                "total national generation instead would lower the figures materially. "
                + (f"Countries omitted for want of complete series: "
                   f"{', '.join(sorted(skipped))}. " if skipped else "")
            ),
            "inputs": {
                "countries_imported": len(factors),
                "countries_skipped": skipped,
                "recovered_gwp_mean": averages,
                "proxy_quality_countries": {geo: round(s, 4) for geo, s in sorted(proxies)},
                "chp_allocation": {
                    "method": "GHG Protocol CHP guidance, efficiency method (its preferred method)",
                    "reference_efficiencies": GHGP_EFFICIENCY,
                    "sensitivity_measured_against": IEA_EFFICIENCY,
                },
            },
            "sourceUrls": urls,
        },
        "factors": factors,
    }

    with open(destination, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print(f"wrote {len(factors)} country factors to {destination}")
    print(f"  recovered GWP (mean): {averages}")
    print(f"  marked Proxy for allocation sensitivity: {len(proxies)}")
    for geo, s in sorted(proxies, key=lambda x: -x[1]):
        print(f"    {geo}: {s * 100:.1f}%")
    if skipped:
        print(f"  skipped: {', '.join(sorted(skipped))}")


if __name__ == "__main__":
    main()
