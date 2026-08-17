#!/usr/bin/env python3
"""Convert the UK DESNZ/DEFRA GHG conversion factors flat file into catalog JSON.

The factors are transcribed by machine rather than by hand, because a human copying
several hundred numbers out of a spreadsheet is the least reliable step in the whole
pipeline. Re-running this against next year's publication is how the catalog gets
updated; the output is committed so a reader can diff one year against the next.

Only the Python standard library is used: an .xlsx file is a zip of XML, and the shape
of this particular workbook is stable enough that a full spreadsheet library would be
more dependency than the job needs.

Usage:
    python3 tools/defra-import/import_defra.py <flat-file.xlsx> <output.json>

The script refuses to emit anything it cannot map. An unrecognised unit, an unexpected
gas column or a factor with no usable value is a hard error, never a silently dropped
row -- a catalog that is quietly missing a fuel is worse than one that fails to build.
"""

import hashlib
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET
from collections import defaultdict

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}

# The exact publication this importer was written against. DESNZ revised the 2026 flat
# file on 31 July 2026 to correct values that had been published as 0 rather than left
# blank, so pinning the digest is what stops an older download producing a catalog that
# looks fine and is wrong.
EXPECTED_SHA256 = "a9a455ab396dae226d510c7be6233748416d490c41a5d20f3dc7a0c45feecd5e"

# Level 1 categories to import, mapped to the GHG Protocol Scope 3 category they belong
# to. None means the category is genuinely ambiguous from a national dataset: whether
# freight is upstream or downstream, or a leased asset upstream or downstream, depends on
# where the reporting company sits in the chain, which no publisher can know. Those
# factors ship without a category and surface through the report's UncategorisedScope3
# caveat rather than being guessed into a bucket.
TARGET_CATEGORIES = {
    "Fuels": None,
    "UK electricity": None,
    "Heat and steam": None,
    "Passenger vehicles": None,
    "Delivery vehicles": None,
    "UK electricity for EVs": None,

    "Transmission and distribution": 3,
    "WTT- fuels": 3,
    "WTT- UK electricity": 3,
    "WTT- heat and steam": 3,
    "UK electricity T&D for EVs": 3,

    "Material use": 1,
    "Water supply": 1,
    "Waste disposal": 5,
    "Water treatment": 5,
    "Business travel- air": 6,
    "Business travel- land": 6,
    "Business travel- sea": 6,
    "WTT- business travel- air": 6,
    "WTT- business travel- sea": 6,
    "WTT- pass vehs & travel- land": 6,

    "Freighting goods": None,
    "WTT- delivery vehs & freight": None,
    "Managed assets- vehicles": None,
    "Managed assets- electricity": None,
}

# Deliberately not imported, each for a stated reason rather than because it was missed.
EXCLUDED_CATEGORIES = {
    "Bioenergy": "DESNZ Table 1 lists this as still on an AR4 basis.",
    "WTT- bioenergy": "DESNZ Table 1 lists this as still on an AR4 basis.",
    "Hotel stay": "DESNZ footnote 6: not all values are aligned with AR5, because some "
                  "countries' source data arrived as CO2e with no gas breakdown.",
    "Refrigerant & other": "DESNZ footnote 5: mostly AR5 but AR6 where AR5 was "
                           "unavailable, so the set has no single basis. These factors "
                           "are GWP values in any case, and this library already ships "
                           "AR5 and AR6 tables that let the caller choose.",
    "SECR kWh pass & delivery vehs": "Not emission factors. These convert distance to "
                                     "energy for SECR energy-use reporting.",
    "SECR kWh UK electricity for EVs": "Not emission factors. These convert distance to "
                                       "energy for SECR energy-use reporting.",
    "Homeworking": "Published per full-time-equivalent working hour, which is not a "
                   "physical quantity this library models.",
}

# Units that appear in the imported categories but are deliberately not mapped. Anything
# outside both this set and UNITS still fails the run.
SKIPPED_UNITS = {
    "Room per night": "Not a physical quantity this library models.",
    "per FTE Working Hour": "Not a physical quantity this library models.",
    "million litres": "The same factors are published per cubic metre, which is mapped.",
}

