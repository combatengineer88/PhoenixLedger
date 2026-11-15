using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

/// <summary>
/// Bulk editor separates the selected record set from the filter and requires
/// an explicit before/after preview before any database write.
/// </summary>
internal sealed class BulkProductEditor : Form
{
    private readonly TextBox search=new(){Width=240,PlaceholderText="Name, SKU, supplier contains..."};
    private readonly TextBox skuPrefix=new(){Width=145,PlaceholderText="SKU prefix (optional)"};
    private readonly ComboBox supplier=new(){Width=185,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox filterMode=new(){Width=145,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly Label matchCount=new(){AutoSize=true,ForeColor=Color.FromArgb(85,101,120)};
    private readonly Label previewStatus=new(){AutoSize=true,ForeColor=Color.FromArgb(55,81,105)};
    private readonly DataGridView grid=new(){Dock=DockStyle.Fill,BackgroundColor=Color.White,
        AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,
        AutoGenerateColumns=false,MultiSelect=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
    private readonly CheckBox changePrice=new(){Text="Change selling price",AutoSize=true};
    private readonly CheckBox changeCost=new(){Text="Change item cost",AutoSize=true};
    private readonly CheckBox changeSupply=new(){Text="Change supply type",AutoSize=true};
    private readonly ComboBox priceMethod=new(){Width=130,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox costMethod=new(){Width=130,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox supplyType=new(){Width=165,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown priceValue=new(){Width=115,DecimalPlaces=2,ThousandsSeparator=true,Maximum=9999999m,Minimum=0};
    private readonly NumericUpDown costValue=new(){Width=115,DecimalPlaces=2,ThousandsSeparator=true,Maximum=9999999m,Minimum=0};
    private readonly TextBox reason=new(){Width=485,PlaceholderText="Reason for changes (required for audit history)"};
    private readonly Button apply=new(){Text="Apply Bulk Changes",Width=191,Height=38,Enabled=false};
    private readonly IReadOnlyCollection<long> initiallySelected;
    private readonly Dictionary<long,BulkProductSnapshot> current=new();
    private IReadOnlyList<BulkProductPreview>? prepared;

    private sealed record SupplierOption(long? Id,string Name)
    {
        public override string ToString()=>Name;
    }

    public BulkProductEditor(IReadOnlyCollection<long> initiallySelected)
    {
        this.initiallySelected=initiallySelected;
        Text="PhoenixLedger — Bulk Product Editor";
        Size=new Size(1420,860);MinimumSize=new Size(1040,700);
        StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",9.6f);BackColor=Color.FromArgb(241,246,251);

        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(14)};
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,92));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,180));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
        Controls.Add(body);

        body.Controls.Add(new Label{Text="BULK EDIT PRODUCTS  •  Only checked active products are changed",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            Font=new Font("Segoe UI",15,FontStyle.Bold),
            ForeColor=Color.FromArgb(29,55,85)},0,0);

        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=true,
            Padding=new Padding(3,2,3,2),BackColor=Color.White};
        supplier.Items.Add(new SupplierOption(null,"All suppliers"));
        foreach(var (id,name) in BulkProductOperations.Suppliers())
            supplier.Items.Add(new SupplierOption(id,name));
        supplier.SelectedIndex=0;
        filterMode.Items.Add("All supply types");
        filterMode.Items.AddRange(BulkProductOperations.SupplyTypes.Cast<object>().ToArray());
        filterMode.SelectedIndex=0;
        filters.Controls.Add(Heading("Filter",51));
        filters.Controls.Add(search);
        filters.Controls.Add(Heading("SKU begins",97));
        filters.Controls.Add(skuPrefix);
        filters.Controls.Add(Heading("Supplier",75));
        filters.Controls.Add(supplier);
        filters.Controls.Add(Heading("Supply type",99));
        filters.Controls.Add(filterMode);
        var runFilter=Button("Find Products",138,()=>RefreshMatches());
        filters.Controls.Add(runFilter);
        filters.Controls.Add(matchCount);
        body.Controls.Add(filters,0,1);

        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Selected",HeaderText="Edit",Width=64,
            SortMode=DataGridViewColumnSortMode.NotSortable});
        AddGridColumn("ID",67);
        AddGridColumn("SKU",185);
        AddGridColumn("Name",270);
        AddGridColumn("Supplier",185);
        AddGridColumn("Current Price",120);
        AddGridColumn("New Price",120);
        AddGridColumn("Current Cost",120);
        AddGridColumn("New Cost",120);
        AddGridColumn("Current Type",150);
        AddGridColumn("New Type",150);
        AddGridColumn("On Hand",98);
        grid.Columns.Cast<DataGridViewColumn>().Where(x=>x.Name!="Selected")
            .ToList().ForEach(x=>x.ReadOnly=true);
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(212,232,252);
        grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(20,42,64);
        grid.RowTemplate.Height=30;
        grid.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged+=(_,e)=>{if(e.ColumnIndex==0)InvalidatePreview();};
        grid.DataError+=(_,e)=>e.ThrowException=false;
        body.Controls.Add(grid,0,2);

        var settings=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,
            BackColor=Color.White,Padding=new Padding(9)};
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute,39));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        body.Controls.Add(settings,0,3);

        priceMethod.Items.AddRange(new object[]{"Set amount ($)","Adjust (%)"});
        costMethod.Items.AddRange(new object[]{"Set amount ($)","Adjust (%)"});
        priceMethod.SelectedIndex=0;costMethod.SelectedIndex=0;
        supplyType.Items.AddRange(BulkProductOperations.SupplyTypes.Cast<object>().ToArray());
        supplyType.SelectedIndex=0;

        var line1=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        line1.Controls.Add(changePrice);line1.Controls.Add(priceMethod);line1.Controls.Add(priceValue);
        line1.Controls.Add(Heading("Example: set to $19.99, or increase/decrease by a percentage.",565));
        settings.Controls.Add(line1,0,0);
        var line2=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        line2.Controls.Add(changeCost);line2.Controls.Add(costMethod);line2.Controls.Add(costValue);
        line2.Controls.Add(Heading("Unit cost; existing reorder total is synchronized. Inventory cost basis is unchanged.",645));
        settings.Controls.Add(line2,0,1);
        var line3=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        line3.Controls.Add(changeSupply);line3.Controls.Add(supplyType);
        line3.Controls.Add(Heading("Changing supply type requires zero on-hand stock.",465));
        settings.Controls.Add(line3,0,2);
        var line4=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        line4.Controls.Add(Heading("Change reason",115));line4.Controls.Add(reason);
        settings.Controls.Add(line4,0,3);

        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.LeftToRight,
            WrapContents=true,AutoScroll=true};
        footer.Controls.Add(Button("Check All Filtered",163,()=>SetCheckAll(true)));
        footer.Controls.Add(Button("Clear Checks",125,()=>SetCheckAll(false)));
        footer.Controls.Add(Button("Preview Changes",168,PreviewSelected));
        apply.Click+=(_,_)=>ApplySelected();
        footer.Controls.Add(apply);
        footer.Controls.Add(Button("Cancel / Close",131,Close));
        footer.Controls.Add(previewStatus);
        body.Controls.Add(footer,0,4);

        foreach(Control control in new Control[]{changePrice,changeCost,changeSupply,
            priceMethod,costMethod,supplyType,priceValue,costValue,reason})
        {
            switch(control)
            {
                case CheckBox c:c.CheckedChanged+=(_,_)=>{UpdateFieldEnabled();InvalidatePreview();};break;
                case ComboBox c:c.SelectedIndexChanged+=(_,_)=>{UpdateFieldEnabled();InvalidatePreview();};break;
                case NumericUpDown n:n.ValueChanged+=(_,_)=>InvalidatePreview();break;
                case TextBox t:t.TextChanged+=(_,_)=>InvalidatePreview();break;
            }
        }
        search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;RefreshMatches();}};
        skuPrefix.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;RefreshMatches();}};
        UpdateFieldEnabled();
        RefreshMatches();
    }

    private void AddGridColumn(string name,int width)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name=name,HeaderText=name,Width=width,
            SortMode=DataGridViewColumnSortMode.NotSortable});
    }

    private static Control Heading(string value,int width)=>
        new Label{Text=value,Width=width,Height=30,Margin=new Padding(6,1,5,1),
            TextAlign=ContentAlignment.MiddleLeft};
    private static Button Button(string name,int width,Action action)
    {
        var b=new Button{Text=name,Width=width,Height=35};
        b.Click+=(_,_)=>{try{action();}catch(Exception e){MessageBox.Show(
            b.FindForm(),e.Message,"Bulk Product Editor",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
        return b;
    }

    private void RefreshMatches()
    {
        var id=(supplier.SelectedItem as SupplierOption)?.Id;
        string? mode=filterMode.SelectedIndex<=0?null:filterMode.SelectedItem?.ToString();
        var matches=BulkProductOperations.Search(search.Text,skuPrefix.Text,id,mode);
        current.Clear();
        grid.Rows.Clear();
        foreach(var product in matches)
        {
            current.Add(product.Id,product);
            grid.Rows.Add(initiallySelected.Contains(product.Id),product.Id,product.Sku,product.Name,
                product.Supplier,Money(product.PriceCents),"",Money(product.CostCents),"",
                product.StockMode,"",product.Stock);
        }
        matchCount.Text=$"  {matches.Count:N0} matched";
        InvalidatePreview();
    }

    private static string Money(long cents)=>Repository.Dollars(cents).ToString("C2",CultureInfo.CurrentCulture);
    private void UpdateFieldEnabled()
    {
        priceMethod.Enabled=priceValue.Enabled=changePrice.Checked;
        costMethod.Enabled=costValue.Enabled=changeCost.Checked;
        supplyType.Enabled=changeSupply.Checked;
        SetupNumber(priceValue,priceMethod.SelectedIndex);
        SetupNumber(costValue,costMethod.SelectedIndex);
    }
    private static void SetupNumber(NumericUpDown n,int method)
    {
        decimal saved=n.Value;
        n.Minimum=method==1?-100m:0m;
        n.Maximum=method==1?1000m:9999999m;
        n.Value=Math.Clamp(saved,n.Minimum,n.Maximum);
    }
    private void InvalidatePreview()
    {
        prepared=null;apply.Enabled=false;
        previewStatus.Text="  Check products, set fields and preview before applying.";
        foreach(DataGridViewRow row in grid.Rows)
        {
            row.Cells["New Price"].Value="";
            row.Cells["New Cost"].Value="";
            row.Cells["New Type"].Value="";
        }
    }
    private void SetCheckAll(bool check)
    {
        foreach(DataGridViewRow row in grid.Rows)
            row.Cells["Selected"].Value=check;
        InvalidatePreview();
    }
    private IReadOnlyList<BulkProductSnapshot> Selected()
    {
        grid.EndEdit();
        return grid.Rows.Cast<DataGridViewRow>()
            .Where(row=>row.Cells["Selected"].Value is bool b && b)
            .Select(row=>current[Convert.ToInt64(row.Cells["ID"].Value,CultureInfo.InvariantCulture)])
            .ToList();
    }
    private BulkProductChanges Changes()=>new(
        changePrice.Checked,priceMethod.SelectedIndex==0?BulkPriceMethod.SetAmount:BulkPriceMethod.AdjustPercent,
        priceValue.Value,
        changeCost.Checked,costMethod.SelectedIndex==0?BulkPriceMethod.SetAmount:BulkPriceMethod.AdjustPercent,
        costValue.Value,
        changeSupply.Checked,supplyType.SelectedItem?.ToString()??"stocked",
        reason.Text.Trim());

    private void PreviewSelected()
    {
        prepared=BulkProductOperations.Preview(Selected(),Changes());
        var lookup=prepared.ToDictionary(x=>x.Original.Id);
        int changed=0;
        foreach(DataGridViewRow row in grid.Rows)
        {
            long id=Convert.ToInt64(row.Cells["ID"].Value,CultureInfo.InvariantCulture);
            if(!lookup.TryGetValue(id,out var preview))continue;
            row.Cells["New Price"].Value=Money(preview.NewPriceCents);
            row.Cells["New Cost"].Value=Money(preview.NewCostCents);
            row.Cells["New Type"].Value=preview.NewStockMode;
            if(preview.Changed)changed++;
        }
        apply.Enabled=changed>0;
        previewStatus.Text=$"  {prepared.Count:N0} selected; {changed:N0} will change. "+
            "Review the proposed values before Apply.";
    }

    private void ApplySelected()
    {
        try
        {
            if(prepared is null||!apply.Enabled)
                throw new InvalidOperationException("Preview the selected products first.");
            if(string.IsNullOrWhiteSpace(reason.Text))
                throw new InvalidOperationException("Enter a reason for the bulk edit before saving.");
            int affected=prepared.Count(x=>x.Changed);
            if(MessageBox.Show(this,
                $"Apply the previewed changes to {affected:N0} product(s)?\n\n"+
                "This will update active product selling prices, unit costs and/or supply types.\n"+
                "Existing invoices, purchase orders, transaction records, and inventory cost basis are unchanged.\n"+
                "A verified database backup and a per-product audit record will be created.\n\n"+
                "Continue?",
                "Confirm Bulk Product Edit",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;

            string dir=Path.Combine(Store.Root,"backups");
            Directory.CreateDirectory(dir);
            string backup=Path.Combine(dir,
                $"lab_before_bulk_products_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.db");
            BusinessData.BackupTo(backup);
            int saved=BulkProductOperations.Apply(prepared,reason.Text);
            MessageBox.Show(this,
                $"{saved:N0} products updated successfully.\n\nVerified backup:\n{backup}\n\n"+
                "Previous and new values were saved to the bulk edit audit history.",
                "Bulk Edit Complete",MessageBoxButtons.OK,MessageBoxIcon.Information);
            DialogResult=DialogResult.OK;Close();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Bulk edit not applied",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
            InvalidatePreview();
        }
    }
}
