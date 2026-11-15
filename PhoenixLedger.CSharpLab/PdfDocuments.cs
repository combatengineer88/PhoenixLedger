using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PhoenixLedger.CSharpLab;

public enum PdfObjectType { Text, Table, Logo, Line, Rectangle }

public sealed class PdfLayoutItem
{
    public string Label {get;set;}="New text";
    public PdfObjectType ObjectType {get;set;}=PdfObjectType.Text;
    public int X {get;set;}=40;
    public int Y {get;set;}=100;
    public int Width {get;set;}=250;
    public int Height {get;set;}=35;
    public string Content {get;set;}="Custom text";
    public string FontName {get;set;}="Arial";
    public double FontSize {get;set;}=11;
    public bool Bold {get;set;}=false;
    public string TextColor {get;set;}="#253A52";
    public string BackgroundColor {get;set;}="#FFFFFF";
    public bool ShowBackground {get;set;}=false;
    public int BorderThickness {get;set;}=0;
    public override string ToString()=>Label;
}

public sealed class PdfLayout
{
    public string Paper {get;set;}="Letter";
    public string Title {get;set;}="";
    public List<PdfLayoutItem> Elements {get;set;}=new();

    public static PdfLayout Default(string kind)
    {
        var layout=new PdfLayout{Title=kind};
        layout.Elements.AddRange(new[]{
            new PdfLayoutItem{Label="Business name",Content="{{BusinessName}}",X=42,Y=35,Width=400,Height=43,Bold=true,FontSize=21},
            new PdfLayoutItem{Label="Logo",ObjectType=PdfObjectType.Logo,X=462,Y=28,Width=110,Height=64,Content="{{Logo}}"},
            new PdfLayoutItem{Label="Document title",Content="{{DocumentType}} #{{DocumentNumber}}",X=42,Y=110,Width=490,Height=39,FontSize=17,Bold=true},
            new PdfLayoutItem{Label="Recipient",Content="To: {{Recipient}}",X=42,Y=158,Width=510,Height=48,FontSize=11},
            new PdfLayoutItem{Label="Date",Content="Date: {{Date}}",X=42,Y=204,Width=280,Height=30},
            new PdfLayoutItem{Label="Items table",ObjectType=PdfObjectType.Table,X=42,Y=267,Width=530,Height=355,FontSize=9,
                TextColor="#263750",BackgroundColor="#DDEAFF",ShowBackground=true},
            new PdfLayoutItem{Label="Total",Content="Total: {{Total}}",X=350,Y=642,Width=222,Height=42,FontSize=14,Bold=true},
            new PdfLayoutItem{Label="Notes",Content="{{Notes}}",X=42,Y=708,Width=530,Height=44,FontSize=9}
        });
        return layout;
    }

    public static PdfLayout Load(string kind)
    {
        string json=BusinessData.Setting("pdf_design_"+kind);
        if(!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var result=JsonSerializer.Deserialize<PdfLayout>(json);
                if(result!=null&&result.Elements is {Count:>0})return result;
            }
            catch(JsonException) { }
        }
        return Default(kind);
    }
    public void Save(string kind)
    {
        Validate();
        BusinessData.SetSettings(new Dictionary<string,string>{
            ["pdf_design_"+kind]=JsonSerializer.Serialize(this, new JsonSerializerOptions{WriteIndented=true})
        });
    }
    public void Validate()
    {
        if(Paper is not ("Letter" or "A4"))throw new InvalidOperationException("Choose Letter or A4 paper.");
        if(Elements.Count>150)throw new InvalidOperationException("Too many PDF design elements.");
        foreach(var item in Elements)
        {
            if(item.Width<1||item.Height<1||item.X<0||item.Y<0||item.FontSize<4||item.FontSize>72)
                throw new InvalidOperationException("Invalid PDF element dimensions or font size: "+item.Label);
        }
    }
}

/// <summary>PDF render from stored user-controlled element positions and real business data.</summary>
internal static class PdfDocuments
{
    internal sealed record DocumentData(Dictionary<string,string> Fields,DataTable Rows);

