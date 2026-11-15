# PhoenixLedger C# Lab v0.3.1 — Automatic update reliability

Fixes the Windows error “the process cannot access the file because it is being used by another process.” In earlier versions, the downloaded installer was renamed while its SHA-256 read handle was still open. The verification handle is now closed before the rename.

Updates are handed off to a **standalone update agent**, copied outside the chosen installation directory to %LOCALAPPDATA%\\PhoenixLedgerCSharpLab\\updates. It verifies the package again, waits for the original application process to exit, runs Setup in the same folder and installation mode, waits for success, then launches the updated application exactly once. The installer no longer launches a second copy during silent upgrades.

**One-time manual repair:** Versions up to v0.3.0 contain the file-handle bug and cannot reliably install this fix through their own updater. Download and run v0.3.1 Setup once over the existing install (choose the same folder and install mode). Updates *after* v0.3.1 should use the corrected built-in update mechanism. The Windows installer and GitHub release are produced by GitHub Actions.

---
# PhoenixLedger C# Lab v0.3.0 — Python-inspired workspace

This experimental C# application retains its separate data in %LOCALAPPDATA%\PhoenixLedgerCSharpLab. It never updates the Python application's database in place.

## Features ported and working
- Classic Windows File / Edit / View / Tools / Help menu bar with actions and keyboard shortcuts.
- Python-based navigation: Home, Dashboard, Customers, Suppliers, Products / Catalog, Purchase Orders, Estimates & Invoices, Expenses, Reports, Accounting, Administration, Settings / Updates; C# Inventory History is also retained.
- Home sidebar quick links, supplier and customer workflow lanes. The unfinished financial actions are clearly disabled.
- Editable C# Lab products, suppliers, customers, stock adjustments, SKU/barcode search, Excel import and CSV export.
- Expenses: add, edit, delete, search, export in the separate Lab database, compatible with the imported Python expenses table.
- Reports: sales, payments (if imported), expense summaries, reorder stock, invoice information, supplier product catalog, customer totals. Export report CSV.
- Accounting: read-only imported journal summary and journal line details. No financial posting.
- Administration: database backups, import a copy of Python records, open Lab folder.
- Settings / Updates: editable business identity and locally saved logo, maintenance and existing GitHub auto-updater.

## Not yet ported
PO creation/receiving, invoice and estimate posting, payments, customer credit, full employee sign-in/roles, and visual PDF templates. Related purchase orders and estimates/invoices remain read-only rather than exposing unfinished controls.

## Release and updater check
GitHub Actions builds the Windows application, packages an Inno Setup installer and creates version v0.3.0 if the main build succeeds. Users with v0.2.1 installed can check Settings / Updates, download the SHA-256-verified installer and update the existing selected installation directory without manually replacing files. Per-user/all-users Windows installation scope is preserved.

---
# PhoenixLedger C# Lab 0.2.1 — Experimental Windows Edition

**A SEPARATE editable C# / Windows Forms project, NOT an upgrade to the Python PhoenixLedger application.**

This is a functional starter edition for testing C# development and working on your data without making changes to the existing Python program. It intentionally does **not** replicate every PhoenixLedger feature yet.

## Windows installer and built-in updates (v0.2.1)

**Normal installation (recommended):** Download `PhoenixLedger-CSharpLab-Setup.exe` from the latest [GitHub Release](https://github.com/combatengineer88/PhoenixLedger/releases/latest). The wizard first asks whether to install **for all users** (administrator approval/UAC, recommended for `C:\Program Files` and other protected destinations) or **just for you** (no administrator approval, using a writable folder under `%LOCALAPPDATA%\Programs`). The installation destination is selectable in both modes; Windows account permissions still apply. Inno Setup remembers your chosen location and installation mode for subsequent upgrades. No .NET SDK or separate runtime is required. The installer provides Start Menu shortcuts, an optional desktop shortcut, and an Installed Apps uninstall entry. This remains independent from the Python PhoenixLedger program.

**Updating:** Open **Updates** in the application. It checks the latest stable GitHub Release, compares version numbers, and offers **Download and install update**. The installer is downloaded to `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\updates` and checked against the SHA-256 digest published by GitHub. The program closes, and the installer silently updates the same installation directory and relaunches the app. An administrator-approved installation may display a standard Windows UAC prompt during updating; the app does not bypass Windows permissions. No manual ZIP extraction or file replacement is needed. The `InstallDir` setting recorded by the installer and the running application directory must agree before an automatic update can be applied.

**Installer permissions fix (v0.2.1):** The v0.2.0 installer used a non-elevated install mode and could fail with 'Access is denied' for protected folders. Choose **all users** in v0.2.1 when installing under Program Files or another protected folder, and approve the Windows UAC prompt. If you cannot elevate, select **just for you** and a user-writable destination. For upgrades, the updater detects both HKCU and HKLM installations (including either registry view) and passes `/CURRENTUSER` or `/ALLUSERS` to the installer so it retains the original privilege scope. Do not grant broad write access to Program Files to work around installer errors.

**Options:** Checking for releases on startup is enabled by default. Automatically **installing** an available update on startup is an **opt-in** checkbox under **Updates**; otherwise installation is initiated using the Update button. An unpacked/portable source build can check releases but cannot overwrite itself; install a setup release first. If GitHub is unavailable or no installer release has been published, the application continues working normally. Only complete versioned releases are used, never development branch files.

**Data safety:** The C# Lab SQLite database and imported media remain at `%LOCALAPPDATA%\PhoenixLedgerCSharpLab` outside the installed application folder; normal updates never touch the database or the existing Python installation. Back up important data as usual. The new C# application is still experimental.

**CI and publishing:** Every push or pull request builds the WinForms app and the Inno Setup installer on GitHub-hosted Windows. A successful push to `main` additionally creates a GitHub Release named after the version in `PhoenixLedger.CSharpLab.csproj` (for example, `v0.2.0`) and attaches `PhoenixLedger-CSharpLab-Setup.exe`, but only if that version does not already exist. Bump the project version before releasing each subsequent update. Do **not** reuse release tags or overwrite published installers. If a release already exists, the workflow leaves it unchanged.

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

This lab is **not** feature-equivalent to Python PhoenixLedger. It currently does not provide production-ready login/recovery, invoice or PO creation, receiving, supplier payments, credit accounts, the visual PDF template editor, reports, barcode sticker printing, or production-grade release signing. Purchase orders and sales documents are read-only by design. Do not use the lab as your sole business system.

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

The workflow restores the project with .NET 10 on a hosted Windows runner, compiles the application, then publishes a **self-contained Windows x64 build**. When the workflow succeeds, open the workflow run and download the `PhoenixLedger-CSharpLab-Windows-Installer` (recommended) or `PhoenixLedger-CSharpLab-Portable-win-x64` (portable) artifact under **Artifacts**. Extract the downloaded ZIP and launch `PhoenixLedger.CSharpLab.exe` on a Windows x64 computer.

A self-contained build does not require installing the .NET runtime, but it has not been tested on a Windows desktop in this environment. Never use the experimental app against your only copy of production data.

The Windows Installer artifact contains the complete selectable-folder setup. Successful main-branch builds publish versioned installer Releases for the in-app updater. The existing Python PhoenixLedger release workflow remains unchanged.

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