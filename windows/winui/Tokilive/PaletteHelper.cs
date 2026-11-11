// PaletteHelper.cs — 种子色 → 色调调色板 → 每种消息卡片的角色色。
//
// 来源：1.2.0 `windows/src/palette.js` 的 buildPalette()（MD3，material-color-utilities）。
// 1.3.1 的 WinUI 工程不能引用那个 JS 库，也没有引入 material-color-utilities 的
// NuGet 包，所以这里只移植思路，不移植库：用一个小的 HSL 色调斜坡近似 MD3 的
// TonalPalette.tone()，用色相旋转近似 Blend.harmonize()。
//
// ============================================================================
// 色调映射表（直接抄自 palette.js；每对数字是 [浅色 tone, 深色 tone]，
// Build() 按 dark 参数取其中一个）
// ----------------------------------------------------------------------------
// 类型       调色板来源（无 override 时）        Bg        Line      Name      Text
// danmaku    neutralVariant（种子色相，低饱和）  [90, 30]  [78, 60]  [50, 70]  [10, 95]
// gift       custom(GOLD #C9B46E，harmonize 后) [85, 30]  [20, 80]  [20, 90]  [10, 95]
// guard      primary（种子色本身）             [85, 30]  [25, 80]  [20, 90]  [10, 95]
// superchat  primary（种子色本身）             [30, 80]  [80, 30]  [75, 40]  [100, 10]
// enter      neutral（种子色相，极低饱和）      [99, 4]   [10, 95]  [50, 65]  [10, 95]
// mini       neutralVariant（同 danmaku）       [90, 30]  [78, 60]  [50, 70]  [10, 95]
//
// palette.js 里还有、但 PaletteRoles（只有 Bg/Line/Name/Text 四个槽位）装不下、
// 本次 API 不包含的角色，已列在这里供后续扩展时照抄，不静默丢弃：
//   gift / guard 的 verb:   gift [55, 70]，guard [55, 70]
//   superchat 的 price:    [90, 20]
//   mini 的 accent:        [40, 80]
//   miniw（互动小窗统计条，neutral）：bg [99, 85]，line [88, 0]
//   透明样式（--tr-*，固定 tone，不分深浅）：dm-line 75 / dm-name 45 / dm-text 8 /
//     em-bg 92；guard-line 80 / guard-name 35 / guard-text 30；
//     sc-line 25 / sc-name 85 / sc-text 30；gift-line 85 / gift-text 30 / verb 40；
//     enter-line 87 / enter-name 40 / enter-text 8
//   房管徽章 badge（error 调色板）：bg [60, 40]，fg [100, 95]
//     —— Build() 的 6 个 key 不含 badge，本文件暂不暴露 badge API；
//        需要时按 error 色相（约 0° 的红色）加一个方法即可。
//
// 粉丝牌 medal（统一样式，fix() 用固定 tone，不分深浅模式）：
//   粉色（默认）：custom(PINK #FF5FC8，harmonize 后）
//     bg tone 72，fg tone 10，lv-bg tone 20，lv-fg tone 95
//   金色（礼物 / 上舰内用）：custom(GOLD #C9B46E，harmonize 后）
//     bg tone 45，fg tone 10，lv-bg tone 22，lv-fg tone 92
// ============================================================================
// 与 MD3 / HCT 的偏差（有意为之，见下；视觉效果需在真机上对照 1.2.0 检查）：
//   1. HCT tone 是 CAM16 感知亮度（L*），本文件用 HSL 的 L（0–100）代替。
//      同一数值下，HSL 在高饱和色上看起来会比 HCT 亮一些、在低饱和色上暗一些；
//      两端点一致（tone 0 = 黑，tone 100 = 白），中间色调是近似值。
//   2. MD3 的 tonal palette 在 HCT 里跨 tone 保持色度（chroma）大致恒定；
//      HSL 斜坡保持饱和度 S 恒定，是最接近的廉价等价物，但色相在极浅 / 极深
//      tone 下的观感仍会与 HCT 有出入。
//   3. neutral / neutralVariant 在 MD3 里是独立的低色度调色板（色度约 6 / 16，
//      HCT 单位）；这里近似为“种子色相 + 种子饱和度 × 系数”：
//      neutral ×0.15，neutralVariant ×0.50。种子本身是灰色时两者都是灰色，
//      与 MD3 一致；种子饱和时仍保留一点色相倾向。
//   4. Blend.harmonize() 在 CAM16 色相上把设计色向种子色旋转一部分；
//      这里在 HSL 色相上沿最短弧旋转一半距离（50%），只动色相、不动饱和度
//      和亮度——与 harmonize“只改色相”的语义相同，旋转量是近似值。
//      注意一个可见后果：默认种子色 #6750A4（色相约 256°）与金色 #C9B46E
//      （色相约 46°）的最短弧经过红色区，旋转一半后金色落在品红附近，
//      所以 gift / 金色粉丝牌在默认种子色下会偏粉紫而不是金黄；
//      这是“向种子色旋转一半”的直接结果，真机对照 1.2.0 时重点看这一处，
//      若 1.2.0 的金色明显更黄，说明 MD3 的旋转量比一半小，应调小该系数。
//   5. overrides（ov()）：1.2.0 里覆盖色直接当调色板用，不 harmonize；
//      只有 GOLD / PINK 这两个内置自定义色才 harmonize。本文件照此区分。
// ============================================================================
// 约定：全部是纯函数，不碰 UI、不读写文件、不依赖 AppState（种子色和深浅模式
// 都由调用方当参数传进来）。任何非法输入都不抛异常，一律回退到默认值。

