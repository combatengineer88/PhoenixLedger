using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private sealed record Option(long Id,string Name);
    private static List<Option> Options(string table)
    {
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText=table switch
        {
            "suppliers"=>"SELECT id,name FROM suppliers WHERE is_archived=0 ORDER BY name",
            "products"=>"SELECT id,sku || '  —  ' || name FROM products WHERE is_archived=0 ORDER BY name",
            _=>throw new InvalidOperationException("Unsupported table.")
        };
        using var r=cmd.ExecuteReader();var list=new List<Option>();
        while(r.Read())list.Add(new Option(r.GetInt64(0),r.GetString(1)));
        return list;
    }

    private static Label FormLabel(string text)=>new()
    {
        Text=text,AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold),
        Margin=new Padding(5,11,5,4)
    };

    private void NewPurchaseOrder()=>PurchaseOrderEditor(0);
    private void EditPurchaseOrder()=>PurchaseOrderEditor(SelectedId());

    private void CancelPurchaseOrder()
    {
        long id=SelectedId();
        if(MessageBox.Show(this,$"Cancel purchase order #{id}? Only unreceived orders can be cancelled.",
            "Cancel purchase order",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        FinancialOperations.CancelPurchaseOrder(id);
        RefreshGrid();
    }

    private void PurchaseOrderEditor(long editId)
    {
        var suppliers=Options("suppliers");
        var products=Options("products");
        if(suppliers.Count==0||products.Count==0)
            throw new InvalidOperationException("Create a supplier and at least one product before creating a PO.");
        if(editId>0 && FinancialOperations.PurchaseOrderInfo(editId).Status is not ("Draft" or "Open"))
            throw new InvalidOperationException("Only unreceived Draft/Open purchase orders can be edited.");
        using var win=new Form{Text=editId>0?"Edit purchase order #"+editId:"Create purchase order — C# Lab",Width=1020,Height=670,
            StartPosition=FormStartPosition.CenterParent,MinimumSize=new Size(850,570)};
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(14)};
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,70));
        var vendor=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=570,DisplayMember="Name",ValueMember="Id",DataSource=suppliers};
        var header=new FlowLayoutPanel{Dock=DockStyle.Fill};
        header.Controls.Add(FormLabel("Supplier:"));header.Controls.Add(vendor);
        var on=new DateTimePicker{Value=DateTime.Today,Width=160};
        header.Controls.Add(FormLabel("Order date:"));header.Controls.Add(on);body.Controls.Add(header,0,0);
        var notes=new TextBox{Width=800,PlaceholderText="Purchase order notes (optional)"};
        var noteBar=new FlowLayoutPanel{Dock=DockStyle.Fill};
        noteBar.Controls.Add(FormLabel("Notes:"));noteBar.Controls.Add(notes);body.Controls.Add(noteBar,0,1);
        body.Controls.Add(new Label{Text="Add product rows, then enter quantity and unit cost. All costs are per item.",
            Dock=DockStyle.Fill,ForeColor=Color.FromArgb(100,117,138)},0,2);
        var lines=new DataGridView{Dock=DockStyle.Fill,AutoGenerateColumns=false,
            AllowUserToAddRows=true,AllowUserToDeleteRows=true,RowHeadersVisible=false,BackgroundColor=Color.White};
        var productCol=new DataGridViewComboBoxColumn
        {
            HeaderText="Product",Name="Product",DataSource=products,
            DisplayMember="Name",ValueMember="Id",Width=490
        };
        lines.Columns.Add(productCol);
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Quantity",Name="Quantity",Width=135});
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Unit Cost ($)",Name="UnitCost",Width=170});
        if(editId>0)
        {
            var info=FinancialOperations.PurchaseOrderInfo(editId);
            vendor.SelectedValue=info.SupplierId;
            on.Value=info.Date;
            notes.Text=info.Notes;
            foreach(var item in FinancialOperations.PurchaseLines(editId))
                lines.Rows.Add(item.ProductId,item.Ordered,Repository.Dollars(item.CostCents).ToString("0.00"));
        }
        lines.DataError+=(_,e)=>{e.ThrowException=false;};
        body.Controls.Add(lines,0,3);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Cancel",Width=130,Height=40,DialogResult=DialogResult.Cancel};
        var draft=new Button{Text="Save Draft",Width=165,Height=40};
        var create=new Button{Text="Create Open PO",Width=185,Height=40};
        if(editId>0)
        {
            draft.Visible=false;
            create.Text="Save PO Changes";
        }
        footer.Controls.Add(cancel);footer.Controls.Add(create);footer.Controls.Add(draft);
        body.Controls.Add(footer,0,4);win.Controls.Add(body);win.CancelButton=cancel;
        void save(bool asDraft)
        {
            try
            {
                lines.EndEdit();
                var items=new List<PurchaseLine>();
                foreach(DataGridViewRow row in lines.Rows)
                {
                    if(row.IsNewRow)continue;
                    if(row.Cells["Product"].Value is null)continue;
                    long product=Convert.ToInt64(row.Cells["Product"].Value,CultureInfo.InvariantCulture);
                    if(!int.TryParse(row.Cells["Quantity"].Value?.ToString(),out int quantity)||quantity<=0)
                        throw new InvalidOperationException("Every product row needs a positive quantity.");
                    if(!decimal.TryParse(row.Cells["UnitCost"].Value?.ToString(),NumberStyles.Currency,
                        CultureInfo.CurrentCulture,out decimal cost)||cost<0)
                        throw new InvalidOperationException("Enter a valid nonnegative unit cost for each row.");
                    items.Add(new PurchaseLine(product,quantity,Repository.Cents(cost)));
                }
                if(items.Count==0)throw new InvalidOperationException("Add at least one product.");
                long id=editId;
                if(editId>0)
                    FinancialOperations.EditPurchaseOrder(editId,(long)vendor.SelectedValue!,items,on.Value,notes.Text);
                else
                    id=FinancialOperations.CreatePurchaseOrder((long)vendor.SelectedValue!,
                        items,on.Value,notes.Text,asDraft);
                MessageBox.Show(win,editId>0?$"Purchase order #{id} updated.":
                    $"Purchase order #{id} created as {(asDraft?"Draft":"Open")}.",
                    "Purchase order saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(win,ex.Message,"Purchase order not saved",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
        draft.Click+=(_,_)=>save(true);
        create.Click+=(_,_)=>save(false);
        if(win.ShowDialog(this)==DialogResult.OK)RefreshGrid();
    }

    private void ApprovePurchaseOrder()
    {
        long id=SelectedId();
        if(MessageBox.Show(this,$"Approve draft purchase order #{id}?","Approve PO",
            MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        FinancialOperations.ApprovePurchaseOrder(id);RefreshGrid();
    }

    private void ReceivePurchaseOrder()
    {
        long po=SelectedId();
        var items=FinancialOperations.PurchaseLines(po);
        if(items.Count==0)throw new InvalidOperationException("Purchase order has no items.");
        using var win=new Form{Text=$"Receive purchase order #{po}",Width=920,Height=570,
            MinimumSize=new Size(800,480),StartPosition=FormStartPosition.CenterParent};
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,Padding=new Padding(12)};
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,63));
        var head=new FlowLayoutPanel{Dock=DockStyle.Fill};
        head.Controls.Add(FormLabel("Receipt date:"));
        var when=new DateTimePicker{Value=DateTime.Today,Width=170};
        head.Controls.Add(when);
        head.Controls.Add(FormLabel("Notes:"));
        var notes=new TextBox{Width=370};head.Controls.Add(notes);body.Controls.Add(head,0,0);
        var rows=new DataGridView
        {
            Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
            RowHeadersVisible=false,AutoGenerateColumns=false,BackgroundColor=Color.White
        };
        rows.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Line ID",Name="Id",ReadOnly=true,Width=75});
        rows.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Product",Name="Product",ReadOnly=true,Width=300});
        rows.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Ordered",Name="Ordered",ReadOnly=true,Width=105});
        rows.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Already Received",Name="Received",ReadOnly=true,Width=140});
        rows.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Receive Now",Name="Now",Width=130});
        foreach(var item in items)
            rows.Rows.Add(item.Id,item.Sku+"  "+item.Name,item.Ordered,item.Received,
                item.Ordered-item.Received);
        body.Controls.Add(rows,0,1);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Cancel",Width=130,Height=39,DialogResult=DialogResult.Cancel};
        var save=new Button{Text="Receive Selected Quantities",Width=245,Height=39};
        footer.Controls.Add(cancel);footer.Controls.Add(save);body.Controls.Add(footer,0,2);
        win.Controls.Add(body);win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                rows.EndEdit();
                var selected=new List<ReceiptLine>();
                foreach(DataGridViewRow row in rows.Rows)
                {
                    if(!int.TryParse(row.Cells["Now"].Value?.ToString(),out int qty)||qty<0)
                        throw new InvalidOperationException("Receipt quantities must be whole numbers, zero or greater.");
                    selected.Add(new ReceiptLine(Convert.ToInt64(row.Cells["Id"].Value,CultureInfo.InvariantCulture),qty));
                }
                string result=FinancialOperations.Receive(po,selected,when.Value,notes.Text);
                MessageBox.Show(win,"Receipt saved. PO status: "+result+".\nInventory and payable journal updated.",
                    "Items received",MessageBoxButtons.OK,MessageBoxIcon.Information);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(win,ex.Message,"Receipt failed",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(win.ShowDialog(this)==DialogResult.OK)RefreshGrid();
    }

    private void PayInvoiceOrSupplier(bool supplier)
    {
        long id=SelectedId();
        long due=supplier?FinancialOperations.SupplierBalance(id):FinancialOperations.InvoiceDue(id);
        if(due<=0)throw new InvalidOperationException("There is no outstanding balance to pay.");
        using var win=new Form{Text=supplier?$"Record supplier payment for PO #{id}":$"Record payment on invoice #{id}",
            Width=530,Height=340,StartPosition=FormStartPosition.CenterParent,
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=5,Padding=new Padding(17)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,153));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<4;i++)panel.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var amount=new NumericUpDown{DecimalPlaces=2,Minimum=0.01M,Maximum=Math.Max(.01M,Repository.Dollars(due)),
            Value=Repository.Dollars(due),ThousandsSeparator=true,Dock=DockStyle.Fill};
        var date=new DateTimePicker{Value=DateTime.Today,Dock=DockStyle.Fill};
        var method=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        method.Items.AddRange(new object[]{"Bank Transfer","ACH","Check","Cash","Credit Card","Other"});
        method.SelectedIndex=0;
        var reference=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Check number or reference"};
        (string,Control)[] fields={
            ($"Due: {Repository.Dollars(due):C2}",amount),("Payment date",date),
            ("Payment method",method),("Reference",reference)};
        for(int i=0;i<fields.Length;i++)
        {
            panel.Controls.Add(new Label{Text=fields[i].Item1,Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft},0,i);
            panel.Controls.Add(fields[i].Item2,1,i);
        }
        if(!supplier)reference.Enabled=false;
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,Width=100,Height=35};
        var save=new Button{Text="Record Payment",Width=160,Height=35};
        footer.Controls.Add(cancel);footer.Controls.Add(save);panel.Controls.Add(footer,1,4);
        win.Controls.Add(panel);win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                if(supplier)FinancialOperations.PaySupplier(id,amount.Value,date.Value,method.Text,reference.Text);
                else FinancialOperations.PayInvoice(id,amount.Value,date.Value,method.Text);
                MessageBox.Show(win,"Payment recorded in the C# Lab and posted to the journal.",
                    "Payment saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(win,ex.Message,"Payment not saved",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(win.ShowDialog(this)==DialogResult.OK)RefreshGrid();
    }
}
