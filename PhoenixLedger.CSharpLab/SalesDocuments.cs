using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal sealed record SalesLine(long? ProductId,string Description,int Quantity,long UnitPriceCents);
internal sealed record SalesDocumentInfo(long Id,string Kind,string Status,long CustomerId,DateTime Date,
    string Notes,long Subtotal,long Tax,long Total,IReadOnlyList<SalesLine> Lines,long? SourceEstimateId);
internal sealed record SalesProduct(long Id,string Sku,string Name,long PriceCents);

/// <summary>
/// Estimate workflow: Draft -> Approved -> Converted (one linked invoice).
/// Direct Invoice creation: Open -> Partially Paid -> Paid via existing payment workflow.
/// Documents retain snapshot prices; posting is conditional on legacy triggers.
/// </summary>
internal static class SalesDocuments
{
    private static string Date(DateTime d)=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    internal static void Initialize()
    {
        using var db=Store.Open();
        FinancialOperations.Execute(db,null,"""
            CREATE TABLE IF NOT EXISTS lab_estimate_invoice_links(
                estimate_id INTEGER PRIMARY KEY REFERENCES documents(id),
                invoice_id INTEGER NOT NULL UNIQUE REFERENCES documents(id),
                converted_on TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE IF NOT EXISTS lab_sales_document_events(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                event_type TEXT NOT NULL,
                detail TEXT NOT NULL DEFAULT '',
                logged_on TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_lab_sales_document_events
                ON lab_sales_document_events(document_id,id);
            """);
    }

