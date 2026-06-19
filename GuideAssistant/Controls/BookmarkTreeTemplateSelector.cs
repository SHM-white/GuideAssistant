using GuideAssistant.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GuideAssistant.Controls;

public class BookmarkTreeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FolderTemplate { get; set; }
    public DataTemplate? BookmarkTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        if (item is BookmarkTreeNode node && node.IsFolder)
            return FolderTemplate;
        return BookmarkTemplate;
    }
}
