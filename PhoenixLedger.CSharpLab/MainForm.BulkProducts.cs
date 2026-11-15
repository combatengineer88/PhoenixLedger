using System.Globalization;
using System.Windows.Forms;

namespace PhoenixLedger.CSharpLab;

internal sealed partial class MainForm
{
    private void OpenBulkProductEditor()
    {
        if(current!="Products / Catalog")
            ShowPage("Products / Catalog");
        if(archivedToggle.Checked)
            throw new InvalidOperationException(
                "Archived products cannot be bulk edited. Uncheck View Archived Only or restore them first.");
        long[] initiallySelected=grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(row=>!row.IsNewRow && row.Cells["ID"].Value is not null)
            .Select(row=>Convert.ToInt64(row.Cells["ID"].Value,CultureInfo.InvariantCulture))
            .Distinct().ToArray();
        using var editor=new BulkProductEditor(initiallySelected);
        if(editor.ShowDialog(this)==DialogResult.OK)
            RefreshGrid();
    }
}
