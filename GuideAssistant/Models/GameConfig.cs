namespace GuideAssistant.Models;

public class GameConfig
{
    public int Id { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string GamePath { get; set; } = string.Empty;
    public string? HelperPath { get; set; }
    public string? LaunchArgs { get; set; }
    public bool AutoDetect { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
