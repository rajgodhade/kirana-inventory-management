# GST Tax Identity and State Foundation

Phase 18A-1 adds normalized GST identity metadata. It does not change the existing sales or
purchase GST engines.

## State model

`IndianGstStateCatalog` is the single catalog of supported Indian states and union territories.
Each entry has a stable two-character GST code and a display name. Persisted `StateCode` is the
authoritative jurisdiction identity. Display names must be obtained from the catalog; application
logic must not infer a code from free-text address or state text.

`Store.State` remains for backward compatibility. When a user explicitly saves store tax identity,
its display value is synchronized from the selected catalog entry. It is not authoritative and the
migration does not convert it.

## Registration model

`GstRegistrationType` has exactly the foundation categories currently needed:

- `Regular`
- `Composition`
- `Unregistered`

The persisted value is nullable for legacy records. A missing value means “not reviewed or
specified”; it is not silently treated as Unregistered. Walk-in/consumer behavior remains a billing
concept and is not duplicated as a registration category.

## GSTIN validation

`GstinValidator` is shared by setup, store settings, customers, and suppliers. It distinguishes:

- missing GSTIN (allowed);
- structurally invalid GSTIN;
- valid GSTIN.

Structural validation checks the 15-character uppercase format, a supported GST state-code prefix,
and the base-36 checksum. If both GSTIN and `StateCode` are supplied, their state codes must match.
The validator never derives or overwrites the selected state.

Validation applies when creating or editing a record. Reads remain side-effect free, so incomplete
legacy data stays readable and is not silently repaired.

## Entity identity

- Store: existing `Name` is the trade name; `LegalName`, `StateCode`, and
  `GstRegistrationType` are added; existing `Gstin` is reused.
- Customer: `StateCode` and `GstRegistrationType` are added; existing address and GSTIN fields are
  reused.
- Supplier: `StateCode` and `GstRegistrationType` are added; existing address and GSTIN fields are
  reused.

Store changes use the existing Settings permission. Customer and supplier changes use their
existing permissions. Identity changes produce distinguishable audit actions with previous and new
values; no new audit subsystem or permission was introduced.

## Migration and historical-data policy

The Phase 18A-1 migration is additive. All seven new columns are nullable and have no data backfill
or default. Existing free-text state and GSTIN values are preserved byte-for-byte. Ambiguous legacy
records remain unresolved until a user explicitly reviews and saves them.

The migration does not update sales, sale items, purchases, purchase items, returns, payments,
inventory, or stock movements. Historical GST snapshots are never recalculated. Real-database
application requires a SHA-256-verified backup, a copy rehearsal, integrity and foreign-key checks,
and fingerprints of every pre-existing table.

## Explicitly outside Phase 18A-1

This phase does not implement CGST/SGST/IGST resolution, place of supply, GST/ITC/return reports,
reverse charge, CESS, B2B/B2C classification, invoice redesign, or tax-component migration. A
future jurisdiction phase may consume normalized `StateCode`; it must continue to use stored
historical transaction snapshots and the existing GST calculation services.

## Historical GST Identity Snapshots

Phase 18A-2 freezes the legal/GST identity that was known when a sale is completed or a purchase
is finalized. This is an identity-only change: stored taxable values, GST amounts, totals, payment
amounts, stock, and all calculation services are unchanged.

### Captured identity

Every new completed sale stores a capture timestamp plus the store trade/legal name, GSTIN,
normalized state code and display name, registration type, invoice address components, and contact
number. When the sale has a customer, it also stores the customer's name, phone, GSTIN, state,
registration type, and address. A walk-in sale stores no invented customer identity.

Every new finalized purchase stores the same buying-store identity and the supplier's name, stable
supplier code, GSTIN, state, registration type, and address. `HistoricalGstIdentitySnapshotFactory`
is the single copy policy used by both transaction services. Capture occurs before the transaction's
existing `SaveChanges` call, so the financial record and its identity snapshot commit atomically.

### Immutable reads

There is no snapshot edit service or update path. Invoice detail, invoice print/reprint, invoice
search/export/recent views, purchase detail/print/list/export, and return views prefer the snapshot
whenever `GstIdentitySnapshotCapturedAtUtc` is present. Editing or deleting mutable master details
therefore cannot rewrite a completed document's identity. Current logo and invoice footer remain
presentation settings rather than GST/legal identity.

Sales and purchase returns keep their existing relationship to the originating `Sale` or
`Purchase`; they do not duplicate or recompute identity. Return views and sales-return receipts use
the origin's frozen identity.

### Legacy policy

All new migration columns are nullable and the migration performs no data update. Existing sales
and purchases retain NULL snapshot fields because the application cannot know their historical
identity. A NULL capture timestamp identifies such a legacy document. Legacy documents remain
readable through the pre-existing current-master fallback, but that fallback is explicitly not
historical evidence and cannot guarantee identity immutability. No GSTIN-to-state inference,
current-master backfill, or other historical guessing is performed.

### Outside Phase 18A-2

This phase does not add CGST, SGST, IGST, CESS, place-of-supply logic, B2B/B2C classification,
reverse charge, GST return filing, new GST arithmetic, or any monetary recalculation. Those remain
future phases and must consume the stored transaction identity rather than mutable masters.

## Phase 18A-3 — GST Tax Jurisdiction Resolution

