namespace GuideAssistant.Services;

/// <summary>
/// Shared direction keywords and angle mappings for minimap overlay.
/// Arrow polygon points UP at 0° rotation; angles increase clockwise:
///   0°=北(UP), 90°=东(RIGHT), 180°=南(DOWN), 270°=西(LEFT).
/// Both <c>All</c> and <c>AngleMap</c> are ordered longest-keyword-first so
/// compound terms (西南方向) match before shorter substrings (西).
///
/// LIMITATION: Perspective-based keywords ("面前", "往前", "前方", "正前方" etc.)
/// assume the player faces NORTH. Without camera orientation data from the game,
/// these are intrinsically ambiguous — "面前" could mean any compass direction
/// depending on which way the player is looking. These keywords map to 0° (UP/North)
/// as a reasonable default for minimap overlay purposes.
/// </summary>
public static class DirectionWords
{
    /// <summary>
    /// All direction keywords in longest-first order for substring matching.
    /// Used by providers' CheckDirectionWords to detect direction words in subtitle text.
    /// </summary>
    public static readonly string[] All = {
        // Intercardinal with 方向 suffix (longest first)
        "西北方向", "东北方向", "西南方向", "东南方向",

        // Cardinal with 方向 suffix
        "西方向",   "东方向",   "北方向",   "南方向",

        // Corner with 方 suffix
        "左上方",   "右上方",   "左下方",   "右下方",

        // 地图 + direction compounds
        "地图左上方", "地图右上方", "地图左下方", "地图右下方",
        "地图左边", "地图右边", "地图上方", "地图下方",
        "地图左侧", "地图右侧", "地图上面", "地图下面",

        // Direction + 方向 variant (左边方向 etc.)
        "左边方向", "右边方向", "上方方向", "下方方向",

        // Cardinal with 方 suffix
        "东方",     "南方",     "西方",     "北方",

        // Perspective-based
        "正前方",   "左前方",   "右前方",   "左后方",   "右后方",

        // Intercardinal short
        "西南",     "东北",     "西北",     "东南",

        // Corner short
        "右上",     "右下",     "左上",     "左下",

        // Action-direction
        "往前",     "向后",     "向左",     "向右",

        // Cardinal single-char
        "东",       "南",       "西",       "北",

        // Screen-relative 2-char
        "前方",     "后方",     "前面",     "后面",
        "左边",     "右边",     "左侧",     "右侧",
        "上面",     "下面",     "上方",     "下方",
        "上边",     "下边",

        // Colloquial
        "面前",     "上头",     "下头",
    };

    /// <summary>
    /// Longest-first ordered map from keyword → (angle-in-degrees, display-label).
    /// For ParseDirection: iterates in insertion order, returns first match.
    /// Arrow polygon points UP (0°); RotateTransform rotates clockwise.
    /// </summary>
    public static readonly Dictionary<string, (double Angle, string Label)> AngleMap = new()
    {
        // ── Intercardinal + 方向 suffix ─────────────────
        { "西北方向", (315, "西北") },
        { "东北方向", (45,  "东北") },
        { "西南方向", (225, "西南") },
        { "东南方向", (135, "东南") },

        // ── Cardinal + 方向 suffix ──────────────────────
        { "西方向",   (270, "西") },
        { "东方向",   (90,  "东") },
        { "北方向",   (0,   "北") },
        { "南方向",   (180, "南") },

        // ── Corner + 方 suffix ──────────────────────────
        { "左上方",   (315, "↖") },
        { "右上方",   (45,  "↗") },
        { "左下方",   (225, "↙") },
        { "右下方",   (135, "↘") },

        // ── 地图 compounds ──────────────────────────────
        { "地图左上方", (315, "↖") },
        { "地图右上方", (45,  "↗") },
        { "地图左下方", (225, "↙") },
        { "地图右下方", (135, "↘") },
        { "地图左边",  (270, "←") },
        { "地图右边",  (90,  "→") },
        { "地图上方",  (0,   "↑") },
        { "地图下方",  (180, "↓") },
        { "地图左侧",  (270, "←") },
        { "地图右侧",  (90,  "→") },
        { "地图上面",  (0,   "↑") },
        { "地图下面",  (180, "↓") },

        // ── Direction + 方向 variant ────────────────────
        { "左边方向", (270, "←") },
        { "右边方向", (90,  "→") },
        { "上方方向", (0,   "↑") },
        { "下方方向", (180, "↓") },

        // ── Cardinal + 方 suffix ────────────────────────
        { "东方",    (90,  "东") },
        { "南方",    (180, "南") },
        { "西方",    (270, "西") },
        { "北方",    (0,   "北") },

        // ── Perspective ─────────────────────────────────
        { "正前方",  (0,   "↑") },
        { "左前方",  (315, "↖") },
        { "右前方",  (45,  "↗") },
        { "左后方",  (225, "↙") },
        { "右后方",  (135, "↘") },

        // ── Intercardinal short ─────────────────────────
        { "西南",    (225, "西南") },
        { "东北",    (45,  "东北") },
        { "西北",    (315, "西北") },
        { "东南",    (135, "东南") },

        // ── Corner short ────────────────────────────────
        { "右上",    (45,  "↗") },
        { "右下",    (135, "↘") },
        { "左上",    (315, "↖") },
        { "左下",    (225, "↙") },

        // ── Action-direction ────────────────────────────
        { "往前",    (0,   "↑") },
        { "向后",    (180, "↓") },
        { "向左",    (270, "←") },
        { "向右",    (90,  "→") },

        // ── Cardinal single-char ────────────────────────
        { "东",      (90,  "东") },
        { "南",      (180, "南") },
        { "西",      (270, "西") },
        { "北",      (0,   "北") },

        // ── Screen-relative 2-char ──────────────────────
        { "前方",    (0,   "↑") },
        { "后方",    (180, "↓") },
        { "前面",    (0,   "↑") },
        { "后面",    (180, "↓") },
        { "左边",    (270, "←") },
        { "右边",    (90,  "→") },
        { "左侧",    (270, "←") },
        { "右侧",    (90,  "→") },
        { "上面",    (0,   "↑") },
        { "下面",    (180, "↓") },
        { "上方",    (0,   "↑") },
        { "下方",    (180, "↓") },
        { "上边",    (0,   "↑") },
        { "下边",    (180, "↓") },

        // ── Colloquial ──────────────────────────────────
        { "面前",    (0,   "↑") },
        { "上头",    (0,   "↑") },
        { "下头",    (180, "↓") },
    };
}
