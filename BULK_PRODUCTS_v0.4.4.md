# PhoenixLedger C# Lab v0.4.4 — Bulk Product Edit

Bulk editing is available from **Products / Catalog → Bulk Edit Products**, **Edit → Bulk edit products**, **Tools → Bulk edit products**, or right-clicking products. This feature changes products only in the separate C# Lab database.

## Selecting similar products

Enter part of a **product name, SKU, or supplier name** in Search. Optionally set a **SKU begins with** filter and/or choose one supplier and one existing supply type. Click **Find Products** to see matching active products. Only currently checked rows will change. You can check individually, or use **Check All Filtered** / **Clear Checks**. Up to 5,000 matches are supported at once; narrow the filters for larger catalogs. Previously selected rows in the main catalog may be prechecked in the editor.

Archived products are not displayed or updated. Restoring a product is required before editing it.

## Editing and previewing

Check any of these independent fields:

- **Change selling price** — set an exact dollar amount or increase/decrease the existing price by a percentage.
- **Change item cost** — set an exact dollar amount or increase/decrease the existing unit cost by a percentage.
- **Change supply type** — set to `stocked`, `made_to_order`, or `supplier_direct` (matches the existing product editor).

You may enable one, two, or all three fields. Fields left unchecked retain their previous values. Enter a reason in the audit field and click **Preview Changes**. The grid shows current and proposed values side-by-side; review them before clicking **Apply Bulk Changes**.

Changes are transactional: if a selected product is edited or archived after the preview, the entire batch is rejected, so no partially applied batch remains. Each changed product generates a timestamped audit record containing its SKU, name, reason, old/new selling price, old/new unit cost, old/new supply type, and batch identifier. The editor makes a verified SQLite backup before applying changes to the Lab database.

## Historical financial safety

- **Existing invoices, estimates, purchase orders and journal entries are not repriced.** They keep the prices already recorded when their transactions were created.
- Changing **item cost** does not rewrite **inventory_unit_cost**, the separately recorded weighted inventory cost basis used by inventory valuation. If the product has an existing reorder-total estimate, it is synchronized to `new item cost × reorder quantity` so reopening the single-product editor does not undo the new unit cost.
- A supply-type change is **blocked while a product's on-hand stock is not zero**, to avoid changing how existing stock is classified for valuation without an inventory adjustment.
- Previewing or saving a product with no changed values does not generate an unnecessary audit event.

## Recovery and verification

A verified backup is saved in `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\backups` before each confirmed bulk edit. Tests on disposable Windows CI data verify filters, archived exclusion, percentage/fixed changes, expected audit entries, historic document price preservation, per-item reorder-total consistency, inventory valuation basis preservation, stock-mode guards, and all-or-nothing rollback when a record changes after preview.

This version edits the **catalog price and cost** used for future transactions; it does not rebook historical cost-of-goods-sold or alter current inventory valuation. Confirm accounting treatment before using new costs for financial statements.
