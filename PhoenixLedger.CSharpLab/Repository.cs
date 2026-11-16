using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal sealed class Product
{
    public long Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Barcode { get; set; } = "";
    public long? SupplierId { get; set; }
    public decimal Price { get; set; }
    public decimal Cost { get; set; }
    public int Quantity { get; set; }
    public int ReorderPoint { get; set; } = 5;
    public int ReorderQuantity { get; set; } = 20;
    public decimal? ReorderCost { get; set; }
    public string StockMode { get; set; } = "stocked";
    public string Bin { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string BarcodeImagePath { get; set; } = "";
}

internal sealed class Supplier
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Website { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Zip { get; set; } = "";
    public string Notes { get; set; } = "";
}

internal sealed class Customer
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Zip { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool TaxExempt { get; set; }
}

internal static class Repository
{
    public static long Cents(decimal number) => checked((long)decimal.Round(number * 100m, 0, MidpointRounding.AwayFromZero));
    public static decimal Dollars(long cents) => cents / 100m;

    public static DataTable Table(string section, string search = "", bool archived = false)
    {
        string sql = section switch
        {
            "Products" => """
                SELECT p.id AS ID, p.sku AS SKU, COALESCE(p.barcode,'') AS Barcode,
                  p.name AS Name, COALESCE(s.name,'') AS Supplier, COALESCE(p.description,'') AS Description,
                  printf('$%.2f',p.price/100.0) AS Price,
                  CASE WHEN p.reorder_cost_total IS NULL THEN '' ELSE printf('$%.2f',p.reorder_cost_total/100.0) END AS 'Reorder Cost',
                  printf('$%.2f',p.cost/100.0) AS 'Item Cost',
                  CASE WHEN p.price>0 THEN printf('%.1f%%',100.0*(p.price-p.cost)/p.price) ELSE '—' END AS 'Margin %',
                  p.qty AS Stock, p.reorder_point AS 'Reorder at', p.reorder_qty AS 'Reorder Qty',
                  p.stock_mode AS Type, p.bin_location AS Bin,
                  CASE WHEN COALESCE(p.image_path,'')<>'' THEN 'Yes' ELSE 'No' END AS Image,
                  CASE WHEN COALESCE(p.barcode_image_path,'')<>'' THEN 'Yes' ELSE 'No' END AS 'Barcode Image',
                  COALESCE(p.archived_at,'') AS 'Archived On'
                FROM products p LEFT JOIN suppliers s ON s.id=p.supplier_id
                WHERE p.is_archived=@archived AND
                  (p.sku LIKE @q OR p.name LIKE @q OR COALESCE(p.barcode,'') LIKE @q OR COALESCE(s.name,'') LIKE @q)
                ORDER BY p.name COLLATE NOCASE,p.id
                """,
            "Suppliers" => """
                SELECT s.id AS ID,s.name AS Name,COALESCE(s.contact_name,'') AS Contact,
                  COALESCE(s.email,'') AS Email,COALESCE(s.phone,'') AS Phone,
                  COALESCE(s.city,'') AS City,COALESCE(s.state,'') AS State,COALESCE(s.zip_code,'') AS ZIP,
                  (SELECT count(*) FROM products p WHERE p.supplier_id=s.id) AS Products,
                  (SELECT count(*) FROM purchase_orders po WHERE po.supplier_id=s.id) AS 'Purchase Orders',
                  COALESCE(s.archived_at,'') AS 'Archived On'
                FROM suppliers s
                WHERE s.is_archived=@archived AND
                  (s.name LIKE @q OR COALESCE(s.contact_name,'') LIKE @q OR COALESCE(s.email,'') LIKE @q)
                ORDER BY s.name COLLATE NOCASE,s.id
                """,
            "Customers" => """
                SELECT id AS ID,name AS Name,COALESCE(email,'') AS Email,COALESCE(phone,'') AS Phone,
                COALESCE(address,'') AS Address,COALESCE(city,'') AS City,
                COALESCE(state,'') AS State,COALESCE(zip_code,'') AS ZIP,
                CASE WHEN tax_exempt=1 THEN 'Yes' ELSE 'No' END AS 'Tax Exempt',
                COALESCE(archived_at,'') AS 'Archived On'
                FROM customers WHERE is_archived=@archived AND
                  (name LIKE @q OR COALESCE(email,'') LIKE @q OR COALESCE(city,'') LIKE @q)
                ORDER BY name COLLATE NOCASE,id
                """,
            "Purchase Orders" => """
                SELECT po.id AS ID,s.name AS Supplier,po.ordered_on AS Date,po.status AS Status,
                  printf('$%.2f',po.total/100.0) AS Total,COALESCE(po.notes,'') AS Notes
                FROM purchase_orders po LEFT JOIN suppliers s ON s.id=po.supplier_id
                WHERE CAST(po.id AS TEXT) LIKE @q OR COALESCE(s.name,'') LIKE @q OR po.status LIKE @q
                ORDER BY po.id DESC
                """,
            "Sales Documents" => """
                SELECT d.id AS ID,d.kind AS Type,c.name AS Customer,d.created AS Date,
                  d.status AS Status,printf('$%.2f',d.total/100.0) AS Total,
                  COALESCE(CAST(link.invoice_id AS TEXT),'') AS 'Converted Invoice',
                  COALESCE(CAST(origin.estimate_id AS TEXT),'') AS 'Source Estimate',
                  COALESCE(d.notes,'') AS Notes
                FROM documents d LEFT JOIN customers c ON c.id=d.customer_id
                LEFT JOIN lab_estimate_invoice_links link ON link.estimate_id=d.id
                LEFT JOIN lab_estimate_invoice_links origin ON origin.invoice_id=d.id
                WHERE CAST(d.id AS TEXT) LIKE @q OR COALESCE(c.name,'') LIKE @q OR d.kind LIKE @q
                ORDER BY d.id DESC
                """,
            "Inventory History" => HistorySql(),
            "Expenses" => """
                SELECT id AS ID,spent_on AS Date,category AS Category,
                       COALESCE(vendor,'') AS Vendor,COALESCE(description,'') AS Description,
                       printf('$%.2f',amount/100.0) AS Amount
                FROM expenses
                WHERE spent_on LIKE @q OR category LIKE @q OR COALESCE(vendor,'') LIKE @q
                    OR COALESCE(description,'') LIKE @q
                ORDER BY spent_on DESC,id DESC
                """,
            "Accounting" => BusinessData.AccountingSql(),
            _ => throw new ArgumentException("Unknown page", nameof(section))
        };
        using var c = Store.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        if (sql.Contains("@q", StringComparison.Ordinal))
            command.Parameters.AddWithValue("@q", "%" + search.Trim() + "%");
        if (sql.Contains("@archived", StringComparison.Ordinal))
            command.Parameters.AddWithValue("@archived", archived ? 1 : 0);
        using var reader = command.ExecuteReader();
        var result = new DataTable(); result.Load(reader);
        return result;
    }

    private static string HistorySql()
    {
        using var c = Store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='inventory_movements'";
        bool old = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        string lab = """
            SELECT e.changed_at AS 'When',p.sku AS SKU,p.name AS Product,
              e.before_qty AS Before,e.after_qty-e.before_qty AS Change,e.after_qty AS After,
              e.reason AS Reason,e.source AS Source
            FROM lab_stock_events e JOIN products p ON p.id=e.product_id
            """;
        if (!old) return lab + " ORDER BY 1 DESC LIMIT 500";
        return """
            SELECT m.changed_at AS 'When',p.sku AS SKU,p.name AS Product,
              m.old_qty AS Before,m.change_qty AS Change,m.new_qty AS After,
              m.reason AS Reason,m.source AS Source
            FROM inventory_movements m JOIN products p ON p.id=m.product_id
            WHERE m.source <> 'csharp-lab'
            UNION ALL
            """ + lab + " ORDER BY 1 DESC LIMIT 500";
    }

    public static (int Products, int Suppliers, int Customers, int Low, int Out, decimal StockValue) Stats()
    {
        using var db = Store.Open();
        int count(string table)
        {
            using var x = db.CreateCommand(); x.CommandText = $"SELECT count(*) FROM {table} WHERE is_archived=0";
            return Convert.ToInt32(x.ExecuteScalar(),CultureInfo.InvariantCulture);
        }
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT SUM(CASE WHEN is_archived=0 AND stock_mode='stocked' AND qty<=reorder_point THEN 1 ELSE 0 END),
              SUM(CASE WHEN is_archived=0 AND stock_mode='stocked' AND qty=0 THEN 1 ELSE 0 END),
              COALESCE(SUM(CASE WHEN stock_mode='stocked' THEN qty*COALESCE(inventory_unit_cost,cost) ELSE 0 END),0)
            FROM products
            """;
        using var r = cmd.ExecuteReader();r.Read();
        int low=r.IsDBNull(0)?0:r.GetInt32(0),outStock=r.IsDBNull(1)?0:r.GetInt32(1);
        decimal value=Dollars(r.GetInt64(2)); r.Close();
        return (count("products"),count("suppliers"),count("customers"),low,outStock,value);
    }

    public static List<(long Id,string Name)> SupplierOptions(long? includeArchivedId=null)
    {
        using var db=Store.Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT id, CASE WHEN is_archived=1 THEN name || ' (Archived)' ELSE name END
            FROM suppliers WHERE is_archived=0 OR id=@prior
            ORDER BY name COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@prior",includeArchivedId.HasValue?(object)includeArchivedId.Value:DBNull.Value);
        var values=new List<(long,string)>();
        using var r=cmd.ExecuteReader();
        while (r.Read()) values.Add((r.GetInt64(0),r.GetString(1)));
        return values;
    }

    private static string Text(SqliteDataReader r,string name) => r.IsDBNull(r.GetOrdinal(name))?"":r.GetString(r.GetOrdinal(name));
    private static long Long(SqliteDataReader r,string name) => r.IsDBNull(r.GetOrdinal(name))?0:r.GetInt64(r.GetOrdinal(name));

    public static Product LoadProduct(long id)
    {
        using var db=Store.Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT * FROM products WHERE id=@id";cmd.Parameters.AddWithValue("@id",id);
        using var r=cmd.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Product no longer exists.");
        return new Product {Id=id,Sku=Text(r,"sku"),Name=Text(r,"name"),Description=Text(r,"description"),Barcode=Text(r,"barcode"),
            SupplierId=r.IsDBNull(r.GetOrdinal("supplier_id"))?null:Long(r,"supplier_id"),
            Price=Dollars(Long(r,"price")),Cost=Dollars(Long(r,"cost")),Quantity=(int)Long(r,"qty"),
            ReorderPoint=(int)Long(r,"reorder_point"),ReorderQuantity=(int)Long(r,"reorder_qty"),
            ReorderCost=r.IsDBNull(r.GetOrdinal("reorder_cost_total"))?null:Dollars(Long(r,"reorder_cost_total")),
            StockMode=Text(r,"stock_mode"),Bin=Text(r,"bin_location"),
            ImagePath=Text(r,"image_path"),BarcodeImagePath=Text(r,"barcode_image_path")};
    }

    private static void Bind(SqliteCommand cmd, params (string Key,object? Value)[] args)
    {
        foreach (var (key,value) in args) cmd.Parameters.AddWithValue("@"+key,value??DBNull.Value);
    }

    public static void SaveProduct(Product p)
    {
        if(string.IsNullOrWhiteSpace(p.Sku)||string.IsNullOrWhiteSpace(p.Name))
            throw new InvalidOperationException("SKU and product name are required.");
        if(p.Price<0||p.Cost<0||p.Quantity<0||p.ReorderQuantity<1||p.ReorderPoint<0)
            throw new InvalidOperationException("Prices and stock must be nonnegative; reorder quantity must be positive.");
        if(p.ReorderCost.HasValue)
            p.Cost=decimal.Round(p.ReorderCost.Value/p.ReorderQuantity,2,MidpointRounding.AwayFromZero);
        using var db=Store.Open();using var tx=db.BeginTransaction();
        int oldStock=0;
        if(p.Id>0)
        {
            using var old=db.CreateCommand();old.Transaction=tx;old.CommandText="SELECT qty FROM products WHERE id=@id AND is_archived=0";
            old.Parameters.AddWithValue("@id",p.Id);
            object? existing=old.ExecuteScalar();if(existing is null)throw new InvalidOperationException("Product no longer exists.");
            oldStock=Convert.ToInt32(existing,CultureInfo.InvariantCulture);
        }
        SetInventoryContext(db,tx,"Product editor", "Product details/quantity updated");
        using(var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;
            cmd.CommandText=p.Id==0?
                """
                    INSERT INTO products(sku,name,description,barcode,supplier_id,price,cost,qty,
                      reorder_point,reorder_qty,reorder_cost_total,stock_mode,bin_location,image_path,barcode_image_path)
                    VALUES(@sku,@name,@desc,@barcode,@supplier,@price,@cost,@qty,@point,@reorder,@bulk,@mode,@bin,@image,@barcodeimg)
                    """:
                """
                    UPDATE products SET sku=@sku,name=@name,description=@desc,barcode=@barcode,
                      supplier_id=@supplier,price=@price,cost=@cost,qty=@qty,reorder_point=@point,
                      reorder_qty=@reorder,reorder_cost_total=@bulk,stock_mode=@mode,bin_location=@bin,
                      image_path=@image,barcode_image_path=@barcodeimg WHERE id=@id AND is_archived=0
                    """;
            Bind(cmd,("sku",p.Sku.Trim()),("name",p.Name.Trim()),("desc",p.Description.Trim()),
                ("barcode",p.Barcode.Trim()),("supplier",p.SupplierId),("price",Cents(p.Price)),("cost",Cents(p.Cost)),
                ("qty",p.Quantity),("point",p.ReorderPoint),("reorder",p.ReorderQuantity),
                ("bulk",p.ReorderCost.HasValue?Cents(p.ReorderCost.Value):null),("mode",p.StockMode),
                ("bin",p.Bin.Trim()),("image",p.ImagePath),("barcodeimg",p.BarcodeImagePath));
            if(p.Id>0)Bind(cmd,("id",p.Id));
            cmd.ExecuteNonQuery();
            if(p.Id==0)
            {
                using var key=db.CreateCommand(); key.Transaction=tx;
                key.CommandText="SELECT last_insert_rowid()";
                p.Id=Convert.ToInt64(key.ExecuteScalar(),CultureInfo.InvariantCulture);
            }
        }
        if(p.Id!=0 && oldStock!=p.Quantity)LogStock(db,tx,p.Id,oldStock,p.Quantity,"Product editor change");
        ClearInventoryContext(db,tx);
        tx.Commit();
    }

    private static bool HasTable(SqliteConnection db,SqliteTransaction tx,string table)
    {
        using var c=db.CreateCommand();c.Transaction=tx;
        c.CommandText="SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name";
        c.Parameters.AddWithValue("@name",table);return c.ExecuteScalar()!=null;
    }
    private static void SetInventoryContext(SqliteConnection db,SqliteTransaction tx,string source,string reason)
    {
        if(!HasTable(db,tx,"inventory_change_context"))return;
        using var c=db.CreateCommand();c.Transaction=tx;
        c.CommandText="UPDATE inventory_change_context SET source='csharp-lab',reason=@r,actor='C# Lab' WHERE id=1";
        c.Parameters.AddWithValue("@r",reason);c.ExecuteNonQuery();
    }
    private static void ClearInventoryContext(SqliteConnection db,SqliteTransaction tx)
    {
        if(!HasTable(db,tx,"inventory_change_context"))return;
        using var c=db.CreateCommand();c.Transaction=tx;
        c.CommandText="UPDATE inventory_change_context SET source='system',reason='',actor='' WHERE id=1";
        c.ExecuteNonQuery();
    }
    private static void LogStock(SqliteConnection db,SqliteTransaction tx,long id,int before,int after,string reason)
    {
        using var c=db.CreateCommand();c.Transaction=tx;
        c.CommandText="INSERT INTO lab_stock_events(product_id,before_qty,after_qty,reason) VALUES(@id,@old,@new,@reason)";
        Bind(c,("id",id),("old",before),("new",after),("reason",reason));c.ExecuteNonQuery();
    }

    public static string AdjustStock(string skuOrBarcode,string mode,int quantity,string reason)
    {
        if(string.IsNullOrWhiteSpace(skuOrBarcode)||string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Scan a product and enter a reason.");
        if(quantity<0 || (mode!="Set" && quantity==0))throw new InvalidOperationException("Enter a valid quantity.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        long id;string name;int current;
        using (var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="SELECT id,name,qty FROM products WHERE is_archived=0 AND (sku=@code COLLATE NOCASE OR barcode=@code COLLATE NOCASE) LIMIT 2";
            cmd.Parameters.AddWithValue("@code",skuOrBarcode.Trim());
            using var reader=cmd.ExecuteReader();if(!reader.Read())throw new InvalidOperationException("No product matches that SKU/barcode.");
            id=reader.GetInt64(0);name=reader.GetString(1);current=reader.GetInt32(2);
            if(reader.Read())throw new InvalidOperationException("Multiple products use this barcode or SKU.");
        }
        int newCount=mode switch {"Add"=>checked(current+quantity),"Remove"=>current-quantity,"Set"=>quantity,
            _=>throw new InvalidOperationException("Select Add, Remove, or Set.")};
        if(newCount<0)throw new InvalidOperationException("Insufficient stock for that adjustment.");
        SetInventoryContext(db,tx,"C# Lab","C# Lab: "+reason);
        using (var cmd=db.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="UPDATE products SET qty=@qty WHERE id=@id";
            Bind(cmd,("qty",newCount),("id",id));cmd.ExecuteNonQuery();
        }
        if(current!=newCount)LogStock(db,tx,id,current,newCount,reason.Trim());
        ClearInventoryContext(db,tx);tx.Commit();
        return $"{name}: {current} → {newCount}";
    }

    public static Supplier LoadSupplier(long id)
    {
        using var db=Store.Open(); using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT * FROM suppliers WHERE id=@id";cmd.Parameters.AddWithValue("@id",id);
        using var r=cmd.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Supplier not found.");
        return new Supplier {Id=id,Name=Text(r,"name"),Contact=Text(r,"contact_name"),Email=Text(r,"email"),
            Phone=Text(r,"phone"),Website=Text(r,"website"),Address=Text(r,"address"),City=Text(r,"city"),
            State=Text(r,"state"),Zip=Text(r,"zip_code"),Notes=Text(r,"notes")};
    }
    public static void SaveSupplier(Supplier s)
    {
        if(string.IsNullOrWhiteSpace(s.Name))throw new InvalidOperationException("Supplier name is required.");
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText=s.Id==0?
            "INSERT INTO suppliers(name,contact_name,email,phone,website,address,city,state,zip_code,notes) VALUES(@name,@contact,@email,@phone,@website,@address,@city,@state,@zip,@notes)":
            "UPDATE suppliers SET name=@name,contact_name=@contact,email=@email,phone=@phone,website=@website,address=@address,city=@city,state=@state,zip_code=@zip,notes=@notes WHERE id=@id AND is_archived=0";
        Bind(cmd,("name",s.Name.Trim()),("contact",s.Contact),("email",s.Email),
            ("phone",s.Phone),("website",s.Website),("address",s.Address),("city",s.City),
            ("state",s.State),("zip",s.Zip),("notes",s.Notes));
        if(s.Id>0)Bind(cmd,("id",s.Id));
        cmd.ExecuteNonQuery();
    }
    public static Customer LoadCustomer(long id)
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT * FROM customers WHERE id=@id";cmd.Parameters.AddWithValue("@id",id);
        using var r=cmd.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Customer not found.");
        return new Customer {Id=id,Name=Text(r,"name"),Email=Text(r,"email"),Phone=Text(r,"phone"),
            Address=Text(r,"address"),City=Text(r,"city"),State=Text(r,"state"),Zip=Text(r,"zip_code"),
            Notes=Text(r,"notes"),TaxExempt=Long(r,"tax_exempt")!=0};
    }
    public static void SaveCustomer(Customer c)
    {
        if(string.IsNullOrWhiteSpace(c.Name))throw new InvalidOperationException("Customer name is required.");
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText=c.Id==0?
            "INSERT INTO customers(name,email,phone,address,city,state,zip_code,notes,tax_exempt) VALUES(@name,@email,@phone,@address,@city,@state,@zip,@notes,@tax)":
            "UPDATE customers SET name=@name,email=@email,phone=@phone,address=@address,city=@city,state=@state,zip_code=@zip,notes=@notes,tax_exempt=@tax WHERE id=@id AND is_archived=0";
        Bind(cmd,("name",c.Name.Trim()),("email",c.Email),("phone",c.Phone),
            ("address",c.Address),("city",c.City),("state",c.State),("zip",c.Zip),("notes",c.Notes),
            ("tax",c.TaxExempt?1:0));
        if(c.Id>0)Bind(cmd,("id",c.Id));
        cmd.ExecuteNonQuery();
    }

    public static DataTable Details(string section,long id)
    {
        string sql=section=="Purchase Orders"?
            """
            SELECT p.sku AS SKU,p.name AS Product,i.qty AS Quantity,
              printf('$%.2f',i.unit_cost/100.0) AS 'Unit Cost',
              printf('$%.2f',(i.qty*i.unit_cost)/100.0) AS 'Line Total'
            FROM po_items i LEFT JOIN products p ON p.id=i.product_id WHERE i.po_id=@id
            """:
            """
            SELECT description AS Product,qty AS Quantity,
              printf('$%.2f',unit_price/100.0) AS 'Unit Price',
              printf('$%.2f',line_total/100.0) AS 'Line Total'
            FROM document_items WHERE document_id=@id
            """;
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText=sql;cmd.Parameters.AddWithValue("@id",id);
        using var r=cmd.ExecuteReader();var t=new DataTable();t.Load(r);return t;
    }
}