    internal static IReadOnlyList<(long Id,string Name)> Customers()
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM customers WHERE is_archived=0 ORDER BY name COLLATE NOCASE";
        using var r=cmd.ExecuteReader();var list=new List<(long,string)>();
        while(r.Read())list.Add((r.GetInt64(0),r.GetString(1)));
        return list;
    }

    internal static IReadOnlyList<SalesProduct> Products()
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id,sku,name,price FROM products
            WHERE is_archived=0 ORDER BY name COLLATE NOCASE,sku
            """;
        using var r=cmd.ExecuteReader();var list=new List<SalesProduct>();
        while(r.Read())list.Add(new SalesProduct(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3)));
        return list;
    }

    internal static (long Subtotal,long Tax,long Total) Totals(
        IReadOnlyList<SalesLine> lines,decimal taxRate)
    {
        if(lines.Count==0||lines.Count>1000)
            throw new InvalidOperationException("Add between 1 and 1,000 line items.");
        if(taxRate<0||taxRate>100)
            throw new InvalidOperationException("The tax percentage must be between 0 and 100.");
        long subtotal=0;
        foreach(var line in lines)
        {
            if(line.Quantity<=0||line.Quantity>1000000||line.UnitPriceCents<0 ||
               string.IsNullOrWhiteSpace(line.Description))
                throw new InvalidOperationException("Each line needs a description, positive quantity, and nonnegative price.");
            subtotal=checked(subtotal+checked(line.UnitPriceCents*line.Quantity));
        }
        if(subtotal<=0)
            throw new InvalidOperationException("The document must have a total greater than zero.");
        long tax=checked((long)decimal.Round(subtotal*taxRate/100m,0,MidpointRounding.AwayFromZero));
        return(subtotal,tax,checked(subtotal+tax));
    }

    private static void AddLines(SqliteConnection db,SqliteTransaction tx,long documentId,
        IReadOnlyList<SalesLine> lines,bool requireActive)
    {
        foreach(var line in lines)
        {
            if(line.ProductId is long product && requireActive &&
               FinancialOperations.Scalar(db,tx,
                   "SELECT count(*) FROM products WHERE id=@id AND is_archived=0",("id",product))!=1)
                throw new InvalidOperationException(
                    "A selected product is no longer active. Remove it or use a custom line.");
            FinancialOperations.Execute(db,tx,"""
                INSERT INTO document_items(document_id,product_id,description,qty,unit_price,line_total)
                VALUES(@doc,@product,@desc,@qty,@price,@total)
                """,
                ("doc",documentId),("product",line.ProductId),
                ("desc",line.Description.Trim()),("qty",line.Quantity),
                ("price",line.UnitPriceCents),
                ("total",checked(line.Quantity*line.UnitPriceCents)));
        }
    }

    private static void Event(SqliteConnection db,SqliteTransaction tx,long documentId,string kind,string detail)
    {
        FinancialOperations.Execute(db,tx,"""
            INSERT INTO lab_sales_document_events(document_id,event_type,detail)
            VALUES(@id,@kind,@detail)
            """,("id",documentId),("kind",kind),("detail",detail));
    }

    private static bool LegacyInvoicePosted(SqliteConnection db,SqliteTransaction tx,long id)=>
        BusinessData.HasTable(db,"journal") &&
        FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM journal WHERE source_type='invoice' AND source_id=@id",("id",id))>0;

    private static void RecognizeInvoice(SqliteConnection db,SqliteTransaction tx,
        long id,DateTime when,long subtotal,long tax,long total)
    {
        // Python snapshots may have AFTER INSERT triggers that already post
        // the invoice journal. Never post the same sale twice.
        if(LegacyInvoicePosted(db,tx,id))return;
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM lab_fin_journal WHERE source_type='lab_sales_invoice' AND source_id=@id",
            ("id",id))>0)return;
        if(tax>0)
            FinancialOperations.Post(db,tx,when,$"Invoice #{id} issued","lab_sales_invoice",id,
                ("Accounts Receivable",total,0),
                ("Sales Revenue",0,subtotal),("Sales Tax Payable",0,tax));
        else
            FinancialOperations.Post(db,tx,when,$"Invoice #{id} issued","lab_sales_invoice",id,
                ("Accounts Receivable",total,0),("Sales Revenue",0,subtotal));
    }

    public static long Create(string kind,long customerId,DateTime on,string notes,
        IReadOnlyList<SalesLine> lines,decimal taxRate)
    {
        if(kind is not ("Invoice" or "Estimate"))
            throw new InvalidOperationException("Select Invoice or Estimate.");
        var (subtotal,tax,total)=Totals(lines,taxRate);
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM customers WHERE id=@id AND is_archived=0",("id",customerId))!=1)
            throw new InvalidOperationException("Select an active customer.");
        string status=kind=="Estimate"?"Draft":"Open";
        FinancialOperations.Execute(db,tx,"""
            INSERT INTO documents(kind,customer_id,created,status,subtotal,tax,total,notes)
            VALUES(@kind,@customer,@date,@status,@subtotal,@tax,@total,@notes)
            """,("kind",kind),("customer",customerId),("date",Date(on)),("status",status),
            ("subtotal",subtotal),("tax",tax),("total",total),("notes",notes.Trim()));
        long id=FinancialOperations.Scalar(db,tx,"SELECT last_insert_rowid()");
        AddLines(db,tx,id,lines,true);
        Event(db,tx,id,"Created",kind+" as "+status);
        if(kind=="Invoice")RecognizeInvoice(db,tx,id,on,subtotal,tax,total);
        tx.Commit();
        return id;
    }

    public static SalesDocumentInfo Load(long id)
    {
        using var db=Store.Open();
        using var header=db.CreateCommand();
        header.CommandText="""
            SELECT kind,status,customer_id,created,coalesce(notes,''),
                   subtotal,tax,total
            FROM documents WHERE id=@id AND kind IN ('Invoice','Estimate')
            """;
        header.Parameters.AddWithValue("@id",id);
        using var r=header.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("Sales document not found.");
        string kind=r.GetString(0),status=r.GetString(1);
        long customer=r.GetInt64(2);
        DateTime on=DateTime.TryParse(r.GetString(3),out var dt)?dt:DateTime.Today;
        string notes=r.GetString(4);
        long subtotal=r.GetInt64(5),tax=r.GetInt64(6),total=r.GetInt64(7);
        r.Close();
        using var items=db.CreateCommand();
        items.CommandText="""
            SELECT product_id,description,qty,unit_price FROM document_items
            WHERE document_id=@id ORDER BY id
            """;
        items.Parameters.AddWithValue("@id",id);
        using var lines=items.ExecuteReader();var rows=new List<SalesLine>();
        while(lines.Read())
            rows.Add(new SalesLine(lines.IsDBNull(0)?null:lines.GetInt64(0),
                lines.GetString(1),lines.GetInt32(2),lines.GetInt64(3)));
        lines.Close();
        using var link=db.CreateCommand();
        link.CommandText="SELECT estimate_id FROM lab_estimate_invoice_links WHERE invoice_id=@id";
        link.Parameters.AddWithValue("@id",id);
        object? src=link.ExecuteScalar();
        return new SalesDocumentInfo(id,kind,status,customer,on,notes,subtotal,tax,total,rows,
            src is null?null:Convert.ToInt64(src,CultureInfo.InvariantCulture));
    }

    public static void UpdateDraftEstimate(long id,long customerId,DateTime date,string notes,
        IReadOnlyList<SalesLine> lines,decimal taxRate)
    {
        var (subtotal,tax,total)=Totals(lines,taxRate);
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM customers WHERE id=@id AND is_archived=0",("id",customerId))!=1)
            throw new InvalidOperationException("Choose an active customer.");
        if(FinancialOperations.Execute(db,tx,"""
            UPDATE documents SET customer_id=@customer,created=@date,notes=@notes,
                subtotal=@subtotal,tax=@tax,total=@total
            WHERE id=@id AND kind='Estimate' AND status='Draft'
            """,("customer",customerId),("date",Date(date)),("notes",notes.Trim()),
            ("subtotal",subtotal),("tax",tax),("total",total),("id",id))!=1)
            throw new InvalidOperationException("Only draft estimates can be edited.");
        FinancialOperations.Execute(db,tx,"DELETE FROM document_items WHERE document_id=@id",("id",id));
        AddLines(db,tx,id,lines,true);
        Event(db,tx,id,"Edited","Draft estimate lines updated");
        tx.Commit();
    }

    public static void Approve(long estimateId)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(FinancialOperations.Execute(db,tx,"""
            UPDATE documents SET status='Approved' WHERE id=@id
            AND kind='Estimate' AND status='Draft'
            """,("id",estimateId))!=1)
            throw new InvalidOperationException("Only draft estimates can be approved.");
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM document_items WHERE document_id=@id",("id",estimateId))==0)
            throw new InvalidOperationException("Cannot approve an estimate without items.");
        Event(db,tx,estimateId,"Approved","Estimate approved for conversion");
        tx.Commit();
    }

    public static long ConvertApprovedEstimate(long estimateId,DateTime invoiceDate)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM lab_estimate_invoice_links WHERE estimate_id=@id",("id",estimateId))>0)
            throw new InvalidOperationException("This estimate was already converted into an invoice.");
        using var estimate=db.CreateCommand();
        estimate.Transaction=tx;
        estimate.CommandText="""
            SELECT customer_id,coalesce(notes,''),subtotal,tax,total
            FROM documents WHERE id=@id AND kind='Estimate' AND status='Approved'
            """;
        estimate.Parameters.AddWithValue("@id",estimateId);
        using var r=estimate.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("Approve the draft estimate before converting it.");
        long customer=r.GetInt64(0);
        string notes=r.GetString(1);
        long subtotal=r.GetInt64(2),tax=r.GetInt64(3),total=r.GetInt64(4);
        r.Close();

        FinancialOperations.Execute(db,tx,"""
            INSERT INTO documents(kind,customer_id,created,status,subtotal,tax,total,notes)
            VALUES('Invoice',@customer,@date,'Open',@subtotal,@tax,@total,@notes)
            """,("customer",customer),("date",Date(invoiceDate)),("subtotal",subtotal),
            ("tax",tax),("total",total),("notes",notes));
        long invoiceId=FinancialOperations.Scalar(db,tx,"SELECT last_insert_rowid()");
        // Exact snapshot: do not reprice after catalog prices change.
        // Retain historic product IDs even if archived since estimate approval.
        FinancialOperations.Execute(db,tx,"""
            INSERT INTO document_items(document_id,product_id,description,qty,unit_price,line_total)
            SELECT @invoice,product_id,description,qty,unit_price,line_total
            FROM document_items WHERE document_id=@estimate
            """,("invoice",invoiceId),("estimate",estimateId));
        if(FinancialOperations.Scalar(db,tx,
            "SELECT count(*) FROM document_items WHERE document_id=@id",("id",invoiceId))==0)
            throw new InvalidOperationException("Approved estimate contains no items.");
        FinancialOperations.Execute(db,tx,"""
            INSERT INTO lab_estimate_invoice_links(estimate_id,invoice_id)
            VALUES(@estimate,@invoice)
            """,("estimate",estimateId),("invoice",invoiceId));
        if(FinancialOperations.Execute(db,tx,"""
            UPDATE documents SET status='Converted'
            WHERE id=@id AND kind='Estimate' AND status='Approved'
            """,("id",estimateId))!=1)
            throw new InvalidOperationException("Estimate status changed. Refresh and retry.");
        Event(db,tx,estimateId,"Converted","Converted into invoice #"+invoiceId);
        Event(db,tx,invoiceId,"Created from estimate","Estimate #"+estimateId);
        RecognizeInvoice(db,tx,invoiceId,invoiceDate,subtotal,tax,total);
        tx.Commit();
        return invoiceId;
    }
}
