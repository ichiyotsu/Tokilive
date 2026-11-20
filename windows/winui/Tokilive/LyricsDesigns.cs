using Microsoft.UI.Text;

namespace Tokilive;

/// <summary>
/// 歌词设计：一组预设值，一次写进歌词的字体、颜色、字号、粗细、
/// 阴影、描边、底色和对齐。设计只是预设、不是锁定——选完之后
/// 用户再改单项设置，设计名还留在那，不会把改动弹回去。
/// 再加一个设计只要在这里加一行，不用动设置页和渲染。
/// </summary>
public sealed record LyricDesign(string Id, string Name, string Font, string Color, string Highlight, double Size, string Weight, bool Shadow, bool Outline, double Bg, string Align);

public static class LyricsDesigns
{
    public static readonly LyricDesign[] All =
    [
        // 默认：就是现在的默认值。
        new("default", "默认", "Inter", "#FFFFFF", "#FFFFFF", 22, "normal", true, false, 0.35, "center"),
        // 赛博朋克 2077：当前行用招牌黄 #FCEE0A，其他行用青 #00F0FF。
        // 字体 Rajdhani 就是游戏内的 UI 字体（OFL，已随程序打包在 Fonts/ 下）。
        new("cyberpunk", "赛博朋克 2077", "Rajdhani", "#00F0FF", "#FCEE0A", 24, "bold", true, false, 0.55, "center"),
        // MC：当前行用 Minecraft 绿 #55FF55，其他行白字。
        // Minecraft 自己的 Mojangles 字体没有官方字体文件（游戏用位图字帖
        // 渲染，网上流传的都是粉丝描摹、无授权），所以用 OFL 的像素字体
        // VT323 代替（已随程序打包在 Fonts/ 下）。
        new("minecraft", "MC", "VT323", "#FFFFFF", "#55FF55", 22, "bold", true, false, 0.6, "center"),
    ];

    public static LyricDesign ById(string? id) => All.FirstOrDefault(d => d.Id == id) ?? All[0];

    public static Windows.UI.Text.FontWeight WeightOf(string weight) => weight switch
    {
        "light" => FontWeights.Light,
        "bold" => FontWeights.Bold,
        "black" => FontWeights.Black,
        _ => FontWeights.Normal,
    };
}
