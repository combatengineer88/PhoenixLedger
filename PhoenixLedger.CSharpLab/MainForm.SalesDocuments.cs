using System.Data;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private void CreateEstimate()=>EditSalesDocument("Estimate",0);
    private void CreateInvoice()=>EditSalesDocument("Invoice",0);
    private void EditDraftEstimate()
    {
        long id=SelectedId();
        var doc=SalesDocuments.Load(id);
        if(doc.Kind!="Estimate"||doc.Status!="Draft")
            throw new InvalidOperationException("Select a draft estimate to edit.");
        EditSalesDocument("Estimate",id);
    }
    private void ApproveEstimate()
    {
        long id=SelectedId();
        var doc=SalesDocuments.Load(id);
        if(doc.Kind!="Estimate"||doc.Status!="Draft")
            throw new InvalidOperationException("Only draft estimates can be approved.");
        if(MessageBox.Show(this,$"Approve estimate #{id} for invoicing?\n\n"+
            "After approval, this estimate becomes read-only. You can convert it to an invoice without changing the original.",
            "Approve Estimate",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        SalesDocuments.Approve(id);
        RefreshGrid();
    }
    private void ConvertEstimateToInvoice()
    {
        long id=SelectedId();
        var doc=SalesDocuments.Load(id);
        if(doc.Kind!="Estimate"||doc.Status!="Approved")
            throw new InvalidOperationException("Select an approved estimate (not a draft or an invoice) to convert.");
        if(MessageBox.Show(this,$"Convert approved estimate #{id} into a new invoice?\n\n"+
            "All line descriptions, quantities, saved prices and taxes will be copied exactly.\n"+
            "The estimate will remain in the catalog with status Converted and cannot be converted twice.",
            "Convert Estimate to Invoice",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        long invoice=SalesDocuments.ConvertApprovedEstimate(id,DateTime.Today);
        RefreshGrid();
        MessageBox.Show(this,$"Invoice #{invoice} created from estimate #{id}.\n\n"+
            "The invoice is Open and can accept payments. The original estimate remains available.",
            "Invoice Created",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private void EditSalesDocument(string kind,long editId)
    {
        if(kind is not ("Estimate" or "Invoice"))
            throw new InvalidOperationException("Unsupported document type.");
        SalesDocumentInfo? previous=editId>0?SalesDocuments.Load(editId):null;
        if(previous!=null && (previous.Kind!="Estimate"||previous.Status!="Draft"))
            throw new InvalidOperationException("Only draft estimates can be edited.");

        var customers=SalesDocuments.Customers().Select(x=>new Option(x.Id,x.Name)).ToList();
        if(customers.Count==0)throw new InvalidOperationException("Create or restore an active customer first.");
        var available=SalesDocuments.Products();
        var products=new List<Option>{new(0,"Custom line / service (no catalog product)")};
        products.AddRange(available.Select(x=>new Option(x.Id,x.Sku+"  —  "+x.Name)));
        var productInfo=available.ToDictionary(x=>x.Id);
        if(previous!=null)
            foreach(var line in previous.Lines.Where(x=>x.ProductId.HasValue))
            {
                long id=line.ProductId!.Value;
                if(products.All(x=>x.Id!=id))
                    products.Add(new Option(id,$"Archived product #{id} — restore before saving"));
            }

        using var win=new Form
        {
            Text=editId>0?$"Edit Draft Estimate #{editId}":$"Create {kind} — PhoenixLedger C# Lab",
            Width=1190,Height=775,MinimumSize=new Size(950,650),
            StartPosition=FormStartPosition.CenterParent,Font=new Font("Segoe UI",10),
            BackColor=Color.FromArgb(242,246,251)
        };
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(14)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
        win.Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text=kind=="Estimate"?"ESTIMATE — Draft → Approve → Convert to Invoice":
                "INVOICE — Create directly without an estimate",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,
            Font=new Font("Segoe UI",16,FontStyle.Bold),
            ForeColor=Color.FromArgb(26,52,77)
        },0,0);
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
        var customer=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember="Name",
            ValueMember="Id",DataSource=customers,Width=490};
        var date=new DateTimePicker{Value=previous?.Date??DateTime.Today,Width=160};
        top.Controls.Add(FormLabel("Customer:"));top.Controls.Add(customer);
        top.Controls.Add(FormLabel(kind+" date:"));top.Controls.Add(date);
        layout.Controls.Add(top,0,1);
        if(previous!=null)
        {
            if(customers.All(x=>x.Id!=previous.CustomerId))
            {
                customers.Add(new Option(previous.CustomerId,$"Archived customer #{previous.CustomerId} — restore before editing"));
                customer.DataSource=null;customer.DataSource=customers;
                customer.DisplayMember="Name";customer.ValueMember="Id";
            }
            customer.SelectedValue=previous.CustomerId;
        }

        var meta=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,WrapContents=false};
        var notes=new TextBox{Width=690,Text=previous?.Notes??"",
            PlaceholderText="Notes, job reference or terms (optional)"};
        var tax=new NumericUpDown{Width=96,Minimum=0,Maximum=100,DecimalPlaces=2,
            Increment=.25m,ThousandsSeparator=false};
        if(previous!=null && previous.Subtotal>0)
            tax.Value=Math.Clamp(decimal.Round(previous.Tax*100m/previous.Subtotal,2),
                tax.Minimum,tax.Maximum);
        meta.Controls.Add(FormLabel("Notes:"));meta.Controls.Add(notes);
        meta.Controls.Add(FormLabel("Tax %:"));meta.Controls.Add(tax);
        layout.Controls.Add(meta,0,2);
        var lines=new DataGridView
        {
            Dock=DockStyle.Fill,AutoGenerateColumns=false,
            AllowUserToAddRows=true,AllowUserToDeleteRows=true,RowHeadersVisible=false,
            BackgroundColor=Color.White,EditMode=DataGridViewEditMode.EditOnEnter
        };
        var productCol=new DataGridViewComboBoxColumn
        {
            HeaderText="Catalog product / custom",Name="Product",DataSource=products,
            ValueMember="Id",DisplayMember="Name",Width=340
        };
        lines.Columns.Add(productCol);
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Line Description",Name="Description",Width=320});
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Qty",Name="Quantity",Width=85});
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Unit Price ($)",Name="UnitPrice",Width=145});
        lines.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Line Total",Name="LineTotal",ReadOnly=true,Width=143});
        lines.RowTemplate.Height=32;
        lines.DataError+=(_,e)=>e.ThrowException=false;
        lines.CurrentCellDirtyStateChanged+=(_,_)=>{
            if(lines.IsCurrentCellDirty && lines.CurrentCell is DataGridViewComboBoxCell)
                lines.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        var totals=new Label{Text="Subtotal: $0.00     Tax: $0.00     Total: $0.00",
            Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleRight,
            Font=new Font("Segoe UI",12,FontStyle.Bold),ForeColor=Color.FromArgb(25,70,122)};
        void updateTotals()
        {
            decimal subtotal=0;
            foreach(DataGridViewRow row in lines.Rows)
            {
                if(row.IsNewRow)continue;
                if(int.TryParse(row.Cells["Quantity"].Value?.ToString(),out int qty)&&qty>0 &&
                   decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(),NumberStyles.Currency,
                       CultureInfo.CurrentCulture,out var amount) && amount>=0)
                {
                    decimal sum=checked(qty*amount);
                    row.Cells["LineTotal"].Value=sum.ToString("C2",CultureInfo.CurrentCulture);
                    subtotal=checked(subtotal+sum);
                }
                else row.Cells["LineTotal"].Value="";
            }
            decimal taxAmount=decimal.Round(subtotal*tax.Value/100m,2,MidpointRounding.AwayFromZero);
            totals.Text=$"Subtotal: {subtotal:C2}     Tax: {taxAmount:C2}     Total: {subtotal+taxAmount:C2}";
        }
        bool loadingExisting=true;
        lines.CellValueChanged+=(_,e)=>{
            if(loadingExisting||e.RowIndex<0||lines.Rows[e.RowIndex].IsNewRow)return;
            var row=lines.Rows[e.RowIndex];
            if(e.ColumnIndex==lines.Columns["Product"]!.Index &&
               row.Cells["Product"].Value is not null)
            {
                long id=Convert.ToInt64(row.Cells["Product"].Value,CultureInfo.InvariantCulture);
                if(productInfo.TryGetValue(id,out var product))
                {
                    row.Cells["Description"].Value=product.Name;
                    row.Cells["UnitPrice"].Value=Repository.Dollars(product.PriceCents).ToString("0.00",
                        CultureInfo.CurrentCulture);
                    if(row.Cells["Quantity"].Value is null)row.Cells["Quantity"].Value=1;
                }
                else if(id==0 && row.Cells["Quantity"].Value is null)
                    row.Cells["Quantity"].Value=1;
            }
            updateTotals();
        };
        lines.RowsRemoved+=(_,_)=>updateTotals();
        tax.ValueChanged+=(_,_)=>updateTotals();

        if(previous!=null)
            foreach(var item in previous.Lines)
                lines.Rows.Add(item.ProductId??0,item.Description,item.Quantity,
                    Repository.Dollars(item.UnitPriceCents).ToString("0.00",CultureInfo.CurrentCulture),
                    Repository.Dollars(checked(item.Quantity*item.UnitPriceCents)).ToString("C2",
                        CultureInfo.CurrentCulture));
        loadingExisting=false;
        layout.Controls.Add(lines,0,3);
        layout.Controls.Add(totals,0,4);
        updateTotals();
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,
            AutoScroll=true,WrapContents=false};
        var cancel=new Button{Text="Cancel",Width=124,Height=41,DialogResult=DialogResult.Cancel};
        var save=new Button{Text=previous!=null?"Save Draft Changes":
            kind=="Estimate"?"Save Draft Estimate":"Create Open Invoice",
            Width=206,Height=41};
        footer.Controls.Add(cancel);footer.Controls.Add(save);
        footer.Controls.Add(new Label
        {
            Text="Catalog prices auto-fill but can be overridden. Custom lines are supported.",
            ForeColor=Color.FromArgb(103,119,138),Width=660,Height=40,
            TextAlign=ContentAlignment.MiddleRight
        });
        layout.Controls.Add(footer,0,5);
        win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                lines.EndEdit();
                var items=new List<SalesLine>();
                foreach(DataGridViewRow row in lines.Rows)
                {
                    if(row.IsNewRow)continue;
                    bool empty=new[]{"Product","Description","Quantity","UnitPrice"}
                        .All(field=>string.IsNullOrWhiteSpace(row.Cells[field].Value?.ToString()));
                    if(empty)continue;
                    long? product=null;
                    if(row.Cells["Product"].Value!=null)
                    {
                        long id=Convert.ToInt64(row.Cells["Product"].Value,CultureInfo.InvariantCulture);
                        if(id>0)product=id;
                    }
                    string description=row.Cells["Description"].Value?.ToString()?.Trim()??"";
                    if(!int.TryParse(row.Cells["Quantity"].Value?.ToString(),out int qty))
                        throw new InvalidOperationException("Each line needs a valid whole-number quantity.");
                    if(!decimal.TryParse(row.Cells["UnitPrice"].Value?.ToString(),NumberStyles.Currency,
                        CultureInfo.CurrentCulture,out var price))
                        throw new InvalidOperationException("Each line needs a valid unit price.");
                    items.Add(new SalesLine(product,description,qty,Repository.Cents(price)));
                }
                SalesDocuments.Totals(items,tax.Value);
                long docId;
                if(previous!=null)
                {
                    SalesDocuments.UpdateDraftEstimate(previous.Id,(long)customer.SelectedValue!,
                        date.Value,notes.Text,items,tax.Value);
                    docId=previous.Id;
                }
                else
                    docId=SalesDocuments.Create(kind,(long)customer.SelectedValue!,date.Value,
                        notes.Text,items,tax.Value);
                MessageBox.Show(win,
                    kind=="Estimate"?$"Estimate #{docId} saved as Draft. Approve it before converting to an invoice.":
                        $"Invoice #{docId} created as Open. You can now record payments.",
                    "Sales Document Saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(win,ex.Message,"Unable to save document",
                    MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(win.ShowDialog(this)==DialogResult.OK)RefreshGrid();
    }
}