    internal static DocumentData ReadDocument(string type,long id)
    {
        string kind=type switch { "Invoice"=>"Invoice","Estimate"=>"Estimate","Purchase Order"=>"Purchase Order",
            _=>throw new ArgumentException("Unsupported document PDF")};
        using var db=Store.Open();
        using var cmd=db.CreateCommand();
        if(kind=="Purchase Order")
        {
            cmd.CommandText="""
                SELECT po.ordered_on,coalesce(s.name,''),po.total,coalesce(po.notes,'')
                FROM purchase_orders po LEFT JOIN suppliers s ON s.id=po.supplier_id WHERE po.id=@id
                """;
        }
        else
        {
            cmd.CommandText="""
                SELECT d.created,coalesce(c.name,''),d.total,coalesce(d.notes,'')
                FROM documents d LEFT JOIN customers c ON c.id=d.customer_id
                WHERE d.id=@id AND d.kind=@kind
                """;
            cmd.Parameters.AddWithValue("@kind",kind);
        }
        cmd.Parameters.AddWithValue("@id",id);
        using var read=cmd.ExecuteReader();
        if(!read.Read())throw new InvalidOperationException(kind+" record was not found.");
        string date=read.GetString(0),recipient=read.GetString(1),total=Repository.Dollars(read.GetInt64(2)).ToString("C2"),notes=read.GetString(3);
        read.Close();
        var rows=new DataTable();
        if(kind=="Purchase Order")
        {
            rows.Columns.Add("SKU");rows.Columns.Add("Product");rows.Columns.Add("Qty");
            rows.Columns.Add("Unit Cost");rows.Columns.Add("Amount");
            using var items=db.CreateCommand();
            items.CommandText="""
                SELECT coalesce(p.sku,''),p.name,l.qty,l.unit_cost
                FROM po_items l JOIN products p ON p.id=l.product_id
                WHERE l.po_id=@id ORDER BY l.id
                """;
            items.Parameters.AddWithValue("@id",id);
            using var r=items.ExecuteReader();
            while(r.Read())
            {
                int q=r.GetInt32(2);long unit=r.GetInt64(3);
                rows.Rows.Add(r.GetString(0),r.GetString(1),q.ToString(),
                    Repository.Dollars(unit).ToString("C2"),Repository.Dollars(q*unit).ToString("C2"));
            }
        }
        else
        {
            rows.Columns.Add("Description");rows.Columns.Add("Qty");rows.Columns.Add("Unit Price");rows.Columns.Add("Amount");
            using var items=db.CreateCommand();
            items.CommandText="""
                SELECT description,qty,unit_price,line_total
                FROM document_items WHERE document_id=@id ORDER BY id
                """;
            items.Parameters.AddWithValue("@id",id);
            using var r=items.ExecuteReader();
            while(r.Read())
                rows.Rows.Add(r.GetString(0),r.GetInt32(1).ToString(),
                    Repository.Dollars(r.GetInt64(2)).ToString("C2"),
                    Repository.Dollars(r.GetInt64(3)).ToString("C2"));
        }
        return new DocumentData(new Dictionary<string,string>{
            ["BusinessName"]=BusinessData.Setting("business_name") is {Length:>0} n?n:"PhoenixLedger C# Lab",
            ["DocumentType"]=kind,["DocumentNumber"]=id.ToString(),["Recipient"]=recipient,
            ["Date"]=date,["Total"]=total,["Notes"]=notes,
            ["Logo"]=BusinessData.Setting("logo_path"),["BusinessAddress"]=BusinessData.Setting("business_address"),
            ["BusinessPhone"]=BusinessData.Setting("business_phone"),["BusinessEmail"]=BusinessData.Setting("business_email")
        },rows);
    }

    internal static DocumentData ReportData(string title,DataTable table)
    {
        var fields=new Dictionary<string,string>{
            ["BusinessName"]=BusinessData.Setting("business_name") is {Length:>0} name?name:"PhoenixLedger C# Lab",
            ["DocumentType"]=title,["DocumentNumber"]="",["Recipient"]="Internal management report",
            ["Date"]=DateTime.Today.ToString("yyyy-MM-dd"),["Total"]="",
            ["Notes"]="Generated from the separate C# Lab database.",
            ["Logo"]=BusinessData.Setting("logo_path"),
            ["BusinessAddress"]=BusinessData.Setting("business_address"),
            ["BusinessPhone"]=BusinessData.Setting("business_phone"),["BusinessEmail"]=BusinessData.Setting("business_email")
        };
        return new DocumentData(fields,table);
    }

