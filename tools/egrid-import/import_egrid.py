#!/usr/bin/env python3
"""Convert the US EPA eGRID subregion sheet into catalog JSON.

eGRID is the source US corporate reporting is pointed at for Scope 2 location-based
electricity, and it publishes what this library actually wants: separate CO2, CH4 and
N2O output rates per subregion, as gas masses rather than as CO2e. Nothing has to be
divided back out, and the caller's GWP set applies cleanly.

The xlsx reader below is deliberately duplicated from the DESNZ importer rather than
shared. Each importer stays a single file that runs on its own with no path setup, which
matters more for a script run once a year than the twenty lines it saves.

Usage:
    python3 tools/egrid-import/import_egrid.py <egrid_data_metric.xlsx> <output.json>
"""

import hashlib
import json
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

NS = {
    "m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main",
    "r": "http://schemas.openxmlformats.org/officeDocument/2006/relationships",
}

# eGRID2023 Revision 2, the metric edition. Pinned so a different revision cannot be
# imported without someone deciding to.
EXPECTED_SHA256 = "3dfbbcf2f949d58d5b2dbee3aab8150bd04a0c8ebb730ba1cd37a013bd4450ab"

DATA_YEAR = 2023
SHEET = "SRL23"

# Annual total output emission rates: emissions over all generation in the subregion,
# which is the grid average a location-based figure needs. eGRID also publishes
# combustion output rates (over combustion generation only) and non-baseload rates
# (for marginal analysis); neither is what Scope 2 asks for.
COLUMNS = {
    "acronym": "SUBRGN",
    "name": "SRNAME",
    "generation": "SRNGENAN",
    "CarbonDioxide": "SRCO2RTA",
    "MethaneFossil": "SRCH4RTA",
    "NitrousOxide": "SRN2ORTA",
}


def read_sheet(path, sheet_name):
    with zipfile.ZipFile(path) as archive:
        strings = [
            "".join(node.text or "" for node in si.iter("{%s}t" % NS["m"]))
            for si in ET.fromstring(archive.read("xl/sharedStrings.xml"))
        ]

        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        rels = {
            rel.get("Id"): rel.get("Target")
            for rel in ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        }

        target = None
        for sheet in workbook.find("m:sheets", NS):
            if sheet.get("name") == sheet_name:
                target = rels[sheet.get("{%s}id" % NS["r"])]
        if target is None:
            raise SystemExit(f"Sheet {sheet_name} not found in {path}")

        sheet_xml = ET.fromstring(archive.read("xl/" + target.lstrip("/")))

    rows = []
    for row in sheet_xml.find("m:sheetData", NS):
        record = {}
        for cell in row.findall("m:c", NS):
            value = cell.find("m:v", NS)
            if value is None:
                continue
            column = "".join(c for c in cell.get("r") if c.isalpha())
            record[column] = strings[int(value.text)] if cell.get("t") == "s" else value.text
        rows.append(record)
    return rows


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def number(value):
    return float(f"{float(value):.12g}")


def build(rows):
    # eGRID sheets carry two header rows: a human description, then the column code.
    codes = rows[1]
    index = {code: column for column, code in codes.items() if code}

    missing = [name for name in COLUMNS.values() if name not in index]
    if missing:
        raise SystemExit(f"Expected columns absent from {SHEET}, refusing to emit: {missing}")

    factors = []
    for row in rows[2:]:
        acronym = row.get(index[COLUMNS["acronym"]])
        if not acronym:
            continue

        components = {}
        for gas in ("CarbonDioxide", "MethaneFossil", "NitrousOxide"):
            raw = row.get(index[COLUMNS[gas]])
            if raw in (None, ""):
                raise SystemExit(f"Subregion {acronym} has no {gas} rate; refusing to emit a partial catalog.")
            components[gas] = number(raw)

        name = row.get(index[COLUMNS["name"]], "").strip()

        factors.append({
            "id": f"egrid-{DATA_YEAR}/subregion/{slug(acronym)}/mwh",
            "activity": f"Purchased electricity — {name} ({acronym})",
            "scope": "Scope2",
            "scope2Method": "LocationBased",
            "unit": "MegawattHour",
            "basis": "NotApplicable",
            "components": components,
            "dataQuality": "Secondary",
            "sourceReference": f"eGRID{DATA_YEAR} sheet {SHEET}, subregion {acronym}",
            "note": (
                "Annual total output emission rate: subregion emissions over all generation "
                "in the subregion. Methane is treated as fossil, which the generation mix "
                "makes overwhelmingly true, though a small biomass-derived share is included."
            ),
        })

    return factors


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)

    source, destination = sys.argv[1], sys.argv[2]

    digest = hashlib.sha256(open(source, "rb").read()).hexdigest()
    if digest != EXPECTED_SHA256:
        raise SystemExit(
            f"Input digest {digest} does not match the pinned release {EXPECTED_SHA256}. "
            "Update EXPECTED_SHA256 deliberately, after checking what changed."
        )

    factors = build(read_sheet(source, SHEET))

    document = {
        "$schema": "../schema/factor-set.schema.json",
        "id": f"egrid-{DATA_YEAR}",
        "name": f"US EPA eGRID {DATA_YEAR} — subregion grid electricity",
        "region": "US",
        "validFrom": f"{DATA_YEAR}-01-01",
        "source": {
            "publisher": "United States Environmental Protection Agency",
            "title": (
                f"Emissions & Generation Resource Integrated Database (eGRID{DATA_YEAR}), "
                f"Revision 2, metric edition, sheet {SHEET}. Annual total output emission "
                f"rates by eGRID subregion. SHA-256 {EXPECTED_SHA256}."
            ),
            "publicationYear": 2025,
            "url": "https://www.epa.gov/egrid/download-data",
            "license": "Work of the United States Government; EPA content is in the public domain unless otherwise noted.",
        },
        "verification": {
            "status": "verified",
            "verifiedBy": "Ege Ağırgöl",
            "verifiedOn": "2026-08-17",
            "method": (
                "Generated by tools/egrid-import/import_egrid.py directly from the published "
                "workbook, whose SHA-256 the importer pins and checks. No value was "
                "transcribed by hand, and a subregion missing any of the three gas rates "
                "fails the run rather than shipping incomplete. Magnitudes were sanity "
                "checked against published grid intensities: WECC California 194, NPCC "
                "Upstate NY 110, ASCC Alaska Grid 408 kg CO2 per MWh."
            ),
            "notes": (
                "Unlike most published grid datasets, eGRID gives CO2, CH4 and N2O as "
                "separate gas masses rather than as a single CO2e figure, so these factors "
                "are GWP-agnostic and nothing had to be derived. The rates chosen are the "
                "annual TOTAL output rates, which divide subregion emissions by all "
                "generation in the subregion; eGRID's combustion output rates and "
                "non-baseload rates answer different questions and are not imported. "
                "Transmission and distribution losses are published separately by eGRID and "
                "are not yet imported, so a US inventory built on these factors covers "
                "Scope 2 but not the Scope 3 category 3 losses that accompany it."
            ),
        },
        "factors": factors,
    }

    with open(destination, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print(f"wrote {len(factors)} subregion factors to {destination}")


if __name__ == "__main__":
    main()