using System;
using System.Collections.Generic;

namespace Tokilive;

/// <summary>一种消息卡片的四个角色色，全部是 "#RRGGBB" 字符串。</summary>
public static class PaletteHelper
{
    /// <summary>种子色解析失败 / 未提供时的回退值；与 AppState.SeedColor 的默认值一致。</summary>
    public const string DefaultSeed = "#6750A4";

    const string Gold = "#C9B46E";
    const string Pink = "#FF5FC8";

    /// <summary>一种消息卡片的四个角色色。</summary>
    public sealed class PaletteRoles
    {
        /// <summary>卡片背景。</summary>
        public string Bg = "#000000";
        /// <summary>描边。</summary>
        public string Line = "#000000";
        /// <summary>用户名 / 标题色。</summary>
        public string Name = "#000000";
        /// <summary>正文色。</summary>
        public string Text = "#000000";
    }

    /// <summary>
    /// 按 1.2.0 buildPalette() 的映射，为 6 种消息卡片生成角色色。
    /// keys：danmaku、gift、guard、superchat、enter、mini。
    /// <paramref name="overrides"/> 把某个类型的调色板换成指定颜色（1.2.0 的 ov()）：
    /// key 是类型名（大小写不敏感），value 是 hex 颜色；null / 空表示全部用种子色。
    /// 非法 hex（种子色或某个 override）一律回退到 <see cref="DefaultSeed"/> / 忽略该 override，
    /// 绝不抛异常。
    /// </summary>
    public static IReadOnlyDictionary<string, PaletteRoles> Build(
        string seedHex, bool dark, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var seed = ParseOrDefault(seedHex);

        // 各调色板的“基色”（色相 + 饱和度）；Tone() 只改亮度来取 tone。
        var primary = seed;
        var neutral = Desaturate(seed, 0.15);
        var neutralVariant = Desaturate(seed, 0.50);
        var gold = ParseOrDefault(Harmonize(Gold, seedHex));

        PaletteRoles Roles(Hsl baseColor, (double Light, double Dark) bg,
            (double Light, double Dark) line, (double Light, double Dark) name,
            (double Light, double Dark) text)
        {
            double Pick((double Light, double Dark) pair) => dark ? pair.Dark : pair.Light;
            return new PaletteRoles
            {
                Bg = ToneFrom(baseColor, Pick(bg)),
                Line = ToneFrom(baseColor, Pick(line)),
                Name = ToneFrom(baseColor, Pick(name)),
                Text = ToneFrom(baseColor, Pick(text)),
            };
        }

        // 某个类型的 override（合法才生效）：直接当调色板基色，不 harmonize（同 ov()）。
        Hsl BaseFor(string type, Hsl fallback)
        {
            if (overrides != null && overrides.TryGetValue(type, out var hex)
                && TryParseHex(hex, out var custom))
            {
                return custom;
            }
            return fallback;
        }

        // 映射表见文件头注释；verb / price / accent 不在 PaletteRoles 里，见注释。
        return new Dictionary<string, PaletteRoles>(StringComparer.Ordinal)
        {
            ["danmaku"] = Roles(BaseFor("danmaku", neutralVariant),
                (90, 30), (78, 60), (50, 70), (10, 95)),
            ["gift"] = Roles(BaseFor("gift", gold),
                (85, 30), (20, 80), (20, 90), (10, 95)),
            ["guard"] = Roles(BaseFor("guard", primary),
                (85, 30), (25, 80), (20, 90), (10, 95)),
            ["superchat"] = Roles(BaseFor("superchat", primary),
                (30, 80), (80, 30), (75, 40), (100, 10)),
            ["enter"] = Roles(BaseFor("enter", neutral),
                (99, 4), (10, 95), (50, 65), (10, 95)),
            ["mini"] = Roles(BaseFor("mini", neutralVariant),
                (90, 30), (78, 60), (50, 70), (10, 95)),
        };
    }

