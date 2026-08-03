using System.Windows;
using System.Windows.Controls;

namespace RadioPulseViewer;

public partial class MainWindow
{
    private bool _reactionAnalysisButtonsAdded;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_reactionAnalysisButtonsAdded || ReactionKeywordTextBox.Parent is not Grid controlGrid)
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

        Button graphRecordButton = new()
        {
            Content = "グラフ値を記録",
            ToolTip = "公開グラフを参照し、画面で確認した件数だけを記録します。ページの自動解析は行いません。"
        };
        graphRecordButton.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        graphRecordButton.Click += OpenPublicGraphRecordWindowButton_Click;

        Button xPostCountButton = new()
        {
            Content = "公式X投稿数",
            ToolTip = "公式X APIから投稿数だけを取得します。Webページのスクレイピングは行いません。"
        };
        xPostCountButton.Click += OpenXPostCountWindowButton_Click;

        int separatorIndex = actionPanel.Children
            .OfType<UIElement>()
            .Select((element, index) => new { element, index })
            .FirstOrDefault(item => item.element is Separator)?.index ?? actionPanel.Children.Count;

        actionPanel.Children.Insert(separatorIndex, graphRecordButton);
        actionPanel.Children.Insert(separatorIndex + 1, xPostCountButton);
        _reactionAnalysisButtonsAdded = true;
    }

    private void OpenPublicGraphRecordWindowButton_Click(object sender, RoutedEventArgs e)
    {
        string query = ReactionKeywordTextBox.Text.Trim();
        PublicGraphRecordWindow window = new(query)
        {
            Owner = this
        };
        window.Show();
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
