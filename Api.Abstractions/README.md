1# Api.Abstractions

Shared primitives for the **Lighting Split backend**.  
This package holds simple enums and value types that need to be consistent across multiple apps (e.g., Web API and Azure Functions).

## Contents
- **Receipts**
  - `ReceiptStatus` – life cycle states of a receipt (PendingParse, Parsed, NeedsReview, FailedParse).
  - `BaselineSource` – indicates which subtotal/total baseline was used during reconciliation.
- **Parsing**
  - `ParseStatus` – result of a parsing attempt (Success, Partial, Failed).

## Usage
Reference this package in your project:

```powershell
dotnet add package Calathea.Api.Abstractions --version 0.1.x