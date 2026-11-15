# PhoenixLedger C# Lab v0.4.0 — Finance and PDF Designer (Beta)

This release is **an experimental financial workspace**, not yet an audited accounting system. It uses the separate C# Lab database at `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\lab.db`. Importing from the Python application creates a separate snapshot; it does **not** write to the Python `business.db`.

## Purchase Orders

Purchase Orders → **New Purchase Order** lets you select a saved supplier and build multiple product lines with quantities and per-item costs. Save as Draft or Open. Drafts require approval before receiving. You can edit and cancel a Draft or Open order as long as no merchandise has been received. Receive items partially or fully, with strict guards against over-receipt; stock quantities and weighted-average inventory basis update transactionally. Receiving posts **debit Inventory / credit Accounts Payable** in the C# Lab journal. **Pay Supplier** records a bank/check/other payment only against previously received, unpaid stock and posts **debit AP / credit Cash**. Receipt and payment history is saved separately.

### Important purchasing limits
- Existing Python purchase orders in a copied database can appear in the C# UI. Review their opening received quantities and supplier balances before posting any new receipt/payment.
- The PO editor currently supports simple merchandise line items; shipping, discounts, multi-currency, and vendor tax calculations have not yet been ported.
- The C# Lab does not submit purchase orders to suppliers electronically.

## Invoice Payments

Estimates & Invoices → select an **Invoice** → **Record Invoice Payment**. Partial/full payments are supported; overpayments are rejected. The balance and Paid/Partially Paid status update together with the journal. Imported Python databases may already have payment-journal triggers; the C# Lab checks these to avoid duplicate payment postings. If an imported invoice has no prior opening A/R entry, the Lab creates an Opening Balance Equity offset rather than falsely treating its historic invoice as current sales revenue.

**Creating or converting estimates/invoices, processing returns, tax handling, and customer-credit accounts are still pending in C#.** Existing document records can be viewed and paid.

## Automatically generated reports

The Reports tab recalculates results from the selected date range and the current stored ledger/transactions.

- Profit and Loss Statement (booked revenue and expenses)
- Balance Sheet with assets/liabilities/equity checks
- Direct-method cash flows from posted Cash journal lines
- Chart of Accounts (seeded Lab accounts + discovered imported accounts)
- A/R aging (invoice balances, credit due dates or 30-day fallback)
- A/P aging (recorded PO receipts/payables, 30-day fallback)
- Trial Balance (with debit/credit balance check)
- General Ledger with running account balances
- Bank Reconciliation **comparison** (manual statement entry totals vs Cash ledger; formal one-to-one clearance/matching is still pending)
- Budget vs Actual (user-entered monthly budgets by ledger account)
- Sales/payment/expense, stock/reorder, invoice, supplier, and customer reports.

**Crucial:** Opening balances, historic payroll, depreciation, loan balances, bank feeds, account mappings, and unrecorded transactions cannot be inferred from a database snapshot. Reports reflect only recorded journal entries. P&L, Balance Sheet, cash flow, aging, and reconciliation require accountant review before any tax, financing, or external business use. The PDF output is not a guarantee of accounting accuracy.

## Customizable PDF layout editor

Open **Tools → PDF Layout Designer** or the maintenance/design actions. Four separate designs are saved in the C# Lab Settings database: **Invoice**, **Estimate**, **Purchase Order**, **Report**.

- Drag elements on a paper preview. Select objects using the Layers list.
- Use Properties to adjust coordinates, dimensions, fonts, colors, borders, labels, and field placeholders.
- Add editable text, lines, rectangles, business logos, and live data tables.
- Switch Letter/A4 paper. Save layouts, preview sample PDFs, or export template JSON.
- Use **Export PO PDF**, **Export Document PDF**, or the Reports **Export PDF** action to render using saved designs.

Uses `PDFsharp-GDI` **6.2.4** (MIT license) to generate PDFs offline on Windows. The first version supports editable element placement and live tables; advanced page headers/footers, variable-length table collision handling, imported JSON design templates, barcode graphics, and freeform chart widgets are not yet implemented. Carefully inspect exported PDFs before distributing them.

## Release process

GitHub Actions on Windows restores and compiles the project, smoke-tests UI navigation, runs transaction/report/PDF checks on disposable CI data, then builds the self-contained setup executable. A successful `main` build publishes version v0.4.0 for the existing built-in updater, preserving the chosen installation location and Windows installation scope.

To test safely: back up the Lab database, apply the update, make a draft PO for a demo supplier and product, receive some items, check A/P and inventory journal, then record a partial invoice payment and verify A/R aging. Restore a backup if you want to discard these experiments.

This repository is publicly visible. Do not commit customer lists, real financial databases, API tokens, or passwords.
