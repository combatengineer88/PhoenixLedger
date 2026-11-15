using System.Drawing;
using System.Data;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed class MainForm : Form
{
    private static readonly Color Navy=Color.FromArgb(25,42,63);
    private static readonly Color Ink=Color.FromArgb(39,55,77);
    private static readonly Color Accent=Color.FromArgb(35,116,199);
    private static readonly Color Canvas=Color.FromArgb(241,246,251);
    private static readonly Color Muted=Color.FromArgb(103,119,138);
    private static readonly string[] Pages={"Home","Products","Suppliers","Customers","Purchase Orders","Sales Documents","Inventory History"};

    private readonly Dictionary<string,RoundedButton> tabs=new();
    private readonly Panel content=new(){Dock=DockStyle.Fill,BackColor=Canvas,Padding=new Padding(16,8,16,12)};
    private readonly Label sectionTitle=new(){Dock=DockStyle.Top,Height=44,ForeColor=Ink,
        Font=new Font("Segoe UI",17,FontStyle.Bold),TextAlign=ContentAlignment.MiddleLeft};
    private readonly Panel searchPanel=new(){Dock=DockStyle.Top,Height=49,BackColor=Canvas};
    private readonly TextBox search=new(){Width=310,Font=new Font("Segoe UI",10.5f),BorderStyle=BorderStyle.FixedSingle};
    private readonly FlowLayoutPanel actions=new(){Dock=DockStyle.Top,Height=99,Padding=new Padding(0,6,0,4),
        WrapContents=true,AutoScroll=true,BackColor=Canvas};
    private readonly DataGridView grid=new();
    private readonly Panel dashboard=new(){Dock=DockStyle.Fill,BackColor=Canvas,AutoScroll=true};
    private readonly Label bottom=new(){Dock=DockStyle.Bottom,Height=28,ForeColor=Muted,BackColor=Color.White,
        Padding=new Padding(17,6,0,0),Font=new Font("Segoe UI",8.7f)};
    private string current="Home";
    private Image? headerImage;

    public MainForm()
    {
        Text="PhoenixLedger C# Lab 0.1 — Separate Experimental Edition";
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        MinimumSize=new Size(950,650);
        Size=new Size(1360,850);
        WindowState=FormWindowState.Maximized;
        StartPosition=FormStartPosition.CenterScreen;
        BackColor=Canvas;
        Font=new Font("Segoe UI",10);
        var top=new Panel{Dock=DockStyle.Top,Height=82,BackColor=Navy};
        var imageFile=Path.Combine(AppContext.BaseDirectory,"Assets","phoenixledger_icon.png");
        if(File.Exists(imageFile))
        {
            headerImage=Image.FromFile(imageFile);
            top.Controls.Add(new PictureBox{Left=24,Top=15,Width=52,Height=52,Image=headerImage,SizeMode=PictureBoxSizeMode.Zoom});
        }
        top.Controls.Add(new Label{Text="PHOENIXLEDGER",Left=85,Top=20,AutoSize=true,
            Font=new Font("Segoe UI",19,FontStyle.Bold),ForeColor=Color.White});
        top.Controls.Add(new Label{Text="C# LAB  /  experimental edition",Left=87,Top=54,AutoSize=true,
            Font=new Font("Segoe UI",9),ForeColor=Color.FromArgb(174,203,234)});
        var tag=new Label{Text="SEPARATE DATABASE  •  NO LIVE DATA CHANGES",Dock=DockStyle.Right,
            Width=410,TextAlign=ContentAlignment.MiddleRight,Padding=new Padding(0,0,25,0),
            Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=Color.FromArgb(180,211,242)};
        top.Controls.Add(tag);

        var navigation=new FlowLayoutPanel{Dock=DockStyle.Top,Height=58,Padding=new Padding(13,10,8,7),
            BackColor=Color.White,WrapContents=false,AutoScroll=true};
        foreach(string page in Pages)
        {
            var b=new RoundedButton{Text=page,Width=page switch {"Purchase Orders"=>165,"Sales Documents"=>170,"Inventory History"=>160,_=>125},
                Height=38,Fill=Color.White,TextColor=Ink,Outline=Color.FromArgb(214,222,234),Margin=new Padding(2,0,6,0)};
            b.Click+=(_,_)=>ShowPage(page);
            tabs.Add(page,b);navigation.Controls.Add(b);
        }
        var shell=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=new Padding(0),Padding=new Padding(0)};
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,82));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,58));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        top.Dock=DockStyle.Fill;navigation.Dock=DockStyle.Fill;content.Dock=DockStyle.Fill;bottom.Dock=DockStyle.Fill;
        shell.Controls.Add(top,0,0);
        shell.Controls.Add(navigation,0,1);
        shell.Controls.Add(content,0,2);
        shell.Controls.Add(bottom,0,3);
        Controls.Add(shell);
        content.Controls.Add(dashboard);
        content.Controls.Add(grid);
        content.Controls.Add(actions);
        content.Controls.Add(searchPanel);
        content.Controls.Add(sectionTitle);

        var searchLabel=new Label{Text="Search",ForeColor=Muted,AutoSize=true,Location=new Point(1,13)};
        searchPanel.Controls.Add(searchLabel);
        search.Location=new Point(68,10);searchPanel.Controls.Add(search);
        search.TextChanged+=(_,_)=>{if(current!="Home")RefreshGrid();};
        searchPanel.Controls.Add(new Label{Text="Double-click to edit  •  Right-click for quick actions",ForeColor=Muted,
            AutoSize=true,Location=new Point(402,13),Font=new Font("Segoe UI",9)});
        ConfigureGrid();
        FormClosed+=(_,_)=>headerImage?.Dispose();
        ShowPage("Home");
    }

    private void ConfigureGrid()
    {
        grid.Dock=DockStyle.Fill;
        grid.BackgroundColor=Color.White;
        grid.BorderStyle=BorderStyle.None;
        grid.AllowUserToAddRows=false;
        grid.AllowUserToDeleteRows=false;
        grid.ReadOnly=true;
        grid.MultiSelect=true;
        grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible=false;
        grid.AutoGenerateColumns=true;
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells;
        grid.RowTemplate.Height=34;
        grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersHeight=43;
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(230,238,247);
        grid.ColumnHeadersDefaultCellStyle.ForeColor=Ink;
        grid.ColumnHeadersDefaultCellStyle.Font=new Font("Segoe UI",9,FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=Color.FromArgb(230,238,247);
        grid.DefaultCellStyle.BackColor=Color.White;
        grid.DefaultCellStyle.ForeColor=Ink;
        grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(212,230,250);
        grid.DefaultCellStyle.SelectionForeColor=Navy;
        grid.DefaultCellStyle.Padding=new Padding(7,3,7,3);
        grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249,251,254);
        grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)OpenSelected();};
        grid.CellMouseDown+=(_,e)=>{
            if(e.Button==MouseButtons.Right && e.RowIndex>=0)
            {
                grid.ClearSelection();grid.Rows[e.RowIndex].Selected=true;
                if(e.ColumnIndex>=0)grid.CurrentCell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            }
        };
        grid.DataBindingComplete+=(_,_)=>{
            if(grid.Columns.Contains("ID"))grid.Columns["ID"]!.Width=60;
            if(grid.Columns.Contains("Name"))grid.Columns["Name"]!.Width=210;
            if(grid.Columns.Contains("Description"))grid.Columns["Description"]!.Width=260;
        };
    }

    private RoundedButton AddAction(string name,Action action,bool primary=false,int width=170)
    {
        var b=new RoundedButton{Text=name,Width=width,Height=39,Fill=primary?Accent:Color.White,
            TextColor=primary?Color.White:Ink,Outline=primary?Accent:Color.FromArgb(190,206,222),
            Margin=new Padding(0,0,9,8)};
        b.Click+=(_,_)=>Safe(action);
        actions.Controls.Add(b);return b;
    }
    private void Safe(Action action)
    {
        try {action();}
        catch(Exception e)
        {MessageBox.Show(this,e.Message,"PhoenixLedger C# Lab",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private void ShowPage(string page)
    {
        current=page;
        foreach(var (name,b) in tabs)
        {
            bool active=name==page;
            b.Fill=active?Color.FromArgb(223,236,250):Color.White;
            b.TextColor=active?Accent:Ink;
            b.Outline=active?Accent:Color.FromArgb(214,222,234);
            b.Invalidate();
        }
        sectionTitle.Text=page=="Home"?"Workspace Overview":page;
        actions.Controls.Clear();
        search.Text="";
        bool isHome=page=="Home";
        searchPanel.Visible=!isHome;
        grid.Visible=!isHome;
        dashboard.Visible=isHome;
        if(isHome)
        {
            actions.Height=57;
            AddAction("Import Database Copy",ImportProductionCopy,true,210);
            AddAction("Open Data Folder",()=>System.Diagnostics.Process.Start("explorer.exe",Store.Root),false,180);
            BuildDashboard();
            return;
        }
        actions.Height=99;
        if(page=="Products")
        {
            AddAction("Add Product",()=>Edit(0),true);
            AddAction("Edit Product",()=>Edit(SelectedId()));
            AddAction("Scan / Adjust Stock",()=>Adjust(),false,195);
            AddAction("Import Excel",()=>ImportExcel(),false,170);
        }
        else if(page=="Suppliers"||page=="Customers")
        {
            AddAction(page=="Suppliers"?"Add Supplier":"Add Customer",()=>Edit(0),true);
            AddAction("Edit Selected",()=>Edit(SelectedId()));
            AddAction("Import Excel",()=>ImportExcel());
        }
        else if(page=="Purchase Orders"||page=="Sales Documents")
            AddAction("View Line Items",()=>ViewDetails(SelectedId()),true);
        AddAction("Export Visible CSV",ExportCsv,false,198);
        AddAction("Refresh",RefreshGrid,false,126);
        AddAction("Import Database Copy",ImportProductionCopy,false,205);
        RefreshGrid();
        var context=new ContextMenuStrip();
        if(page is "Products" or "Suppliers" or "Customers")
        {
            context.Items.Add("Edit selected",null,(_,_)=>Safe(()=>Edit(SelectedId())));
            if(page=="Products")
            {
                context.Items.Add("Scan / adjust stock",null,(_,_)=>Safe(Adjust));
                context.Items.Add("Copy SKU",null,(_,_)=>Safe(()=>{
                    string? sku=CurrentCell("SKU");if(!string.IsNullOrEmpty(sku))Clipboard.SetText(sku);
                }));
            }
        }
        if(page is "Purchase Orders" or "Sales Documents")
            context.Items.Add("View line items",null,(_,_)=>Safe(()=>ViewDetails(SelectedId())));
        context.Items.Add("Export visible CSV",null,(_,_)=>Safe(ExportCsv));
        grid.ContextMenuStrip=context;
    }

    private void BuildDashboard()
    {
        dashboard.Controls.Clear();
        var stats=Repository.Stats();
        var outer=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=3,RowCount=2,Height=280,
            BackColor=Canvas,Padding=new Padding(0,8,14,0)};
        for(int i=0;i<3;i++)outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent,50));outer.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        (string label,string value)[] cards={
            ("Products",stats.Products.ToString("N0")),("Suppliers",stats.Suppliers.ToString("N0")),
            ("Customers",stats.Customers.ToString("N0")),("Low Stock",stats.Low.ToString("N0")),
            ("Out of Stock",stats.Out.ToString("N0")),("Stock Value",stats.StockValue.ToString("C2"))};
        for(int i=0;i<cards.Length;i++)
        {
            var card=new Panel{BackColor=Color.White,Dock=DockStyle.Fill,Margin=new Padding(0,0,14,14),Padding=new Padding(18)};
            card.Controls.Add(new Label{Text=cards[i].value,Font=new Font("Segoe UI",24,FontStyle.Bold),
                ForeColor=Ink,Dock=DockStyle.Bottom,Height=63});
            card.Controls.Add(new Label{Text=cards[i].label,Font=new Font("Segoe UI",10),
                ForeColor=Muted,Dock=DockStyle.Top,Height=29});
            outer.Controls.Add(card,i%3,i/3);
        }
        var notes=new Panel{Dock=DockStyle.Top,Height=184,Padding=new Padding(18),BackColor=Color.White};
        notes.Controls.Add(new Label{Dock=DockStyle.Fill,ForeColor=Ink,AutoSize=false,
            Font=new Font("Segoe UI",11),Text="This is a separate experimental C# application, not an upgrade to the Python PhoenixLedger.\n\n"+
            "Products, suppliers, customers, Excel imports and stock adjustments are editable in the lab. Purchase orders and " +
            "estimates/invoices can be inspected, but financial posting is intentionally not enabled yet.\n\n"+
            "Use Import Database Copy to work with a SQLite snapshot of your current business records."});
        dashboard.Controls.Add(notes);
        dashboard.Controls.Add(outer);
        bottom.Text="Local-only C# Lab  •  " + Store.Database;
    }

    private void RefreshGrid()
    {
        if(current=="Home")return;
        grid.DataSource=Repository.Table(current,search.Text);
        bottom.Text=$"{grid.Rows.Count:N0} records visible  •  C# Lab database: {Store.Database}";
    }
    private long SelectedId()
    {
        if(grid.SelectedRows.Count==0)throw new InvalidOperationException("Select a row first.");
        var row=grid.SelectedRows[0];
        return Convert.ToInt64(row.Cells["ID"].Value,CultureInfo.InvariantCulture);
    }
    private string? CurrentCell(string column)
    {
        if(grid.SelectedRows.Count==0||!grid.Columns.Contains(column))return null;
        return grid.SelectedRows[0].Cells[column].Value?.ToString();
    }
    private void OpenSelected()
    {
        Safe(()=>{
            long id=SelectedId();
            if(current is "Products" or "Suppliers" or "Customers")Edit(id);
            else if(current is "Purchase Orders" or "Sales Documents")ViewDetails(id);
        });
    }
    private void Edit(long id)
    {
        bool changed=current switch
        {
            "Products"=>Editors.Product(this,id),
            "Suppliers"=>Editors.Supplier(this,id),
            "Customers"=>Editors.Customer(this,id),
            _=>false
        };
        if(changed)RefreshGrid();
    }
    private void Adjust()
    {
        string code=CurrentCell("SKU")??"";
        if(Editors.AdjustStock(this,code))RefreshGrid();
    }
    private void ViewDetails(long id)
    {
        var detail=Repository.Details(current,id);
        using var f=new Form {Text=$"{current} #{id} — Line Items (read-only)",Width=950,Height=620,
            StartPosition=FormStartPosition.CenterParent,BackColor=Canvas,Font=new Font("Segoe UI",10)};
        var g=new DataGridView{Dock=DockStyle.Fill,DataSource=detail,ReadOnly=true,RowHeadersVisible=false,
            AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
        var header=new Label{Dock=DockStyle.Top,Height=60,Text=$"{current} #{id} — {detail.Rows.Count} line items",
            Font=new Font("Segoe UI",16,FontStyle.Bold),Padding=new Padding(18,15,0,0),ForeColor=Ink};
        f.Controls.Add(g);f.Controls.Add(header);f.ShowDialog(this);
    }
    private void ImportProductionCopy()
    {
        using var picker=new OpenFileDialog{Filter="SQLite database (*.db)|*.db|All files (*.*)|*.*",
            Title="Select your EXISTING PhoenixLedger database — a copy will be made"};
        string? source=Store.SuggestedSource;
        if(source!=null){picker.InitialDirectory=Path.GetDirectoryName(source);picker.FileName=Path.GetFileName(source);}
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        if(MessageBox.Show(this,"This will replace only the C# LAB database with a new snapshot of:\n\n"+
            picker.FileName+"\n\nYour live Python PhoenixLedger database will NOT be changed. Continue?",
            "Import read-only snapshot",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        int count=Store.ImportSnapshot(picker.FileName);
        MessageBox.Show(this,$"The database was copied into the lab successfully.\n{count} media files copied.\n\n"+
            "Your original PhoenixLedger database was not changed.","Snapshot imported",MessageBoxButtons.OK,MessageBoxIcon.Information);
        ShowPage(current);
    }
    private void ImportExcel()
    {
        if(current is not ("Products" or "Suppliers" or "Customers"))return;
        using var picker=new OpenFileDialog{Filter="Excel workbook (*.xlsx)|*.xlsx",Title=$"Import {current} from Excel"};
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        if(MessageBox.Show(this,$"Import {current} from this Excel workbook into the C# LAB database only?\n\n"+
            picker.FileName+"\n\nRows with matching SKU/name/email may be updated.",
            "Confirm experimental import",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        var result=ExcelImporter.ImportXlsx(picker.FileName,current);
        MessageBox.Show(this,$"Imported: {result.Inserted} new, {result.Updated} updated.\n\nOnly the C# Lab database was modified.",
            "Excel import complete",MessageBoxButtons.OK,MessageBoxIcon.Information);
        RefreshGrid();
    }
    private void ExportCsv()
    {
        if(grid.DataSource is not DataTable)return;
        using var save=new SaveFileDialog{Filter="CSV file (*.csv)|*.csv",FileName=$"PhoenixLedger_CSharpLab_{current.Replace(' ','_')}_{DateTime.Now:yyyyMMdd}.csv"};
        if(save.ShowDialog(this)!=DialogResult.OK)return;
        static string Escape(string? value)
        {
            value??="";
            return "\""+value.Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ")+"\"";
        }
        using var writer=new StreamWriter(save.FileName,false,new UTF8Encoding(true));
        var cols=grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).OrderBy(c=>c.DisplayIndex).ToList();
        writer.WriteLine(string.Join(",",cols.Select(c=>Escape(c.HeaderText))));
        foreach(DataGridViewRow row in grid.Rows)
        {
            if(row.IsNewRow||!row.Visible)continue;
            writer.WriteLine(string.Join(",",cols.Select(c=>Escape(row.Cells[c.Index].FormattedValue?.ToString()))));
        }
        MessageBox.Show(this,"CSV saved. Image fields show Yes/No, not file paths.",
            "Export complete",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
}

internal sealed class RoundedButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
    public Color Fill {get;set;}=Color.White;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
    public Color Outline {get;set;}=Color.FromArgb(180,196,214);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
    public Color TextColor {get;set;}=Color.FromArgb(40,55,75);
    private bool hover;
    public RoundedButton()
    {
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                 ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        Cursor=Cursors.Hand;
        Font=new Font("Segoe UI",9.3f,FontStyle.Bold);
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor??Color.White);
        int w=Width-2,h=Height-4;
        using var shadow=Round(2,4,w,h,12);
        using var shadowBrush=new SolidBrush(Color.FromArgb(213,222,232));
        e.Graphics.FillPath(shadowBrush,shadow);
        using var shape=Round(1,1,w,h,12);
        Color fill=hover?ControlPaint.Light(Fill,0.12f):Fill;
        using var brush=new SolidBrush(fill);
        using var pen=new Pen(Outline,1);
        e.Graphics.FillPath(brush,shape);
        e.Graphics.DrawPath(pen,shape);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(4,1,w-6,h),TextColor,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        if(Focused){using var focus=new Pen(AccentColor(),1){DashStyle=DashStyle.Dot};
            using var focusPath=Round(4,4,Math.Max(4,w-7),Math.Max(4,h-7),10);e.Graphics.DrawPath(focus,focusPath);}
    }
    private static Color AccentColor()=>Color.FromArgb(47,133,206);
    private static GraphicsPath Round(int x,int y,int width,int height,int radius)
    {
        int diameter=radius*2;var p=new GraphicsPath();
        p.AddArc(x,y,diameter,diameter,180,90);
        p.AddArc(x+width-diameter,y,diameter,diameter,270,90);
        p.AddArc(x+width-diameter,y+height-diameter,diameter,diameter,0,90);
        p.AddArc(x,y+height-diameter,diameter,diameter,90,90);
        p.CloseFigure();return p;
    }
}
