using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private DateTimePicker? reportStart;
    private DateTimePicker? reportEnd;
    private ComboBox? reportKind;

    private void ExportDocumentPdf(string kind)
    {
        if(kind=="Selected")
        {
            if(current!="Estimates & Invoices")
                throw new InvalidOperationException("Select an estimate or invoice first.");
            kind=CurrentCell("Type")??"Invoice";
        }
        long id=SelectedId();
        if(kind is not ("Invoice" or "Estimate" or "Purchase Order"))
            throw new InvalidOperationException("This document type is not supported for PDF export.");
        using var picker=new SaveFileDialog
        {
            Filter="PDF document (*.pdf)|*.pdf",
            FileName=$"PhoenixLedger_{kind.Replace(' ','_')}_{id}.pdf",
            Title="Export "+kind+" PDF"
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        PdfDocuments.Export(kind,PdfDocuments.ReadDocument(kind,id),picker.FileName);
        MessageBox.Show(this,"PDF generated with your saved "+kind+" layout.",
            "PDF export",MessageBoxButtons.OK,MessageBoxIcon.Information);
        Process.Start(new ProcessStartInfo(picker.FileName){UseShellExecute=true});
    }

    private void OpenPdfDesigner(string kind)
    {
        using var designer=new PdfDesigner(kind);
        designer.ShowDialog(this);
    }

    private void ExportFinancialReportPdf()
    {
        if(current!="Reports"||reportGrid?.DataSource is not DataTable table)
            throw new InvalidOperationException("Run the report before exporting.");
        using var picker=new SaveFileDialog
        {
            Filter="PDF document (*.pdf)|*.pdf",
            Title="Export report using saved layout",
            FileName="PhoenixLedger_"+(reportKind?.Text??"Report").Replace(' ','_')+".pdf"
        };
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        PdfDocuments.Export("Report",PdfDocuments.ReportData(reportKind?.Text??"Financial Report",table),picker.FileName);
        Process.Start(new ProcessStartInfo(picker.FileName){UseShellExecute=true});
    }

    private void ManageBudget()
    {
        using var win=new Form{Text="Budget — enter monthly target",StartPosition=FormStartPosition.CenterParent,
            Size=new Size(550,300),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(18)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++)panel.RowStyles.Add(new RowStyle(SizeType.Absolute,51));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var month=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="yyyy-MM",
            ShowUpDown=true,Width=170};
        var account=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        using(var db=Store.Open())
        using(var cmd=db.CreateCommand())
        {
            cmd.CommandText="SELECT name FROM lab_fin_accounts WHERE type IN ('Revenue','Expense') ORDER BY code";
            using var r=cmd.ExecuteReader();
            while(r.Read())account.Items.Add(r.GetString(0));
        }
        if(account.Items.Count>0)account.SelectedIndex=0;
        var amount=new NumericUpDown{DecimalPlaces=2,Maximum=999999999,Minimum=0,ThousandsSeparator=true,Dock=DockStyle.Fill};
        (string,Control)[] fields={("Budget month",month),("Income / Expense account",account),("Budget amount",amount)};
        for(int i=0;i<fields.Length;i++)
        {
            panel.Controls.Add(new Label{Text=fields[i].Item1,Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft},0,i);
            panel.Controls.Add(fields[i].Item2,1,i);
        }
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Save budget",Width=140,Height=39};
        var cancel=new Button{Text="Cancel",Width=100,Height=39,DialogResult=DialogResult.Cancel};
        footer.Controls.Add(cancel);footer.Controls.Add(save);panel.Controls.Add(footer,1,3);
        win.Controls.Add(panel);win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                FinancialReports.SaveBudget(month.Value.ToString("yyyy-MM"),account.Text,amount.Value);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex){MessageBox.Show(win,ex.Message,"Budget not saved",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        };
        if(win.ShowDialog(this)==DialogResult.OK && current=="Reports")RefreshCurrent();
    }

    private void EnterBankTransaction()
    {
        using var win=new Form{Text="Bank statement entry — C# Lab",StartPosition=FormStartPosition.CenterParent,
            Size=new Size(560,300),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(18)};
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++)panel.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var date=new DateTimePicker{Dock=DockStyle.Fill};
        var description=new TextBox{Dock=DockStyle.Fill};
        var amount=new NumericUpDown{Minimum=-999999999,Maximum=999999999,DecimalPlaces=2,
            ThousandsSeparator=true,Dock=DockStyle.Fill};
        (string,Control)[] fields={("Bank posting date",date),("Description",description),("Amount (+ deposit / - withdrawal)",amount)};
        for(int i=0;i<fields.Length;i++)
        {
            panel.Controls.Add(new Label{Text=fields[i].Item1,Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft},0,i);
            panel.Controls.Add(fields[i].Item2,1,i);
        }
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Add statement entry",Width=200,Height=40};
        var cancel=new Button{Text="Cancel",Width=110,Height=40,DialogResult=DialogResult.Cancel};
        footer.Controls.Add(cancel);footer.Controls.Add(save);panel.Controls.Add(footer,1,3);
        win.Controls.Add(panel);win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                FinancialReports.AddBankLine(date.Value,description.Text,amount.Value);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception ex){MessageBox.Show(win,ex.Message,"Bank entry not saved",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        };
        if(win.ShowDialog(this)==DialogResult.OK && current=="Reports")RefreshCurrent();
    }
}
