using System.Windows;
using System.Windows.Controls;

namespace RadioPulseViewer;

public partial class MainWindow
{
    private bool _xPostCountButtonAdded;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_xPostCountButtonAdded || ReactionKeywordTextBox.Parent is not Grid controlGrid)
        {
            return;
        }

        WrapPanel? actionPanel = controlGrid.Children
            .OfType<WrapPanel>()
            .FirstOrDefault();
        if (actionPanel is null)
        {
            return;
        }

        Button button = new()
        {
            Content = "公式X投稿数",
            ToolTip = "スクレイピングを行わず、公式X APIから投稿数だけを取得します。"
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        button.Click += OpenXPostCountWindowButton_Click;

        int separatorIndex = actionPanel.Children
            .OfType<UIElement>()
            .Select((element, index) => new { element, index })
            .FirstOrDefault(item => item.element is Separator)?.index ?? actionPanel.Children.Count;
        actionPanel.Children.Insert(separatorIndex, button);
        _xPostCountButtonAdded = true;
    }

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
