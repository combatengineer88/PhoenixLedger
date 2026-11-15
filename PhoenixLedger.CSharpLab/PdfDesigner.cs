using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

/// <summary>
/// WYSIWYG-like drag-and-drop layout editor. Each document type has an
/// independent persisted JSON layout. The PDF exporter uses identical point
/// coordinates to those displayed on the canvas.
/// </summary>
internal sealed class PdfDesigner : Form
{
    private readonly ComboBox documentKind=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=205};
    private readonly ComboBox paper=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};
    private readonly Panel scroll=new(){Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.FromArgb(223,229,238)};
    private readonly Panel page=new(){BackColor=Color.White,Location=new Point(25,18)};
    private readonly PropertyGrid properties=new(){Dock=DockStyle.Fill,ToolbarVisible=false,HelpVisible=true};
    private readonly ListBox layers=new(){Dock=DockStyle.Fill,IntegralHeight=false};
    private PdfLayout design=new();
    private PdfLayoutItem? selected;
    private Point dragStart;
    private Point origin;
    private bool dragging;
    private bool changing;

    public PdfDesigner(string selectedKind="Invoice")
    {
        Text="PhoenixLedger — PDF Layout Designer";
        WindowState=FormWindowState.Maximized;
        MinimumSize=new Size(1050,700);
        Font=new Font("Segoe UI",9);
        BackColor=Color.FromArgb(242,246,252);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=94,WrapContents=true,AutoScroll=true,
            Padding=new Padding(8),BackColor=Color.White};
        documentKind.Items.AddRange(new object[]{"Invoice","Estimate","Purchase Order","Report"});
        documentKind.SelectedItem=selectedKind;
        paper.Items.AddRange(new object[]{"Letter","A4"});
        Button button(string title,Action action,int width=112)
        {
            var b=new Button{Text=title,Width=width,Height=33,Margin=new Padding(3,1,3,1)};
            b.Click+=(_,_)=>{
                try{action();}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"PDF Designer",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
            };
            return b;
        }
        toolbar.Controls.Add(new Label{Text="Document",Width=84,Height=30,TextAlign=ContentAlignment.MiddleRight});
        toolbar.Controls.Add(documentKind);
        toolbar.Controls.Add(new Label{Text="Paper",Width=47,Height=30,TextAlign=ContentAlignment.MiddleRight});
        toolbar.Controls.Add(paper);
        toolbar.Controls.Add(button("Add text",()=>Add(PdfObjectType.Text)));
        toolbar.Controls.Add(button("Add table",()=>Add(PdfObjectType.Table)));
        toolbar.Controls.Add(button("Add logo",()=>Add(PdfObjectType.Logo)));
        toolbar.Controls.Add(button("Add line",()=>Add(PdfObjectType.Line)));
        toolbar.Controls.Add(button("Add shape",()=>Add(PdfObjectType.Rectangle)));
        toolbar.Controls.Add(button("Duplicate",Duplicate));
        toolbar.Controls.Add(button("Delete",DeleteSelected));
        toolbar.Controls.Add(button("Save design",SaveLayout,132));
        toolbar.Controls.Add(button("Preview PDF",PreviewPdf,124));
        toolbar.Controls.Add(button("Export JSON",ExportJson,132));
        toolbar.Controls.Add(button("Reset design",ResetLayout,120));

        var outer=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Vertical,SplitterDistance=780};
        scroll.Controls.Add(page);outer.Panel1.Controls.Add(scroll);
        var sidebar=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(10)};
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,145));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        sidebar.Controls.Add(new Label{Text="ELEMENT LAYERS",Dock=DockStyle.Fill,
            Font=new Font("Segoe UI",10,FontStyle.Bold)},0,0);
        sidebar.Controls.Add(layers,0,1);
        sidebar.Controls.Add(new Label{Text="SELECTED ELEMENT PROPERTIES",Dock=DockStyle.Fill,
            Font=new Font("Segoe UI",10,FontStyle.Bold)},0,2);
        sidebar.Controls.Add(properties,0,3);
        outer.Panel2.Controls.Add(sidebar);
        Controls.Add(outer);
        Controls.Add(toolbar);

        layers.SelectedIndexChanged+=(_,_)=>{
            if(changing)return;
            if(layers.SelectedItem is PdfLayoutItem item)Select(item);
        };
        properties.PropertyValueChanged+=(_,_)=>{RefreshCanvas();};
        paper.SelectedIndexChanged+=(_,_)=>{
            if(changing)return;
            design.Paper=paper.Text;RefreshCanvas();
        };
        documentKind.SelectedIndexChanged+=(_,_)=>LoadCurrent();
        Shown+=(_,_)=>LoadCurrent();
    }

    private void LoadCurrent()
    {
        changing=true;
        try
        {
            design=PdfLayout.Load(documentKind.Text);
            paper.SelectedItem=design.Paper;
            selected=null;
            RefreshCanvas();
        }
        finally{changing=false;}
    }
    private void RefreshCanvas()
    {
        changing=true;
        try
        {
            page.SuspendLayout();
            page.Controls.Clear();
            page.Size=design.Paper=="A4"?new Size(595,842):new Size(612,792);
            scroll.AutoScrollMinSize=new Size(page.Width+60,page.Height+50);
            layers.Items.Clear();
            foreach(var item in design.Elements)
            {
                layers.Items.Add(item);
                var label=new Label
                {
                    Tag=item,Left=item.X,Top=item.Y,Width=Math.Max(12,item.Width),
                    Height=Math.Max(12,item.Height),AutoSize=false,
                    Text=item.ObjectType switch
                    {
                        PdfObjectType.Table=>"[LIVE DATA TABLE]\n"+item.Label,
                        PdfObjectType.Logo=>"[BUSINESS LOGO]",
                        PdfObjectType.Line=>"─────────",
                        PdfObjectType.Rectangle=>"SHAPE",
                        _=>item.Content
                    },
                    Font=new Font("Arial",Math.Clamp((float)item.FontSize,7,24),
                        item.Bold?FontStyle.Bold:FontStyle.Regular),
                    ForeColor=item==selected?Color.FromArgb(25,104,195):SafeColor(item.TextColor),
                    BackColor=item==selected?Color.FromArgb(227,240,255):
                        item.ShowBackground?SafeColor(item.BackgroundColor):Color.White,
                    BorderStyle=BorderStyle.FixedSingle,
                    Cursor=Cursors.SizeAll
                };
                label.MouseDown+=(_,e)=>{
                    if(e.Button!=MouseButtons.Left)return;
                    // Do not redraw the canvas during mouse-down: doing so would
                    // dispose the control before it can receive MouseMove.
                    selected=item;
                    changing=true;
                    try
                    {
                        layers.SelectedItem=item;
                        properties.SelectedObject=item;
                    }
                    finally {changing=false;}
                    label.BackColor=Color.FromArgb(227,240,255);
                    label.ForeColor=Color.FromArgb(25,104,195);
                    dragStart=Control.MousePosition;
                    origin=new Point(item.X,item.Y);
                    dragging=true;
                    label.Capture=true;
                };
                label.MouseMove+=(_,e)=>{
                    if(!dragging||selected!=item)return;
                    var p=Control.MousePosition;
                    item.X=Math.Clamp(origin.X+p.X-dragStart.X,0,Math.Max(0,page.Width-item.Width));
                    item.Y=Math.Clamp(origin.Y+p.Y-dragStart.Y,0,Math.Max(0,page.Height-item.Height));
                    label.Location=new Point(item.X,item.Y);
                    properties.Refresh();
                };
                label.MouseUp+=(_,_)=>{
                    if(dragging&&selected==item)
                    {
                        dragging=false;
                        label.Capture=false;
                        RefreshCanvas();
                    }
                };
                page.Controls.Add(label);
            }
            if(selected!=null)
            {
                layers.SelectedItem=selected;
                properties.SelectedObject=selected;
            }
            else properties.SelectedObject=null;
            page.ResumeLayout();
        }
        finally {changing=false;}
    }

    private static Color SafeColor(string html)
    {
        try{return ColorTranslator.FromHtml(html);}
        catch{return Color.FromArgb(40,58,78);}
    }
    private void Select(PdfLayoutItem item)
    {
        selected=item;
        RefreshCanvas();
    }
    private void Add(PdfObjectType type)
    {
        var item=new PdfLayoutItem{ObjectType=type,
            Label=type+" "+(design.Elements.Count+1),
            Content=type==PdfObjectType.Text?"{{BusinessName}}":type.ToString(),
            X=50,Y=Math.Min(page.Height-120,100+design.Elements.Count*28),
            Width=type==PdfObjectType.Table?510:240,
            Height=type==PdfObjectType.Table?320:60};
        design.Elements.Add(item);
        Select(item);
    }
    private void Duplicate()
    {
        if(selected==null)return;
        var copy=JsonSerializer.Deserialize<PdfLayoutItem>(JsonSerializer.Serialize(selected))!;
        copy.Label=selected.Label+" copy";
        copy.X=Math.Min(page.Width-copy.Width,copy.X+20);
        copy.Y=Math.Min(page.Height-copy.Height,copy.Y+20);
        design.Elements.Add(copy);Select(copy);
    }
    private void DeleteSelected()
    {
        if(selected==null)return;
        design.Elements.Remove(selected);selected=null;RefreshCanvas();
    }
    private void SaveLayout()
    {
        design.Save(documentKind.Text);
        MessageBox.Show(this,"The PDF layout was saved separately for "+documentKind.Text+".",
            "Layout saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    private void PreviewPdf()
    {
        string output=Path.Combine(Path.GetTempPath(),"PhoenixLedger_PDF_Preview_"+Guid.NewGuid().ToString("N")+".pdf");
        PdfDocuments.Export(documentKind.Text,PdfDocuments.Sample(documentKind.Text),output,design);
        Process.Start(new ProcessStartInfo(output){UseShellExecute=true});
    }
    private void ExportJson()
    {
        design.Validate();
        using var dlg=new SaveFileDialog{Filter="PDF layout JSON (*.json)|*.json",
            FileName="PhoenixLedger_"+documentKind.Text.Replace(' ','_')+"_Layout.json"};
        if(dlg.ShowDialog(this)!=DialogResult.OK)return;
        File.WriteAllText(dlg.FileName,JsonSerializer.Serialize(design,
            new JsonSerializerOptions{WriteIndented=true}));
    }
    private void ResetLayout()
    {
        if(MessageBox.Show(this,"Restore the default design? Save any changes first.",
            "Restore defaults",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        design=PdfLayout.Default(documentKind.Text);
        selected=null;RefreshCanvas();
    }
}
