namespace GuideAssistant.Overlays;

public interface IOverlayController : IDisposable
{
    void ShowMiniMap();
    void HideMiniMap();
    void ShowDirection(string directionText);
    void ShowSubtitle();
    void HideSubtitle();
    void UpdateSubtitle(string text);
    bool IsMiniMapVisible { get; }
    bool IsSubtitleVisible { get; }
    event Action? MiniMapClosed;
    event Action? SubtitleClosed;
}
