namespace GuideAssistant.Services;

/// <summary>Convert a complete subtitle into a compass/map arrow heading.</summary>
public sealed class DirectionService
{
    public static (double angle, string label)? ParseDirection(string text)
    {
        var match = DirectionWords.TryResolve(text);
        return match is { } direction ? (direction.Angle, direction.Label) : null;
    }
}
