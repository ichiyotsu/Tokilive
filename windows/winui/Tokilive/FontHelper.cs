using Microsoft.UI.Xaml.Media;

namespace Tokilive;

/// <summary>
/// 1.2.0 的字体是三路分开的：拉丁字体（fontFamily，默认 Google Sans）、
/// 中文字体（cjkFont，默认 Source Han Sans SC）、日文字体（jpFont），
/// 再加一串系统回退（Noto Sans SC / Microsoft YaHei / PingFang SC……）。
/// 1.3.1 只剩一个 CardFont 文本框，空了就硬编码 "Segoe UI Variable"，
/// 歌词字体连这个回退都没有，直接 new FontFamily(state.LyricFont)。
/// WinUI 的 FontFamily 接受逗号分隔的回退列表，所以这里把用户选的字体
/// 放在最前面，后面补上 1.2.0 那套 CJK 回退，保证中文/日文不会掉回默认字体。
/// </summary>
public static class FontHelper
{
    public static readonly string[] Fallbacks =
    [
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "PingFang SC",
        "Source Han Sans SC",
        "Segoe UI Variable",
    ];

    // 字体选择框里给用户点选的列表：1.2.0 自带的那几个 + 系统常见字体。
    public static readonly string[] KnownFonts =
    [
        "Segoe UI Variable",
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "Noto Sans CJK SC",
        "Source Han Sans SC",
        "PingFang SC",
        "Inter",
        "Google Sans",
        "One UI Sans",
        "Yu Gothic UI",
        "Meiryo",
        "DengXian",
        "SimHei",
        "SimSun",
    ];

    public static string Resolve(string? name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();
        void Add(string? font)
        {
            var f = (font ?? "").Trim();
            if (f.Length == 0 || !seen.Add(f)) return;
            parts.Add(f);
        }
        Add(name);
        foreach (var fallback in Fallbacks) Add(fallback);
        return string.Join(", ", parts);
    }

    public static FontFamily Family(string? name) => new(Resolve(name));
}