    /// <summary>只取一种类型的角色色；<paramref name="type"/> 未知时回退到 danmaku。</summary>
    public static PaletteRoles For(string type, string seedHex, bool dark)
    {
        var key = (type ?? "").Trim().ToLowerInvariant();
        var all = Build(seedHex, dark);
        return all.TryGetValue(key, out var roles) ? roles : all["danmaku"];
    }

    /// <summary>
    /// 粉丝牌（统一样式）的四个角色色。gold=false 用粉色，gold=true 用金色深色版。
    /// tone 是固定值（1.2.0 的 fix()），不分深浅模式。
    /// 粉色：bg 72 / fg 10 / lv-bg 20 / lv-fg 95；金色：bg 45 / fg 10 / lv-bg 22 / lv-fg 92。
    /// 本重载用 <see cref="DefaultSeed"/> 做 harmonize（签名里没有种子色参数）。
    /// </summary>
    public static (string Bg, string Fg, string LvBg, string LvFg) Medal(bool gold)
        => Medal(gold, DefaultSeed);

    /// <summary>同 <see cref="Medal(bool)"/>，但按给定的种子色 harmonize 粉 / 金基色。</summary>
    public static (string Bg, string Fg, string LvBg, string LvFg) Medal(bool gold, string seedHex)
    {
        var baseHex = Harmonize(gold ? Gold : Pink, seedHex);
        var pal = ParseOrDefault(baseHex);
        if (gold)
        {
            return (ToneFrom(pal, 45), ToneFrom(pal, 10), ToneFrom(pal, 22), ToneFrom(pal, 92));
        }
        return (ToneFrom(pal, 72), ToneFrom(pal, 10), ToneFrom(pal, 20), ToneFrom(pal, 95));
    }

    /// <summary>
    /// 把颜色向种子色协调：色相沿最短弧向种子色相旋转一半距离（近似 MD3 Blend.harmonize，
    /// 只改色相，不动饱和度和亮度）。非法 hex 回退到 <see cref="DefaultSeed"/> 再处理，
    /// 绝不抛异常。
    /// </summary>
    public static string Harmonize(string colorHex, string seedHex)
    {
        var color = ParseOrDefault(colorHex);
        var seed = ParseOrDefault(seedHex);
        // 最短弧差值，范围 (-180, 180]；旋转一半。
        var diff = seed.H - color.H;
        while (diff > 180) diff -= 360;
        while (diff <= -180) diff += 360;
        var h = color.H + diff * 0.5;
        while (h < 0) h += 360;
        while (h >= 360) h -= 360;
        return ToHex(FromHsl(h, color.S, color.L));
    }

