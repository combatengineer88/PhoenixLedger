using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal sealed record PurchaseLine(long ProductId,int Quantity,long UnitCents);
internal sealed record ReceiptLine(long LineId,int Quantity);
internal sealed record PurchaseItem(long Id,long ProductId,string Sku,string Name,int Ordered,int Received,long CostCents);

/// <summary>
/// C# Lab financial posting. Database writes share one SQLite transaction, with
/// balanced debit/credit entries in an independently namespaced audit ledger.
/// The original Python business.db is NEVER opened for writing.
/// </summary>
internal static class FinancialOperations
{
    public static void Initialize()
    {
        using var db=Store.Open();
        using (var c=db.CreateCommand())
        {
            c.CommandText="""
              CREATE TABLE IF NOT EXISTS lab_fin_accounts(
                name TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE, type TEXT NOT NULL);
              CREATE TABLE IF NOT EXISTS lab_fin_journal(
                id INTEGER PRIMARY KEY, posted_on TEXT NOT NULL, memo TEXT NOT NULL,
                source_type TEXT NOT NULL, source_id INTEGER NOT NULL,
                UNIQUE(source_type,source_id));
              CREATE TABLE IF NOT EXISTS lab_fin_lines(
                id INTEGER PRIMARY KEY,journal_id INTEGER NOT NULL REFERENCES lab_fin_journal(id),
                account TEXT NOT NULL,debit INTEGER NOT NULL DEFAULT 0,credit INTEGER NOT NULL DEFAULT 0,
                CHECK(debit>=0 AND credit>=0));
              CREATE TABLE IF NOT EXISTS lab_fin_expense_changes(
                id INTEGER PRIMARY KEY, expense_id INTEGER NOT NULL,
                changed_on TEXT NOT NULL, old_amount INTEGER NOT NULL,
                new_amount INTEGER NOT NULL,old_category TEXT NOT NULL,
                new_category TEXT NOT NULL,operation TEXT NOT NULL);
              CREATE TABLE IF NOT EXISTS lab_fin_budgets(
                month TEXT NOT NULL,account TEXT NOT NULL,amount INTEGER NOT NULL,
                PRIMARY KEY(month,account));
              CREATE TABLE IF NOT EXISTS lab_fin_bank(
                id INTEGER PRIMARY KEY,posted_on TEXT NOT NULL,description TEXT NOT NULL,
                amount INTEGER NOT NULL,cleared INTEGER NOT NULL DEFAULT 0 CHECK(cleared IN (0,1)));
              CREATE TABLE IF NOT EXISTS po_receipts(
                id INTEGER PRIMARY KEY, po_id INTEGER NOT NULL REFERENCES purchase_orders(id),
                received_on TEXT NOT NULL,total INTEGER NOT NULL,notes TEXT NOT NULL DEFAULT '');
              CREATE TABLE IF NOT EXISTS po_receipt_lines(
                id INTEGER PRIMARY KEY,receipt_id INTEGER NOT NULL REFERENCES po_receipts(id),
                po_item_id INTEGER NOT NULL REFERENCES po_items(id),
                qty INTEGER NOT NULL,unit_cost INTEGER NOT NULL);
              CREATE TABLE IF NOT EXISTS supplier_payments(
                id INTEGER PRIMARY KEY,po_id INTEGER NOT NULL REFERENCES purchase_orders(id),
                paid_on TEXT NOT NULL,amount INTEGER NOT NULL,method TEXT NOT NULL DEFAULT '',
                reference TEXT NOT NULL DEFAULT '');
              CREATE TABLE IF NOT EXISTS payments(
                id INTEGER PRIMARY KEY,document_id INTEGER NOT NULL REFERENCES documents(id),
                paid_on TEXT NOT NULL,amount INTEGER NOT NULL,method TEXT NOT NULL DEFAULT '');
              CREATE INDEX IF NOT EXISTS idx_lab_fin_lines_journal ON lab_fin_lines(journal_id);
              CREATE INDEX IF NOT EXISTS idx_lab_fin_journal_date ON lab_fin_journal(posted_on);
              CREATE INDEX IF NOT EXISTS idx_lab_fin_invoice_payment ON payments(document_id);
              CREATE INDEX IF NOT EXISTS idx_lab_fin_supplier_payment ON supplier_payments(po_id);
            """;
            c.ExecuteNonQuery();
        }
        bool hasReceived=false;
        using(var info=db.CreateCommand())
        {
            info.CommandText="PRAGMA table_info(po_items)";
            using var rd=info.ExecuteReader();
            while(rd.Read())if(rd.GetString(1)=="received_qty")hasReceived=true;
        }
        if(!hasReceived)
        {
            Execute(db,null,"ALTER TABLE po_items ADD COLUMN received_qty INTEGER NOT NULL DEFAULT 0");
            Execute(db,null,"UPDATE po_items SET received_qty=qty WHERE po_id IN (SELECT id FROM purchase_orders WHERE status='Received')");
        }
        (string name,string code,string type)[] accounts={
            ("Cash","1000","Asset"),("Accounts Receivable","1100","Asset"),
            ("Inventory","1200","Asset"),("Other Assets","1900","Asset"),
            ("Accounts Payable","2000","Liability"),("Sales Tax Payable","2100","Liability"),
            ("Owner Equity","3000","Equity"),("Opening Balance Equity","3100","Equity"),
            ("Sales Revenue","4000","Revenue"),("Other Revenue","4100","Revenue"),
            ("Cost of Goods Sold","5000","Expense"),("Operating Expenses","6000","Expense")
        };
        foreach(var (name,code,type) in accounts)
            Execute(db,null,"INSERT OR IGNORE INTO lab_fin_accounts(name,code,type) VALUES(@name,@code,@type)",
                ("name",name),("code",code),("type",type));
    }

