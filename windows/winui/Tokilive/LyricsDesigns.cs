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
        new("default", "默认", "Segoe UI Variable", "#FFFFFF", "#FFFFFF", 22, "normal", true, false, 0.35, "center"),
        // 赛博朋克 2077：当前行用招牌黄 #FCEE0A，其他行用青 #00F0FF。
        // 字体 Bahnschrift 是 Win10 自带的几何无衬线，有科技感；
        // 机器上没有它时 FontHelper.Resolve 会顺着回退到微软雅黑。
        new("cyberpunk", "赛博朋克 2077", "Bahnschrift", "#00F0FF", "#FCEE0A", 24, "bold", true, false, 0.55, "center"),
        // MC：当前行用 Minecraft 绿 #55FF55，其他行白字。
        // Minecraft 自己的像素字体不随 Windows 提供、也不能打包进来，
        // 用系统自带的等宽字体 Consolas 近似那种方块感；
        // 没有它时同样走 FontHelper 的回退。
        new("minecraft", "MC", "Consolas", "#FFFFFF", "#55FF55", 22, "bold", true, false, 0.6, "center"),
    ];

    public static LyricDesign ById(string? id) => All.FirstOrDefault(d => d.Id == id) ?? All[0];

    public static FontWeight WeightOf(string weight) => weight switch
    {
        "light" => FontWeights.Light,
        "bold" => FontWeights.Bold,
        "black" => FontWeights.Black,
        _ => FontWeights.Normal,
    };
}
