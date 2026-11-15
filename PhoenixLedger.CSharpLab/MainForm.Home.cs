using System.Drawing;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private void BuildHomeWorkspace()
    {
        dashboard.Controls.Clear();
        var split=new SplitContainer
        {
            Dock=DockStyle.Fill,Orientation=Orientation.Vertical,
            FixedPanel=FixedPanel.Panel1,SplitterWidth=6,
            BackColor=Color.FromArgb(226,234,243),BorderStyle=BorderStyle.None
        };
        split.SplitterDistance=255;
        split.Panel1.BackColor=Color.White;
        split.Panel2.BackColor=Color.FromArgb(241,246,251);

        var quick=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,
            WrapContents=false,Padding=new Padding(12,10,6,8)
        };
        quick.Controls.Add(new Label
        {
            Text="QUICK ACCESS",Font=new Font("Segoe UI",12,FontStyle.Bold),
            ForeColor=Color.FromArgb(39,55,77),Width=216,Height=35
        });
        void heading(string title)
        {
            quick.Controls.Add(new Label
            {
                Text=title,Width=215,Height=31,TextAlign=ContentAlignment.BottomLeft,
                Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=Color.FromArgb(100,120,148)
            });
        }
        void shortcut(string text,string page,Action? after=null)
        {
            var b=new Button
            {
                Text=text,Width=216,Height=32,FlatStyle=FlatStyle.Flat,
                TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,1,0,1),
                BackColor=Color.White,ForeColor=Color.FromArgb(40,75,116),
                Font=new Font("Segoe UI",9)
            };
            b.FlatAppearance.BorderSize=0;
            b.Click+=(_,_)=>Safe(()=>{ShowPage(page);after?.Invoke();});
            quick.Controls.Add(b);
        }
        void planned(string text)
        {
            quick.Controls.Add(new Label
            {
                Text=text+"  (not yet ported)",Width=218,Height=33,
                Font=new Font("Segoe UI",8.5f),ForeColor=Color.FromArgb(151,164,180),
                TextAlign=ContentAlignment.MiddleLeft
            });
        }
        heading("SUPPLIERS");
        shortcut("Supplier directory","Suppliers");
        shortcut("Add supplier","Suppliers",()=>Edit(0));
        shortcut("Import suppliers from Excel","Suppliers",ImportExcel);
        shortcut("View purchase orders","Purchase Orders");
        planned("Create / receive purchase order");
        heading("CUSTOMERS");
        shortcut("Customer directory","Customers");
        shortcut("Add new customer","Customers",()=>Edit(0));
        shortcut("Import customers from Excel","Customers",ImportExcel);
        shortcut("View estimates / invoices","Estimates & Invoices");
        planned("Invoice payment and credit");
        heading("PRODUCTS");
        shortcut("Product catalog","Products / Catalog");
        shortcut("Add new product","Products / Catalog",()=>Edit(0));
        shortcut("Import product spreadsheet","Products / Catalog",ImportExcel);
        shortcut("Scan to adjust stock","Products / Catalog",Adjust);
        shortcut("Inventory history","Inventory History");
        heading("MANAGEMENT");
        shortcut("Dashboard","Dashboard");
        shortcut("Record an expense","Expenses",()=>EditExpense(0));
        shortcut("Reports","Reports");
        shortcut("Accounting (read only)","Accounting");
        shortcut("Administration","Administration");
        shortcut("Settings / Updates","Settings / Updates");
        split.Panel1.Controls.Add(quick);

        var work=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
            WrapContents=false,AutoScroll=true,Padding=new Padding(17,11,10,11)
        };
        work.Controls.Add(Heading("Home workspace",890));
        work.Controls.Add(Paragraph(
            "Follow the supplier and customer processes or use the shortcuts on the left. " +
            "Only the blue and green buttons are active C# Lab workflows.",890,44));
        var stats=Repository.Stats();
        var metrics=new FlowLayoutPanel
        {
            Height=104,Width=1010,WrapContents=false,AutoScroll=true,FlowDirection=FlowDirection.LeftToRight
        };
        void metric(string title,string value)
        {
            var card=new Panel{Width=212,Height=91,BackColor=Color.White,Margin=new Padding(0,0,10,0)};
            card.Controls.Add(new Label{Text=title,Top=11,Left=14,Width=180,Height=22,ForeColor=Color.FromArgb(103,119,138)});
            card.Controls.Add(new Label{
                Text=value,Top=33,Left=14,Width=182,Height=46,
                ForeColor=Color.FromArgb(39,55,77),Font=new Font("Segoe UI",20,FontStyle.Bold)});
            metrics.Controls.Add(card);
        }
        metric("Suppliers",stats.Suppliers.ToString("N0"));
        metric("Customers",stats.Customers.ToString("N0"));
        metric("Products",stats.Products.ToString("N0"));
        metric("Low stock",stats.Low.ToString("N0"));
        work.Controls.Add(metrics);
        work.Controls.Add(new Label
        {
            Text="BUSINESS WORKFLOWS",Font=new Font("Segoe UI",9,FontStyle.Bold),
            ForeColor=Color.FromArgb(102,118,140),Width=800,Height=40,
            TextAlign=ContentAlignment.BottomLeft
        });

        Panel lane(string title,string text,Color color,Color tint,int height)
        {
            var p=new Panel{Width=1010,Height=height,BackColor=tint,Margin=new Padding(0,6,0,10),Padding=new Padding(18)};
            p.Controls.Add(new Label{Text=title,Left=18,Top=15,Height=28,Width=800,
                ForeColor=color,Font=new Font("Segoe UI",12,FontStyle.Bold)});
            p.Controls.Add(new Label{Text=text,Left=18,Top=45,Height=28,Width=935,ForeColor=Color.FromArgb(103,119,138)});
            return p;
        }
        void steps(Panel parent,Color accent,params (string title,string page,bool enabled)[] nodes)
        {
            int x=18;
            foreach(var n in nodes)
            {
                var button=new RoundedButton
                {
                    Text=n.title,Left=x,Top=88,Width=186,Height=69,
                    Enabled=n.enabled,Fill=n.enabled?Color.White:Color.FromArgb(244,247,250),
                    Outline=n.enabled?accent:Color.FromArgb(188,196,205),
                    TextColor=n.enabled?Color.FromArgb(39,55,77):Color.Gray
                };
                if(n.enabled)button.Click+=(_,_)=>ShowPage(n.page);
                parent.Controls.Add(button);
                x+=205;
                if(x<820)
                {
                    parent.Controls.Add(new Label
                    {
                        Text="→",Left=x-13,Top=105,Width=25,Height=35,
                        Font=new Font("Segoe UI",19,FontStyle.Bold),ForeColor=accent
                    });
                }
            }
        }
        var supplier=lane("SUPPLIER / SUPPLY CHAIN",
            "Select a supplier, review purchase orders, and inspect stock changes.",Color.FromArgb(35,116,199),
            Color.FromArgb(235,244,255),185);
        steps(supplier,Color.FromArgb(35,116,199),
            ("Supplier\ndirectory","Suppliers",true),
            ("New purchase\norder — soon","Purchase Orders",false),
            ("Receive PO\n— soon","Purchase Orders",false),
            ("Inventory\nhistory","Inventory History",true));
        work.Controls.Add(supplier);
        var customer=lane("CUSTOMER / SALES CHAIN",
            "Maintain customers, review invoices, and export business reports.",Color.FromArgb(19,129,107),
            Color.FromArgb(235,249,245),185);
        steps(customer,Color.FromArgb(19,129,107),
            ("Customer\ndirectory","Customers",true),
            ("New estimate\n— soon","Estimates & Invoices",false),
            ("Invoice payments\n— soon","Estimates & Invoices",false),
            ("Sales\nreports","Reports",true));
        work.Controls.Add(customer);
        work.Controls.Add(Paragraph(
            "C# Lab is a separate development build. Purchasing and financial posting workflows " +
            "shown as 'soon' are disabled until their underlying accounting logic is ported.",920,60));
        split.Panel2.Controls.Add(work);
        dashboard.Controls.Add(split);
        bottom.Text="C# Lab Home — navigation aligned with Python PhoenixLedger v3.6.x";
    }
}
