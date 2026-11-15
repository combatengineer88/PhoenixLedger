using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PhoenixLedger.CSharpLab;

/// <summary>
/// C#-Lab-only UI data helpers. Financial posting in the Python application is
/// intentionally NOT called: the existing Python database is never written.
/// </summary>
internal static class BusinessData
{
    public static readonly string[] ReportNames =
    {
        "Sales by month", "Payments by month", "Expenses by category",
        "Low stock and reorder needs", "Invoice balances",
        "Supplier product catalog", "Customer sales totals"
    };

    public static bool HasTable(SqliteConnection db, string table)
    {
        using var c=db.CreateCommand();
        c.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@name";
        c.Parameters.AddWithValue("@name",table);
        return Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture)>0;
    }

    public static string AccountingSql()
    {
        using var db=Store.Open();
        string lab="""
          SELECT j.id + 1000000000 AS ID,j.posted_on AS Date,j.memo AS Memo,
                 j.source_type AS Source,
                 printf('$%.2f',coalesce(sum(l.debit),0)/100.0) AS Debits,
                 printf('$%.2f',coalesce(sum(l.credit),0)/100.0) AS Credits
          FROM lab_fin_journal j JOIN lab_fin_lines l ON l.journal_id=j.id
          GROUP BY j.id
          """;
        string legacy=HasTable(db,"journal")&&HasTable(db,"journal_lines")?"""
          UNION ALL
          SELECT j.id AS ID,j.posted_on AS Date,j.memo AS Memo,
                 j.source_type AS Source,
                 printf('$%.2f',coalesce(sum(l.debit),0)/100.0) AS Debits,
                 printf('$%.2f',coalesce(sum(l.credit),0)/100.0) AS Credits
          FROM journal j JOIN journal_lines l ON l.journal_id=j.id GROUP BY j.id
          """:"";
        return "SELECT * FROM ("+lab+legacy+
            ") WHERE Memo LIKE @q OR Source LIKE @q OR Date LIKE @q ORDER BY Date DESC,ID DESC LIMIT 1500";
    }

    public static DataTable Report(string name)
    {
        using var db=Store.Open();
        string sql=name switch
        {
            "Sales by month" => """
                SELECT substr(created,1,7) AS Month, count(*) AS Invoices,
                       printf('$%.2f',COALESCE(sum(total),0)/100.0) AS Billed
                FROM documents WHERE kind='Invoice'
                GROUP BY substr(created,1,7) ORDER BY Month DESC
                """,
            "Expenses by category" => """
                SELECT category AS Category, count(*) AS Transactions,
                       printf('$%.2f',coalesce(sum(amount),0)/100.0) AS Spent
                FROM expenses GROUP BY category ORDER BY sum(amount) DESC
                """,
            "Low stock and reorder needs" => """
                SELECT sku AS SKU,name AS Product,qty AS OnHand,
                       reorder_point AS ReorderAt,reorder_qty AS ReorderQty,
                       COALESCE(stock_mode,'stocked') AS StockMode
                FROM products WHERE stock_mode='stocked' AND qty<=reorder_point
                ORDER BY qty,name
                """,
            "Supplier product catalog" => """
                SELECT s.name AS Supplier,p.sku AS SKU,p.name AS Product,
                       printf('$%.2f',p.cost/100.0) AS ItemCost,
                       p.qty AS Stock
                FROM products p LEFT JOIN suppliers s ON s.id=p.supplier_id
                ORDER BY s.name,p.name
                """,
            "Customer sales totals" => """
                SELECT c.name AS Customer,count(d.id) AS Invoices,
                       printf('$%.2f',coalesce(sum(d.total),0)/100.0) AS Billed
                FROM customers c LEFT JOIN documents d
                     ON d.customer_id=c.id AND d.kind='Invoice'
                GROUP BY c.id ORDER BY sum(d.total) DESC
                """,
            "Payments by month" when HasTable(db,"payments") => """
                SELECT substr(paid_on,1,7) AS Month,
                       printf('$%.2f',coalesce(sum(amount),0)/100.0) AS Received
                FROM payments GROUP BY substr(paid_on,1,7) ORDER BY Month DESC
                """,
            "Invoice balances" when HasTable(db,"payments") => """
                SELECT d.id AS ID,c.name AS Customer,d.created AS Date,
                       printf('$%.2f',d.total/100.0) AS Total,
                       printf('$%.2f',coalesce(sum(p.amount),0)/100.0) AS Paid,
                       printf('$%.2f',(d.total-coalesce(sum(p.amount),0))/100.0) AS Balance
                FROM documents d LEFT JOIN customers c ON c.id=d.customer_id
                     LEFT JOIN payments p ON p.document_id=d.id
                WHERE d.kind='Invoice' GROUP BY d.id ORDER BY d.id DESC
                """,
            "Invoice balances" => """
                SELECT d.id AS ID,c.name AS Customer,d.created AS Date,
                       printf('$%.2f',d.total/100.0) AS InvoiceTotal,
                       'Payment tracking unavailable in this Lab database' AS Note
                FROM documents d LEFT JOIN customers c ON c.id=d.customer_id
                WHERE d.kind='Invoice' ORDER BY d.id DESC
                """,
            "Payments by month" => """
                SELECT 'Payment table is not present in the C# Lab database' AS Note
                """,
            _=>throw new ArgumentException("Unknown report",nameof(name))
        };
        using var cmd=db.CreateCommand();cmd.CommandText=sql;
        using var reader=cmd.ExecuteReader();
        var table=new DataTable();table.Load(reader);return table;
    }

    public static (DateTime Date,string Category,string Vendor,string Description,decimal Amount) LoadExpense(long id)
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT spent_on,category,COALESCE(vendor,''),COALESCE(description,''),amount FROM expenses WHERE id=@id";
        cmd.Parameters.AddWithValue("@id",id);
        using var r=cmd.ExecuteReader();
        if(!r.Read())throw new InvalidOperationException("Expense no longer exists.");
        DateTime date=DateTime.TryParse(r.GetString(0),out var parsed)?parsed:DateTime.Today;
        return (date,r.GetString(1),r.GetString(2),r.GetString(3),Repository.Dollars(r.GetInt64(4)));
    }

    public static void SaveExpense(long id,DateTime date,string category,string vendor,string description,decimal amount)
    {
        if(string.IsNullOrWhiteSpace(category))throw new InvalidOperationException("Expense category is required.");
        if(amount<0)throw new InvalidOperationException("Expense cannot be negative.");
        long cents=Repository.Cents(amount);
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText=id>0?"""
            UPDATE expenses SET spent_on=@date,category=@category,vendor=@vendor,
                description=@description,amount=@amount WHERE id=@id
            """:"""
            INSERT INTO expenses(spent_on,category,vendor,description,amount)
                VALUES(@date,@category,@vendor,@description,@amount)
            """;
        cmd.Parameters.AddWithValue("@date",date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@category",category.Trim());
        cmd.Parameters.AddWithValue("@vendor",vendor.Trim());
        cmd.Parameters.AddWithValue("@description",description.Trim());
        cmd.Parameters.AddWithValue("@amount",cents);
        cmd.Parameters.AddWithValue("@id",id);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Expense was not saved.");
    }

    public static void DeleteExpense(long id)
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="DELETE FROM expenses WHERE id=@id";
        cmd.Parameters.AddWithValue("@id",id);
        if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Expense was not found.");
    }

    public static void BackupTo(string destination)
    {
        string full=Path.GetFullPath(destination);
        if(string.Equals(full,Path.GetFullPath(Store.Database),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different location from the active database.");
        if(File.Exists(full))
            throw new InvalidOperationException("Backup file already exists. Choose a new filename.");
        using var source=Store.Open();
        using var target=new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource=full, Mode=SqliteOpenMode.ReadWriteCreate }.ToString());
        target.Open();
        source.BackupDatabase(target);
        using var cmd=target.CreateCommand();
        cmd.CommandText="PRAGMA quick_check";
        if(!string.Equals(cmd.ExecuteScalar()?.ToString(),"ok",StringComparison.OrdinalIgnoreCase))
            throw new IOException("The database backup failed its integrity check.");
    }

    public static string Setting(string key)
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT value FROM settings WHERE key=@key";
        cmd.Parameters.AddWithValue("@key",key);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    public static void SetSettings(IReadOnlyDictionary<string,string> values)
    {
        using var db=Store.Open();using var tx=db.BeginTransaction();
        foreach(var (key,value) in values)
        {
            using var cmd=db.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="INSERT INTO settings(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("@key",key);
            cmd.Parameters.AddWithValue("@value",value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static string DataCounts()
    {
        var s=Repository.Stats();
        return $"Products: {s.Products:N0}   Customers: {s.Customers:N0}   " +
               $"Suppliers: {s.Suppliers:N0}   Low stock: {s.Low:N0}   " +
               $"Stock value: {s.StockValue:C2}";
    }
}
