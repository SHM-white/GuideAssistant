namespace GuideAssistant.Services;

public enum DirectionReference
{
    Compass, // World direction: 0° is geographic north.
    Map      // North-up map position: 0° is screen top.
}

public readonly record struct DirectionMatch(
    string Keyword, double Angle, string Label, DirectionReference Reference);

/// <summary>
/// Classifies complete subtitle text, not detached keywords. Player/camera-relative
/// words like 正前方, 左前方, 往前 and 向右 have no absolute bearing without the
/// current game camera orientation and deliberately never produce an arrow.
/// </summary>
public static class DirectionWords
{
    private static readonly (string Word, double Angle, string Label)[] Compass =
    {
        ("西北方向", 315, "西北"), ("东北方向", 45, "东北"),
        ("西南方向", 225, "西南"), ("东南方向", 135, "东南"),
        ("正西北", 315, "西北"), ("正东北", 45, "东北"),
        ("正西南", 225, "西南"), ("正东南", 135, "东南"),
        ("西北", 315, "西北"), ("东北", 45, "东北"),
        ("西南", 225, "西南"), ("东南", 135, "东南"),
        ("西方向", 270, "西"), ("东方向", 90, "东"),
        ("北方向", 0, "北"), ("南方向", 180, "南"),
        ("正西", 270, "西"), ("正东", 90, "东"),
        ("正北", 0, "北"), ("正南", 180, "南"),
        ("向西", 270, "西"), ("向东", 90, "东"),
        ("向北", 0, "北"), ("向南", 180, "南"),
        ("往西", 270, "西"), ("往东", 90, "东"),
        ("往北", 0, "北"), ("往南", 180, "南"),
        ("朝西", 270, "西"), ("朝东", 90, "东"),
        ("朝北", 0, "北"), ("朝南", 180, "南"),
        ("西边", 270, "西"), ("东边", 90, "东"),
        ("北边", 0, "北"), ("南边", 180, "南"),
        ("西侧", 270, "西"), ("东侧", 90, "东"),
        ("北侧", 0, "北"), ("南侧", 180, "南"),
        ("西方", 270, "西"), ("东方", 90, "东"),
        ("北方", 0, "北"), ("南方", 180, "南")
    };

    private static readonly (string Word, double Angle, string Label)[] MapRelative =
    {
        ("左上角", 315, "↖"), ("右上角", 45, "↗"),
        ("左下角", 225, "↙"), ("右下角", 135, "↘"),
        ("左上方", 315, "↖"), ("右上方", 45, "↗"),
        ("左下方", 225, "↙"), ("右下方", 135, "↘"),
        ("左上", 315, "↖"), ("右上", 45, "↗"),
        ("左下", 225, "↙"), ("右下", 135, "↘"),
        ("左边", 270, "←"), ("右边", 90, "→"),
        ("左侧", 270, "←"), ("右侧", 90, "→"),
        ("上面", 0, "↑"), ("下面", 180, "↓"),
        ("上方", 0, "↑"), ("下方", 180, "↓"),
        ("上边", 0, "↑"), ("下边", 180, "↓")
    };

    public static DirectionMatch? TryResolve(string? fullText, bool requireMinimapMention = false)
    {
        if (string.IsNullOrWhiteSpace(fullText)) return null;
        bool mentionsMap = fullText.Contains("地图", StringComparison.Ordinal);
        if (requireMinimapMention &&
            (!mentionsMap || fullText.Contains("大地图", StringComparison.Ordinal)))
            return null;

        // Resolve map-space left/right/top/bottom only with an explicit map anchor.
        if (mentionsMap)
        {
            foreach (var (word, angle, label) in MapRelative)
                if (fullText.Contains(word, StringComparison.Ordinal))
                    return new DirectionMatch(word, angle, label, DirectionReference.Map);
        }
        foreach (var (word, angle, label) in Compass)
            if (fullText.Contains(word, StringComparison.Ordinal))
                return new DirectionMatch(word, angle, label, DirectionReference.Compass);

        // A single standalone compass character is meaningful; arbitrary substrings
        // like 东西 or 东门 should not accidentally create a world-direction arrow.
        var single = fullText.Trim(' ', '。', '！', '？', ',', '，', '：', ':');
        return single switch
        {
            "北" => new DirectionMatch("北", 0, "北", DirectionReference.Compass),
            "东" => new DirectionMatch("东", 90, "东", DirectionReference.Compass),
            "南" => new DirectionMatch("南", 180, "南", DirectionReference.Compass),
            "西" => new DirectionMatch("西", 270, "西", DirectionReference.Compass),
            _ => null
        };
    }
}