    public static void Export(string kind,DocumentData data,string file,PdfLayout? layout=null)
    {
        layout??=PdfLayout.Load(kind);
        layout.Validate();
        using var doc=new PdfDocument();
        doc.Info.Title=kind+" — "+data.Fields.GetValueOrDefault("DocumentNumber","");
        PdfPage AddPage()
        {
            var page=doc.AddPage();
            page.Size=layout.Paper=="A4"?PageSize.A4:PageSize.Letter;
            return page;
        }
        var first=AddPage();
        double pageWidth=first.Width.Point,pageHeight=first.Height.Point;
        using(var gfx=XGraphics.FromPdfPage(first))
        {
            foreach(var element in layout.Elements)
            {
                if(element.ObjectType==PdfObjectType.Table)continue;
                DrawElement(gfx,element,data.Fields,pageWidth,pageHeight);
            }
        }
        foreach(var tableItem in layout.Elements.Where(x=>x.ObjectType==PdfObjectType.Table))
        {
            PdfPage page=first;
            var g=XGraphics.FromPdfPage(page);
            try
            {
                double y=tableItem.Y;
                double rowHeight=Math.Max(18,tableItem.FontSize+10);
                double headerHeight=rowHeight+6;
                void drawHeader()
                {
                    var headings=data.Rows.Columns.Cast<DataColumn>().ToList();
                    int count=Math.Max(1,headings.Count);
                    double width=tableItem.Width/(double)count;
                    var bg=new XSolidBrush(XColor.FromArgb(225,236,250));
                    g.DrawRectangle(bg,tableItem.X,y,tableItem.Width,headerHeight);
                    for(int col=0;col<headings.Count;col++)
                        DrawWrapped(g,headings[col].ColumnName,tableItem.FontName,tableItem.FontSize,true,"#263750",
                            tableItem.X+col*width+4,y+5,width-8,headerHeight-5);
                    y+=headerHeight;
                }
                drawHeader();
                for(int row=0;row<data.Rows.Rows.Count;row++)
                {
                    if(y+rowHeight>pageHeight-65)
                    {
                        g.Dispose();
                        page=AddPage();
                        g=XGraphics.FromPdfPage(page);
                        y=48;
                        DrawWrapped(g,kind,tableItem.FontName,13,true,"#253A52",
                            tableItem.X,22,tableItem.Width,24);
                        drawHeader();
                    }
                    int columns=Math.Max(1,data.Rows.Columns.Count);
                    double width=tableItem.Width/(double)columns;
                    if(row%2==0)
                        g.DrawRectangle(new XSolidBrush(XColor.FromArgb(248,250,253)),tableItem.X,y,tableItem.Width,rowHeight);
                    for(int col=0;col<data.Rows.Columns.Count;col++)
                    {
                        string value=data.Rows.Rows[row][col]?.ToString()??"";
                        DrawWrapped(g,value,tableItem.FontName,Math.Min(tableItem.FontSize,9),false,"#253A52",
                            tableItem.X+col*width+3,y+4,width-6,rowHeight-4);
                    }
                    g.DrawLine(new XPen(XColors.LightGray,.35),tableItem.X,y+rowHeight,tableItem.X+tableItem.Width,y+rowHeight);
                    y+=rowHeight;
                }
            }
            finally {g.Dispose();}
        }
        string full=Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        doc.Save(full);
    }

