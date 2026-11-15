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
        foreach(string kind in new[]{"Invoice","Estimate","Purchase Order","Report"})
        {
            string filename=Path.Combine(Path.GetTempPath(),"PhoenixLedger_CI_"+kind.Replace(' ','_')+".pdf");
            PdfDocuments.Export(kind,PdfDocuments.Sample(kind),filename);
            if(new FileInfo(filename).Length<100)throw new Exception("PDF preview failed: "+kind);
            File.Delete(filename);
        }
    }
}