    private static SqliteCommand Command(SqliteConnection db,SqliteTransaction? tx,string sql,
        params (string key,object? val)[] args)
    {
        var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (key,val) in args)cmd.Parameters.AddWithValue("@"+key,val??DBNull.Value);
        return cmd;
    }
    internal static int Execute(SqliteConnection db,SqliteTransaction? tx,string sql,
        params (string key,object? val)[] args)
    {
        using var c=Command(db,tx,sql,args);
        return c.ExecuteNonQuery();
    }
    internal static long Scalar(SqliteConnection db,SqliteTransaction? tx,string sql,
        params (string key,object? val)[] args)
    {
        using var c=Command(db,tx,sql,args);
        return Convert.ToInt64(c.ExecuteScalar()??0L,CultureInfo.InvariantCulture);
    }
    private static string Date(DateTime date)=>date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private static long NewId(SqliteConnection db,SqliteTransaction tx)=>Scalar(db,tx,"SELECT last_insert_rowid()");

    internal static void Post(SqliteConnection db,SqliteTransaction tx,DateTime when,
        string memo,string source,long sourceId,params (string account,long debit,long credit)[] entries)
    {
        long debits=entries.Sum(e=>e.debit),credits=entries.Sum(e=>e.credit);
        if(debits!=credits || debits<=0 || entries.Any(e=>e.debit<0||e.credit<0))
            throw new InvalidOperationException("Financial transaction is not balanced.");
        Execute(db,tx,"INSERT INTO lab_fin_journal(posted_on,memo,source_type,source_id) VALUES(@d,@m,@s,@id)",
            ("d",Date(when)),("m",memo),("s",source),("id",sourceId));
        long jid=NewId(db,tx);
        foreach(var e in entries)
            Execute(db,tx,"INSERT INTO lab_fin_lines(journal_id,account,debit,credit) VALUES(@j,@a,@d,@c)",
                ("j",jid),("a",e.account),("d",e.debit),("c",e.credit));
    }

    public static void AddAccount(string name,string code,string type)
    {
        name=name.Trim();code=code.Trim();
        if(name.Length<2||code.Length<3||type is not ("Asset" or "Liability" or "Equity" or "Revenue" or "Expense"))
            throw new InvalidOperationException("Enter an account name, a unique code and a valid account category.");
        using var db=Store.Open();
        Execute(db,null,"INSERT INTO lab_fin_accounts(name,code,type) VALUES(@name,@code,@type)",
            ("name",name),("code",code),("type",type));
    }

    public static void ManualJournal(DateTime when,string memo,
        IReadOnlyList<(string Account,long Debit,long Credit)> entries)
    {
        if(string.IsNullOrWhiteSpace(memo)||entries.Count<2)
            throw new InvalidOperationException("Provide a memo and at least two journal lines.");
        if(entries.Any(x=>string.IsNullOrWhiteSpace(x.Account)||x.Debit<0||x.Credit<0||
            (x.Debit>0&&x.Credit>0)))
            throw new InvalidOperationException("Each journal line needs one nonnegative debit or credit.");
        long debits=entries.Sum(x=>x.Debit),credits=entries.Sum(x=>x.Credit);
        if(debits!=credits||debits<=0)throw new InvalidOperationException("Journal debits and credits must balance.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        foreach(var entry in entries)
        {
            if(Scalar(db,tx,"SELECT count(*) FROM lab_fin_accounts WHERE name=@a",("a",entry.Account))!=1)
                throw new InvalidOperationException("Unknown account: "+entry.Account);
        }
        Execute(db,tx,"""
            CREATE TABLE IF NOT EXISTS lab_fin_manual_ids(
                id INTEGER PRIMARY KEY,memo TEXT NOT NULL)
            """);
        Execute(db,tx,"INSERT INTO lab_fin_manual_ids(memo) VALUES(@memo)",("memo",memo.Trim()));
        long id=NewId(db,tx);
        Post(db,tx,when,memo.Trim(),"lab_manual_journal",id,
            entries.Select(x=>(x.Account,x.Debit,x.Credit)).ToArray());
        tx.Commit();
    }

    public static long CreatePurchaseOrder(long supplierId,IReadOnlyList<PurchaseLine> lines,
        DateTime when,string notes,bool draft)
    {
        if(supplierId<1 || lines.Count==0)throw new InvalidOperationException("Select a supplier and at least one product.");
        if(lines.Any(x=>x.Quantity<=0||x.UnitCents<0))
            throw new InvalidOperationException("PO quantities must be positive and costs cannot be negative.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(Scalar(db,tx,"SELECT count(*) FROM suppliers WHERE id=@id",("id",supplierId))!=1)
            throw new InvalidOperationException("Supplier not found.");
        foreach(var item in lines)
        {
            if(Scalar(db,tx,"SELECT count(*) FROM products WHERE id=@id",("id",item.ProductId))!=1)
                throw new InvalidOperationException("A selected product no longer exists.");
        }
        long total=checked(lines.Sum(x=>checked(x.Quantity*x.UnitCents)));
        Execute(db,tx, "INSERT INTO purchase_orders(supplier_id,ordered_on,status,notes,total) VALUES(@s,@d,@st,@n,@t)",
            ("s",supplierId),("d",Date(when)),("st",draft?"Draft":"Open"),("n",notes),("t",total));
        long po=NewId(db,tx);
        foreach(var item in lines)
            Execute(db,tx,"INSERT INTO po_items(po_id,product_id,qty,unit_cost,received_qty) VALUES(@po,@p,@q,@c,0)",
                ("po",po),("p",item.ProductId),("q",item.Quantity),("c",item.UnitCents));
        tx.Commit();
        return po;
    }

    public static (long SupplierId,DateTime Date,string Notes,string Status) PurchaseOrderInfo(long id)
    {
        using var db=Store.Open();using var c=db.CreateCommand();
        c.CommandText="SELECT supplier_id,ordered_on,coalesce(notes,''),status FROM purchase_orders WHERE id=@id";
        c.Parameters.AddWithValue("@id",id);
        using var r=c.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("Purchase order not found.");
        string date=r.GetString(1);
        return(r.GetInt64(0),DateTime.TryParse(date,out var parsed)?parsed:DateTime.Today,
            r.GetString(2),r.GetString(3));
    }

    public static void EditPurchaseOrder(long poId,long supplierId,IReadOnlyList<PurchaseLine> lines,DateTime on,string notes)
    {
        if(lines.Count==0||lines.Any(l=>l.Quantity<=0||l.UnitCents<0))
            throw new InvalidOperationException("Enter one or more valid purchase order lines.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        string? status;
        using(var cmd=Command(db,tx,"SELECT status FROM purchase_orders WHERE id=@id",("id",poId)))
            status=cmd.ExecuteScalar()?.ToString();
        if(status is not ("Draft" or "Open")||
           Scalar(db,tx,"SELECT count(*) FROM po_items WHERE po_id=@p AND received_qty>0",("p",poId))>0)
            throw new InvalidOperationException("Only unreceived Draft/Open purchase orders can be edited.");
        if(Scalar(db,tx,"SELECT count(*) FROM suppliers WHERE id=@s",("s",supplierId))!=1)
            throw new InvalidOperationException("Supplier is not available.");
        foreach(var item in lines)
            if(Scalar(db,tx,"SELECT count(*) FROM products WHERE id=@p",("p",item.ProductId))!=1)
                throw new InvalidOperationException("A product is no longer available.");
        long total=checked(lines.Sum(x=>checked(x.Quantity*x.UnitCents)));
        Execute(db,tx,"""
            UPDATE purchase_orders SET supplier_id=@s,ordered_on=@d,notes=@n,total=@t
            WHERE id=@id
            """,("s",supplierId),("d",Date(on)),("n",notes),("t",total),("id",poId));
        Execute(db,tx,"DELETE FROM po_items WHERE po_id=@id",("id",poId));
        foreach(var item in lines)
            Execute(db,tx,"""
                INSERT INTO po_items(po_id,product_id,qty,unit_cost,received_qty)
                VALUES(@po,@product,@q,@price,0)
                """,("po",poId),("product",item.ProductId),("q",item.Quantity),("price",item.UnitCents));
        tx.Commit();
    }

    public static void CancelPurchaseOrder(long poId)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(Scalar(db,tx,"SELECT count(*) FROM po_items WHERE po_id=@id AND received_qty>0",("id",poId))>0)
            throw new InvalidOperationException("Purchase orders with received merchandise cannot be cancelled.");
        if(Execute(db,tx,"""
            UPDATE purchase_orders SET status='Cancelled'
            WHERE id=@id AND status IN ('Draft','Open')
            """,("id",poId))!=1)
            throw new InvalidOperationException("Only unreceived Draft/Open orders can be cancelled.");
        tx.Commit();
    }

    public static void ApprovePurchaseOrder(long id)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(Execute(db,tx,"UPDATE purchase_orders SET status='Open' WHERE id=@id AND status='Draft'",("id",id))!=1)
            throw new InvalidOperationException("Only a draft purchase order can be approved.");
        tx.Commit();
    }

    public static List<PurchaseItem> PurchaseLines(long poId)
    {
        using var db=Store.Open();
        using var c=Command(db,null,"""
          SELECT l.id,l.product_id,p.sku,p.name,l.qty,l.received_qty,l.unit_cost
          FROM po_items l JOIN products p ON p.id=l.product_id
          WHERE l.po_id=@id ORDER BY l.id
        """,("id",poId));
        using var r=c.ExecuteReader();var result=new List<PurchaseItem>();
        while(r.Read())result.Add(new PurchaseItem(r.GetInt64(0),r.GetInt64(1),r.GetString(2),
            r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetInt64(6)));
        return result;
    }

    public static string Receive(long poId,IReadOnlyList<ReceiptLine> received,DateTime on,string notes)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        using(var cmd=Command(db,tx,"SELECT status FROM purchase_orders WHERE id=@id",("id",poId)))
        {
            string? purchaseOrderStatus=cmd.ExecuteScalar()?.ToString();
            if(purchaseOrderStatus is not ("Open" or "Partially Received"))
                throw new InvalidOperationException("Approve the purchase order before receiving. Fully received POs cannot be received again.");
        }
        var lines=new List<PurchaseItem>();
        using(var cmd=Command(db,tx,"""
            SELECT l.id,l.product_id,p.sku,p.name,l.qty,l.received_qty,l.unit_cost
            FROM po_items l JOIN products p ON p.id=l.product_id WHERE l.po_id=@po
            """,("po",poId)))
        using(var r=cmd.ExecuteReader())
            while(r.Read())lines.Add(new PurchaseItem(r.GetInt64(0),r.GetInt64(1),r.GetString(2),
                r.GetString(3),r.GetInt32(4),r.GetInt32(5),r.GetInt64(6)));
        var requested=received.ToDictionary(x=>x.LineId,x=>x.Quantity);
        if(requested.Count==0||requested.Values.All(x=>x==0))
            throw new InvalidOperationException("Choose at least one positive receipt quantity.");
        if(requested.Any(x=>x.Value<0||lines.All(l=>l.Id!=x.Key)))
            throw new InvalidOperationException("A receipt contains an invalid line or quantity.");
        long amount=0;
        foreach(var line in lines)
        {
            int qty=requested.GetValueOrDefault(line.Id);
            if(qty>line.Ordered-line.Received)
                throw new InvalidOperationException("Receipt exceeds the outstanding quantity for "+line.Name);
            amount=checked(amount+checked(qty*line.CostCents));
        }
        Execute(db,tx,"INSERT INTO po_receipts(po_id,received_on,total,notes) VALUES(@p,@d,@t,@n)",
            ("p",poId),("d",Date(on)),("t",amount),("n",notes));
        long receiptId=NewId(db,tx);
        foreach(var line in lines)
        {
            int qty=requested.GetValueOrDefault(line.Id);
            if(qty<=0)continue;
            Execute(db,tx,"INSERT INTO po_receipt_lines(receipt_id,po_item_id,qty,unit_cost) VALUES(@r,@l,@q,@c)",
                ("r",receiptId),("l",line.Id),("q",qty),("c",line.CostCents));
            Execute(db,tx,"UPDATE po_items SET received_qty=received_qty+@q WHERE id=@l",
                ("q",qty),("l",line.Id));
            long priorQty=Scalar(db,tx,"SELECT qty FROM products WHERE id=@id",("id",line.ProductId));
            long costBasis=Scalar(db,tx,"SELECT coalesce(inventory_unit_cost,cost) FROM products WHERE id=@id",("id",line.ProductId));
            long next=checked(priorQty+qty);
            long cost=checked((priorQty*costBasis+qty*line.CostCents+next/2)/next);
            Execute(db,tx,"UPDATE products SET qty=qty+@q,inventory_unit_cost=@cost WHERE id=@id",
                ("q",qty),("cost",cost),("id",line.ProductId));
            Execute(db,tx,"INSERT INTO lab_stock_events(product_id,before_qty,after_qty,reason,source) VALUES(@p,@b,@a,@r,'C# Lab PO')",
                ("p",line.ProductId),("b",priorQty),("a",next),("r",$"Received PO #{poId}"));
        }
        string state=Scalar(db,tx,
            "SELECT count(*) FROM po_items WHERE po_id=@p AND received_qty<qty",("p",poId))==0?"Received":"Partially Received";
        Execute(db,tx,"UPDATE purchase_orders SET status=@s WHERE id=@p",("s",state),("p",poId));
        if(amount>0)
            Post(db,tx,on,$"Receipt PO #{poId}","lab_po_receipt",receiptId,
                ("Inventory",amount,0),("Accounts Payable",0,amount));
        tx.Commit();
        return state;
    }

    public static long SupplierBalance(long poId)
    {
        using var db=Store.Open();
        return SupplierBalance(db,null,poId);
    }
    private static long SupplierBalance(SqliteConnection db,SqliteTransaction? tx,long poId)
    {
        long received=Scalar(db,tx,"SELECT coalesce(sum(total),0) FROM po_receipts WHERE po_id=@id",("id",poId));
        if(BusinessData.HasTable(db,"journal")&&BusinessData.HasTable(db,"journal_lines"))
            received+=Scalar(db,tx,"""
              SELECT coalesce(sum(l.credit-l.debit),0)
              FROM journal j JOIN journal_lines l ON l.journal_id=j.id
              WHERE j.source_type='po' AND j.source_id=@id AND l.account='Accounts Payable'
              """,("id",poId));
        long paid=Scalar(db,tx,"SELECT coalesce(sum(amount),0) FROM supplier_payments WHERE po_id=@id",("id",poId));
        return received-paid;
    }
    public static void PaySupplier(long poId,decimal dollars,DateTime when,string method,string reference)
    {
        long amount=Repository.Cents(dollars);
        if(amount<=0)throw new InvalidOperationException("Payment amount must be positive.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        if(amount>SupplierBalance(db,tx,poId))
            throw new InvalidOperationException("Payment exceeds the outstanding amount for received goods.");
        Execute(db,tx,"INSERT INTO supplier_payments(po_id,paid_on,amount,method,reference) VALUES(@p,@d,@a,@m,@r)",
            ("p",poId),("d",Date(when)),("a",amount),("m",method),("r",reference));
        long id=NewId(db,tx);
        Post(db,tx,when,$"Supplier payment PO #{poId}","lab_supplier_payment",id,
            ("Accounts Payable",amount,0),("Cash",0,amount));
        tx.Commit();
    }

    public static long InvoiceDue(long invoiceId)
    {
        using var db=Store.Open();
        return InvoiceDue(db,null,invoiceId);
    }
    private static long InvoiceDue(SqliteConnection db,SqliteTransaction? tx,long invoiceId)
    {
        using var cmd=Command(db,tx,"SELECT total,kind FROM documents WHERE id=@id",("id",invoiceId));
        using var reader=cmd.ExecuteReader();
        if(!reader.Read() || !string.Equals(reader.GetString(1),"Invoice",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select an invoice (not an estimate).");
        long total=reader.GetInt64(0);reader.Close();
        return total-Scalar(db,tx,"SELECT coalesce(sum(amount),0) FROM payments WHERE document_id=@id",("id",invoiceId));
    }

    private static bool Recognized(SqliteConnection db,SqliteTransaction tx,long invoice)
    {
        if(Scalar(db,tx,"SELECT count(*) FROM lab_fin_journal WHERE source_type='lab_invoice_opening' AND source_id=@id",("id",invoice))>0)
            return true;
        if(!BusinessData.HasTable(db,"journal"))return false;
        return Scalar(db,tx,"SELECT count(*) FROM journal WHERE source_type='invoice' AND source_id=@id",("id",invoice))>0;
    }

    public static void PayInvoice(long invoiceId,decimal dollars,DateTime when,string method)
    {
        long amount=Repository.Cents(dollars);
        if(amount<=0)throw new InvalidOperationException("Payment amount must be greater than zero.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        long due=InvoiceDue(db,tx,invoiceId);
        if(amount>due)throw new InvalidOperationException($"Overpayment is not allowed. Amount due: {Repository.Dollars(due):C2}");
        if(!Recognized(db,tx,invoiceId))
        {
            using var invoice=Command(db,tx,"SELECT created,total FROM documents WHERE id=@id",("id",invoiceId));
            using var r=invoice.ExecuteReader();
            if(!r.Read())throw new InvalidOperationException("Invoice not found.");
            DateTime original=DateTime.TryParse(r.GetString(0),out var date)?date:when;
            long total=r.GetInt64(1);r.Close();
            if(total>0)
                Post(db,tx,original,$"Recognize imported invoice #{invoiceId} opening A/R","lab_invoice_opening",invoiceId,
                    ("Accounts Receivable",total,0),("Opening Balance Equity",0,total));
        }
        Execute(db,tx,"INSERT INTO payments(document_id,paid_on,amount,method) VALUES(@id,@d,@a,@m)",
            ("id",invoiceId),("d",Date(when)),("a",amount),("m",method));
        long pid=NewId(db,tx);
        // Imported Python databases have an AFTER INSERT payment trigger that
        // posts the legacy journal automatically. Do not post it twice.
        bool triggerPosted=BusinessData.HasTable(db,"journal") &&
            Scalar(db,tx,"SELECT count(*) FROM journal WHERE source_type='payment' AND source_id=@id",("id",pid))>0;
        if(!triggerPosted)
            Post(db,tx,when,$"Payment received for invoice #{invoiceId}","lab_invoice_payment",pid,
                ("Cash",amount,0),("Accounts Receivable",0,amount));
        Execute(db,tx,"UPDATE documents SET status=@status WHERE id=@id",
            ("status",due==amount?"Paid":"Partially Paid"),("id",invoiceId));
        tx.Commit();
    }
}
