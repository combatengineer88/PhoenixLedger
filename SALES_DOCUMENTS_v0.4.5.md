# PhoenixLedger C# Lab v0.4.5 — Purchase Order pricing and Sales Documents

## Purchase orders: unit cost auto-fills
The Purchase Orders editor now fills **Unit Cost ($)** from the selected product's saved catalog **Item Cost**, instead of leaving it blank. You may override this cost for that specific supplier order, and the **Line Total** updates as quantity or cost changes. Reopening an existing purchase order retains the cost originally recorded on each line instead of replacing it with today's catalog cost.

## Estimates
Open **Estimates & Invoices → New Estimate**, select an active customer, and enter a date, notes, optional percentage sales tax, and line items. Selecting an active catalog product fills its description and current selling price; you can override them. Select **Custom line / service** to invoice work that isn't in the product catalog. Quantities, prices and line totals are reviewed before saving. A new estimate is **Draft**, with no accounts-receivable or sales journal entry.

Use **Edit Draft Estimate** to correct draft details before approval. Click **Approve Estimate** to mark the estimate approved and make it read-only. This is an internal approval control; there is no electronic signature or client-facing approval portal.

Use **Convert to Invoice** on an **Approved** estimate. A new **Open Invoice** is created, with an explicit link to the original estimate. The estimate changes to **Converted** but remains accessible. Prices, quantities, descriptions, subtotal and tax are copied **exactly**; catalog price changes after approval do not reprice the invoice. The same estimate cannot be invoiced twice. The invoice appears under Sales Documents with a **Source Estimate** reference; the estimate shows its **Converted Invoice** number.

## Direct invoices
Choose **New Invoice** in Estimates & Invoices, or **File → New → Invoice (without estimate)**. The direct invoice uses the same editor but immediately creates an **Open Invoice** without an estimate. Customer, custom services, product line items and tax are supported. After creation, select the invoice and choose **Record Payment** for a partial or complete payment.

Invoice issue postings debit Accounts Receivable and credit Sales Revenue and, if applicable, Sales Tax Payable. A copied Python database may contain journal triggers; the C# edition checks for existing invoice postings and avoids posting a second sales journal. Invoice payment logic recognizes new C# issued invoices and will not accidentally classify them as historical opening balances.

## Important limits
- This is the separate C# Lab database. The original Python application database remains untouched.
- This first sales-editor iteration supports estimates, approval, conversion and direct invoice creation. It **does not** automatically reserve, pick or decrement inventory when an invoice is issued. Inventory movement and associated COGS recognition must be managed separately until fulfillment integration is implemented.
- It has no multi-level approval, quote-expiry reminders, email delivery, automatic customer signatures, variable tax jurisdiction/line-level tax or advanced discount rules.
- Approved estimates are immutable. Invoice line editing, credit notes and returns are not yet supported. Historic invoice/estimate PDF generation continues using the editable PDF layouts.
- Tax input is an optional percentage; verify the appropriate tax rules and financial statements before using for tax or external reporting.

## Verification
Windows CI runs the actual application and installer and tests PO catalog cost loading, estimate totals with taxes, draft editing, approval and immutability, exact invoice conversion, prevention of duplicate conversions, invoice sales journal issuance, payment workflows, direct invoices, historical line prices and source-estimate linkage. Test these features with sample data before relying on them for business accounting.