    private static void DrawElement(XGraphics g,PdfLayoutItem it,IReadOnlyDictionary<string,string> values,
        double pageWidth,double pageHeight)
    {
        if(it.X>=pageWidth||it.Y>=pageHeight)return;
        var c=ParseColor(it.TextColor);
        if(it.ShowBackground)
            g.DrawRectangle(new XSolidBrush(ParseColor(it.BackgroundColor)),it.X,it.Y,it.Width,it.Height);
        if(it.BorderThickness>0)
            g.DrawRectangle(new XPen(c,it.BorderThickness),it.X,it.Y,it.Width,it.Height);
        if(it.ObjectType==PdfObjectType.Rectangle)
        {
            if(!it.ShowBackground)g.DrawRectangle(new XPen(c,1),it.X,it.Y,it.Width,it.Height);
            return;
        }
        if(it.ObjectType==PdfObjectType.Line)
        {
            g.DrawLine(new XPen(c,Math.Max(1,it.BorderThickness)),it.X,it.Y,it.X+it.Width,it.Y+it.Height);
            return;
        }
        if(it.ObjectType==PdfObjectType.Logo)
        {
            string path=values.GetValueOrDefault("Logo","");
            if(File.Exists(path))
            {
                try
                {
                    using var image=XImage.FromFile(path);
                    double scale=Math.Min(it.Width/(double)image.PixelWidth,it.Height/(double)image.PixelHeight);
                    g.DrawImage(image,it.X,it.Y,image.PixelWidth*scale,image.PixelHeight*scale);
                }
                catch(Exception) { /* Never crash a financial export for an invalid optional logo. */ }
            }
            return;
        }
        string text=it.Content;
        foreach(var (key,value) in values)
            text=text.Replace("{{"+key+"}}",value,StringComparison.OrdinalIgnoreCase);
        DrawWrapped(g,text,it.FontName,it.FontSize,it.Bold,it.TextColor,it.X,it.Y,it.Width,it.Height);
    }

    private static XColor ParseColor(string html)
    {
        try
        {
            Color color=ColorTranslator.FromHtml(html);
            return XColor.FromArgb(color.R,color.G,color.B);
        }
        catch(Exception)
        {
            return XColor.FromArgb(37,58,82);
        }
    }
    private static XFont Font(string name,double size,bool bold)
    {
        try{return new XFont(name,Math.Clamp(size,4,72),bold?XFontStyleEx.Bold:XFontStyleEx.Regular);}
        catch(Exception){return new XFont("Arial",Math.Clamp(size,4,72),
            bold?XFontStyleEx.Bold:XFontStyleEx.Regular);}
    }

    private static void DrawWrapped(XGraphics g,string text,string fontName,double fontSize,bool bold,
        string color,double x,double y,double width,double maxHeight)
    {
        if(width<4||maxHeight<4)return;
        var font=Font(fontName,fontSize,bold);
        var ink=new XSolidBrush(ParseColor(color));
        double line=fontSize*1.3,cur=y;
        foreach(string paragraph in text.Replace("\r","").Split('\n'))
        {
            string buffer="";
            foreach(string word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries))
            {
                string test=buffer.Length==0?word:buffer+" "+word;
                if(g.MeasureString(test,font).Width>width && buffer.Length>0)
                {
                    if(cur+line>y+maxHeight) return;
                    g.DrawString(buffer,font,ink,new XRect(x,cur,width,line),XStringFormats.TopLeft);
                    cur+=line;buffer=word;
                }
                else buffer=test;
            }
            if(cur+line>y+maxHeight)return;
            g.DrawString(buffer,font,ink,new XRect(x,cur,width,line),XStringFormats.TopLeft);
            cur+=line;
        }
    }

    internal static DocumentData Sample(string kind)
    {
        var table=new DataTable();
        table.Columns.Add("Description");table.Columns.Add("Qty");
        table.Columns.Add("Unit price");table.Columns.Add("Amount");
        table.Rows.Add("Custom printed apparel",24,"$11.50","$276.00");
        table.Rows.Add("Embroidery setup",1,"$35.00","$35.00");
        return new DocumentData(new Dictionary<string,string>{
            ["BusinessName"]=BusinessData.Setting("business_name") is {Length:>0} name?name:"PhoenixLedger C# Lab",
            ["DocumentType"]=kind,["DocumentNumber"]="SAMPLE-101",
            ["Recipient"]="Sample customer / supplier",["Date"]=DateTime.Today.ToString("yyyy-MM-dd"),
            ["Total"]="$311.00",["Notes"]="Preview only — no transaction created.",
            ["BusinessAddress"]=BusinessData.Setting("business_address"),["BusinessPhone"]=BusinessData.Setting("business_phone"),
            ["BusinessEmail"]=BusinessData.Setting("business_email"),["Logo"]=BusinessData.Setting("logo_path")
        },table);
    }
}
