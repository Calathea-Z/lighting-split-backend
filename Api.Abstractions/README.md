# Api.Abstractions (Shared NuGet)

Minimal shared contracts/helpers used by **Functions** and **API**.

## Contents

- `Receipts/` — core enums (ReceiptStatus, ParseStatus, etc.)
- `Reconciliation/`
  - `ReconcileOptions` caps & toggles
  - `ReconcilePolicy` → `CanAutoAdjust(...)`
  - `ReconcileConstants` → labels/notes
- `Parsing/`
  - `IgnorePhrases` shared vocabulary for non-items
  - `NormalizerHints` (optional DTO for LLM)
- `Transport/`
  - `ReplaceReceiptItemDto`, `ReplaceReceiptItemsRequest` (replace-mode endpoint)
- `Math/`
  - `Money.Round2`, `Money.EqualsWithin`

## Guidelines

- No EF/Azure/HTTP/DI/logging dependencies.
- Only stable contracts & pure helpers.
- Version with semver; bump **minor** when adding, **major** on breaking changes.