    /// <summary>
    /// 取 <paramref name="hex"/> 颜色在指定亮度（tone，0–100，超出则夹取）下的颜色：
    /// 保持色相和饱和度，只把 HSL 亮度设为 tone/100（近似 TonalPalette.tone()）。
    /// 非法 hex 回退到 <see cref="DefaultSeed"/> 的对应 tone，绝不抛异常。
    /// </summary>
    public static string Tone(string hex, double lightness)
    {
        var hsl = ParseOrDefault(hex);
        return ToneFrom(hsl, lightness);
    }

    // ---- 内部：HSL 表示与转换（全部纯函数，不抛异常）-------------------------

    readonly struct Hsl
    {
        public Hsl(double h, double s, double l) { H = h; S = s; L = l; }
        public double H { get; } // 0–360
        public double S { get; } // 0–1
        public double L { get; } // 0–1
    }

    static string ToneFrom(Hsl hsl, double lightness)
    {
        var l = Math.Clamp(lightness, 0, 100) / 100.0;
        return ToHex(FromHsl(hsl.H, hsl.S, l));
    }

    static Hsl Desaturate(Hsl hsl, double factor)
        => new(hsl.H, Math.Clamp(hsl.S * factor, 0, 1), hsl.L);

    static Hsl ParseOrDefault(string? hex)
        => TryParseHex(hex, out var hsl) ? hsl : RgbToHsl(0x67, 0x50, 0xA4); // #6750A4

    static bool TryParseHex(string? hex, out Hsl hsl)
    {
        hsl = default;
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var s = hex.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s[1..];
            if (s.Length == 3)
            {
                // #RGB → #RRGGBB。
                s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
            }
            if (s.Length != 6) return false;
            var r = Convert.ToInt32(s.Substring(0, 2), 16);
            var g = Convert.ToInt32(s.Substring(2, 2), 16);
            var b = Convert.ToInt32(s.Substring(4, 2), 16);
            hsl = RgbToHsl(r, g, b);
            return true;
        }
        catch (Exception)
        {
            // FormatException / OverflowException 等：非法 hex 一律按失败处理。
            return false;
        }
    }

    static Hsl RgbToHsl(int r, int g, int b)
    {
        double R = r / 255.0, G = g / 255.0, B = b / 255.0;
        var max = Math.Max(R, Math.Max(G, B));
        var min = Math.Min(R, Math.Min(G, B));
        var l = (max + min) / 2.0;
        if (max == min) return new Hsl(0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == R) h = (G - B) / d + (G < B ? 6 : 0);
        else if (max == G) h = (B - R) / d + 2;
        else h = (R - G) / d + 4;
        return new Hsl(h * 60.0, s, l);
    }

    static (int R, int G, int B) FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s == 0) { var v = (int)Math.Round(l * 255); return (v, v, v); }
        double C = (1 - Math.Abs(2 * l - 1)) * s;
        double X = C * (1 - Math.Abs((h / 60.0 % 2) - 1));
        double m = l - C / 2.0;
        double r, g, b;
        if (h < 60) (r, g, b) = (C, X, 0);
        else if (h < 120) (r, g, b) = (X, C, 0);
        else if (h < 180) (r, g, b) = (0, C, X);
        else if (h < 240) (r, g, b) = (0, X, C);
        else if (h < 300) (r, g, b) = (X, 0, C);
        else (r, g, b) = (C, 0, X);
        return ((int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
    }

    static string ToHex((int R, int G, int B) rgb)
        => $"#{Math.Clamp(rgb.R, 0, 255):X2}{Math.Clamp(rgb.G, 0, 255):X2}{Math.Clamp(rgb.B, 0, 255):X2}";
}
