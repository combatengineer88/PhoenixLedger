# PhoenixLedger C# Lab 0.1 — Experimental Windows Edition

**A SEPARATE editable C# / Windows Forms project, NOT an upgrade to the Python PhoenixLedger application.**

This is a functional starter edition for testing C# development and working on your data without making changes to the existing Python program. It intentionally does **not** replicate every PhoenixLedger feature yet.

## What you can do now

- Modern blue/slate PhoenixLedger branding, maximized startup, navigation, styled buttons, searchable lists and right-click actions.
- **Products:** add/edit items, supplier assignment, SKU/barcode, bulk reorder cost, auto-calculated unit cost, stock mode, reorder point, images, barcode PNG, bin location.
- **Suppliers:** search, add, and edit details.
- **Customers:** search, add, and edit details (including city/state/ZIP and tax-exempt flag).
- **Inventory:** scan a barcode/SKU with a keyboard-wedge scanner, add/remove/set on-hand quantity with a reason, view change history.
- **Excel import:** `.xlsx` import for products, customers, and suppliers, using ClosedXML. Matching SKUs, supplier names or customer emails can be updated. Import affects the lab database only.
- **CSV export:** current table's visible rows and headers, including **Yes/No** for product and barcode images (no file paths).
- **Purchase Orders and Sales Documents:** search and view existing records and their line items **read-only** after importing a database snapshot.
- **Import Database Copy:** copy an existing PhoenixLedger SQLite database safely into the lab. Media files referenced by products are copied into the lab folder when available.

## What is not implemented in this C# experiment

This lab is **not** feature-equivalent to Python PhoenixLedger. It currently does not provide production-ready login/recovery, invoice or PO creation, receiving, supplier payments, credit accounts, the visual PDF template editor, reports, barcode sticker printing, or the full updater. Purchase orders and sales documents are read-only by design. Do not use the lab as your sole business system.

## Installation & running

**Requirements:** Windows 10 or 11, and the **.NET 10 SDK** (not just the .NET runtime). Visual Studio 2026 with the **.NET desktop development** workload is optional but recommended if you plan to edit the project. First build requires internet access to restore the NuGet packages.

1. Extract the whole ZIP to a folder separate from your Python PhoenixLedger installation.
2. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0.
3. Double-click `Run_CSharpLab_Windows.bat` to build and launch it, **or** open `PhoenixLedger.CSharpLab/PhoenixLedger.CSharpLab.csproj` in Visual Studio 2026 and press F5.
4. The first launch displays a few obvious **demo records** in the lab's own database.
5. Use **Import Database Copy** if you want to test against a snapshot of your real records.

To make a standalone EXE: run `Build_Standalone_Windows.bat`. The expected output is `dist/win-x64/PhoenixLedger.CSharpLab.exe`. This publish is intended to be self-contained; do not delete the adjacent support files if produced.

## Your production database stays untouched

| Program | SQLite database path |
|---|---|
| Existing Python PhoenixLedger | `%LOCALAPPDATA%\PhoenixLedger\business.db` |
| Separate C# Lab | `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\lab.db` |

The **Import Database Copy** button opens the existing database with SQLite read-only mode and uses SQLite's backup API to take a consistent snapshot, including any uncheckpointed WAL data. It changes **only the C# Lab database**. The previous lab data is backed up under `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\backups` before replacement. Existing product/barcode photos are copied when the referenced files are available.

The experimental app does **not** authenticate users; it is intended for a trusted local development PC only. Do not share copies of a real business database or the experimental application's data folder.

## Excel import columns

First worksheet, header row at top. Supported headers include:

| Entity | Required header(s) | Optional headers |
|---|---|---|
| Products | SKU, Name | Description, Barcode, Price, Cost, Quantity, Supplier, Reorder Qty, Reorder Cost |
| Suppliers | Name | Contact, Email, Phone, Website, Address, City, State, ZIP, Notes |
| Customers | Name | Email, Phone, Address, City, State, ZIP, Notes |

Quantities and money values are validated and the import is committed as one transaction. Any error rolls the import back. Note that matching product SKUs and supplier names are updated; this experimental importer is simpler than the mature Python import process. Work only with the copied lab database.

## GitHub source and Windows builds

This repository contains the **independent C# Windows Forms experimental edition**, not the Python PhoenixLedger application. The CI workflow in `.github/workflows/build-csharp-lab.yml` runs on pushes to `main`, pull requests to `main`, or from **Actions → Build PhoenixLedger C# Lab → Run workflow**.

The workflow restores the project with .NET 10 on a hosted Windows runner, compiles the application, then publishes a **self-contained Windows x64 build**. When the workflow succeeds, open the workflow run and download the `PhoenixLedger-CSharpLab-v0.1.0-win-x64` artifact under **Artifacts**. Extract the downloaded ZIP and launch `PhoenixLedger.CSharpLab.exe` on a Windows x64 computer.

A self-contained build does not require installing the .NET runtime, but it has not been tested on a Windows desktop in this environment. Never use the experimental app against your only copy of production data.

The downloadable Actions artifact is a build artifact, **not** an installer and **not** an automatic updater. The existing Python PhoenixLedger release workflow remains unchanged.

You can also keep the project locally and use Visual Studio without using GitHub at all.

## Source organization

- `PhoenixLedger.CSharpLab/Program.cs` — entry point
- `PhoenixLedger.CSharpLab/MainForm.cs` — Windows Forms navigation, tables, dashboard, CSV export, contextual actions
- `PhoenixLedger.CSharpLab/Editors.cs` — product, supplier, customer, stock forms
- `PhoenixLedger.CSharpLab/Repository.cs` — parameterized SQLite reads/writes, models, inventory audit
- `PhoenixLedger.CSharpLab/ExcelImporter.cs` — ClosedXML workbook import
- `PhoenixLedger.CSharpLab/Store.cs` — schema, demo data and isolated SQLite snapshot migration
- `PhoenixLedger.CSharpLab/Assets/` — phoenix icon and logo

## Testing status

The C# project is fully written, but **the C# executable has not been compiled or run here** because the current development container does not have the Windows .NET SDK/WinForms runtime. The included GitHub Action or Visual Studio will perform the actual compile on Windows. If the build reports a compiler or dependency error, copy the output so it can be fixed.

NuGet package references: `Microsoft.Data.Sqlite` 10.0.15 and `ClosedXML` 0.105.1. Both are downloaded by `dotnet restore` on your Windows PC.
