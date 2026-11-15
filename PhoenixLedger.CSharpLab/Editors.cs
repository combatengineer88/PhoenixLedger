using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal static class Editors
{
    private static readonly Color Ink = Color.FromArgb(33, 48, 68);
    private static readonly Color Slate = Color.FromArgb(235, 241, 247);

    private static Form Window(string title, int width=690, int height=690)
    {
        return new Form {Text=title,Width=width,Height=height,StartPosition=FormStartPosition.CenterParent,
            MinimumSize=new Size(560,460), BackColor=Color.White,Font=new Font("Segoe UI",10),
            ShowIcon=true,Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)};
    }
    private static (Form Form,TableLayoutPanel Table,FlowLayoutPanel Footer) Editor(string title)
    {
        var f=Window(title);
        var footer=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            Height=64,Padding=new Padding(12),WrapContents=false,BackColor=Slate};
        var scroller=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(22,20,20,20)};
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Padding=new Padding(0)};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        shell.Controls.Add(scroller,0,0);
        shell.Controls.Add(footer,0,1);
        f.Controls.Add(shell);
        var table=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
            Dock=DockStyle.Top,ColumnCount=2,Padding=new Padding(0),BackColor=Color.White};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        scroller.Controls.Add(table);
        scroller.Resize+=(_,_)=> table.Width=Math.Max(470,scroller.ClientSize.Width-25);
        return(f,table,footer);
    }
    private static void Add(TableLayoutPanel t,string label,Control input)
    {
        int row=t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption=new Label {Text=label,ForeColor=Ink,AutoSize=true,Margin=new Padding(0,11,10,10)};
        t.Controls.Add(caption,0,row);
        input.Dock=DockStyle.Top;
        input.Margin=new Padding(0,5,0,7);
        t.Controls.Add(input,1,row);
    }
    private static TextBox Field(string value="",bool multiline=false)
    {
        return new TextBox {Text=value,BorderStyle=BorderStyle.FixedSingle,
            Multiline=multiline,Height=multiline?66:29,ScrollBars=multiline?ScrollBars.Vertical:ScrollBars.None};
    }
    private static Button Action(string text,Action run,bool primary=false)
    {
        var b=new Button{Text=text,Width=150,Height=35,FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false,
            BackColor=primary?Color.FromArgb(29,111,190):Color.White,
            ForeColor=primary?Color.White:Ink,Cursor=Cursors.Hand,Margin=new Padding(4)};
        b.FlatAppearance.BorderColor=Color.FromArgb(185,198,214);
        b.Click+=(_,_)=>run();return b;
    }
    private static bool TryMoney(string text,out decimal amount)
    {
        text=text.Replace("$","").Replace(",","").Trim();
        return decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out amount) && amount>=0 && amount<=1_000_000_000m;
    }
    private static string Price(decimal d)=>d.ToString("0.00",CultureInfo.InvariantCulture);
    private static void Fail(Form f,Exception ex)=>MessageBox.Show(f,ex.Message,"Unable to save",MessageBoxButtons.OK,MessageBoxIcon.Warning);

    public static bool Product(IWin32Window owner,long id)
    {
        var model=id==0?new Product():Repository.LoadProduct(id);
        var (f,table,footer)=Editor(id==0?"Add Product — C# Lab":"Edit Product — C# Lab");
        var sku=Field(model.Sku);var name=Field(model.Name);var barcode=Field(model.Barcode);
        var description=Field(model.Description,true);var bin=Field(model.Bin);
        var price=Field(Price(model.Price));var cost=Field(Price(model.Cost));
        var bulk=Field(model.ReorderCost.HasValue?Price(model.ReorderCost.Value):"");
        var qty=new NumericUpDown{Minimum=0,Maximum=100000000,Value=Math.Min(100000000,model.Quantity)};
        var min=new NumericUpDown{Minimum=0,Maximum=100000000,Value=Math.Min(100000000,model.ReorderPoint)};
        var reorder=new NumericUpDown{Minimum=1,Maximum=100000000,Value=Math.Clamp(model.ReorderQuantity,1,100000000)};
        var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        mode.Items.AddRange(new object[]{"stocked","made_to_order","supplier_direct"});
        mode.SelectedItem=model.StockMode is "stocked" or "made_to_order" or "supplier_direct"?model.StockMode:"stocked";
        var suppliers=Repository.SupplierOptions(model.SupplierId);
        var supplier=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList};
        supplier.Items.Add("(No supplier)");
        foreach(var entry in suppliers)supplier.Items.Add(entry.Name);
        supplier.SelectedIndex=Math.Max(0,suppliers.FindIndex(x=>x.Id==model.SupplierId)+1);
        string photoPath=model.ImagePath,barcodePath=model.BarcodeImagePath;
        var photoLabel=new Label {Text=string.IsNullOrEmpty(photoPath)?"None":Path.GetFileName(photoPath),AutoSize=true,ForeColor=Ink};
        var barcodeLabel=new Label {Text=string.IsNullOrEmpty(barcodePath)?"None":Path.GetFileName(barcodePath),AutoSize=true,ForeColor=Ink};
        var attachPhoto=Action("Choose product image",()=>{
            using var picker=new OpenFileDialog{Filter="Image files|*.png;*.jpg;*.jpeg;*.webp"};
            if(picker.ShowDialog(f)==DialogResult.OK){photoPath=picker.FileName;photoLabel.Text=Path.GetFileName(photoPath);}
        });
        var attachBarcode=Action("Choose barcode PNG",()=>{
            using var picker=new OpenFileDialog{Filter="PNG barcode image|*.png"};
            if(picker.ShowDialog(f)==DialogResult.OK){barcodePath=picker.FileName;barcodeLabel.Text=Path.GetFileName(barcodePath);}
        });
        var imagePanel=new FlowLayoutPanel{Height=38,AutoSize=true};imagePanel.Controls.Add(attachPhoto);imagePanel.Controls.Add(photoLabel);
        var barcodePanel=new FlowLayoutPanel{Height=38,AutoSize=true};barcodePanel.Controls.Add(attachBarcode);barcodePanel.Controls.Add(barcodeLabel);
        var calculated=new Label{Text="",AutoSize=true,ForeColor=Color.FromArgb(26,107,171),Font=new Font("Segoe UI",10,FontStyle.Bold)};
        void Recalc(object? sender,EventArgs e)
        {
            if(string.IsNullOrWhiteSpace(bulk.Text)) {cost.ReadOnly=false;calculated.Text="Manual item cost";return;}
            if(TryMoney(bulk.Text,out decimal total))
            {
                decimal per=decimal.Round(total/reorder.Value,2,MidpointRounding.AwayFromZero);
                cost.Text=Price(per);cost.ReadOnly=true;
                calculated.Text=$"Bulk {Price(total)} ÷ {reorder.Value} = {Price(per)} per item";
            }
            else {cost.ReadOnly=true;calculated.Text="Enter a valid bulk cost";}
        }
        bulk.TextChanged+=Recalc;reorder.ValueChanged+=Recalc;
        Add(table,"SKU *",sku);Add(table,"Product name *",name);
        Add(table,"Barcode / UPC",barcode);Add(table,"Supplier",supplier);
        Add(table,"Description",description);Add(table,"Selling price ($)",price);
        Add(table,"Reorder quantity",reorder);Add(table,"Bulk reorder cost ($)",bulk);
        Add(table,"Calculated item cost",calculated);Add(table,"Unit cost ($)",cost);
        Add(table,"Quantity on hand",qty);Add(table,"Reorder point",min);
        Add(table,"Stock mode",mode);Add(table,"Bin / shelf",bin);
        Add(table,"Product image",imagePanel);Add(table,"Barcode PNG",barcodePanel);
        Recalc(null,EventArgs.Empty);
        footer.Controls.Add(Action("Save Product",()=>{
            try
            {
                if(!TryMoney(price.Text,out var priceValue)||!TryMoney(cost.Text,out var costValue))
                    throw new InvalidOperationException("Enter valid nonnegative price and cost values.");
                decimal? bulkValue=null;
                if(!string.IsNullOrWhiteSpace(bulk.Text))
                {
                    if(!TryMoney(bulk.Text,out var parsed))throw new InvalidOperationException("Invalid bulk reorder cost.");
                    bulkValue=parsed;
                }
                model.Sku=sku.Text.Trim();model.Name=name.Text.Trim();model.Barcode=barcode.Text.Trim();
                model.Description=description.Text;model.Price=priceValue;model.Cost=costValue;
                model.ReorderQuantity=(int)reorder.Value;model.ReorderPoint=(int)min.Value;
                model.ReorderCost=bulkValue;model.Quantity=(int)qty.Value;model.StockMode=mode.SelectedItem?.ToString()??"stocked";
                model.Bin=bin.Text;model.SupplierId=supplier.SelectedIndex>0?suppliers[supplier.SelectedIndex-1].Id:null;
                model.ImagePath=SaveMediaCopy(photoPath,model.ImagePath,false);
                model.BarcodeImagePath=SaveMediaCopy(barcodePath,model.BarcodeImagePath,true);
                Repository.SaveProduct(model);f.DialogResult=DialogResult.OK;f.Close();
            }
            catch(Exception ex){Fail(f,ex);}
        },true));
        footer.Controls.Add(Action("Cancel",()=>f.Close()));
        return f.ShowDialog(owner)==DialogResult.OK;
    }
    private static string SaveMediaCopy(string chosen,string original,bool barcode)
    {
        if(string.IsNullOrWhiteSpace(chosen)||chosen==original)return original;
        if(!File.Exists(chosen))throw new FileNotFoundException("Selected image could not be found.",chosen);
        if(barcode && !".png".Equals(Path.GetExtension(chosen),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Barcode attachments must be PNG images.");
        string destination=Path.Combine(Store.Assets,(barcode?"barcode_":"product_")+Guid.NewGuid().ToString("N")+Path.GetExtension(chosen));
        File.Copy(chosen,destination,false);return destination;
    }

    public static bool Supplier(IWin32Window owner,long id)
    {
        var model=id==0?new Supplier():Repository.LoadSupplier(id);
        var (f,table,footer)=Editor(id==0?"Add Supplier — C# Lab":"Edit Supplier — C# Lab");
        var fields=new (string Key,string Label,string Value)[]{
            ("name","Supplier name *",model.Name),("contact","Contact",model.Contact),
            ("email","Email",model.Email),("phone","Phone",model.Phone),("website","Website",model.Website),
            ("address","Street address",model.Address),("city","City",model.City),
            ("state","State",model.State),("zip","ZIP / Postal Code",model.Zip),("notes","Notes",model.Notes)};
        var inputs=new Dictionary<string,TextBox>();
        foreach(var (key,label,value) in fields){var box=Field(value,key=="notes");inputs.Add(key,box);Add(table,label,box);}
        footer.Controls.Add(Action("Save Supplier",()=>{
            try
            {
                model.Name=inputs["name"].Text;model.Contact=inputs["contact"].Text;model.Email=inputs["email"].Text;
                model.Phone=inputs["phone"].Text;model.Website=inputs["website"].Text;model.Address=inputs["address"].Text;
                model.City=inputs["city"].Text;model.State=inputs["state"].Text;model.Zip=inputs["zip"].Text;model.Notes=inputs["notes"].Text;
                Repository.SaveSupplier(model);f.DialogResult=DialogResult.OK;f.Close();
            }
            catch(Exception ex){Fail(f,ex);}
        },true));
        footer.Controls.Add(Action("Cancel",()=>f.Close()));
        return f.ShowDialog(owner)==DialogResult.OK;
    }
    public static bool Customer(IWin32Window owner,long id)
    {
        var model=id==0?new Customer():Repository.LoadCustomer(id);
        var (f,table,footer)=Editor(id==0?"Add Customer — C# Lab":"Edit Customer — C# Lab");
        var fields=new (string Key,string Label,string Value)[]{
            ("name","Customer name *",model.Name),("email","Email",model.Email),
            ("phone","Phone",model.Phone),("address","Street address",model.Address),
            ("city","City",model.City),("state","State",model.State),
            ("zip","ZIP / Postal Code",model.Zip),("notes","Notes",model.Notes)};
        var inputs=new Dictionary<string,TextBox>();
        foreach(var (key,label,value) in fields){var box=Field(value,key=="notes");inputs.Add(key,box);Add(table,label,box);}
        var tax=new CheckBox{Text="Sales-tax exempt",Checked=model.TaxExempt,AutoSize=true,ForeColor=Ink};Add(table,"Tax status",tax);
        footer.Controls.Add(Action("Save Customer",()=>{
            try
            {
                model.Name=inputs["name"].Text;model.Email=inputs["email"].Text;model.Phone=inputs["phone"].Text;
                model.Address=inputs["address"].Text;model.City=inputs["city"].Text;model.State=inputs["state"].Text;
                model.Zip=inputs["zip"].Text;model.Notes=inputs["notes"].Text;model.TaxExempt=tax.Checked;
                Repository.SaveCustomer(model);f.DialogResult=DialogResult.OK;f.Close();
            }
            catch(Exception ex){Fail(f,ex);}
        },true));
        footer.Controls.Add(Action("Cancel",()=>f.Close()));
        return f.ShowDialog(owner)==DialogResult.OK;
    }
    public static bool AdjustStock(IWin32Window owner,string code="")
    {
        var (f,table,footer)=Editor("Scan to Adjust Inventory — C# Lab");
        f.Height=450;
        var scan=Field(code);var reason=Field();
        var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList};
        mode.Items.AddRange(new object[]{"Add","Remove","Set"});mode.SelectedIndex=0;
        var qty=new NumericUpDown{Minimum=0,Maximum=100000000,Value=1};
        Add(table,"Scan barcode / SKU",scan);Add(table,"Operation",mode);
        Add(table,"Quantity",qty);Add(table,"Reason *",reason);
        footer.Controls.Add(Action("Save Adjustment",()=>{
            try
            {
                string result=Repository.AdjustStock(scan.Text,mode.SelectedItem?.ToString()??"",(int)qty.Value,reason.Text);
                MessageBox.Show(f,result,"Inventory updated in C# Lab",MessageBoxButtons.OK,MessageBoxIcon.Information);
                f.DialogResult=DialogResult.OK;f.Close();
            }
            catch(Exception ex){Fail(f,ex);}
        },true));
        footer.Controls.Add(Action("Cancel",()=>f.Close()));
        f.Shown+=(_,_)=>scan.Focus();return f.ShowDialog(owner)==DialogResult.OK;
    }
}
