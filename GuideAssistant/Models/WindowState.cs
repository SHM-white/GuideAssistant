namespace GuideAssistant.Models;

public class WindowState
{
    public int Id { get; set; }
    public string WindowName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 960;
    public double Height { get; set; } = 640;
    public double Opacity { get; set; } = 0.9;
    public bool IsAlwaysOnTop { get; set; } = true;
}
