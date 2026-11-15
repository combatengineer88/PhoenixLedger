using Microsoft.Data.Sqlite;
using System.Data;

namespace PhoenixLedger.CSharpLab;

internal static class Store
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoenixLedgerCSharpLab");
    public static readonly string Database = Path.Combine(Root, "lab.db");
    public static readonly string Assets = Path.Combine(Root, "media");
    private static string Backups => Path.Combine(Root, "backups");
    public static string? SuggestedSource
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string path in new[] { Path.Combine(local, "PhoenixLedger", "business.db"),
                                               Path.Combine(local, "BusinessDesk", "business.db") })
                if (File.Exists(path)) return path;
            return null;
        }
    }

    public static SqliteConnection Open()
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database,
            Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000";
        cmd.ExecuteNonQuery();
        return c;
    }

    public static void Initialize()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Backups);
        bool created = !File.Exists(Database);
        using var conn = Open();
        if(!created)
        {
            using var probe=conn.CreateCommand();
            probe.CommandText="SELECT count(*) FROM pragma_table_info('customers') WHERE name='is_archived'";
            if(Convert.ToInt64(probe.ExecuteScalar()??0L)==0)
            {
                // Back up the entire existing Lab database once before adding
                // archive columns. No backup is necessary on later launches.
                string file=Path.Combine(Backups,
                    $"lab_before_archive_v0.4.3_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");
                using var target=new SqliteConnection(new SqliteConnectionStringBuilder{
                    DataSource=file,Mode=SqliteOpenMode.ReadWriteCreate}.ToString());
                target.Open();
                conn.BackupDatabase(target);
                using var integrity=target.CreateCommand();
                integrity.CommandText="PRAGMA quick_check";
                if(!string.Equals(integrity.ExecuteScalar()?.ToString(),"ok",StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Archive migration backup failed integrity verification.");
            }
        }
        Schema(conn);
        if (created) Seed(conn);
        else
        {
            using var check=conn.CreateCommand();
            check.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name='lab_fin_journal'";
            bool newFinancialSchema=Convert.ToInt64(check.ExecuteScalar()??0L)==0;
            if(newFinancialSchema)
            {
                // One-time, consistent pre-finance-migration copy. An unavailable
                // backup destination is fatal: never migrate without a backup.
                string backup=Path.Combine(Backups,
                    $"lab_before_finance_v0.4.0_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                using var copy=new SqliteConnection(new SqliteConnectionStringBuilder {
                    DataSource=backup,Mode=SqliteOpenMode.ReadWriteCreate }.ToString());
                copy.Open();
                conn.BackupDatabase(copy);
                using var integrity=copy.CreateCommand();
                integrity.CommandText="PRAGMA quick_check";
                if(!string.Equals(integrity.ExecuteScalar()?.ToString(),"ok",StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Pre-migration C# Lab backup failed its integrity check.");
            }
        }
        conn.Close();
        FinancialOperations.Initialize();
    }

    public static void Schema(SqliteConnection db)
    {
        Exec(db, """
            CREATE TABLE IF NOT EXISTS suppliers(
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT, phone TEXT, address TEXT);
            CREATE TABLE IF NOT EXISTS customers(
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT,
                phone TEXT, address TEXT, notes TEXT);
            CREATE TABLE IF NOT EXISTS products(
                id INTEGER PRIMARY KEY, sku TEXT UNIQUE NOT NULL, name TEXT NOT NULL,
                description TEXT DEFAULT '', price INTEGER NOT NULL DEFAULT 0,
                cost INTEGER NOT NULL DEFAULT 0, qty INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS purchase_orders(
                id INTEGER PRIMARY KEY, supplier_id INTEGER NOT NULL,
                ordered_on TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Open',
                notes TEXT DEFAULT '', total INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS po_items(
                id INTEGER PRIMARY KEY, po_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL, qty INTEGER NOT NULL, unit_cost INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS documents(
                id INTEGER PRIMARY KEY, kind TEXT NOT NULL, customer_id INTEGER NOT NULL,
                created TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Open',
                subtotal INTEGER NOT NULL DEFAULT 0, tax INTEGER NOT NULL DEFAULT 0,
                total INTEGER NOT NULL DEFAULT 0, notes TEXT DEFAULT '');
            CREATE TABLE IF NOT EXISTS document_items(
                id INTEGER PRIMARY KEY, document_id INTEGER NOT NULL,
                product_id INTEGER, description TEXT NOT NULL, qty INTEGER NOT NULL,
                unit_price INTEGER NOT NULL, line_total INTEGER NOT NULL);
            -- Local to C# Lab; compatible with the Python expense table when a snapshot is imported.
            CREATE TABLE IF NOT EXISTS expenses(
                id INTEGER PRIMARY KEY, spent_on TEXT NOT NULL,
                category TEXT NOT NULL, vendor TEXT DEFAULT '',
                description TEXT DEFAULT '', amount INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS settings(
                key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS lab_stock_events(
                id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL,
                changed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                before_qty INTEGER NOT NULL, after_qty INTEGER NOT NULL,
                reason TEXT NOT NULL, source TEXT NOT NULL DEFAULT 'C# Lab');
            """);
        foreach (var (table, columns) in new (string, (string, string)[])[]
        {
            ("products", new (string, string)[] {
                ("is_archived", "INTEGER NOT NULL DEFAULT 0 CHECK(is_archived IN (0,1))"),
                ("archived_at", "TEXT DEFAULT NULL"),
                ("barcode", "TEXT DEFAULT ''"), ("supplier_id", "INTEGER"),
                ("image_path", "TEXT DEFAULT ''"), ("barcode_image_path", "TEXT DEFAULT ''"),
                ("reorder_point", "INTEGER NOT NULL DEFAULT 5"), ("reorder_qty", "INTEGER NOT NULL DEFAULT 20"),
                ("reorder_cost_total", "INTEGER DEFAULT NULL"), ("case_pack_qty", "INTEGER NOT NULL DEFAULT 1"),
                ("min_order_qty", "INTEGER NOT NULL DEFAULT 1"), ("stock_mode", "TEXT NOT NULL DEFAULT 'stocked'"),
                ("bin_location", "TEXT NOT NULL DEFAULT ''"), ("inventory_unit_cost", "INTEGER DEFAULT NULL") }),
            ("suppliers", new (string, string)[] {
                ("is_archived", "INTEGER NOT NULL DEFAULT 0 CHECK(is_archived IN (0,1))"),
                ("archived_at", "TEXT DEFAULT NULL"),
                ("contact_name", "TEXT DEFAULT ''"), ("website", "TEXT DEFAULT ''"),
                ("city", "TEXT DEFAULT ''"), ("state", "TEXT DEFAULT ''"),
                ("zip_code", "TEXT DEFAULT ''"), ("notes", "TEXT DEFAULT ''") }),
            ("customers", new (string, string)[] {
                ("is_archived", "INTEGER NOT NULL DEFAULT 0 CHECK(is_archived IN (0,1))"),
                ("archived_at", "TEXT DEFAULT NULL"),
                ("city", "TEXT DEFAULT ''"), ("state", "TEXT DEFAULT ''"),
                ("zip_code", "TEXT DEFAULT ''"), ("tax_exempt", "INTEGER NOT NULL DEFAULT 0") })
        })
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) found.Add(reader.GetString(1));
            foreach (var (name, def) in columns)
                if (!found.Contains(name)) Exec(db, $"ALTER TABLE {table} ADD COLUMN {name} {def}");
        }
        Exec(db, """
            CREATE TABLE IF NOT EXISTS lab_bulk_product_changes(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                batch_id TEXT NOT NULL,
                product_id INTEGER NOT NULL,
                sku TEXT NOT NULL,
                name TEXT NOT NULL,
                reason TEXT NOT NULL,
                old_price INTEGER NOT NULL,
                new_price INTEGER NOT NULL,
                old_cost INTEGER NOT NULL,
                new_cost INTEGER NOT NULL,
                old_supply_type TEXT NOT NULL,
                new_supply_type TEXT NOT NULL,
                changed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_lab_bulk_product_changes
                ON lab_bulk_product_changes(product_id,changed_at DESC);
            CREATE TABLE IF NOT EXISTS lab_catalog_archive_events(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_type TEXT NOT NULL,
                entity_id INTEGER NOT NULL,
                display_name TEXT NOT NULL,
                action TEXT NOT NULL CHECK(action IN ('Archive','Restore','Purge')),
                recorded_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_lab_catalog_archive_events
              ON lab_catalog_archive_events(entity_type,entity_id,id DESC);
            CREATE INDEX IF NOT EXISTS idx_lab_customers_archived ON customers(is_archived,name);
            CREATE INDEX IF NOT EXISTS idx_lab_suppliers_archived ON suppliers(is_archived,name);
            CREATE INDEX IF NOT EXISTS idx_lab_products_archived ON products(is_archived,name);
            CREATE INDEX IF NOT EXISTS idx_lab_stock_product ON lab_stock_events(product_id,id DESC);
            """);
    }

    private static void Seed(SqliteConnection db)
    {
        using var tx = db.BeginTransaction();
        Run(db, tx, "INSERT INTO suppliers(name,contact_name,email,city,state) VALUES('Sample Garments Co.','Sales Desk','sales@example.invalid','Chicago','IL')");
        Run(db, tx, "INSERT INTO suppliers(name,contact_name,email,city,state) VALUES('Sample Promo Supply','','orders@example.invalid','Milwaukee','WI')");
        Run(db, tx, "INSERT INTO customers(name,email,city,state,zip_code) VALUES('Demo Customer','customer@example.invalid','Watertown','WI','53094')");
        Run(db, tx, "INSERT INTO products(sku,name,barcode,description,price,cost,qty,supplier_id,reorder_point,reorder_qty,reorder_cost_total,stock_mode) VALUES('DEMO-TEE-BLK','Black T-Shirt','DEMO-0001','Demonstration item',1299,400,48,1,12,48,19200,'stocked')");
        Run(db, tx, "INSERT INTO products(sku,name,barcode,description,price,cost,qty,supplier_id,reorder_point,reorder_qty,reorder_cost_total,stock_mode) VALUES('DEMO-MUG','Promotional Mug','DEMO-0002','Demonstration item',999,250,32,2,10,36,9000,'stocked')");
        Run(db, tx, "INSERT INTO products(sku,name,barcode,description,price,cost,qty,supplier_id,stock_mode) VALUES('DEMO-HAT','Embroidered Cap','DEMO-0003','Made-to-order demo',1999,600,0,1,'made_to_order')");
        tx.Commit();
    }

    private static void Run(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var c = conn.CreateCommand(); c.Transaction = tx; c.CommandText = sql; c.ExecuteNonQuery();
    }
    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    /// <summary>Creates a consistent SQLite backup. Reads source ONLY; never edits live PhoenixLedger.</summary>
    public static int ImportSnapshot(string source)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Selected SQLite database was not found", source);
        if (Path.GetFullPath(Database).Equals(source, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot import the lab database into itself.");
        string staging = Path.Combine(Root, "snapshot_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var origin = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = staging, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                origin.Open(); target.Open();
                origin.BackupDatabase(target);
                using var check = target.CreateCommand(); check.CommandText = "PRAGMA quick_check";
                if (!"ok".Equals(check.ExecuteScalar()?.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Database integrity check failed.");
            }
            // Back up the current C# lab database before replacing it.
            string backup = Path.Combine(Backups, "lab_before_import_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".db");
            using (var current = Open())
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = backup, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            { target.Open(); current.BackupDatabase(target); }
            // The prior LAB database has been backed up and all connections are closed.
            // Never allow an old lab WAL file to be replayed against the new snapshot.
            foreach (string suffix in new[] { "-wal", "-shm" })
            {
                string journal = Database + suffix;
                if (File.Exists(journal)) File.Delete(journal);
            }
            File.Move(staging, Database, true);
            using (var lab = Open()) Schema(lab);
            return CopyAttachedMediaFromSnapshot(source);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private static int CopyAttachedMediaFromSnapshot(string sourceDb)
    {
        // Use copies of existing media wherever files are accessible. Never modify originals.
        int copied = 0;
        using var db = Open();
        using var select = db.CreateCommand();
        select.CommandText = "SELECT id,image_path,barcode_image_path FROM products";
        var files = new List<(long id, string field, string path)>();
        using (var rd = select.ExecuteReader())
            while (rd.Read())
                foreach (var (field, pos) in new[] { ("image_path", 1), ("barcode_image_path", 2) })
                    if (!rd.IsDBNull(pos) && !string.IsNullOrWhiteSpace(rd.GetString(pos)))
                        files.Add((rd.GetInt64(0), field, rd.GetString(pos)));
        foreach (var entry in files)
        {
            try
            {
                if (!File.Exists(entry.path)) continue;
                // No reference to a live file remains in this lab database after successful copy.
                string extension = Path.GetExtension(entry.path);
                string copy = Path.Combine(Assets, $"{entry.id}_{entry.field}_{Guid.NewGuid():N}{extension}");
                File.Copy(entry.path, copy, false);
                using var upd = db.CreateCommand();
                upd.CommandText = $"UPDATE products SET {entry.field}=@path WHERE id=@id";
                upd.Parameters.AddWithValue("@path", copy);
                upd.Parameters.AddWithValue("@id", entry.id);
                upd.ExecuteNonQuery();
                copied++;
            }
            catch (IOException) { /* Missing/inaccessible original media is not fatal. */ }
            catch (UnauthorizedAccessException) { }
        }
        return copied;
    }
}
