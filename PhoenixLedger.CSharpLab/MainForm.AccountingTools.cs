using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private static List<string> AccountOptions()
    {
        using var db=Store.Open();using var c=db.CreateCommand();
        c.CommandText="SELECT name FROM lab_fin_accounts ORDER BY code";
        using var r=c.ExecuteReader();var list=new List<string>();
        while(r.Read())list.Add(r.GetString(0));
        return list;
    }

    private void AddChartAccount()
    {
        using var dialog=new Form
        {
            Text="Add Chart of Accounts entry",StartPosition=FormStartPosition.CenterParent,
            Size=new Size(510,340),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false
        };
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=4,Padding=new Padding(20)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var name=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Example: Equipment"};
        var code=new TextBox{Dock=DockStyle.Fill,PlaceholderText="Example: 1500"};
        var type=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        type.Items.AddRange(new object[]{"Asset","Liability","Equity","Revenue","Expense"});type.SelectedIndex=0;
        var values=new (string Label,Control Editor)[]{("Account name",name),("Account code",code),("Account type",type)};
        for(int i=0;i<values.Length;i++)
        {
            layout.Controls.Add(new Label{Text=values[i].Label,Dock=DockStyle.Fill,
                TextAlign=ContentAlignment.MiddleLeft},0,i);
            layout.Controls.Add(values[i].Editor,1,i);
        }
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Add account",Width=140,Height=38};
        var cancel=new Button{Text="Cancel",Width=100,Height=38,DialogResult=DialogResult.Cancel};
        footer.Controls.Add(cancel);footer.Controls.Add(save);layout.Controls.Add(footer,1,3);
        dialog.Controls.Add(layout);dialog.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                FinancialOperations.AddAccount(name.Text,code.Text,type.Text);
                dialog.DialogResult=DialogResult.OK;dialog.Close();
            }
            catch(Exception error)
            {
                MessageBox.Show(dialog,error.Message,"Account not saved",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(dialog.ShowDialog(this)==DialogResult.OK)
            MessageBox.Show(this,"Account added. It is available for journal entries and report budgets.",
                "Account created",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }

    private void EnterManualJournal()
    {
        var accounts=AccountOptions();
        if(accounts.Count<2)throw new InvalidOperationException("Create at least two ledger accounts first.");
        using var win=new Form
        {
            Text="Post balanced manual journal entry — C# Lab",Size=new Size(940,650),
            MinimumSize=new Size(750,520),StartPosition=FormStartPosition.CenterParent
        };
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(16)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,68));
        var header=new FlowLayoutPanel{Dock=DockStyle.Fill};
        var date=new DateTimePicker{Width=155};
        var memo=new TextBox{Width=540,PlaceholderText="Required memo (e.g. opening balances)"};
        header.Controls.Add(new Label{Text="Journal date:",Height=33,Width=106,TextAlign=ContentAlignment.MiddleRight});
        header.Controls.Add(date);header.Controls.Add(new Label{Text="Memo:",Height=33,Width=70,
            TextAlign=ContentAlignment.MiddleRight});header.Controls.Add(memo);panel.Controls.Add(header,0,0);
        var grid=new DataGridView
        {
            Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=true,
            AllowUserToDeleteRows=true,RowHeadersVisible=false,BackgroundColor=Color.White
        };
        var combo=new DataGridViewComboBoxColumn{HeaderText="Account",Name="Account",Width=460};
        foreach(var account in accounts)combo.Items.Add(account);
        grid.Columns.Add(combo);
        grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Debit ($)",Name="Debit",Width=160});
        grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Credit ($)",Name="Credit",Width=160});
        grid.Rows.Add("Cash","100.00","0.00");
        grid.Rows.Add("Opening Balance Equity","0.00","100.00");
        grid.DataError+=(_,e)=>e.ThrowException=false;
        panel.Controls.Add(grid,0,1);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var save=new Button{Text="Post balanced journal",Width=220,Height=40};
        var cancel=new Button{Text="Cancel",Width=120,Height=40,DialogResult=DialogResult.Cancel};
        footer.Controls.Add(cancel);footer.Controls.Add(save);panel.Controls.Add(footer,0,2);
        win.Controls.Add(panel);win.CancelButton=cancel;
        save.Click+=(_,_)=>{
            try
            {
                grid.EndEdit();
                var lines=new List<(string Account,long Debit,long Credit)>();
                foreach(DataGridViewRow row in grid.Rows)
                {
                    if(row.IsNewRow||row.Cells["Account"].Value==null)continue;
                    string account=row.Cells["Account"].Value?.ToString()??"";
                    string drText=row.Cells["Debit"].Value?.ToString()??"0";
                    string crText=row.Cells["Credit"].Value?.ToString()??"0";
                    if(!decimal.TryParse(drText,NumberStyles.Currency,CultureInfo.CurrentCulture,out var dr)||
                       !decimal.TryParse(crText,NumberStyles.Currency,CultureInfo.CurrentCulture,out var cr))
                        throw new InvalidOperationException("Enter valid currency values for all journal lines.");
                    lines.Add((account,Repository.Cents(dr),Repository.Cents(cr)));
                }
                FinancialOperations.ManualJournal(date.Value,memo.Text,lines);
                win.DialogResult=DialogResult.OK;win.Close();
            }
            catch(Exception error)
            {
                MessageBox.Show(win,error.Message,"Journal was not posted",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        };
        if(win.ShowDialog(this)==DialogResult.OK)
        {
            RefreshGrid();
            MessageBox.Show(this,"Balanced journal entry posted to the C# Lab ledger.",
                "Entry posted",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
}
