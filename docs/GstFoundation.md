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
