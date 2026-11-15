using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    internal void SmokeNavigate()
    {
        foreach (string page in Pages) ShowPage(page);
        ShowPage("Settings / Updates");
        if (settingsTabs==null || updateView.Parent is not TabPage)
            throw new InvalidOperationException("Settings / Updates page did not initialize.");
        ShowPage("Home");
    }

    private TabControl? settingsTabs;
    private DataGridView? reportGrid;

    private static string CanonicalPage(string page)=>page switch
    {
        "Products / Catalog"=>"Products",
        "Estimates & Invoices"=>"Sales Documents",
        _=>page
    };

    private MenuStrip BuildMainMenu()
    {
        var menu=new MenuStrip
        {
            Dock=DockStyle.Fill, BackColor=Color.White,
            Font=new Font("Segoe UI",10), Padding=new Padding(8,2,8,2)
        };

        ToolStripMenuItem command(string caption,Action action,Keys shortcut=Keys.None)
        {
            var item=new ToolStripMenuItem(caption);
            if(shortcut!=Keys.None) item.ShortcutKeys=shortcut;
            item.Click+=(_,_)=>Safe(action);
            return item;
        }
        ToolStripMenuItem submenu(string name, params ToolStripItem[] children)
        {
            var item=new ToolStripMenuItem(name);
            item.DropDownItems.AddRange(children);
            return item;
        }

        var file=submenu("&File",
            submenu("&New",
                command("Product",()=>{ShowPage("Products / Catalog");Edit(0);}),
                command("Customer",()=>{ShowPage("Customers");Edit(0);}),
                command("Supplier",()=>{ShowPage("Suppliers");Edit(0);}),
                command("Expense",()=>{ShowPage("Expenses");EditExpense(0);})
            ),
            new ToolStripSeparator(),
            command("Import &Python database copy...",ImportProductionCopy),
            command("Import &Excel spreadsheet...",()=>{
                if(current is not ("Products / Catalog" or "Suppliers" or "Customers"))ShowPage("Products / Catalog");
                ImportExcel();
            }),
            command("Export &visible table CSV...",ExportCurrentTable,Keys.Control|Keys.E),
            new ToolStripSeparator(),
            command("&Backup C# Lab database...",BackupLab,Keys.Control|Keys.Shift|Keys.S),
            command("Open &data folder",()=>OpenDirectory(Store.Root)),
            new ToolStripSeparator(),
            command("E&xit",Close,Keys.Alt|Keys.F4)
        );
        var edit=submenu("&Edit",
            command("&Add record",AddCurrentRecord,Keys.Control|Keys.N),
            command("&Edit selected record",()=>EditSelectedCurrent(),Keys.Control|Keys.Shift|Keys.E),
            command("Delete selected expense",DeleteSelectedExpense),
            new ToolStripSeparator(),
            command("Copy selected cell",CopySelectedCell,Keys.Control|Keys.C),
            command("&Find / Search",()=>{if(!searchPanel.Visible)ShowPage("Products / Catalog");search.Focus();},Keys.Control|Keys.F),
            command("&Refresh",RefreshCurrent,Keys.F5)
        );
        var view=new ToolStripMenuItem("&View");
        foreach(string page in Pages)
        {
            string destination=page;
            view.DropDownItems.Add(command(page,()=>ShowPage(destination)));
        }
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(command("&Maximize",()=>WindowState=FormWindowState.Maximized));
        view.DropDownItems.Add(command("&Restore window",()=>WindowState=FormWindowState.Normal));

        var tools=submenu("&Tools",
            command("Scan / adjust inventory...",()=>{ShowPage("Products / Catalog");Adjust();}),
            command("Inventory movement history",()=>ShowPage("Inventory History")),
            command("Business &reports",()=>ShowPage("Reports")),
            command("Business settings and &updates",()=>{ShowPage("Settings / Updates");OpenUpdateTab();}),
            new ToolStripSeparator(),
            command("Open &application folder",()=>OpenDirectory(AppContext.BaseDirectory)),
            command("View update &log",OpenUpdateLog),
            new ToolStripSeparator(),
            command("Create &purchase order...",()=>{ShowPage("Purchase Orders");NewPurchaseOrder();}),
            command("Record &invoice payment...",()=>{ShowPage("Estimates & Invoices");PayInvoiceOrSupplier(false);}),
            command("PDF &Layout Designer...",()=>OpenPdfDesigner("Invoice")),
            command("Record &budget...",ManageBudget),
            command("Enter &bank transaction...",EnterBankTransaction),
            command("New chart &account...",AddChartAccount),
            command("Post manual &journal...",EnterManualJournal)
        );
        var help=submenu("&Help",
            command("GitHub source / user guide",()=>Process.Start(new ProcessStartInfo(
                "https://github.com/combatengineer88/PhoenixLedger"){UseShellExecute=true})),
            command("Latest release notes",()=>Process.Start(new ProcessStartInfo(
                "https://github.com/combatengineer88/PhoenixLedger/releases/latest"){UseShellExecute=true})),
            new ToolStripSeparator(),
            command("&About PhoenixLedger",ShowAbout)
        );
        menu.Items.AddRange(new ToolStripItem[]{file,edit,view,tools,help});
        return menu;
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"PhoenixLedger C# Lab {UpdateService.CurrentVersion.ToString(3)}\n" +
            "Windows Forms edition — independent experimental workspace\n\n" +
            "Navigation and workflow layout reference: Python PhoenixLedger v3.6.x.\n" +
            "C# Lab supports products, customers, suppliers, inventory, purchase-order " +
            "receiving, supplier and invoice payments, financial reports and PDF layout design.\n\n" +
            "Employee authentication and complete invoice/estimate authoring are not yet ported. " +
            "Reconcile imported historical accounts before using reports for business decisions.\n\n" +
            $"C# Lab database:\n{Store.Database}",
            "About PhoenixLedger",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private void OpenDirectory(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe",$"\"{folder}\""){UseShellExecute=true});
    }

    private void OpenUpdateLog()
    {
        string path=Path.Combine(Store.Root,"updates","last-update-install.log");
        if(File.Exists(path))
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        else
            OpenDirectory(Path.Combine(Store.Root,"updates"));
    }

    private void RefreshCurrent()
    {
        if(current=="Home")BuildHomeWorkspace();
        else if(current=="Dashboard")BuildDashboard();
        else if(current=="Reports")BuildBusinessPage("Reports");
        else if(current is "Administration" or "Settings / Updates")BuildBusinessPage(current);
        else RefreshGrid();
    }

    private void ExportCurrentTable()
    {
        if(current=="Reports"){ExportReport();return;}
        if(current is "Home" or "Dashboard" or "Administration" or "Settings / Updates")
            throw new InvalidOperationException("Open a data table or run a report before exporting.");
        ExportCsv();
    }

    private void CopySelectedCell()
    {
        if(grid.Visible && grid.CurrentCell?.Value != null)
            Clipboard.SetText(grid.CurrentCell.FormattedValue?.ToString() ?? "");
        else
            throw new InvalidOperationException("Select a cell in a table first.");
    }

    private void AddCurrentRecord()
    {
        if(current is "Products / Catalog" or "Suppliers" or "Customers")Edit(0);
        else if(current=="Expenses")EditExpense(0);
        else throw new InvalidOperationException("Adding records to this section is not available in the C# Lab yet.");
    }

    private void EditSelectedCurrent()
    {
        if(current is "Products / Catalog" or "Suppliers" or "Customers")Edit(SelectedId());
        else if(current=="Expenses")EditExpense(SelectedId());
        else if(current is "Purchase Orders" or "Estimates & Invoices")ViewDetails(SelectedId());
        else throw new InvalidOperationException("This section has no editable records yet.");
    }

    private void BackupLab()
    {
        using var picker=new SaveFileDialog
        {
            Title="Back up the separate C# Lab database",
            Filter="SQLite database (*.db)|*.db",
            FileName=$"PhoenixLedger_CSharpLab_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            OverwritePrompt=false
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        BusinessData.BackupTo(picker.FileName);
        MessageBox.Show(this,"Verified C# Lab database backup saved to:\n"+picker.FileName,
            "Backup complete",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private void EditExpense(long id)
    {
        var expense=id>0?BusinessData.LoadExpense(id):
            (Date:DateTime.Today,Category:"",Vendor:"",Description:"",Amount:0m);
        using var form=new Form
        {
            Text=id>0?"Edit C# Lab expense":"Add C# Lab expense",
            StartPosition=FormStartPosition.CenterParent,
            Font=new Font("Segoe UI",10),
            Width=560,Height=380,MinimumSize=new Size(530,360)
        };
        var layout=new TableLayoutPanel
        {
            Dock=DockStyle.Fill,Padding=new Padding(20),
            ColumnCount=2,RowCount=6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<5;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var date=new DateTimePicker{Format=DateTimePickerFormat.Short,Value=expense.Date,Dock=DockStyle.Fill};
        var category=new TextBox{Text=expense.Category,Dock=DockStyle.Fill};
        var vendor=new TextBox{Text=expense.Vendor,Dock=DockStyle.Fill};
        var description=new TextBox{Text=expense.Description,Dock=DockStyle.Fill};
        var amount=new NumericUpDown
        {
            DecimalPlaces=2,Minimum=0,Maximum=100000000,Increment=1,
            Value=decimal.Clamp(expense.Amount,0,100000000),
            ThousandsSeparator=true,Dock=DockStyle.Fill
        };
        (string label,Control editor)[] fields={
            ("Date",date),("Category",category),("Vendor",vendor),
            ("Description",description),("Amount",amount)
        };
        for(int i=0;i<fields.Length;i++)
        {
            layout.Controls.Add(new Label{
                Text=fields[i].label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft
            },0,i);
            layout.Controls.Add(fields[i].editor,1,i);
        }
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Save expense",Width=138,Height=35};
        var cancel=new Button{Text="Cancel",Width=100,Height=35,DialogResult=DialogResult.Cancel};
        footer.Controls.Add(save);footer.Controls.Add(cancel);
        layout.Controls.Add(footer,1,5);
        form.Controls.Add(layout);
        form.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                BusinessData.SaveExpense(id,date.Value,category.Text,vendor.Text,description.Text,amount.Value);
                form.DialogResult=DialogResult.OK;form.Close();
            }
            catch(Exception ex)
            {
                MessageBox.Show(form,ex.Message,"Expense not saved",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(form.ShowDialog(this)==DialogResult.OK)RefreshGrid();
    }

    private void DeleteSelectedExpense()
    {
        if(current!="Expenses")
            throw new InvalidOperationException("Open Expenses to delete an expense entry.");
        long id=SelectedId();
        if(MessageBox.Show(this,$"Permanently delete expense #{id} from the separate C# Lab database?",
            "Delete expense",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        BusinessData.DeleteExpense(id);
        RefreshGrid();
    }

    private void ViewAccountingLines()
    {
        if(current!="Accounting")
            throw new InvalidOperationException("Open Accounting first.");
        long selected=SelectedId();
        bool lab=selected>=1000000000;
        long id=lab?selected-1000000000:selected;
        using var db=Store.Open();
        if(!lab && !BusinessData.HasTable(db,"journal_lines"))
            throw new InvalidOperationException("This journal has no imported line items.");
        using var command=db.CreateCommand();
        command.CommandText=lab?"""
            SELECT account AS Account,printf('$%.2f',debit/100.0) AS Debit,
                printf('$%.2f',credit/100.0) AS Credit
            FROM lab_fin_lines WHERE journal_id=@id ORDER BY id
            """:"""
            SELECT account AS Account,printf('$%.2f',debit/100.0) AS Debit,
                printf('$%.2f',credit/100.0) AS Credit
            FROM journal_lines WHERE journal_id=@id ORDER BY id
            """;
        command.Parameters.AddWithValue("@id",id);
        using var reader=command.ExecuteReader();
        var table=new DataTable();table.Load(reader);
        using var window=new Form
        {
            Text=$"Journal #{id} — Line Items",Width=800,Height=480,
            StartPosition=FormStartPosition.CenterParent
        };
        window.Controls.Add(new DataGridView
        {
            DataSource=table,Dock=DockStyle.Fill,ReadOnly=true,
            AllowUserToAddRows=false,RowHeadersVisible=false,
            AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill
        });
        window.ShowDialog(this);
    }

    private void BuildBusinessPage(string page)
    {
        dashboard.Controls.Clear();
        if(page=="Dashboard")
        {
            BuildDashboard();
            return;
        }
        if(page=="Settings / Updates")
        {
            BuildSettings();
            return;
        }
        if(page=="Reports")
        {
            BuildReports();
            return;
        }
        if(page=="Administration")
        {
            BuildAdministration();
            return;
        }
    }

    private void BuildAdministration()
    {
        var panel=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
            WrapContents=false,AutoScroll=true,Padding=new Padding(12)
        };
        panel.Controls.Add(Heading("Administration and workspace maintenance",680));
        panel.Controls.Add(Paragraph(
            "Python PhoenixLedger reference: employee accounts, password management, " +
            "database backups and administrative settings.\n\n" +
            "This C# Lab edition does not yet implement employee sign-in or role permissions. " +
            "The buttons below perform maintenance on the separate C# Lab database only.",740,120));
        var backup=ActionButton("Back up C# Lab database",BackupLab,240);
        panel.Controls.Add(backup);
        panel.Controls.Add(ActionButton("Import Python database copy",ImportProductionCopy,260));
        panel.Controls.Add(ActionButton("Open lab data folder",()=>OpenDirectory(Store.Root),240));
        panel.Controls.Add(Paragraph("Current lab data: "+Store.Database,750,85));
        dashboard.Controls.Add(panel);
    }

    private void BuildReports()
    {
        var layout=new TableLayoutPanel
        {
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(6)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(Heading("Automated financial and operating reports — C# Lab",990),0,0);
        var bar=new FlowLayoutPanel
        {
            Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.LeftToRight
        };
        reportKind=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=275};
        reportKind.Items.AddRange(FinancialReports.Names.Cast<object>().ToArray());
        reportKind.SelectedIndex=0;
        reportKind.SelectedIndexChanged+=(_,_)=>Safe(()=>LoadReport(reportKind.Text));
        bar.Controls.Add(reportKind);
        bar.Controls.Add(new Label{Text="From:",Width=48,Height=28,TextAlign=ContentAlignment.MiddleRight});
        reportStart=new DateTimePicker{Value=new DateTime(DateTime.Today.Year,1,1),Width=142};
        bar.Controls.Add(reportStart);
        bar.Controls.Add(new Label{Text="Through:",Width=78,Height=28,TextAlign=ContentAlignment.MiddleRight});
        reportEnd=new DateTimePicker{Value=DateTime.Today,Width=145};
        bar.Controls.Add(reportEnd);
        bar.Controls.Add(ActionButton("Generate",()=>LoadReport(reportKind.Text),120));
        layout.Controls.Add(bar,0,1);
        var actionsBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        actionsBar.Controls.Add(ActionButton("Export CSV",ExportReport,125));
        actionsBar.Controls.Add(ActionButton("Export PDF",ExportFinancialReportPdf,125));
        actionsBar.Controls.Add(ActionButton("Edit PDF Layout",()=>OpenPdfDesigner("Report"),150));
        actionsBar.Controls.Add(ActionButton("Set Monthly Budget",ManageBudget,175));
        actionsBar.Controls.Add(ActionButton("Enter Bank Transaction",EnterBankTransaction,202));
        layout.Controls.Add(actionsBar,0,2);
        reportGrid=new DataGridView
        {
            Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,
            AllowUserToDeleteRows=false,RowHeadersVisible=false,
            AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells,
            BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect
        };
        layout.Controls.Add(reportGrid,0,3);
        dashboard.Controls.Add(layout);
        LoadReport(reportKind.Text);
    }

    private void LoadReport(string name)
    {
        if(reportGrid==null||reportStart==null||reportEnd==null)return;
        reportGrid.DataSource=FinancialReports.Generate(name,reportStart.Value,reportEnd.Value);
        bottom.Text=$"{reportGrid.Rows.Count:N0} rows — {name} — {reportStart.Value:yyyy-MM-dd} to {reportEnd.Value:yyyy-MM-dd}" +
            "  •  C# Lab data; imported journal history may require reconciliation";
    }

    private void ExportReport()
    {
        if(current!="Reports" || reportGrid?.DataSource is not DataTable data)
            throw new InvalidOperationException("Run a report first.");
        using var picker=new SaveFileDialog
        {
            Title="Save report CSV",Filter="CSV (*.csv)|*.csv",
            FileName=$"PhoenixLedger_Lab_Report_{DateTime.Now:yyyyMMdd}.csv"
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        static string Quote(string? v)=>"\""+(v??"").Replace("\"","\"\"").Replace("\r"," ").Replace("\n"," ")+"\"";
        using var writer=new StreamWriter(picker.FileName,false,new UTF8Encoding(true));
        writer.WriteLine(string.Join(",",data.Columns.Cast<DataColumn>().Select(x=>Quote(x.ColumnName))));
        foreach(DataRow row in data.Rows)
            writer.WriteLine(string.Join(",",data.Columns.Cast<DataColumn>().Select(c=>Quote(row[c]?.ToString()))));
        MessageBox.Show(this,"Report CSV saved.","Export complete",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private void OpenUpdateTab()
    {
        if(settingsTabs!=null)settingsTabs.SelectedIndex=1;
    }

    private void BuildSettings()
    {
        if(settingsTabs==null)
        {
            settingsTabs=new TabControl
            {
                Dock=DockStyle.Fill,Font=new Font("Segoe UI",10)
            };
            var branding=new TabPage("Branding");
            var updates=new TabPage("Updates");
            var storage=new TabPage("Database & maintenance");
            settingsTabs.TabPages.AddRange(new[]{branding,updates,storage});
            updates.Controls.Add(updateView);
            updateView.Dock=DockStyle.Fill;
            updateView.Visible=true;
            BuildBranding(branding);
            var maintenance=new FlowLayoutPanel
            {
                Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
                WrapContents=false,AutoScroll=true,Padding=new Padding(16)
            };
            maintenance.Controls.Add(Heading("Local-only workspace",740));
            maintenance.Controls.Add(Paragraph(
                "This C# edition uses a separate database and media folder. " +
                "Importing a Python database creates a copy without modifying the original.",730,90));
            maintenance.Controls.Add(ActionButton("Back up C# Lab data",BackupLab,240));
            maintenance.Controls.Add(ActionButton("Import a Python database copy",ImportProductionCopy,270));
            maintenance.Controls.Add(ActionButton("Open data directory",()=>OpenDirectory(Store.Root),240));
            maintenance.Controls.Add(ActionButton("Open application directory",()=>OpenDirectory(AppContext.BaseDirectory),270));
            maintenance.Controls.Add(ActionButton("View update log",OpenUpdateLog,240));
            maintenance.Controls.Add(ActionButton("Design Invoice PDF",()=>OpenPdfDesigner("Invoice"),240));
            maintenance.Controls.Add(ActionButton("Design Estimate PDF",()=>OpenPdfDesigner("Estimate"),240));
            maintenance.Controls.Add(ActionButton("Design Purchase Order PDF",()=>OpenPdfDesigner("Purchase Order"),270));
            maintenance.Controls.Add(ActionButton("Design Report PDF",()=>OpenPdfDesigner("Report"),240));
            maintenance.Controls.Add(Paragraph("Lab SQLite database:\n"+Store.Database,730,85));
            storage.Controls.Add(maintenance);
            settingsTabs.SelectedIndex=1;
        }
        dashboard.Controls.Add(settingsTabs);
        bottom.Text="Branding, GitHub updates and local C# Lab maintenance";
    }

    private void BuildBranding(TabPage tab)
    {
        var panel=new TableLayoutPanel
        {
            Dock=DockStyle.Top,Height=360,ColumnCount=2,RowCount=6,Padding=new Padding(20),
            AutoScroll=true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        for(int i=0;i<4;i++)panel.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        panel.Controls.Add(Heading("Business identity (C# Lab)",570),0,0);
        panel.SetColumnSpan(panel.GetControlFromPosition(0,0)!,2);
        var fields=new Dictionary<string,TextBox>();
        (string key,string caption)[] specs={
            ("business_name","Business name"),
            ("business_address","Address"),
            ("business_phone","Telephone"),
            ("business_email","Email")
        };
        for(int i=0;i<specs.Length;i++)
        {
            var s=specs[i];
            panel.Controls.Add(new Label{Text=s.caption,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,i+1);
            var entry=new TextBox{Dock=DockStyle.Fill,Text=BusinessData.Setting(s.key)};
            fields[s.key]=entry;panel.Controls.Add(entry,1,i+1);
        }
        var actionsBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        actionsBar.Controls.Add(ActionButton("Save business details",()=>{
            BusinessData.SetSettings(fields.ToDictionary(k=>k.Key,v=>v.Value.Text));
            MessageBox.Show(this,"C# Lab branding saved. Use the PDF Designer to apply it to documents.",
                "Business identity",MessageBoxButtons.OK,MessageBoxIcon.Information);
        },210));
        actionsBar.Controls.Add(ActionButton("Choose company logo",ChooseCompanyLogo,200));
        panel.Controls.Add(actionsBar,1,5);
        tab.Controls.Add(new Label
        {
            Dock=DockStyle.Fill,Padding=new Padding(20),ForeColor=Color.FromArgb(103,119,138),
            Text="These settings are stored in the C# Lab database. " +
                "Open the PDF Designer from Tools or the maintenance tab to create independent drag-and-drop layouts for invoices, estimates, purchase orders and reports."
        });
        tab.Controls.Add(panel);
    }

    private void ChooseCompanyLogo()
    {
        using var picker=new OpenFileDialog
        {
            Title="Choose business logo for the C# Lab",
            Filter="Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        string folder=Path.Combine(Store.Root,"branding");
        Directory.CreateDirectory(folder);
        string dest=Path.Combine(folder,"company_logo"+Path.GetExtension(picker.FileName).ToLowerInvariant());
        if(!string.Equals(Path.GetFullPath(picker.FileName),Path.GetFullPath(dest),StringComparison.OrdinalIgnoreCase))
            File.Copy(picker.FileName,dest,overwrite:true);
        BusinessData.SetSettings(new Dictionary<string,string>{{"logo_path",dest}});
        MessageBox.Show(this,"Company logo copied into the C# Lab data directory.\n"+
            "PDF template rendering is not yet part of this build.",
            "Branding saved",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private static Label Heading(string caption,int width)=>new()
    {
        Text=caption,Width=width,Height=44,Font=new Font("Segoe UI",16,FontStyle.Bold),
        ForeColor=Color.FromArgb(39,55,77),TextAlign=ContentAlignment.MiddleLeft
    };

    private static Label Paragraph(string caption,int width,int height)=>new()
    {
        Text=caption,Width=width,Height=height,AutoSize=false,
        Font=new Font("Segoe UI",10),ForeColor=Color.FromArgb(70,85,105)
    };

    private static Button ActionButton(string text,Action action,int width)
    {
        var button=new Button
        {
            Text=text,Width=width,Height=39,Margin=new Padding(0,6,12,6),
            Font=new Font("Segoe UI",9.5f)
        };
        button.Click+=(_,_)=>{
            try{action();}
            catch(Exception ex)
            {
                MessageBox.Show(button.FindForm(),ex.Message,"PhoenixLedger C# Lab",
                    MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        return button;
    }
}
