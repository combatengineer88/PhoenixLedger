# PhoenixLedger C# Lab v0.4.3 — Archive and individual permanent deletion

This feature works **only in the separate C# Lab database**, not the Python PhoenixLedger database.

## Archive a record

1. Open **Customers**, **Suppliers**, or **Products / Catalog**.
2. Select one entry and click **Archive Selected**, or right-click and choose **Archive selected**.
3. Confirm. The record disappears from the active list and from selectors for **new** purchase orders, but it is **not deleted**.

Existing invoice, PO, customer, supplier, inventory, payment and accounting records keep their original foreign keys. The archived catalog ID and historical descriptions remain available in reports. Product on-hand stock is **not removed or zeroed** when a product is archived; any existing stock value stays reflected in business data. Low-stock reorder reports use active products only. Archive/restore events are recorded in an audit table.

## Find and restore old entries

Check **View Archived Only** at the top of any catalog to list archived records (including archive timestamps). Select one and click **Restore Selected** or use the right-click menu. Restoring keeps the original ID and all historical relationships.

## Delete an archived entry permanently, one at a time

With **View Archived Only** enabled, select exactly the item to clear and click **Delete Permanently** (or right-click). The app checks existing records and blocks permanent deletion unless:

- The item is already archived.
- A product has zero inventory quantity.
- No related rows reference its catalog ID, including invoices, purchase orders, line items, product-supplier links, inventory history, and other identifiable foreign-key relationships.

**If a record was ever used by an invoice, PO, stock movement, or another linked business record, it should remain archived instead of being deleted.** This is intentional to preserve reporting accuracy. There is no bulk-delete operation. A confirmation dialog shows the selected record and explains that deletion cannot be undone through Restore.

Before each permitted deletion, PhoenixLedger automatically makes a verified SQLite database backup under `%LOCALAPPDATA%\PhoenixLedgerCSharpLab\backups`. The app then deletes only that selected catalog entry. Its archive/purge event audit remains, and old backups are not automatically removed. Once an unused archived product is purged, its unique SKU can be reused by a future product.

Historical report amounts are sourced from the existing sales/payment and inventory transaction tables, not recalculated by deleting catalog records. The accounts and reports remain subject to financial reconciliation and accountant review.

## Important import behavior

Excel imports will not silently overwrite an archived entry with a matching SKU or supplier/customer identifier. Restore that entry before importing updates to it, or permanently purge it first **only when no linked records depend on it**.

This release includes a one-time pre-migration backup, Windows UI navigation testing, and transaction tests for archived-vs-active visibility, report preservation, restoration, deletion prevention, and purging individual unused entries.
