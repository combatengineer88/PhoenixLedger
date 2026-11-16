using Microsoft.Data.Sqlite;

namespace PhoenixLedger.CSharpLab;

internal static class FinancialSmokeTests
{
    public static void Run()
    {
        if(!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),"true",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Financial smoke test is permitted only on disposable GitHub CI runners.");
        using var db=Store.Open();
        long supplier=FinancialOperations.Scalar(db,null,"SELECT id FROM suppliers ORDER BY id LIMIT 1");
        long product=FinancialOperations.Scalar(db,null,"SELECT id FROM products ORDER BY id LIMIT 1");
        long customer=FinancialOperations.Scalar(db,null,"SELECT id FROM customers ORDER BY id LIMIT 1");
        if(supplier<=0||product<=0||customer<=0)throw new Exception("Sample data missing.");
        long opening=FinancialOperations.Scalar(db,null,"SELECT qty FROM products WHERE id=@p",("p",product));
        long po=FinancialOperations.CreatePurchaseOrder(supplier,new[]{
            new PurchaseLine(product,10,425)
        },DateTime.Today,"CI financial test",draft:true);
        FinancialOperations.EditPurchaseOrder(po,supplier,new[]{new PurchaseLine(product,10,425)},DateTime.Today,"Edited CI PO");
        FinancialOperations.ApprovePurchaseOrder(po);
        var line=FinancialOperations.PurchaseLines(po).Single();
        string state=FinancialOperations.Receive(po,new[]{new ReceiptLine(line.Id,4)},DateTime.Today,"Part 1");
        if(state!="Partially Received")throw new Exception("Partial receipt status mismatch.");
        long stock=FinancialOperations.Scalar(db,null,"SELECT qty FROM products WHERE id=@p",("p",product));
        if(stock!=opening+4)throw new Exception("Stock not incremented exactly once.");
        if(FinancialOperations.SupplierBalance(po)!=1700)throw new Exception("PO payable must reflect partial receipt.");
        try
        {
            FinancialOperations.Receive(po,new[]{new ReceiptLine(line.Id,7)},DateTime.Today,"Too many");
            throw new Exception("Over-receipt was incorrectly allowed.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("exceeds")) { }
        FinancialOperations.Receive(po,new[]{new ReceiptLine(line.Id,6)},DateTime.Today,"Final");
        if(FinancialOperations.SupplierBalance(po)!=4250)throw new Exception("PO balance incorrect.");
        FinancialOperations.PaySupplier(po,12.50M,DateTime.Today,"ACH","CI");
        if(FinancialOperations.SupplierBalance(po)!=3000)throw new Exception("PO payment outstanding balance incorrect.");
        try
        {
            FinancialOperations.PaySupplier(po,30.01M,DateTime.Today,"ACH","Overpay");
            throw new Exception("Supplier overpayment was incorrectly allowed.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("exceeds")) { }

        FinancialOperations.Execute(db,null,
            "INSERT INTO documents(kind,customer_id,created,status,total,subtotal,tax,notes) VALUES('Invoice',@c,@d,'Open',10000,10000,0,'Smoke test invoice')",
            ("c",customer),("d",DateTime.Today.ToString("yyyy-MM-dd")));
        long inv=FinancialOperations.Scalar(db,null,"SELECT last_insert_rowid()");
        // SQLite last_insert_rowid is per connection: read directly from id field.
        inv=FinancialOperations.Scalar(db,null,"SELECT max(id) FROM documents");
        FinancialOperations.PayInvoice(inv,30m,DateTime.Today,"Bank Transfer");
        if(FinancialOperations.InvoiceDue(inv)!=7000)throw new Exception("Invoice partial payment not tracked.");
        FinancialOperations.PayInvoice(inv,70m,DateTime.Today,"Bank Transfer");
        if(FinancialOperations.InvoiceDue(inv)!=0)throw new Exception("Invoice balance remains after full payment.");
        try
        {
            FinancialOperations.PayInvoice(inv,1m,DateTime.Today,"Bank Transfer");
            throw new Exception("Invoice overpayment was incorrectly allowed.");
        }
        catch(InvalidOperationException) { }

        FinancialOperations.AddAccount("CI Test Asset","1999","Asset");
        try
        {
            FinancialOperations.ManualJournal(DateTime.Today,"Unbalanced CI entry",
                new[]{("Cash",100L,0L),("CI Test Asset",0L,99L)});
            throw new Exception("Unbalanced manual journal was allowed.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("balance")) { }
        FinancialOperations.ManualJournal(DateTime.Today,"Opening cash CI",
            new[]{("Cash",100L,0L),("Opening Balance Equity",0L,100L)});

        BusinessData.SaveExpense(0,DateTime.Today,"Supplies","CI Vendor","Test expense",10m);
        long expenseId=FinancialOperations.Scalar(db,null,"SELECT max(id) FROM expenses");
        BusinessData.SaveExpense(expenseId,DateTime.Today,"Supplies","CI Vendor","Edited expense",15m);
        BusinessData.DeleteExpense(expenseId);
        long expenseBalance=FinancialReports.Ledger(DateTime.Today)
            .Where(x=>x.Account=="Expense: Supplies").Sum(x=>x.Debit-x.Credit);
        if(expenseBalance!=0)throw new Exception("Deleted expense journal must be reversed.");

        using(var j=Store.Open())
        using(var cmd=j.CreateCommand())
        {
            cmd.CommandText="""
              SELECT count(*) FROM (
                SELECT j.id FROM lab_fin_journal j JOIN lab_fin_lines l ON l.journal_id=j.id
                GROUP BY j.id HAVING sum(l.debit)<>sum(l.credit)
              )
              """;
            if(Convert.ToInt64(cmd.ExecuteScalar())>0)throw new Exception("Unbalanced journal entries.");
        }
        DateTime start=DateTime.Today.AddYears(-1),end=DateTime.Today;
        foreach(string report in FinancialReports.Names)
        {
            var output=FinancialReports.Generate(report,start,end);
            if(output.Columns.Count==0)throw new Exception("Report has no columns: "+report);
        }
        // Purchase order product picker now reads the catalog's saved unit cost.
        var poCatalogCosts=MainForm.PurchaseOrderSavedCosts();
        if(!poCatalogCosts.TryGetValue(product,out var catalogUnitCost) || catalogUnitCost<=0)
            throw new Exception("PO picker did not load the product's saved unit cost.");

        // Create/edit/approve/convert estimate. The linked invoice copies snapshot
        // prices and descriptions, can be paid, and posts income only once.
        var salesItems=new List<SalesLine>
        {
            new(product,"Custom printed tee",2,1299),
            new(null,"Graphic setup fee",1,550)
        };
        var quoteTotals=SalesDocuments.Totals(salesItems,7.5m);
        if(quoteTotals.Subtotal!=3148 || quoteTotals.Tax!=236 || quoteTotals.Total!=3384)
            throw new Exception("Estimate tax calculation or subtotal incorrect.");
        long estimate=SalesDocuments.Create("Estimate",customer,DateTime.Today,
            "Estimate for CI",salesItems,7.5m);
        if(SalesDocuments.Load(estimate).Status!="Draft")
            throw new Exception("New estimates must start in Draft status.");
        SalesDocuments.UpdateDraftEstimate(estimate,customer,DateTime.Today,
            "Updated quote notes",salesItems,7.5m);
        SalesDocuments.Approve(estimate);
        try
        {
            SalesDocuments.UpdateDraftEstimate(estimate,customer,DateTime.Today,
                "Too late",salesItems,7.5m);
            throw new Exception("Approved estimate was edited.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("draft")) { }
        if(SalesDocuments.Load(estimate).Status!="Approved")
            throw new Exception("Estimate approval did not persist.");
        long converted=SalesDocuments.ConvertApprovedEstimate(estimate,DateTime.Today);
        if(SalesDocuments.Load(estimate).Status!="Converted")
            throw new Exception("Converted estimate did not retain its original record.");
        var resultingInvoice=SalesDocuments.Load(converted);
        if(resultingInvoice.Kind!="Invoice" || resultingInvoice.Status!="Open" ||
           resultingInvoice.SourceEstimateId!=estimate ||
           resultingInvoice.Total!=3384 ||
           resultingInvoice.Lines.Count!=2 ||
           resultingInvoice.Lines[0].UnitPriceCents!=1299)
            throw new Exception("Converted invoice did not preserve estimate totals and line prices.");
        try
        {
            SalesDocuments.ConvertApprovedEstimate(estimate,DateTime.Today);
            throw new Exception("Estimate was converted twice.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("already converted")) { }
        if(FinancialOperations.InvoiceDue(converted)!=3384)
            throw new Exception("Converted invoice outstanding balance is incorrect.");
        if(FinancialOperations.Scalar(db,null,"""
            SELECT count(*) FROM lab_fin_journal
            WHERE source_type='lab_sales_invoice' AND source_id=@id
            """,("id",converted))!=1)
            throw new Exception("New invoice financial recognition missing or duplicated.");
        FinancialOperations.PayInvoice(converted,10m,DateTime.Today,"ACH");
        if(FinancialOperations.InvoiceDue(converted)!=2384)
            throw new Exception("Converted invoice cannot accept a partial payment.");
        long directInvoice=SalesDocuments.Create("Invoice",customer,DateTime.Today,
            "Direct billing for lab",new List<SalesLine>{new(null,"On-site service",1,2000)},0m);
        if(SalesDocuments.Load(directInvoice).Status!="Open" ||
           SalesDocuments.Load(directInvoice).SourceEstimateId.HasValue)
            throw new Exception("Direct invoice should open without an originating estimate.");
        if(FinancialOperations.InvoiceDue(directInvoice)!=2000)
            throw new Exception("Direct invoice has incorrect balance.");
        if(FinancialOperations.Scalar(db,null,"""
            SELECT count(*) FROM lab_fin_lines l
            JOIN lab_fin_journal j ON j.id=l.journal_id
            WHERE j.source_type='lab_sales_invoice' AND j.source_id=@id AND l.account='Sales Revenue'
            """,("id",directInvoice))!=1)
            throw new Exception("Direct invoice sales revenue not posted.");
        FinancialOperations.PayInvoice(directInvoice,20m,DateTime.Today,"Bank Transfer");
        if(FinancialOperations.InvoiceDue(directInvoice)!=0)
            throw new Exception("Direct invoice could not be paid in full.");
        if(Repository.Table("Sales Documents").Rows.Cast<System.Data.DataRow>()
            .All(x=>Convert.ToInt64(x["ID"])!=converted ||
                x["Source Estimate"]?.ToString()!=estimate.ToString()))
            throw new Exception("Source estimate relationship is not visible in sales catalog.");

        // Bulk product edit: filter similar SKUs by prefix and supplier, exclude
        // archived products, preview individual prices and costs, and preserve
        // historic document line values.
        string bulkSuffix=Guid.NewGuid().ToString("N")[..8];
        var bulkIds=new List<long>();
        for(int i=1;i<=3;i++)
        {
            FinancialOperations.Execute(db,null,"""
                INSERT INTO products(sku,name,supplier_id,price,cost,qty,stock_mode,
                    reorder_cost_total,reorder_qty,inventory_unit_cost)
                VALUES(@sku,@name,@supplier,1000,400,0,'stocked',12000,30,450)
                """,("sku",$"BULK-{bulkSuffix}-{i}"),
                ("name","Bulk Sample Tee "+i),("supplier",supplier));
            bulkIds.Add(FinancialOperations.Scalar(db,null,
                "SELECT max(id) FROM products"));
        }
        CatalogArchive.SetArchived("Products",bulkIds[2],true);
        var matching=BulkProductOperations.Search("Bulk Sample Tee","BULK-"+bulkSuffix,supplier,"stocked");
        if(matching.Count!=2||matching.Any(x=>x.Id==bulkIds[2]))
            throw new Exception("Similar-products filter included archived or unrelated records.");

        FinancialOperations.Execute(db,null,"""
            INSERT INTO document_items(document_id,product_id,description,qty,unit_price,line_total)
            VALUES(@d,@p,'Historical price sample',1,1000,1000)
            """,("d",inv),("p",bulkIds[0]));

        var changes=new BulkProductChanges(
            true,BulkPriceMethod.AdjustPercent,15m,
            true,BulkPriceMethod.SetAmount,6.25m,
            true,"made_to_order","Updated sample apparel pricing");
        var preview=BulkProductOperations.Preview(matching,changes);
        if(preview.Count!=2||preview.Any(x=>x.NewPriceCents!=1150||
            x.NewCostCents!=625||x.NewStockMode!="made_to_order"))
            throw new Exception("Bulk changes preview did not compute expected values.");
        if(BulkProductOperations.Apply(preview,changes.Reason)!=2)
            throw new Exception("Bulk edit updated an unexpected number of products.");
        foreach(long id in bulkIds.Take(2))
        {
            using var actual=db.CreateCommand();
            actual.CommandText="""
                SELECT price,cost,stock_mode,reorder_cost_total,inventory_unit_cost
                FROM products WHERE id=@id
                """;
            actual.Parameters.AddWithValue("@id",id);
            using var record=actual.ExecuteReader();
            if(!record.Read()||record.GetInt64(0)!=1150||record.GetInt64(1)!=625||
                record.GetString(2)!="made_to_order"||record.GetInt64(3)!=18750||
                record.GetInt64(4)!=450)
                throw new Exception("Bulk update failed to preserve inventory basis or synchronize reorder total.");
        }
        if(FinancialOperations.Scalar(db,null,"""
            SELECT count(*) FROM lab_bulk_product_changes WHERE product_id IN (@a,@b)
                AND old_price=1000 AND new_price=1150 AND old_cost=400 AND new_cost=625
            """,("a",bulkIds[0]),("b",bulkIds[1]))!=2)
            throw new Exception("Bulk before/after price audit missing.");
        if(FinancialOperations.Scalar(db,null,
            "SELECT unit_price FROM document_items WHERE product_id=@id",("id",bulkIds[0]))!=1000)
            throw new Exception("Bulk edit changed historic invoice/estimate prices.");
        if(FinancialOperations.Scalar(db,null,
            "SELECT cost FROM products WHERE id=@id",("id",bulkIds[2]))!=400)
            throw new Exception("Archived item changed during bulk update.");

        var second=BulkProductOperations.Search("Bulk Sample Tee","BULK-"+bulkSuffix,supplier,"made_to_order");
        var attempted=BulkProductOperations.Preview(second,new BulkProductChanges(
            true,BulkPriceMethod.SetAmount,20m,
            false,BulkPriceMethod.SetAmount,0m,
            false,"made_to_order","Test atomic conflict"));
        // Simulate another editor modifying one selected product after preview.
        FinancialOperations.Execute(db,null,"UPDATE products SET price=1200 WHERE id=@id",
            ("id",bulkIds[1]));
        try
        {
            BulkProductOperations.Apply(attempted,"Reject stale preview");
            throw new Exception("Stale bulk preview was accepted.");
        }
        catch(InvalidOperationException ex) when(ex.Message.Contains("changed since the preview")) { }
        if(FinancialOperations.Scalar(db,null,"SELECT price FROM products WHERE id=@id",("id",bulkIds[0]))!=1150)
            throw new Exception("Atomic bulk edit was not rolled back after conflict.");

        var stockedItem=BulkProductOperations.Search("DEMO-TEE-BLK").First();
        try
        {
            BulkProductOperations.Preview(new[]{stockedItem},new BulkProductChanges(
                false,BulkPriceMethod.SetAmount,0m,false,BulkPriceMethod.SetAmount,0m,
                true,"supplier_direct","Cannot change stocked product with inventory"));
            throw new Exception("Bulk edit allowed changing supply type with stock on hand.");
        }
        catch(InvalidOperationException ex) when(ex.Message.Contains("units remain in inventory")) { }

        foreach(string kind in new[]{"Invoice","Estimate","Purchase Order","Report"})
        {
            string filename=Path.Combine(Path.GetTempPath(),"PhoenixLedger_CI_"+kind.Replace(' ','_')+".pdf");
            PdfDocuments.Export(kind,PdfDocuments.Sample(kind),filename);
            if(new FileInfo(filename).Length<100)throw new Exception("PDF preview failed: "+kind);
            File.Delete(filename);
        }
        // Archived entries stay in the database, preserve their original IDs,
        // and remain joined to invoices, purchase orders and accounting reports.
        CatalogArchive.SetArchived("Customers",customer,true);
        if(Repository.Table("Customers").Rows.Cast<System.Data.DataRow>()
            .Any(r=>Convert.ToInt64(r["ID"])==customer))
            throw new Exception("Archived customer visible in the active catalog.");
        if(!Repository.Table("Customers","",archived:true).Rows.Cast<System.Data.DataRow>()
            .Any(r=>Convert.ToInt64(r["ID"])==customer))
            throw new Exception("Archived customer missing from archived-only view.");
        var customerReport=BusinessData.Report("Customer sales totals");
        if(!customerReport.Rows.Cast<System.Data.DataRow>()
            .Any(r=>r["Customer"]?.ToString()=="Demo Customer"))
            throw new Exception("Historical customer sales disappeared after archiving.");
        try
        {
            CatalogArchive.Purge("Customers",customer);
            throw new Exception("Purge incorrectly deleted a customer with invoices.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("historical") ||
            e.Message.Contains("linked")) { }
        CatalogArchive.SetArchived("Customers",customer,false);

        CatalogArchive.SetArchived("Suppliers",supplier,true);
        try
        {
            FinancialOperations.CreatePurchaseOrder(supplier,new[]{new PurchaseLine(product,1,100)},
                DateTime.Today,"Should be denied",draft:true);
            throw new Exception("Archived supplier accepted on a new purchase order.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("not found")) { }
        try
        {
            CatalogArchive.Purge("Suppliers",supplier);
            throw new Exception("Supplier with linked purchase orders was purged.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("historical") ||
            e.Message.Contains("linked")) { }
        CatalogArchive.SetArchived("Suppliers",supplier,false);

        CatalogArchive.SetArchived("Products",product,true);
        try
        {
            CatalogArchive.Purge("Products",product);
            throw new Exception("Product with stock and history was purged.");
        }
        catch(InvalidOperationException e) when(e.Message.Contains("inventory")) { }
        CatalogArchive.SetArchived("Products",product,false);

        string suffix=Guid.NewGuid().ToString("N")[..10];
        FinancialOperations.Execute(db,null,"INSERT INTO customers(name) VALUES(@n)",
            ("n","Unused Customer "+suffix));
        long disposableCustomer=FinancialOperations.Scalar(db,null,"SELECT max(id) FROM customers");
        FinancialOperations.Execute(db,null,"INSERT INTO suppliers(name) VALUES(@n)",
            ("n","Unused Supplier "+suffix));
        long disposableSupplier=FinancialOperations.Scalar(db,null,"SELECT max(id) FROM suppliers");
        FinancialOperations.Execute(db,null,"INSERT INTO products(sku,name,qty) VALUES(@sku,@name,0)",
            ("sku","ARCHIVE-TEST-"+suffix),("name","Unused Product "+suffix));
        long disposableProduct=FinancialOperations.Scalar(db,null,"SELECT max(id) FROM products");

        foreach(var (section,id) in new[]{
            ("Customers",disposableCustomer),
            ("Suppliers",disposableSupplier),
            ("Products",disposableProduct)
        })
        {
            CatalogArchive.SetArchived(section,id,true);
            var info=CatalogArchive.Info(section,id);
            if(!info.CanPurge)throw new Exception("Unreferenced "+section+" cannot be purged.");
            CatalogArchive.Purge(section,id);
            if(CatalogArchive.IsCatalog(section) &&
                Repository.Table(section).Rows.Cast<System.Data.DataRow>()
                    .Any(row=>Convert.ToInt64(row["ID"])==id))
                throw new Exception("Purged record remains in active list.");
            string table=section switch{"Customers"=>"customers","Suppliers"=>"suppliers",_=>"products"};
            if(FinancialOperations.Scalar(db,null,$"SELECT count(*) FROM {table} WHERE id=@id",("id",id))!=0)
                throw new Exception("Purged catalog entry still exists.");
            if(FinancialOperations.Scalar(db,null,"""
                SELECT count(*) FROM lab_catalog_archive_events
                WHERE entity_type=@section AND entity_id=@id AND action='Purge'
                """,("section",section),("id",id))!=1)
                throw new Exception("Purge audit entry missing.");
        }

    }
}
