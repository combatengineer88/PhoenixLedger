using System.Drawing;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private RoundedButton? archiveButton;
    private RoundedButton? restoreButton;
    private RoundedButton? purgeButton;

    internal void SmokeArchivedCatalogNavigation()
    {
        foreach(string page in new[]{"Customers","Suppliers","Products / Catalog"})
        {
            ShowPage(page);
            archivedToggle.Checked=true;
            if(!grid.Columns.Contains("Archived On") ||
                !grid.Columns["Archived On"]!.Visible)
                throw new InvalidOperationException("Archived-only column/view was not initialized for "+page);
            foreach(DataGridViewRow row in grid.Rows.Cast<DataGridViewRow>().Take(5))
                foreach(DataGridViewCell cell in row.Cells)
                    _=cell.FormattedValue;
            archivedToggle.Checked=false;
        }
        ShowPage("Home");
    }

    private void AddCatalogButtons()
    {
        archiveButton=AddAction("Archive Selected",ArchiveSelected,false,175);
        restoreButton=AddAction("Restore Selected",RestoreSelected,false,172);
        purgeButton=AddAction("Delete Permanently",PurgeSelected,false,190);
        UpdateArchiveButtons();
    }

    private void UpdateArchiveButtons()
    {
        bool archiveView=archivedToggle.Checked;
        if(archiveButton!=null){archiveButton.Enabled=!archiveView;archiveButton.Invalidate();}
        if(restoreButton!=null){restoreButton.Enabled=archiveView;restoreButton.Invalidate();}
        if(purgeButton!=null){purgeButton.Enabled=archiveView;purgeButton.Invalidate();}
    }

    private void ArchiveSelected()
    {
        if(!CatalogArchive.IsCatalog(CanonicalPage(current)))
            throw new InvalidOperationException("Open Customers, Suppliers, or Products first.");
        if(archivedToggle.Checked)
            throw new InvalidOperationException("This view contains archived entries; switch off View Archived Only.");
        string section=CanonicalPage(current);
        long id=SelectedId();
        var record=CatalogArchive.Info(section,id);
        if(record.Archived)throw new InvalidOperationException("This record is already archived.");
        string message=$"Archive {section.ToLowerInvariant().TrimEnd('s')} '{record.Name}' (ID {id})?\n\n"+
            "The entry will disappear from active catalogs and new-transaction pickers.\n"+
            "Its original ID, related transactions, and financial/inventory history will remain available to reports.\n\n"+
            "You can restore it from View Archived Only.";
        if(section=="Products"&&record.Stock>0)
            message+=$"\n\nThis product still holds {record.Stock:N0} units. Its inventory value will remain recorded.";
        if(MessageBox.Show(this,message,"Archive catalog entry",
            MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        CatalogArchive.SetArchived(section,id,true);
        RefreshGrid();
    }

    private void RestoreSelected()
    {
        if(!CatalogArchive.IsCatalog(CanonicalPage(current))||!archivedToggle.Checked)
            throw new InvalidOperationException("Select an entry in View Archived Only.");
        string section=CanonicalPage(current);
        long id=SelectedId();
        var record=CatalogArchive.Info(section,id);
        if(!record.Archived)throw new InvalidOperationException("This record is already active.");
        if(MessageBox.Show(this,
            $"Restore '{record.Name}' (ID {id}) to the active {section} catalog?\n\n"+
            "Historical transactions and report references will not change.",
            "Restore archived entry",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        CatalogArchive.SetArchived(section,id,false);
        RefreshGrid();
    }

    private void PurgeSelected()
    {
        if(!CatalogArchive.IsCatalog(CanonicalPage(current))||!archivedToggle.Checked)
            throw new InvalidOperationException("Select an entry in View Archived Only first.");
        string section=CanonicalPage(current);
        long id=SelectedId();
        var record=CatalogArchive.Info(section,id);
        if(!record.Archived)throw new InvalidOperationException("Only archived entries can be permanently deleted.");
        if(!record.CanPurge)
        {
            string reasons=record.Stock!=0?
                $"Product inventory is {record.Stock:N0} units. Resolve its balance before deletion.\n":"";
            if(record.Dependencies.Count>0)
                reasons+=string.Join("\n",record.Dependencies.Take(10));
            MessageBox.Show(this,
                "This entry cannot be deleted because it is still needed by the database.\n\n"+
                reasons+"\n\nKeep it archived to preserve past reports and transaction details.",
                "Permanent deletion blocked",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }

        if(MessageBox.Show(this,
            $"PERMANENTLY DELETE '{record.Name}' (ID {id}) from {section}?\n\n"+
            "This archived entry has no identified transaction, inventory-history, or other linked database records.\n"+
            "Deleting it permanently removes the catalog record. It cannot be restored with the Restore button.\n\n"+
            "A verified backup will be saved automatically before deletion. Continue?",
            "Confirm permanent deletion",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;

        string backupDirectory=Path.Combine(Store.Root,"backups");
        Directory.CreateDirectory(backupDirectory);
        string backup=Path.Combine(backupDirectory,
            $"lab_before_catalog_delete_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.db");
        BusinessData.BackupTo(backup);
        CatalogArchive.Purge(section,id);
        RefreshGrid();
        MessageBox.Show(this,
            $"Archived entry permanently deleted from the catalog.\n\n"+
            $"A safety backup was saved at:\n{backup}\n\n"+
            "Historical archive-action audit records remain. Existing backup copies are not automatically deleted.",
            "Catalog entry deleted",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
}
