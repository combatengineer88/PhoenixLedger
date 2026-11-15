using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal sealed record CatalogArchiveInfo(
    string Section,long Id,string Name,bool Archived,long Stock,
    IReadOnlyList<string> Dependencies)
{
    public bool CanPurge => Archived && Stock==0 && Dependencies.Count==0;
}

/// <summary>
/// Soft archive preserves source IDs and financial/inventory history. Individual
/// hard-purge is limited to archived, unreferenced records; checks are repeated
/// inside the delete transaction. A minimal audit trail survives purging.
/// </summary>
internal static class CatalogArchive
{
    public static bool IsCatalog(string section)=>section is "Products" or "Suppliers" or "Customers";

    private static (string Table,string RelatedColumn) Entity(string section)=>section switch
    {
        "Products"=>("products","product_id"),
        "Suppliers"=>("suppliers","supplier_id"),
        "Customers"=>("customers","customer_id"),
        _=>throw new ArgumentException("This page does not contain archivable catalog entries.",nameof(section))
    };

    private static SqliteCommand Command(SqliteConnection db,SqliteTransaction? tx,string sql,
        params (string,object)[] args)
    {
        var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText=sql;
        foreach(var (key,value) in args)cmd.Parameters.AddWithValue("@"+key,value);
        return cmd;
    }

    private static string Quote(string identifier)=>"\""+identifier.Replace("\"","\"\"")+"\"";

    private static List<string> Dependencies(SqliteConnection db,SqliteTransaction? tx,
        string section,long id)
    {
        var (entity,relatedColumn)=Entity(section);
        var dependencies=new List<string>();
        var tables=new List<string>();
        using(var cmd=Command(db,tx,"SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
        using(var r=cmd.ExecuteReader())
            while(r.Read())tables.Add(r.GetString(0));

        foreach(string table in tables)
        {
            if(string.Equals(table,entity,StringComparison.OrdinalIgnoreCase) ||
                string.Equals(table,"lab_catalog_archive_events",StringComparison.OrdinalIgnoreCase))
                continue;
            var candidateColumns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using(var info=Command(db,tx,"PRAGMA table_info("+Quote(table)+")"))
            using(var r=info.ExecuteReader())
                while(r.Read())
                {
                    string col=r.GetString(1);
                    if(string.Equals(col,relatedColumn,StringComparison.OrdinalIgnoreCase))
                        candidateColumns.Add(col);
                }

            // Also detect schema-declared references with different column names.
            using(var fks=Command(db,tx,"PRAGMA foreign_key_list("+Quote(table)+")"))
            using(var r=fks.ExecuteReader())
                while(r.Read())
                    if(string.Equals(r.GetString(2),entity,StringComparison.OrdinalIgnoreCase))
                        candidateColumns.Add(r.GetString(3));

            foreach(string column in candidateColumns)
            {
                using var count=Command(db,tx,$"SELECT COUNT(*) FROM {Quote(table)} WHERE {Quote(column)}=@id",
                    ("id",id));
                long matches=Convert.ToInt64(count.ExecuteScalar(),CultureInfo.InvariantCulture);
                if(matches>0)dependencies.Add($"{table}: {matches:N0} related record(s)");
            }
        }
        return dependencies;
    }

    private static CatalogArchiveInfo Get(SqliteConnection db,SqliteTransaction? tx,string section,long id)
    {
        var (table,_)=Entity(section);
        using var cmd=Command(db,tx,$"SELECT name,is_archived{(section=="Products"?",qty":"")} FROM {table} WHERE id=@id",
            ("id",id));
        using var reader=cmd.ExecuteReader();
        if(!reader.Read())throw new InvalidOperationException("The selected catalog entry does not exist.");
        string name=reader.GetString(0);
        bool archived=reader.GetInt64(1)!=0;
        long stock=section=="Products"?reader.GetInt64(2):0;
        reader.Close();
        return new CatalogArchiveInfo(section,id,name,archived,stock,Dependencies(db,tx,section,id));
    }

    public static CatalogArchiveInfo Info(string section,long id)
    {
        using var db=Store.Open();
        return Get(db,null,section,id);
    }

    private static void Audit(SqliteConnection db,SqliteTransaction tx,string section,long id,
        string name,string action)
    {
        using var cmd=Command(db,tx,"""
            INSERT INTO lab_catalog_archive_events(entity_type,entity_id,display_name,action)
            VALUES(@type,@id,@name,@action)
            """,("type",section),("id",id),("name",name),("action",action));
        cmd.ExecuteNonQuery();
    }

    public static void SetArchived(string section,long id,bool archived)
    {
        var (table,_)=Entity(section);
        using var db=Store.Open();using var tx=db.BeginTransaction();
        var record=Get(db,tx,section,id);
        if(record.Archived==archived)
            throw new InvalidOperationException(archived?"Already archived.":"Already active.");
        using(var cmd=Command(db,tx,$"""
            UPDATE {table} SET is_archived=@flag,
            archived_at=CASE WHEN @flag=1 THEN CURRENT_TIMESTAMP ELSE NULL END
            WHERE id=@id AND is_archived=@previous
            """,("flag",archived?1:0),("id",id),("previous",archived?0:1)))
        {
            if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Catalog record changed. Refresh and try again.");
        }
        Audit(db,tx,section,id,record.Name,archived?"Archive":"Restore");
        tx.Commit();
    }

    public static void Purge(string section,long id)
    {
        var (table,_)=Entity(section);
        using var db=Store.Open();using var tx=db.BeginTransaction();
        var record=Get(db,tx,section,id);
        if(!record.Archived)
            throw new InvalidOperationException("Only archived records can be permanently deleted.");
        if(record.Stock!=0)
            throw new InvalidOperationException(
                $"This product still has {record.Stock:N0} units in inventory. " +
                "Resolve the inventory balance before permanent deletion.");
        if(record.Dependencies.Count>0)
            throw new InvalidOperationException(
                "This archived record is still used by historical or linked data and cannot be deleted.\n\n"+
                string.Join("\n",record.Dependencies.Take(10))+
                "\n\nKeep it archived to preserve accurate invoices, purchase orders, and reports.");
        using(var cmd=Command(db,tx,$"DELETE FROM {table} WHERE id=@id AND is_archived=1",("id",id)))
            if(cmd.ExecuteNonQuery()!=1)
                throw new InvalidOperationException("Record changed before deletion. Refresh and try again.");
        Audit(db,tx,section,id,record.Name,"Purge");
        tx.Commit();
    }
}