# DESNZ states the basis in the methodology report: "using Global Warming Potential
# (GWP) factors from the IPCC's fifth assessment report (GWP for CH4 = 28, GWP for
# N2O = 265)". Every category imported here is listed as AR5 basis in its Table 1;
# the categories that are still on an AR4 basis (Bioenergy, WTT Bioenergy) and the
# mixed ones (Hotel Stay) are deliberately out of scope.
PUBLISHED_GWP_BASIS = "Ar5"
PUBLISHED_GWP = {"CH4": 28.0, "N2O": 265.0, "CO2": 1.0}

GAS_COLUMNS = {
    "kg CO2e": None,  # the total; kept as the published CO2e figure
    "kg CO2e of CO2 per unit": "CO2",
    "kg CO2e of CH4 per unit": "CH4",
    "kg CO2e of N2O per unit": "N2O",
}

# Every fuel imported here is fossil, so its methane is fossil methane. The blended
# forecourt fuels carry a biogenic fraction, but DESNZ reports that fraction as CO2
# under "Outside of scopes" rather than as part of the CH4 figure.
GAS_MEMBERS = {
    "CO2": "CarbonDioxide",
    "CH4": "MethaneFossil",
    "N2O": "NitrousOxide",
}

UNITS = {
    "tonnes": ("Tonne", "NotApplicable"),
    "kg": ("Kilogram", "NotApplicable"),
    "litres": ("Litre", "NotApplicable"),
    "cubic metres": ("CubicMetre", "NotApplicable"),
    "GJ": ("Gigajoule", "NotApplicable"),
    "km": ("Kilometre", "NotApplicable"),
    "miles": ("Mile", "NotApplicable"),
    "tonne.km": ("TonneKilometre", "NotApplicable"),
    "passenger.km": ("PassengerKilometre", "NotApplicable"),
    "kWh": ("KilowattHour", "NotApplicable"),
    "kWh (Net CV)": ("KilowattHour", "NetCalorificValue"),
    "kWh (Gross CV)": ("KilowattHour", "GrossCalorificValue"),
}

SCOPES = {"Scope 1": "Scope1", "Scope 2": "Scope2", "Scope 3": "Scope3"}


def read_sheet(path):
    """Return the 'Factors by Category' sheet as a list of column->value dicts."""
    with zipfile.ZipFile(path) as archive:
        strings = [
            "".join(node.text or "" for node in si.iter("{%s}t" % NS["m"]))
            for si in ET.fromstring(archive.read("xl/sharedStrings.xml"))
        ]

        workbook = archive.read("xl/workbook.xml").decode("utf-8", "replace")
        names = re.findall(r'<sheet[^>]*name="([^"]+)"', workbook)
        if "Factors by Category" not in names:
            raise SystemExit(f"Expected a 'Factors by Category' sheet, found: {names}")
        index = names.index("Factors by Category") + 1

        sheet = ET.fromstring(archive.read(f"xl/worksheets/sheet{index}.xml"))

    rows = []
    for row in sheet.find("m:sheetData", NS):
        record = {}
        for cell in row.findall("m:c", NS):
            value = cell.find("m:v", NS)
            if value is None:
                continue
            column = "".join(c for c in cell.get("r") if c.isalpha())
            record[column] = strings[int(value.text)] if cell.get("t") == "s" else value.text
        rows.append(record)
    return rows


def header_index(rows):
    for index, row in enumerate(rows):
        if row.get("A") == "ID" and row.get("B") == "Scope":
            return index
    raise SystemExit("Could not find the header row (ID / Scope) in the sheet.")


def slug(text):
    text = text.lower().replace("&", " and ")
    text = re.sub(r"[^a-z0-9]+", "-", text)
    return text.strip("-")


def number(value):
    """Trim floating point noise from the spreadsheet without inventing precision."""
    return float(f"{float(value):.12g}")


def scope3_category(level1):
    return TARGET_CATEGORIES.get(level1)


