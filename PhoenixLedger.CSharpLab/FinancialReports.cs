using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PhoenixLedger.CSharpLab;

internal sealed record FinancialLine(DateTime Date,string Memo,string Account,long Debit,long Credit,string Source);

/// <summary>Reports computed from the live C# Lab database, never cached snapshots.</summary>
internal static class FinancialReports
{
    public static readonly string[] Names={
        "Profit and Loss Statement","Balance Sheet","Cash Flow Statement","Chart of Accounts",
        "Accounts Receivable Aging","Accounts Payable Aging","Trial Balance","General Ledger",
        "Bank Reconciliation","Budget vs. Actual",
        "Sales by month","Payments by month","Expenses by category",
        "Low stock and reorder needs","Invoice balances","Supplier product catalog","Customer sales totals"
    };

    internal static List<FinancialLine> Ledger(DateTime asOf)
    {
        var result=new List<FinancialLine>();
        using var db=Store.Open();
        void read(string sql)
        {
            using var cmd=db.CreateCommand();cmd.CommandText=sql;
            cmd.Parameters.AddWithValue("@end",asOf.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
            using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                var date=DateTime.TryParse(r.GetString(0),out var parsed)?parsed:DateTime.Today;
                result.Add(new FinancialLine(date,r.GetString(1),r.GetString(2),
                    r.GetInt64(3),r.GetInt64(4),r.GetString(5)));
            }
        }
        read("""
            SELECT j.posted_on,j.memo,l.account,l.debit,l.credit,j.source_type
            FROM lab_fin_journal j JOIN lab_fin_lines l ON l.journal_id=j.id
            WHERE substr(j.posted_on,1,10)<=@end
            """);
        if(BusinessData.HasTable(db,"journal")&&BusinessData.HasTable(db,"journal_lines"))
            read("""
                SELECT j.posted_on,j.memo,l.account,l.debit,l.credit,j.source_type
                FROM journal j JOIN journal_lines l ON l.journal_id=j.id
                WHERE substr(j.posted_on,1,10)<=@end
                """);
        return result.OrderBy(x=>x.Date).ThenBy(x=>x.Memo).ToList();
    }

