using System.Windows;

namespace RadioPulseViewer;

public partial class MainWindow
{
    private void OpenXPostCountWindowButton_Click(object sender, RoutedEventArgs e)
    {
        string query = ReactionKeywordTextBox.Text.Trim();
        XPostCountWindow window = new(query)
        {
            Owner = this
        };
        window.Show();
    }
}