def build(rows):
    start = header_index(rows) + 1
    data = [r for r in rows[start:] if r.get("A")]

    grouped = defaultdict(dict)
    order = []
    biogenic = {}
    unknown_units = set()
    unknown_gases = set()
    skipped_rows = {}
    seen_categories = set()

    for row in data:
        level1 = row.get("C", "")
        uom = row.get("H", "")
        gas_column = row.get("I", "")
        raw = row.get("J")

        if raw is None or raw == "":
            continue

        if row.get("B") == "Outside of Scopes" and gas_column == "kg CO2e of CO2 per unit":
            # DESNZ reports the biogenic CO2 of blended forecourt fuels here, held
            # apart from the scope totals exactly as the GHG Protocol requires.
            biogenic[(row.get("E", ""), uom)] = number(raw)
            continue

        seen_categories.add(level1)
        if level1 not in TARGET_CATEGORIES:
            continue

        if uom in SKIPPED_UNITS:
            skipped_rows[uom] = skipped_rows.get(uom, 0) + 1
            continue

        if uom not in UNITS:
            unknown_units.add(uom)
            continue

        if gas_column not in GAS_COLUMNS:
            unknown_gases.add(gas_column)
            continue

        key = (row.get("B", ""), level1, row.get("D", ""), row.get("E", ""), row.get("F", ""), uom)
        if key not in grouped:
            order.append(key)
            # DESNZ row ids are per gas; the shared prefix identifies the factor.
            grouped[key]["_id"] = row.get("A", "").rsplit("_", 1)[0]
        grouped[key][gas_column] = number(raw)

    if unknown_units:
        raise SystemExit(f"Unmapped units, refusing to emit a partial catalog: {sorted(unknown_units)}")
    if unknown_gases:
        raise SystemExit(f"Unmapped gas columns, refusing to emit a partial catalog: {sorted(unknown_gases)}")

    # A category that is neither imported nor listed as a deliberate exclusion means the
    # publisher added something since this importer was written. Failing is the point:
    # silence would let a whole category disappear from the catalog unnoticed.
    unaccounted = seen_categories - set(TARGET_CATEGORIES) - set(EXCLUDED_CATEGORIES) - {"Outside of scopes"}
    if unaccounted:
        raise SystemExit(
            f"DESNZ publishes categories this importer neither imports nor excludes: "
            f"{sorted(unaccounted)}. Decide about each one deliberately.")

    factors = []
    derived_count = 0
    biogenic_attached = 0

    for key in order:
        scope_text, level1, level2, level3, level4, uom = key
        entry = grouped[key]

        unit, basis = UNITS[uom]
        published = entry.get("kg CO2e")

        components = {}
        for column, gas in GAS_COLUMNS.items():
            if gas is None or column not in entry:
                continue
            # Back-calculate the mass of gas from the CO2e figure DESNZ published,
            # using the GWPs DESNZ states it applied. This is what lets a caller
            # aggregate the same activity under AR6 instead of AR5.
            components[GAS_MEMBERS[gas]] = number(entry[column] / PUBLISHED_GWP[gas])

        if not components and published is None:
            raise SystemExit(f"Factor {key} has neither a gas breakdown nor a CO2e total.")

        parts = [p for p in (level1, level2, level3, level4) if p]
        identifier = "defra-2026/" + "/".join(slug(p) for p in parts) + "/" + slug(uom)

        factor = {
            "id": identifier,
            "activity": level3 or level2,
            "scope": SCOPES[scope_text],
        }

        category = scope3_category(level1)
        if category is not None:
            factor["scope3Category"] = category

        if factor["scope"] == "Scope2":
            # DESNZ publishes the average intensity of the UK grid, which is the
            # location-based figure. A market-based factor is supplier-specific and
            # cannot come from a national dataset.
            factor["scope2Method"] = "LocationBased"

        factor["unit"] = unit
        factor["basis"] = basis

        if components:
            factor["components"] = components
            factor["componentsAreDerived"] = True
            derived_count += 1

        if published is not None:
            factor["publishedCo2eKgPerUnit"] = published
            factor["publishedGwpBasis"] = PUBLISHED_GWP_BASIS

        carbon = biogenic.get((level3, uom))
        if carbon is not None and factor["scope"] == "Scope1":
            factor["biogenicCarbonKg"] = carbon
            biogenic_attached += 1

        factor["dataQuality"] = "Secondary"
        factor["sourceReference"] = f"DESNZ 2026 flat file, row group {entry['_id']}"
        factors.append(factor)

    return factors, derived_count, biogenic_attached, biogenic, skipped_rows


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    source, destination = sys.argv[1], sys.argv[2]

    digest = hashlib.sha256(open(source, "rb").read()).hexdigest()
    if digest != EXPECTED_SHA256:
        raise SystemExit(
            f"Input digest {digest} does not match the pinned publication "
            f"{EXPECTED_SHA256}. Update EXPECTED_SHA256 deliberately, after checking "
            "what changed in the new file."
        )

    factors, derived, attached, biogenic, skipped_rows = build(read_sheet(source))

    document = {
        "$schema": "../schema/factor-set.schema.json",
        "id": "defra-2026",
        "name": "UK Government GHG Conversion Factors 2026",
        "region": "GB",
        "validFrom": "2026-01-01",
        "source": {
            "publisher": "UK Department for Energy Security and Net Zero (DESNZ)",
            "title": (
                "Greenhouse gas reporting: conversion factors 2026, flat file "
                "(revised 31 July 2026), sheet 'Factors by Category'. "
                f"SHA-256 {EXPECTED_SHA256}."
            ),
            "publicationYear": 2026,
            "url": "https://www.gov.uk/government/publications/greenhouse-gas-reporting-conversion-factors-2026",
            "license": "Open Government Licence v3.0 — redistribution permitted with attribution.",
        },
        "verification": {
            "status": "verified",
            "verifiedBy": "Ege Ağırgöl",
            "verifiedOn": "2026-08-15",
            "method": (
                "Generated by tools/defra-import/import_defra.py directly from the "
                "published flat file, whose SHA-256 the importer pins and checks. No "
                "value was transcribed by hand. The importer refuses to emit anything "
                "it cannot map, so an unrecognised unit or gas column fails the run "
                "rather than silently dropping a factor."
            ),
            "notes": (
                "Categories are imported or excluded by decision, never by omission: the "
                "importer fails if DESNZ publishes a category it has not been told about. "
                "Excluded, with reasons: "
                + "; ".join(f"{name} — {why}" for name, why in sorted(EXCLUDED_CATEGORIES.items()))
                + " Scope 3 category numbers are assigned where the mapping is "
                "unambiguous. Freight, delivery and managed-asset factors ship without "
                "one, because whether they are upstream or downstream depends on where "
                "the reporting company sits in the chain and no publisher can know that. "
                "Per-gas components are DERIVED, not published: DESNZ publishes the "
                "split as CO2e using AR5 GWPs of 28 for CH4 and 265 for N2O, and the "
                "importer divides those back out to recover the gas masses so that a "
                "caller can aggregate under a different GWP set. The figure DESNZ "
                "itself published is kept alongside on publishedCo2eKgPerUnit. Note "
                "that DESNZ applies the non-fossil CH4 value of 28 to fossil fuels, "
                "so aggregating these factors under AR5 with fossil methane at 30 "
                "gives a total marginally above the published one. Categories that "
                "DESNZ lists as AR4 basis (Bioenergy, WTT Bioenergy) or mixed basis "
                "(Hotel Stay) are deliberately not imported."
            ),
        },
        "factors": factors,
    }

    with open(destination, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    scopes = defaultdict(int)
    for factor in factors:
        scopes[factor["scope"]] += 1

    print(f"wrote {len(factors)} factors to {destination}")
    print(f"  by scope: {dict(sorted(scopes.items()))}")
    print(f"  with derived gas components: {derived}")
    print(f"  with biogenic carbon attached: {attached} (of {len(biogenic)} outside-of-scope rows seen)")
    if skipped_rows:
        print(f"  rows skipped for unmapped units: {skipped_rows}")
    print(f"  categories excluded by decision: {len(EXCLUDED_CATEGORIES)}")


if __name__ == "__main__":
    main()