Phase 18A-3 introduces one deterministic Application-layer jurisdiction resolver. It is a pure,
read-only service: it has no database dependency, performs no writes, and does not alter GST
rates, taxable values, stored tax, totals, posting, payments, stock, or return behavior.

### Authoritative evidence and decision matrix

`StateCode` values already frozen by Phase 18A-2 are the only jurisdiction authority. The resolver
never derives a state from GSTIN prefixes, state display names, free-text addresses, registration
types, or current store/customer/supplier masters.

- Sale seller: `Sale.StoreStateCodeSnapshot`
- Sale buyer: `Sale.CustomerStateCodeSnapshot`
- Purchase seller: `Purchase.SupplierStateCodeSnapshot`
- Purchase buyer: `Purchase.StoreStateCodeSnapshot`

If both codes are supported by `IndianGstStateCatalog`, equal codes resolve to `IntraState` and
different codes resolve to `InterState`. Missing or invalid evidence resolves to `Unresolved` with
a specific reason. A null `GstIdentitySnapshotCapturedAtUtc` always means `LegacyTransaction` and
prevents any fallback. A walk-in sale has no customer state evidence and therefore remains
unresolved. GST registration classification remains separate from jurisdiction.

### Historical safety and returns

Resolution happens from the transaction snapshot each time it is read. Later master edits or
deletions cannot change the answer, and no read path backfills or repairs historical records.
Sales and purchase returns continue to reference their originating `Sale` or `Purchase`; callers
resolve that origin and no return-specific identity snapshot is created. Multiple returns against
one origin therefore share the same immutable jurisdiction evidence.

### Existing GST report integration

The existing GST report now uses `IGstJurisdictionResolver` for each stored sale and purchase.
Intra-state tax is shown as CGST plus SGST, inter-state tax as IGST, and transactions without
sufficient historical evidence in an explicit Unresolved GST column. The report continues to
aggregate the already-stored GST amounts through `IGstCalculationService`; it never recalculates
historical GST or reads current master state. This replaces the former blanket intra-state
assumption without creating a new reporting module.

### Persistence and scope

No migration or new column is required because Phase 18A-2 already persists all authoritative
inputs. Phase 18A-3 adds no tax-component persistence and no snapshot update path. Place-of-supply
exceptions, reverse charge, CESS, GST return filing, B2B/B2C classification, and new GST arithmetic
remain outside this phase.

## Phase 18A-4 — B2B/B2C GST Classification

Phase 18A-4 adds a centralized, deterministic classification foundation without changing GST
arithmetic, stored monetary values, posting, stock, payment, invoice, report-total, or export
behavior. `IGstTransactionClassifier` classifies historical party identity and
`IGstTaxContextResolver` composes that independent result with the Phase 18A-3 jurisdiction
resolver. Neither service depends on the database, UI, or mutable master data.

### Classification policy

- **B2B** means the completed sale snapshot records an authoritative `Regular` or `Composition`
  registration type and a GSTIN that passes the shared `GstinValidator`, including its match to
  the historical customer state when that state is present.
- **B2C** means the completed sale snapshot records `Unregistered`, or the new-format sale is
  structurally an explicit walk-in sale (`CustomerId` is null and the historical capture marker is
  present).
- **Unresolved** means historical evidence is insufficient or invalid. This includes a missing
  registration type, a missing/invalid registered-party GSTIN, or a legacy transaction without a
  Phase 18A-2 capture marker.

`RegistrationType` is the classification authority. A GSTIN is supporting identity evidence for
registered parties; it never creates B2B status by itself. A name, phone, address, state, or words
that look like a business name are not classification evidence. `StateCode` remains solely the
Phase 18A-3 jurisdiction authority and the classifier does not compare states or derive a state
from a GSTIN.

### Historical, walk-in, purchase, and return policy

Classification reads only immutable Phase 18A-2 transaction snapshots. Later edits to customer,
supplier, or store registration, GSTIN, state, legal name, or other master data cannot change a
completed transaction. Legacy transactions with no capture marker remain `Unresolved`; there is no
current-master fallback, backfill, or guessing.

A captured sale with no `CustomerId` is the existing explicit walk-in representation and resolves
to B2C. A legacy row with the same null customer reference remains unresolved because it lacks the
capture marker that distinguishes reviewed historical evidence.

Purchase terminology remains supplier-specific rather than calling suppliers B2C. Purchases
resolve to `RegisteredSupplier`, `UnregisteredSupplier`, or `Unresolved`, using the same historical
registration and GSTIN evidence policy. Sales returns and purchase returns delegate to their
originating transaction; no duplicate classification fields or return snapshots are introduced.

### Reporting, persistence, and scope

The existing GST report continues to aggregate stored GST values and expose the Phase 18A-3
jurisdiction split. Phase 18A-4 does not add B2B/B2C report columns or exports because no current
calculation, UI, or export consumer requires them. Unresolved classification is therefore not
silently presented as B2B or B2C. The resolver is available for a later explicitly scoped reporting
phase.

No migration is required: all authoritative classification inputs are already stored in the
Phase 18A-2 snapshots. This phase does not implement ITC, reverse charge, CESS, GST filing,
e-invoicing, e-way bills, HSN classification, new GST rates, place-of-supply exceptions,
composition-specific tax rules, or any new GST arithmetic.