    private static DataTable Table(params string[] names)
    {
        var t=new DataTable();
        foreach(string name in names)t.Columns.Add(name,typeof(string));
        return t;
    }
    private static void Add(DataTable t,params object?[] values)=>
        t.Rows.Add(values.Select(v=>v?.ToString()??"").ToArray());
    private static string Money(long cents)=>Repository.Dollars(cents).ToString("C2",CultureInfo.CurrentCulture);
    private static string SignMoney(long cents)=>Money(cents);
    private static string Classify(string account)
    {
        if(account.Contains("payable",StringComparison.OrdinalIgnoreCase))return "Liability";
        if(account.Contains("receivable",StringComparison.OrdinalIgnoreCase)||
            account.Contains("cash",StringComparison.OrdinalIgnoreCase)||
            account.Contains("inventory",StringComparison.OrdinalIgnoreCase))return "Asset";
        if(account.Contains("equity",StringComparison.OrdinalIgnoreCase)||
            account.Contains("retained",StringComparison.OrdinalIgnoreCase))return "Equity";
        if(account.Contains("revenue",StringComparison.OrdinalIgnoreCase)||
            account.Contains("income",StringComparison.OrdinalIgnoreCase)||
            account.Contains("sales",StringComparison.OrdinalIgnoreCase))return "Revenue";
        if(account.Contains("expense",StringComparison.OrdinalIgnoreCase)||
            account.Contains("cost of goods",StringComparison.OrdinalIgnoreCase))return "Expense";
        return "Unclassified";
    }
    private static Dictionary<string,string> Classes()
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        using var db=Store.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT name,type FROM lab_fin_accounts";
        using var r=cmd.ExecuteReader();
        while(r.Read())result[r.GetString(0)]=r.GetString(1);
        return result;
    }
    private static string AccountType(string name,Dictionary<string,string> types)=>
        types.TryGetValue(name,out var type)?type:Classify(name);

    public static DataTable Generate(string name,DateTime start,DateTime end)
    {
        if(start.Date>end.Date)throw new InvalidOperationException("The report start date is after its end date.");
        if(!Names.Contains(name,StringComparer.Ordinal))
            throw new ArgumentException("Unknown report type.",nameof(name));
        if(!new[]{"Profit and Loss Statement","Balance Sheet","Cash Flow Statement","Chart of Accounts",
            "Accounts Receivable Aging","Accounts Payable Aging","Trial Balance","General Ledger",
            "Bank Reconciliation","Budget vs. Actual"}.Contains(name))
            return BusinessData.Report(name);
        var ledger=Ledger(end);
        var types=Classes();
        var period=ledger.Where(x=>x.Date.Date>=start.Date).ToList();
        if(name=="Chart of Accounts")
        {
            var t=Table("Account","Code","Category","Current Balance");
            using var db=Store.Open();using var c=db.CreateCommand();
            c.CommandText="SELECT name,code,type FROM lab_fin_accounts ORDER BY code";
            using(var r=c.ExecuteReader())
                while(r.Read())
                {
                    string account=r.GetString(0),type=r.GetString(2);
                    long net=ledger.Where(x=>x.Account==account).Sum(x=>x.Debit-x.Credit);
                    Add(t,account,r.GetString(1),type,Money(type is "Revenue" or "Liability" or "Equity"?-net:net));
                }
            foreach(string acct in ledger.Select(x=>x.Account).Distinct(StringComparer.OrdinalIgnoreCase).Where(x=>!types.ContainsKey(x)))
            {
                long net=ledger.Where(x=>x.Account==acct).Sum(x=>x.Debit-x.Credit);
                Add(t,acct,"—",AccountType(acct,types),Money(net));
            }
            return t;
        }
        if(name=="Trial Balance")
        {
            var t=Table("Account","Debit","Credit");
            long d=0,c=0;
            foreach(var g in ledger.GroupBy(x=>x.Account).OrderBy(x=>x.Key))
            {
                long net=g.Sum(x=>x.Debit-x.Credit);
                long debit=Math.Max(0,net),credit=Math.Max(0,-net);
                d+=debit;c+=credit;Add(t,g.Key,Money(debit),Money(credit));
            }
            Add(t,"TOTAL",Money(d),Money(c));
            Add(t,d==c?"BALANCED":"OUT OF BALANCE",Money(Math.Max(d-c,0)),Money(Math.Max(c-d,0)));
            return t;
        }
        if(name=="General Ledger")
        {
            var t=Table("Date","Account","Description","Debit","Credit","Running Balance");
            var grouped=ledger.GroupBy(x=>x.Account).OrderBy(x=>x.Key);
            foreach(var g in grouped)
            {
                long balance=0;
                foreach(var line in g.OrderBy(x=>x.Date))
                {
                    balance+=line.Debit-line.Credit;
                    if(line.Date.Date>=start.Date)
                        Add(t,line.Date.ToString("yyyy-MM-dd"),g.Key,line.Memo,
                            Money(line.Debit),Money(line.Credit),Money(balance));
                }
            }
            return t;
        }
        if(name=="Profit and Loss Statement")
        {
            var t=Table("Section","Account","Amount");
            long revenue=0,costs=0;
            foreach(var g in period.GroupBy(x=>x.Account).OrderBy(x=>x.Key))
            {
                string type=AccountType(g.Key,types);
                if(type is not ("Revenue" or "Expense"))continue;
                long net=g.Sum(x=>x.Credit-x.Debit);
                if(type=="Revenue")revenue+=net;else costs-=net;
                Add(t,type,g.Key,Money(type=="Revenue"?net:-net));
            }
            Add(t,"TOTAL","Total revenue",Money(revenue));
            Add(t,"TOTAL","Total expense",Money(costs));
            Add(t,"RESULT","Net income / (loss)",Money(revenue-costs));
            return t;
        }
        if(name=="Balance Sheet")
        {
            var t=Table("Section","Account","Balance");
            long assets=0,liabilities=0,equity=0,revenue=0,expenses=0;
            foreach(var g in ledger.GroupBy(x=>x.Account).OrderBy(x=>x.Key))
            {
                var type=AccountType(g.Key,types);
                long debits=g.Sum(x=>x.Debit-x.Credit);
                if(type=="Asset"){assets+=debits;Add(t,"ASSET",g.Key,Money(debits));}
                if(type=="Liability"){liabilities-=debits;Add(t,"LIABILITY",g.Key,Money(-debits));}
                if(type=="Equity"){equity-=debits;Add(t,"EQUITY",g.Key,Money(-debits));}
                if(type=="Revenue")revenue-=debits;
                if(type=="Expense")expenses+=debits;
            }
            long netIncome=revenue-expenses;
            Add(t,"TOTAL","Assets",Money(assets));
            Add(t,"TOTAL","Liabilities",Money(liabilities));
            Add(t,"EQUITY","Current / prior earnings",Money(netIncome));
            Add(t,"TOTAL","Liabilities + Equity + Earnings",Money(liabilities+equity+netIncome));
            Add(t,"CHECK","Assets - Liabilities - Equity",Money(assets-liabilities-equity-netIncome));
            return t;
        }
        if(name=="Cash Flow Statement")
        {
            var t=Table("Cash flow category","Source","Amount");
            long operating=0,investing=0,financing=0;
            foreach(var g in period.Where(x=>x.Account.Equals("Cash",StringComparison.OrdinalIgnoreCase))
                       .GroupBy(x=>x.Source).OrderBy(x=>x.Key))
            {
                long net=g.Sum(x=>x.Debit-x.Credit);
                string group=g.Key.Contains("owner",StringComparison.OrdinalIgnoreCase)?"Financing":
                    g.Key.Contains("asset",StringComparison.OrdinalIgnoreCase)?"Investing":"Operating";
                if(group=="Operating")operating+=net;
                else if(group=="Investing")investing+=net;
                else financing+=net;
                Add(t,group,g.Key,SignMoney(net));
            }
            Add(t,"TOTAL","Operating activities",Money(operating));
            Add(t,"TOTAL","Investing activities",Money(investing));
            Add(t,"TOTAL","Financing activities",Money(financing));
            Add(t,"RESULT","Change in cash",Money(operating+investing+financing));
            return t;
        }
        if(name=="Accounts Receivable Aging")
        {
            var t=Table("Invoice","Customer","Due date","Outstanding","Current","1–30","31–60","61–90","Over 90");
            using var db=Store.Open();using var cmd=db.CreateCommand();
            bool dueCol=false;
            using(var pragma=db.CreateCommand())
            {
                pragma.CommandText="PRAGMA table_info(documents)";
                using var rr=pragma.ExecuteReader();
                while(rr.Read())if(rr.GetString(1)=="credit_due_on")dueCol=true;
            }
            cmd.CommandText=$"""
                SELECT d.id,coalesce(c.name,''),d.created,d.total,
                       coalesce((SELECT sum(p.amount) FROM payments p WHERE p.document_id=d.id AND substr(p.paid_on,1,10)<=@end),0),
                       {(dueCol?"coalesce(d.credit_due_on,'')":"''")}
                FROM documents d LEFT JOIN customers c ON c.id=d.customer_id
                WHERE d.kind='Invoice' AND substr(d.created,1,10)<=@end
                ORDER BY d.created,d.id
                """;
            cmd.Parameters.AddWithValue("@end",end.ToString("yyyy-MM-dd"));
            using var rr2=cmd.ExecuteReader();
            while(rr2.Read())
            {
                long balance=rr2.GetInt64(3)-rr2.GetInt64(4);
                if(balance<=0)continue;
                DateTime created=DateTime.TryParse(rr2.GetString(2),out var dt)?dt:end;
                DateTime due=DateTime.TryParse(rr2.GetString(5),out var dd)?dd:created.AddDays(30);
                int days=Math.Max(0,(end.Date-due.Date).Days);
                int bucket=(end.Date<=due.Date)?0:days<=30?1:days<=60?2:days<=90?3:4;
                var values=new object[9]{$"INV-{rr2.GetInt64(0)}",rr2.GetString(1),
                    due.ToString("yyyy-MM-dd"),Money(balance),"","","","",""};
                values[4+bucket]=Money(balance);
                Add(t,values);
            }
            return t;
        }
        if(name=="Accounts Payable Aging")
        {
            var t=Table("Purchase order","Supplier","Due date","Outstanding","Current","1–30","31–60","61–90","Over 90");
            using var db=Store.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="""
                SELECT po.id,coalesce(s.name,''),po.ordered_on,
                       coalesce((SELECT sum(r.total) FROM po_receipts r WHERE r.po_id=po.id AND substr(r.received_on,1,10)<=@end),0),
                       coalesce((SELECT sum(p.amount) FROM supplier_payments p WHERE p.po_id=po.id AND substr(p.paid_on,1,10)<=@end),0)
                FROM purchase_orders po LEFT JOIN suppliers s ON s.id=po.supplier_id
                WHERE substr(po.ordered_on,1,10)<=@end
                ORDER BY po.ordered_on,po.id
                """;
            cmd.Parameters.AddWithValue("@end",end.ToString("yyyy-MM-dd"));
            using var rr=cmd.ExecuteReader();
            while(rr.Read())
            {
                long balance=rr.GetInt64(3)-rr.GetInt64(4);
                if(balance<=0)continue;
                DateTime ordered=DateTime.TryParse(rr.GetString(2),out var dt)?dt:end;
                DateTime due=ordered.AddDays(30);
                int days=Math.Max(0,(end.Date-due.Date).Days);
                int bucket=(end.Date<=due.Date)?0:days<=30?1:days<=60?2:days<=90?3:4;
                var values=new object[9]{$"PO-{rr.GetInt64(0)}",rr.GetString(1),due.ToString("yyyy-MM-dd"),
                    Money(balance),"","","","",""};
                values[4+bucket]=Money(balance);
                Add(t,values);
            }
            return t;
        }
        if(name=="Budget vs. Actual")
        {
            var t=Table("Month","Account","Budget","Actual","Variance");
            using var db=Store.Open();using var cmd=db.CreateCommand();
            cmd.CommandText="""
                SELECT month,account,amount FROM lab_fin_budgets
                WHERE month BETWEEN @start AND @end ORDER BY month,account
                """;
            cmd.Parameters.AddWithValue("@start",start.ToString("yyyy-MM"));
            cmd.Parameters.AddWithValue("@end",end.ToString("yyyy-MM"));
            using var rr=cmd.ExecuteReader();
            while(rr.Read())
            {
                string month=rr.GetString(0),account=rr.GetString(1);
                long budget=rr.GetInt64(2);
                long actual=period.Where(x=>x.Date.ToString("yyyy-MM")==month && x.Account==account)
                    .Sum(x=>x.Debit-x.Credit);
                if(AccountType(account,types)=="Revenue")actual=-actual;
                Add(t,month,account,Money(budget),Money(actual),Money(budget-actual));
            }
            return t;
        }
        if(name=="Bank Reconciliation")
        {
            var t=Table("Item","Date / detail","Amount","Cleared?");
            using var db=Store.Open();using var c=db.CreateCommand();
            c.CommandText="SELECT posted_on,description,amount,cleared FROM lab_fin_bank WHERE posted_on<=@end ORDER BY posted_on,id";
            c.Parameters.AddWithValue("@end",end.ToString("yyyy-MM-dd"));
            using(var r=c.ExecuteReader())
                while(r.Read())Add(t,"Bank statement",r.GetString(0)+" "+r.GetString(1),
                    Money(r.GetInt64(2)),r.GetInt64(3)==1?"Yes":"No");
            long book=ledger.Where(x=>x.Account.Equals("Cash",StringComparison.OrdinalIgnoreCase))
                .Sum(x=>x.Debit-x.Credit);
            long statement=FinancialOperations.Scalar(db,null,
                "SELECT coalesce(sum(amount),0) FROM lab_fin_bank WHERE posted_on<=@d",("d",end.ToString("yyyy-MM-dd")));
            Add(t,"BOOK","Cash ledger balance",Money(book),"");
            Add(t,"BANK","Imported / entered bank statement total",Money(statement),"");
            Add(t,"DIFFERENCE","Unreconciled difference",Money(statement-book),"");
            return t;
        }
        throw new InvalidOperationException("Report not available.");
    }

    public static void SaveBudget(string month,string account,decimal amount)
    {
        if(!DateTime.TryParseExact(month+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new InvalidOperationException("Enter a month in YYYY-MM format.");
        if(string.IsNullOrWhiteSpace(account)||amount<0)
            throw new InvalidOperationException("Choose an account and a nonnegative budget.");
        using var db=Store.Open();
        FinancialOperations.Execute(db,null,"""
            INSERT INTO lab_fin_budgets(month,account,amount) VALUES(@m,@a,@v)
            ON CONFLICT(month,account) DO UPDATE SET amount=excluded.amount
            """,("m",month),("a",account),("v",Repository.Cents(amount)));
    }

    public static void AddBankLine(DateTime date,string description,decimal amount)
    {
        if(string.IsNullOrWhiteSpace(description))
            throw new InvalidOperationException("A bank transaction description is required.");
        using var db=Store.Open();
        FinancialOperations.Execute(db,null,
            "INSERT INTO lab_fin_bank(posted_on,description,amount,cleared) VALUES(@d,@n,@a,0)",
            ("d",date.ToString("yyyy-MM-dd")),("n",description),("a",Repository.Cents(amount)));
    }
}
