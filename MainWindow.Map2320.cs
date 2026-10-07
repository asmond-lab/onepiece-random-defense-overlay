using System.Windows;

namespace OrandOverlay;

public partial class MainWindow
{
    // Static reference only: no recognition, catalog switch, settings write or native approval.
    private void Map2320Patch_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Map2320PatchWindow { Owner = this };
        dialog.ShowDialog();
    }
}
