using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal static class ExcelImporter
{
    private static string Norm(string key) => new string(key.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static long PriceInCents(string input)
    {
        input=input.Trim().Replace("$", "").Replace(",", "");
        if (string.IsNullOrEmpty(input)) return 0;
        if (!decimal.TryParse(input,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal n)||n<0)
            throw new InvalidDataException("Invalid money amount: " + input);
        return Repository.Cents(n);
    }
    private static int Stock(string input,int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(input)) return defaultValue;
        if (!int.TryParse(input,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)||n<0)
            throw new InvalidDataException("Invalid quantity: " + input);
        return n;
    }
    private static void Param(SqliteCommand cmd,string key,object? value)=>cmd.Parameters.AddWithValue("@"+key,value??DBNull.Value);

    /// <summary>All imports are transactional and only target the C# lab database.</summary>
    public static (int Inserted,int Updated) ImportXlsx(string filename,string entity)
    {
        if(!File.Exists(filename))throw new FileNotFoundException("Workbook not found.",filename);
        using var book=new XLWorkbook(filename);
        var worksheet=book.Worksheets.FirstOrDefault()??throw new InvalidDataException("Workbook has no worksheet.");
        var used=worksheet.RangeUsed()??throw new InvalidDataException("Worksheet is empty.");
        var rows=used.RowsUsed().ToList();
        if(rows.Count<2)throw new InvalidDataException("Workbook must have a header row and at least one data row.");
        var headers=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach(var cell in rows[0].CellsUsed())
        {
            string name=Norm(cell.GetFormattedString());
            if(!string.IsNullOrWhiteSpace(name) && !headers.ContainsKey(name))headers[name]=cell.Address.ColumnNumber;
        }
        string Get(IXLRangeRow r,params string[] keys)
        {
            foreach(var key in keys)
                if(headers.TryGetValue(Norm(key),out int i))return r.Cell(i).GetFormattedString().Trim();
            return "";
        }
        bool Has(params string[] keys)=>keys.Any(k=>headers.ContainsKey(Norm(k)));
        string required=entity=="Products"?"SKU":"Name";
        if(!Has(required))throw new InvalidDataException($"A {required} column is required for {entity} import.");
        using var db=Store.Open();using var tx=db.BeginTransaction();
        int created=0,updated=0;
        foreach(var r in rows.Skip(1))
        {
            if(r.IsEmpty())continue;
            if(entity=="Products")
            {
                string sku=Get(r,"SKU","Product SKU"); if(string.IsNullOrWhiteSpace(sku))continue;
                string name=Get(r,"Name","Product Name","Title");
                if(string.IsNullOrWhiteSpace(name))throw new InvalidDataException("Missing product name for SKU " + sku);
                long? supplierId=null;
                string supplierName=Get(r,"Supplier","Vendor","Supplier Name");
                if(!string.IsNullOrEmpty(supplierName))
                {
                    using var lookup=db.CreateCommand();lookup.Transaction=tx;
                    lookup.CommandText="SELECT id FROM suppliers WHERE name=@name COLLATE NOCASE LIMIT 1";
                    Param(lookup,"name",supplierName);object? value=lookup.ExecuteScalar();
                    if(value is null)
                    {
                        using var add=db.CreateCommand();add.Transaction=tx;
                        add.CommandText="INSERT INTO suppliers(name) VALUES(@name)";Param(add,"name",supplierName);
                        add.ExecuteNonQuery();using var last=db.CreateCommand();last.Transaction=tx;last.CommandText="SELECT last_insert_rowid()";
                        supplierId=Convert.ToInt64(last.ExecuteScalar(),CultureInfo.InvariantCulture);
                    }
                    else supplierId=Convert.ToInt64(value,CultureInfo.InvariantCulture);
                }
                long id=0;
                using(var lookup=db.CreateCommand())
                {
                    lookup.Transaction=tx;lookup.CommandText="SELECT id FROM products WHERE sku=@sku COLLATE NOCASE LIMIT 1";
                    Param(lookup,"sku",sku);object? value=lookup.ExecuteScalar();
                    if(value is not null)id=Convert.ToInt64(value,CultureInfo.InvariantCulture);
                }
                long price=PriceInCents(Get(r,"Price","Selling Price")),cost=PriceInCents(Get(r,"Cost","Item Cost","Unit Cost"));
                int qty=Stock(Get(r,"Qty","Quantity","Stock","On Hand"),0);
                int reorderQty=Stock(Get(r,"Reorder Qty","Reorder Quantity"),20);
                if(reorderQty<1)throw new InvalidDataException("Reorder quantity must be positive for "+sku);
                string bulkText=Get(r,"Reorder Cost","Bulk Reorder Cost");
                long? bulk=string.IsNullOrWhiteSpace(bulkText)?null:PriceInCents(bulkText);
                if(bulk.HasValue)cost=(long)decimal.Round((decimal)bulk.Value/reorderQty,0,MidpointRounding.AwayFromZero);
                using var cmd=db.CreateCommand();cmd.Transaction=tx;
                if(id==0)
                {
                    cmd.CommandText="""
                        INSERT INTO products(sku,name,description,barcode,price,cost,qty,supplier_id,reorder_qty,reorder_cost_total)
                        VALUES(@sku,@name,@desc,@barcode,@price,@cost,@qty,@supplier,@reorder,@bulk)
                        """;
                    created++;
                }
                else
                {
                    cmd.CommandText="""
                        UPDATE products SET name=@name,
                            description=CASE WHEN @hasdesc=1 THEN @desc ELSE description END,
                            barcode=CASE WHEN @hasbarcode=1 THEN @barcode ELSE barcode END,
                            price=CASE WHEN @hasprice=1 THEN @price ELSE price END,
                            cost=CASE WHEN @hascost=1 THEN @cost ELSE cost END,
                            qty=CASE WHEN @hasqty=1 THEN @qty ELSE qty END,
                            supplier_id=CASE WHEN @hassupplier=1 THEN @supplier ELSE supplier_id END,
                            reorder_qty=CASE WHEN @hasreorder=1 THEN @reorder ELSE reorder_qty END,
                            reorder_cost_total=CASE WHEN @hasbulk=1 THEN @bulk ELSE reorder_cost_total END
                        WHERE id=@id
                        """;
                    updated++;
                }
                if(id>0)Param(cmd,"id",id);
                Param(cmd,"sku",sku);Param(cmd,"name",name);
                Param(cmd,"desc",Get(r,"Description"));Param(cmd,"barcode",Get(r,"Barcode","UPC"));
                Param(cmd,"price",price);Param(cmd,"cost",cost);Param(cmd,"qty",qty);Param(cmd,"supplier",supplierId);
                Param(cmd,"reorder",reorderQty);Param(cmd,"bulk",bulk);
                if(id>0)
                {
                    Param(cmd,"hasdesc",Has("Description")?1:0);Param(cmd,"hasbarcode",Has("Barcode","UPC")?1:0);
                    Param(cmd,"hasprice",Has("Price","Selling Price")?1:0);
                    Param(cmd,"hascost",Has("Cost","Item Cost","Unit Cost","Reorder Cost","Bulk Reorder Cost")?1:0);
                    Param(cmd,"hasqty",Has("Qty","Quantity","Stock","On Hand")?1:0);
                    Param(cmd,"hassupplier",Has("Supplier","Vendor","Supplier Name")?1:0);
                    Param(cmd,"hasreorder",Has("Reorder Qty","Reorder Quantity")?1:0);
                    Param(cmd,"hasbulk",Has("Reorder Cost","Bulk Reorder Cost")?1:0);
                }
                cmd.ExecuteNonQuery();
            }
            else if(entity=="Suppliers")
            {
                string name=Get(r,"Name","Supplier","Supplier Name");if(name=="")continue;
                long id=0;
                using(var lookup=db.CreateCommand())
                {
                    lookup.Transaction=tx;lookup.CommandText="SELECT id FROM suppliers WHERE name=@name COLLATE NOCASE LIMIT 1";
                    Param(lookup,"name",name);object? value=lookup.ExecuteScalar();
                    if(value is not null)id=Convert.ToInt64(value,CultureInfo.InvariantCulture);
                }
                using var cmd=db.CreateCommand();cmd.Transaction=tx;
                cmd.CommandText=id==0?
                    "INSERT INTO suppliers(name,contact_name,email,phone,website,address,city,state,zip_code,notes) VALUES(@name,@contact,@email,@phone,@website,@address,@city,@state,@zip,@notes)":
                    "UPDATE suppliers SET contact_name=@contact,email=@email,phone=@phone,website=@website,address=@address,city=@city,state=@state,zip_code=@zip,notes=@notes WHERE id=@id";
                if(id>0)Param(cmd,"id",id);
                Param(cmd,"name",name);Param(cmd,"contact",Get(r,"Contact","Contact Name"));
                Param(cmd,"email",Get(r,"Email"));Param(cmd,"phone",Get(r,"Phone"));
                Param(cmd,"website",Get(r,"Website"));Param(cmd,"address",Get(r,"Address","Street Address"));
                Param(cmd,"city",Get(r,"City"));Param(cmd,"state",Get(r,"State"));Param(cmd,"zip",Get(r,"ZIP","ZIP Code"));
                Param(cmd,"notes",Get(r,"Notes"));cmd.ExecuteNonQuery();
                if(id==0)created++;else updated++;
            }
            else if(entity=="Customers")
            {
                string name=Get(r,"Name","Customer","Customer Name");if(name=="")continue;
                string email=Get(r,"Email");long id=0;
                using(var lookup=db.CreateCommand())
                {
                    lookup.Transaction=tx;lookup.CommandText=string.IsNullOrWhiteSpace(email)?
                        "SELECT id FROM customers WHERE name=@key COLLATE NOCASE LIMIT 1":
                        "SELECT id FROM customers WHERE email=@key COLLATE NOCASE LIMIT 1";
                    Param(lookup,"key",string.IsNullOrWhiteSpace(email)?name:email);
                    object? v=lookup.ExecuteScalar();if(v is not null)id=Convert.ToInt64(v,CultureInfo.InvariantCulture);
                }
                using var cmd=db.CreateCommand();cmd.Transaction=tx;
                cmd.CommandText=id==0?
                    "INSERT INTO customers(name,email,phone,address,city,state,zip_code,notes) VALUES(@name,@email,@phone,@address,@city,@state,@zip,@notes)":
                    "UPDATE customers SET name=@name,email=@email,phone=@phone,address=@address,city=@city,state=@state,zip_code=@zip,notes=@notes WHERE id=@id";
                if(id>0)Param(cmd,"id",id);
                Param(cmd,"name",name);Param(cmd,"email",email);Param(cmd,"phone",Get(r,"Phone"));
                Param(cmd,"address",Get(r,"Address","Street Address"));Param(cmd,"city",Get(r,"City"));
                Param(cmd,"state",Get(r,"State"));Param(cmd,"zip",Get(r,"ZIP","ZIP Code"));Param(cmd,"notes",Get(r,"Notes"));
                cmd.ExecuteNonQuery();if(id==0)created++;else updated++;
            }
            else throw new InvalidOperationException("Unsupported import type.");
        }
        tx.Commit();return (created,updated);
    }
}
