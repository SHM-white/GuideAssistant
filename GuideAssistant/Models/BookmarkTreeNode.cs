using System.Collections.ObjectModel;

namespace GuideAssistant.Models;

public class BookmarkTreeNode
{
    public string? IconGlyph { get; set; }
    public string Label { get; set; } = "";
    public string? SubLabel { get; set; }
    public ObservableCollection<BookmarkTreeNode> Children { get; set; } = new();
    public Bookmark? Bookmark { get; set; }
    public bool IsFolder { get; set; }
    public int FolderId { get; set; }
}
