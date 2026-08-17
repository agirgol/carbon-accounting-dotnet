# GhgAccounting

[![CI](https://github.com/agirgol/carbon-accounting-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/agirgol/carbon-accounting-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Greenhouse gas accounting for .NET, built to the GHG Protocol Corporate Standard and
ISO 14064-1. Scope 1/2/3 calculation, a versioned and source-cited emission factor
catalog, unit conversion, explicit AR5/AR6 GWP set selection, and data-quality scoring.

**No runtime dependencies.** The factor catalog is compiled into the assembly at build
time, so consuming this library adds exactly one package to your graph and nothing else.

> ### Status: pre-release (0.1.x)
>
> The AR5 and AR6 GWP sets are verified value by value against the IPCC tables they cite.
> Three factor sets ship, none of them transcribed by hand: 234 UK DESNZ 2026 factors
> covering Scope 1 fuels, Scope 2 electricity and the Scope 3 category 3 counterparts of
> both; 27 US EPA eGRID subregion grid factors; and 11 national grid factors derived
> from Eurostat, with the method and every input recorded in the set. Coverage elsewhere
> is still thin and Scope 3 beyond category 3 is not covered at all. Every set exposes
> its own `VerificationStatus` at run time, and
> `dotnet pack -p:GhgRequireVerifiedCatalog=true` refuses to build a package
> containing unverified data. See [Catalog data](#catalog-data).

## Why this exists

Carbon accounting is not arithmetic on a spreadsheet. Two teams can start from the same
metering data and publish totals that differ by 10% or more, entirely legitimately,
because they made different disclosed choices: a different IPCC assessment report, a
different calorific basis, location-based instead of market-based electricity. Most
tooling hides those choices behind a single `getCo2e()` call.

This library makes each of them an explicit, typed, documented decision — and refuses to
guess when it cannot know.

## Install

```
dotnet add package GhgAccounting
```

Targets `netstandard2.0`, `net8.0` and `net10.0`. The netstandard target keeps .NET
Framework 4.6.1+ consumers in scope, which matters because a large share of the ERP and
finance systems that need an inventory still run there.

## Quick start

Build an inventory:

```csharp
using System;
using GhgAccounting;
using GhgAccounting.Calculation;
using GhgAccounting.Units;

var calculator = new EmissionCalculator(GwpSet.Ar6);

Inventory inventory = calculator.CreateInventory()
    .Add(new Quantity(250_000, Unit.KilowattHour),
         "defra-2026/fuels/gaseous-fuels/natural-gas/kwh-gross-cv")               // Scope 1
    .Add(new Quantity(400_000, Unit.KilowattHour),
         "defra-2026/uk-electricity/electricity-generated/electricity-uk/kwh/kwh") // Scope 2
    .Add(new Quantity(250_000, Unit.KilowattHour),
         "defra-2026/wtt-fuels/gaseous-fuels/natural-gas/kwh-gross-cv")            // Scope 3 cat 3
    .Build();

// There is no inventory.Total. A corporate inventory does not have one.
Quantity locationBased = inventory.TotalWith(Scope2Method.LocationBased).ConvertTo(Unit.Tonne);

Quantity biogenic = inventory.BiogenicCarbon;          // disclosed, never inside a total
double? spread    = inventory.UncertaintyPercentFor(Scope2Method.LocationBased);

foreach (Scope3CategoryTotal category in inventory.Scope3ByCategory)
{
    Console.WriteLine($"Category {category.Category}: {category.Co2e}");
}
```

Asking for `Scope2Method.MarketBased` here throws `Scope2MethodNotReportedException`,
and should: a national dataset can only publish the grid average, so a market-based
figure has to come from the company's own contracts. Returning zero would silently drop
the whole of purchased electricity from the total, and nothing downstream would show it.

Choosing the GWP set:

```csharp
using GhgAccounting;
using GhgAccounting.Factors;
using GhgAccounting.Units;

// The GWP set is always a caller decision, never a library default.
// One tonne of fugitive fossil methane from a gas network:
var leak = new Quantity(1.0, Unit.Tonne);

Quantity underAr5 = GwpTable.For(GwpSet.Ar5).ToCo2e(leak, GreenhouseGas.MethaneFossil);
Quantity underAr6 = GwpTable.For(GwpSet.Ar6).ToCo2e(leak, GreenhouseGas.MethaneFossil);

// Same activity data, two defensible answers. Which one you publish is a
// disclosure decision, and the standard requires you to state which set you used.
Console.WriteLine($"{underAr5.Value} vs {underAr6.Value} tCO2e");
```

Every factor carries its provenance, so a report can print the citation next to the
number:

```csharp
EmissionFactor factor = FactorCatalog.Get("defra-2026/fuels/gaseous-fuels/natural-gas/kwh-gross-cv");

Console.WriteLine(factor.Set.Source);        // publisher, document, year
Console.WriteLine(factor.Set.Region);        // where the factor is valid
Console.WriteLine(factor.Basis);             // gross or net calorific value
Console.WriteLine(factor.DataQuality);       // Primary / Secondary / Proxy / Estimated
Console.WriteLine(factor.Set.Verification);  // has anyone checked these numbers?
```

Conversions that need a substance property are refused rather than guessed:

```csharp
UnitConverter.Convert(1000, Unit.CubicMetre, Unit.Litre);        // 1_000_000
UnitConverter.Convert(1000, Unit.CubicMetre, Unit.KilowattHour); // throws UnitConversionException
```

Cubic metres of gas to kilowatt hours depends on the calorific value of the gas actually
delivered, which varies by supplier, network and season. That is a factor, not a unit
ratio, and silently applying an average is how a gas inventory ends up wrong by an order
of magnitude with nothing downstream to flag it.

## Design decisions

**Factors are stored per gas where a split exists, and as published CO₂e where it does
not.** A catalog that ships only CO₂e has already baked in an assessment report, so the
per-gas form is preferred — it keeps the GWP set a caller decision. But most real
datasets publish only aggregates for value-chain categories, and there is no split behind
them to recover. Those factors carry a `PublishedGwpBasis` and refuse to be used under
any other set, because re-aggregating an aggregate means inventing the split.

Where a publisher gives the split as CO₂e rather than as gas masses, the masses are
divided back out and the factor is marked `ComponentsAreDerived`. The publisher's own
figure is kept alongside on `PublishedCo2eKgPerUnit`, so a filing that has to reproduce
the published total exactly still can. Both numbers appear on the result:

```csharp
EmissionResult r = calculator.Calculate(activity, factor);
r.Co2e;           // recomputed under the caller's GWP set
r.PublishedCo2e;  // what the publisher would have reported
```

They differ slightly for DESNZ fuels, because DESNZ applies the non-fossil methane
potential of 28 to fossil fuels while AR5 publishes 30 for them. Roughly 0.01% — small,
real, and disclosed rather than reconciled away.

**A derived factor says so, shows its working, and knows when to stop.** Some countries
have no published grid factor at all. One can be computed from published statistics —
and then the set records the method, every input value, and the endpoint each came from,
so a reader can redo the arithmetic without trusting this repository.

But a derivation should also measure how much of the answer is its own convention rather
than the data. National inventories report public electricity *and heat* together, so the
heat has to be allocated out. The importer computes what that choice is worth per country:
in Türkiye it moves the result by 0.8%, in Lithuania by 201%. Where it exceeds 5% the
factor is marked `Proxy`; where it exceeds 10% no factor is published at all, and the
country is listed in the set with its measured sensitivity. Twenty countries with large
district heating networks are excluded on that test. A number governed more by an
accounting convention than by the underlying statistics is not made trustworthy by having
a citation attached.

**Fossil and biogenic methane are different gases.** AR6 gives them 29.8 and 27.0; AR5
gives 30 and 28. A single `Methane` member would force the library to pick one silently.

**Missing data throws instead of returning zero.** A gas quietly valued at zero drops out
of the total without leaving a trace in the report. `GetGwp` raises
`GasNotCoveredException`; `TryGetGwp` exists for callers that want to handle it.

**There is no `Inventory.Total`.** A corporate inventory has a location-based total and a
market-based total, and which one a company leads with is a disclosure decision.
`TotalWith(Scope2Method)` forces that choice to be made at the call site, and the two
figures can never be accidentally summed.

**Uncertainty is null unless every contributing factor declares one.** Combining only the
lines that happen to publish an uncertainty understates the real spread, and nothing in
the output would reveal it. Where all lines do declare one, they are combined in
quadrature weighted by contribution, following the IPCC error propagation approach for
sums.

**The catalog is JSON in the repository and C# in the assembly.** JSON is what makes a
factor change reviewable — a pull request shows the old value, the new value and the
citation side by side. Compiled C# is what makes it cheap at run time: static arrays, no
parser, no embedded resource, no start-up cost, no dependency. A source generator does
the translation, and maps every enum-valued field by *name*, so a typo in the data is a
compiler error rather than a wrong number.

**Verification status is part of the public API.** A compliance report generator must be
able to refuse data nobody has checked. Status is readable at run time, surfaced as a
build warning (`GHG006`), and turned into a hard error (`GHG005`) when packing for
release.

## Catalog data

Every shipped set records its publisher, the exact document, the publication year, the
redistribution licence, and whether the values have been checked against that source.

| Set | Source | Year | Licence | Status |
|---|---|---|---|---|
| `Ar5` | IPCC AR5 WG1 Ch.8, Appendix 8.A, Table 8.A.1 | 2013 | Factual constants, reproduced with attribution | ✅ `verified` |
| `Ar6` | IPCC AR6 WG1 Ch.7, Table 7.15 and Supplementary Table 7.SM.7 | 2021 | Factual constants, reproduced with attribution | ✅ `verified` |
| `defra-2026-secr` | UK DESNZ conversion factors 2026, flat file (revised 31 July 2026) | 2026 | Open Government Licence v3.0 | ✅ `verified` |
| `egrid-2023` | US EPA eGRID2023 Rev. 2, subregion annual total output rates | 2025 | US Government work, public domain | ✅ `verified` |
| `eurostat-grid-2023` | **Derived**: Eurostat `env_air_gge` CRF 1.A.1.a ÷ `nrg_bal_c`, 11 countries | 2023 | Eurostat reuse policy, with acknowledgement | ✅ `verified` |
| `example-fuels`, `example-value-chain` | None — synthetic values authored for this repository | — | MIT, same as the code | 🚫 `placeholder` |

The DESNZ and eGRID sets are **generated, not transcribed**. `tools/defra-import/import_defra.py`
reads the published spreadsheet, pins its SHA-256 so an older download cannot quietly
produce a different catalog, and refuses to emit anything it cannot map — an
unrecognised unit fails the run rather than dropping a fuel. Re-running it against next
year's publication is how the set gets updated, and because the output is committed, one
year diffs cleanly against the next.

`verified` means every value was checked against the cited table by a named reviewer on a
recorded date; the method is written into each file. `placeholder` means the numbers are
invented and exist only so the pipeline has something to compile, and they are **never**
valid for reporting.

Synthetic sets live under `data/examples/` rather than `data/factors/`, so the
distinction is visible in the directory tree and not only in a status field. They are
excluded from the build entirely when packing for release — invented numbers can never
reach `verified`, so shipping them is not something the gate should have to catch.

### What verification actually caught

The AR6 set originally cited Table 7.SM.7 for all its values. It shouldn't have: that
table publishes a **single** methane GWP of 27.9 with no fossil / non-fossil split,
because it deliberately excludes the carbon content of the methane so that users can do
their own carbon budgeting. The 29.8 and 27.0 values this library ships come from
**Table 7.15**, the headline metrics table.

That distinction also flipped a flag. AR5 Table 8.A.1 states that climate-carbon
feedbacks are included for CO<sub>2</sub> only, so `Ar5.IncludesClimateCarbonFeedback` is
`false`. AR6 changed approach and includes carbon cycle responses in its headline
metrics, so `Ar6.IncludesClimateCarbonFeedback` is `true`. Every number was right; the
description of what those numbers *were* was not.

Because the AR6 set draws on two tables, each value records its own `SourceTable` — a
set-level citation would have misstated where half the numbers came from.

### Planned sources

Datasets are only added once their redistribution terms are confirmed to allow it:

| Publisher | Coverage | Terms |
|---|---|---|
| UK DESNZ, remaining categories | Transport, waste, water, material use | Open Government Licence v3 |
| US EPA eGRID, grid loss | US transmission and distribution, Scope 3 cat. 3 | US public domain |
| European Environment Agency | European grid intensity | EEA reuse policy |
| National inventories | Türkiye and other non-EU grids | Varies; checked per source |

IEA emission factor data is deliberately **not** on this list. It is a commercially
licensed product, and embedding its values in a redistributable package is not something
an MIT licence can cover.

## Standards coverage

| Requirement | Standard | Where it appears in the API |
|---|---|---|
| Emissions classified into Scope 1, 2 and 3 | GHG Protocol Corporate Standard, operational boundaries | `Scope` |
| Scope 2 reported by both location-based and market-based methods | GHG Protocol Scope 2 Guidance (2015) | `Scope2Method`, dual factors per grid |
| Scope 3 split across the fifteen defined categories | GHG Protocol Scope 3 Standard (2011) | `EmissionFactor.Scope3Category` |
| CO₂e aggregated using a disclosed set of 100-year GWPs | IPCC AR5 / AR6 via GHG Protocol | `GwpSet`, `GwpTable` |
| Biogenic CO₂ reported separately from the scope totals | GHG Protocol; ISO 14064-1 | `EmissionFactor.BiogenicCarbonKg` |
| Data quality distinguished between primary and secondary sources | ISO 14064-1 inventory quality management | `DataQuality` |
| Uncertainty recorded per factor | ISO 14064-1 uncertainty assessment | `EmissionFactor.UncertaintyPercent` |
| Every reported figure traceable to its factor source | ISO 14064-1 reporting and verification | `CatalogSource`, `FactorSet.Verification` |

> Clause-level citations are deliberately absent. ISO 14064-1:2018 is a paywalled
> document, and quoting sub-clause numbers from secondary sources is exactly the kind of
> unverified claim this project refuses to make elsewhere. They will be added once
> checked against a purchased copy of the standard text.

## Deliberately out of scope

Named here so nobody has to read the source to find out:

- **Organizational boundary consolidation.** Equity share versus operational control
  changes which entities are in the inventory at all. That is a corporate structure
  question, not a calculation, and it belongs above this library.
- **Scope 3 spend-based modelling (EEIO).** Input-output tables are large, national,
  annually revised datasets with their own licensing. A future separate package.
- **Product carbon footprints and LCA.** ISO 14067 and ISO 14040/44 model a product life
  cycle, not a corporate reporting year. Different standard, different data model.
- **Target setting and pathways.** SBTi validation, 1.5°C alignment and scenario
  analysis operate on a completed inventory. This library produces the input.
- **Carbon credits, offsets and removals.** Distinct accounting rules; conflating them
  with gross emissions is a reporting error, so the type system will not allow it.
- **CBAM and CSRD report rendering.** Regulatory output formats change on their own
  schedule and do not belong in a calculation engine.
- **Currency, spend and financial data.** No monetary units, by design.
- **Lifecycle grid intensity datasets.** Several widely used open datasets publish a
  national "CO₂ intensity of electricity" that is not a Scope 2 location-based factor.
  Ember's, for instance, attributes 12.8 gCO₂/kWh to wind, 47.6 to solar and 4.9 to
  nuclear — technologies with no combustion at all — which only makes sense as a
  lifecycle figure, and it counts biogenic CO₂ from bioenergy inside the intensity where
  the GHG Protocol requires it outside the scope totals. Against DESNZ for the same grid
  and year the gap is 66%. Convenient global coverage is not worth shipping a number
  under a label it does not fit, so grid factors come from sources that publish on the
  right basis.

## Implementation status

| Area | State |
|---|---|
| GWP sets (AR5 / AR6), explicit selection, CO₂e conversion | ✅ |
| Unit and dimension layer, cross-dimension refusal | ✅ |
| Factor catalog model, provenance, verification gate | ✅ |
| Compile-time catalog generator with build diagnostics | ✅ |
| Scope 1/2/3 calculation engine and inventory aggregation | ✅ |
| Scope 2 dual-reporting result type | ✅ |
| Biogenic carbon reported outside the scope totals | ✅ |
| Uncertainty propagation and data-quality breakdown | ✅ |
| AR5 and AR6 GWP sets verified against the IPCC tables | ✅ |
| UK DESNZ 2026 SECR core: 234 factors, machine-generated from the source file | ✅ |
| US EPA eGRID 2023: 27 subregion grid factors, published per gas | ✅ |
| 11 national grid factors derived from Eurostat, method and inputs disclosed | ✅ |
| Grid factors for district-heating countries, which need a defensible CHP convention | 🚧 next |
| Remaining DESNZ categories: transport, waste, water, material use | 🚧 planned |

## Repository layout

```
data/
  gwp/            GWP sets, one file per IPCC assessment report
  factors/        Emission factor sets, one file per publisher-year
  examples/       Synthetic sets for tests. Excluded from release builds.
  schema/         JSON Schema for both catalog shapes
src/
  GhgAccounting/            The shipping library. Zero PackageReference entries.
  GhgAccounting.Generators/ Build-time source generator. Never shipped.
tests/
  GhgAccounting.Tests/      Runs against net8.0 and net10.0
tools/
  defra-import/                Turns the published DESNZ spreadsheet into catalog JSON
  egrid-import/                Turns the published EPA eGRID workbook into catalog JSON
  eurostat-import/             Derives a national grid factor from Eurostat series
```

## Build and test

Requires the .NET 10 SDK.

```
dotnet build GhgAccounting.slnx
dotnet test  GhgAccounting.slnx
```

The `net8.0` test leg rolls forward onto the .NET 10 runtime locally, so a fresh clone is
green with a single SDK installed. CI installs the real 8.0 runtime so that leg executes
on .NET 8 for real.

To check the release gate:

```
dotnet pack src/GhgAccounting/GhgAccounting.csproj -c Release -p:GhgRequireVerifiedCatalog=true
```

This now succeeds: both GWP sets are verified, and the synthetic factor sets are dropped
from the build rather than shipped. Adding an unverified set to `data/gwp/` or
`data/factors/` makes it fail again.

## Contributing catalog data

1. Add or edit a JSON file under `data/`, matching the schema in `data/schema/`.
2. Record the exact source: publisher, document *and table*, publication year, URL,
   redistribution licence. A set with no clear licence will not be merged.
3. Leave `verification.status` at `needs-review` until every value has been checked
   against the primary source, then set it to `verified` with `verifiedBy` and
   `verifiedOn` filled in.
4. Never change a factor's value under its existing `id`. Publish a new `id`, so a
   restated inventory is distinguishable from an unchanged one.

## Licence

MIT for the code — see [LICENSE](LICENSE).

Emission factor and GWP data is **not** covered by that licence. It is reproduced from
third-party publications and stays subject to its publishers' terms, which
[NOTICE](NOTICE) sets out per set. The DESNZ factors contain public sector information
licensed under the Open Government Licence v3.0. Each catalog file also records its own
source and licence in its `source` block, and those records compile into the library, so
a consumer can read the provenance of any figure at run time rather than taking it on
trust.
