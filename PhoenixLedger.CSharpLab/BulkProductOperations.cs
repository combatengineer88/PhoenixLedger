using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PhoenixLedger.CSharpLab;

internal enum BulkPriceMethod { SetAmount, AdjustPercent }

internal sealed record BulkProductSnapshot(
    long Id,string Sku,string Name,string Supplier,int Stock,long PriceCents,
    long CostCents,string StockMode,long? ReorderCostCents,int ReorderQuantity);

internal sealed record BulkProductChanges(
    bool ChangePrice,BulkPriceMethod PriceMethod,decimal PriceValue,
    bool ChangeCost,BulkPriceMethod CostMethod,decimal CostValue,
    bool ChangeSupplyType,string SupplyType,string Reason);

internal sealed record BulkProductPreview(BulkProductSnapshot Original,
    long NewPriceCents,long NewCostCents,string NewStockMode)
{
    public bool Changed => Original.PriceCents!=NewPriceCents ||
        Original.CostCents!=NewCostCents ||
        !string.Equals(Original.StockMode,NewStockMode,StringComparison.Ordinal);
}

/// <summary>
/// Atomic price/cost/supply-type changes on explicitly selected active products.
/// Historical invoices, purchase orders, inventory cost basis, and posted
/// journals are never rewritten. A separate audit log retains both values.
/// </summary>
internal static class BulkProductOperations
{
    internal static readonly string[] SupplyTypes={"stocked","made_to_order","supplier_direct"};
    public static IReadOnlyList<(long Id,string Name)> Suppliers()
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT id,name FROM suppliers WHERE is_archived=0 ORDER BY name COLLATE NOCASE";
        using var reader=cmd.ExecuteReader();var list=new List<(long,string)>();
        while(reader.Read())list.Add((reader.GetInt64(0),reader.GetString(1)));
        return list;
    }

    public static IReadOnlyList<BulkProductSnapshot> Search(
        string terms="",string skuPrefix="",long? supplierId=null,string? stockMode=null)
    {
        if(stockMode is not null && !SupplyTypes.Contains(stockMode,StringComparer.Ordinal))
            throw new InvalidOperationException("Unknown supply type.");
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT p.id,p.sku,p.name,coalesce(s.name,''),p.qty,p.price,p.cost,
                   p.stock_mode,p.reorder_cost_total,p.reorder_qty
            FROM products p LEFT JOIN suppliers s ON s.id=p.supplier_id
            WHERE p.is_archived=0
                AND (@terms='' OR p.sku LIKE @termLike ESCAPE '\' OR
                     p.name LIKE @termLike ESCAPE '\' OR
                     coalesce(s.name,'') LIKE @termLike ESCAPE '\')
                AND (@prefix='' OR p.sku LIKE @prefixLike ESCAPE '\')
                AND (@supplier IS NULL OR p.supplier_id=@supplier)
                AND (@mode IS NULL OR p.stock_mode=@mode)
            ORDER BY p.name COLLATE NOCASE,p.sku COLLATE NOCASE,p.id LIMIT 5001
            """;
        static string EscapeLike(string value)=>value.Replace("\\","\\\\")
            .Replace("%","\\%").Replace("_","\\_");
        cmd.Parameters.AddWithValue("@terms",terms.Trim());
        cmd.Parameters.AddWithValue("@termLike","%"+EscapeLike(terms.Trim())+"%");
        cmd.Parameters.AddWithValue("@prefix",skuPrefix.Trim());
        cmd.Parameters.AddWithValue("@prefixLike",EscapeLike(skuPrefix.Trim())+"%");
        cmd.Parameters.AddWithValue("@supplier",supplierId.HasValue?(object)supplierId.Value:DBNull.Value);
        cmd.Parameters.AddWithValue("@mode",stockMode is null?DBNull.Value:stockMode);
        using var reader=cmd.ExecuteReader();var result=new List<BulkProductSnapshot>();
        while(reader.Read())
        {
            result.Add(new BulkProductSnapshot(
                reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                reader.GetInt32(4),reader.GetInt64(5),reader.GetInt64(6),reader.GetString(7),
                reader.IsDBNull(8)?null:reader.GetInt64(8),reader.GetInt32(9)));
        }
        if(result.Count>5000)
            throw new InvalidOperationException(
                "More than 5,000 products match. Narrow the search, SKU prefix, supplier, or type before editing.");
        return result;
    }

    private static long Price(long old,BulkPriceMethod method,decimal amount)
    {
        if(method==BulkPriceMethod.SetAmount)
        {
            if(amount<0 || amount>9999999m)throw new InvalidOperationException("Dollar amount is out of range.");
            return Repository.Cents(amount);
        }
        if(amount < -100m || amount>1000m)
            throw new InvalidOperationException("Percentage change must be between -100% and +1,000%.");
        decimal cents=decimal.Round(old*(1m+amount/100m),0,MidpointRounding.AwayFromZero);
        return checked((long)cents);
    }

    public static IReadOnlyList<BulkProductPreview> Preview(
        IReadOnlyList<BulkProductSnapshot> selection,BulkProductChanges changes)
    {
        if(selection.Count==0)throw new InvalidOperationException("Select at least one product to edit.");
        if(selection.Count!=selection.Select(x=>x.Id).Distinct().Count())
            throw new InvalidOperationException("A product was selected more than once.");
        if(!(changes.ChangePrice||changes.ChangeCost||changes.ChangeSupplyType))
            throw new InvalidOperationException("Choose at least one field to update.");
        if(changes.ChangeSupplyType&&!SupplyTypes.Contains(changes.SupplyType,StringComparer.Ordinal))
            throw new InvalidOperationException("Select a valid supply type.");
        var results=new List<BulkProductPreview>(selection.Count);
        foreach(var item in selection)
        {
            long nextPrice=changes.ChangePrice?Price(item.PriceCents,changes.PriceMethod,changes.PriceValue):item.PriceCents;
            long nextCost=changes.ChangeCost?Price(item.CostCents,changes.CostMethod,changes.CostValue):item.CostCents;
            string nextMode=changes.ChangeSupplyType?changes.SupplyType:item.StockMode;
            if(nextMode!=item.StockMode && item.Stock!=0)
                throw new InvalidOperationException(
                    $"Cannot change supply type for {item.Sku}: {item.Stock:N0} units remain in inventory. "+
                    "Use Inventory / Adjust Stock to resolve its quantity before changing stock treatment.");
            results.Add(new BulkProductPreview(item,nextPrice,nextCost,nextMode));
        }
        return results;
    }

    public static int Apply(IReadOnlyList<BulkProductPreview> preview,string reason)
    {
        if(preview.Count==0)throw new InvalidOperationException("No products were selected.");
        if(preview.Count!=preview.Select(x=>x.Original.Id).Distinct().Count())
            throw new InvalidOperationException("Duplicate product IDs detected.");
        if(string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Enter a reason for the bulk price/cost/supply-type change.");

        using var db=Store.Open();using var tx=db.BeginTransaction();
        string batch=Guid.NewGuid().ToString("N");
        int affected=0;
        foreach(var item in preview)
        {
            var old=item.Original;
            if(!item.Changed)continue;
            if(item.NewPriceCents<0||item.NewCostCents<0||
               !SupplyTypes.Contains(item.NewStockMode,StringComparer.Ordinal))
                throw new InvalidOperationException("Invalid preview data.");
            if(item.NewStockMode!=old.StockMode&&old.Stock!=0)
                throw new InvalidOperationException("Cannot change supply type while on-hand inventory is not zero.");
            using var cmd=db.CreateCommand();
            cmd.Transaction=tx;
            cmd.CommandText="""
                UPDATE products SET price=@newprice,cost=@newcost,stock_mode=@newmode,
                    reorder_cost_total=CASE WHEN @costchanged=1 AND reorder_cost_total IS NOT NULL
                         THEN @reorderTotal ELSE reorder_cost_total END
                WHERE id=@id AND is_archived=0 AND qty=@qty
                    AND price=@oldprice AND cost=@oldcost AND stock_mode=@oldmode
                    AND ((reorder_cost_total IS NULL AND @oldreorder IS NULL)
                       OR reorder_cost_total=@oldreorder)
                    AND reorder_qty=@reorderQty
                """;
            long? newReorder=old.ReorderCostCents.HasValue && item.NewCostCents!=old.CostCents?
                checked(item.NewCostCents*old.ReorderQuantity):null;
            cmd.Parameters.AddWithValue("@id",old.Id);
            cmd.Parameters.AddWithValue("@qty",old.Stock);
            cmd.Parameters.AddWithValue("@oldprice",old.PriceCents);
            cmd.Parameters.AddWithValue("@oldcost",old.CostCents);
            cmd.Parameters.AddWithValue("@oldmode",old.StockMode);
            cmd.Parameters.AddWithValue("@oldreorder",old.ReorderCostCents.HasValue?
                (object)old.ReorderCostCents.Value:DBNull.Value);
            cmd.Parameters.AddWithValue("@reorderQty",old.ReorderQuantity);
            cmd.Parameters.AddWithValue("@newprice",item.NewPriceCents);
            cmd.Parameters.AddWithValue("@newcost",item.NewCostCents);
            cmd.Parameters.AddWithValue("@newmode",item.NewStockMode);
            cmd.Parameters.AddWithValue("@costchanged",item.NewCostCents!=old.CostCents?1:0);
            cmd.Parameters.AddWithValue("@reorderTotal",newReorder.HasValue?(object)newReorder.Value:DBNull.Value);
            if(cmd.ExecuteNonQuery()!=1)
                throw new InvalidOperationException(
                    $"Product {old.Sku} changed since the preview was prepared, or was archived. "+
                    "No changes were saved. Refresh and review the selection again.");
            using var audit=db.CreateCommand();audit.Transaction=tx;
            audit.CommandText="""
                INSERT INTO lab_bulk_product_changes(
                    batch_id,product_id,sku,name,reason,
                    old_price,new_price,old_cost,new_cost,old_supply_type,new_supply_type)
                VALUES(@batch,@id,@sku,@name,@reason,
                       @op,@np,@oc,@nc,@om,@nm)
                """;
            audit.Parameters.AddWithValue("@batch",batch);
            audit.Parameters.AddWithValue("@id",old.Id);
            audit.Parameters.AddWithValue("@sku",old.Sku);
            audit.Parameters.AddWithValue("@name",old.Name);
            audit.Parameters.AddWithValue("@reason",reason.Trim());
            audit.Parameters.AddWithValue("@op",old.PriceCents);
            audit.Parameters.AddWithValue("@np",item.NewPriceCents);
            audit.Parameters.AddWithValue("@oc",old.CostCents);
            audit.Parameters.AddWithValue("@nc",item.NewCostCents);
            audit.Parameters.AddWithValue("@om",old.StockMode);
            audit.Parameters.AddWithValue("@nm",item.NewStockMode);
            audit.ExecuteNonQuery();
            affected++;
        }
        tx.Commit();
        return affected;
    }
}
