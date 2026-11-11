using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Tokilive;

public sealed partial class OverlayWindow : Window
{
    public enum Kind { Danmaku, Live, Lyrics }
    readonly Kind _kind;
    public static bool DarkText = true;

    public OverlayWindow(Kind kind)
    {
        _kind = kind;
        InitializeComponent();
        var title = kind switch { Kind.Live => "tokilive · 实时窗", Kind.Lyrics => "tokilive · 歌词", _ => "tokilive · 弹幕" };
        NativeWin.ApplyOverlay(this, title, ClickThrough, resizable: kind != Kind.Lyrics, noActivate: true);
        AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(80 + (int)kind * 40, 80, kind == Kind.Lyrics ? 760 : 420, kind == Kind.Live ? 160 : 280));
        Closed += (_, _) => { };
        Render();
    }

    bool ClickThrough => _kind switch
    {
        Kind.Lyrics => AppState.Current.LyricClickThrough,
        _ => AppState.Current.ClickThrough
    };

    public void ApplyChrome()
    {
        var locked = _kind == Kind.Lyrics && AppState.Current.LyricLocked;
        NativeWin.ApplyOverlay(this, AppWindow.Title, ClickThrough || locked, resizable: !locked && _kind != Kind.Lyrics, noActivate: true);
        var border = _kind == Kind.Live ? AppState.Current.ShowLiveBorder : _kind == Kind.Danmaku && AppState.Current.ShowWindowBorder;
        Root.BorderBrush = border ? new SolidColorBrush(Parse(AppState.Current.UseSystemAccent ? NativeWin.SystemAccent() ?? AppState.Current.AccentHex : AppState.Current.AccentHex)) : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Root.BorderThickness = border ? new Thickness(2) : new Thickness(0);
    }

    public void Render()
    {
        Host.Children.Clear();
        var state = AppState.Current;
        if (_kind == Kind.Danmaku)
        {
            var lines = LiveService.Lines.TakeLast(8).ToList();
            if (lines.Count == 0) Host.Children.Add(Card("等待弹幕", "连接后会显示在这里", state));
            foreach (var line in lines) Host.Children.Add(Card(line.Name, line.Text, state));
        }
        else if (_kind == Kind.Live)
        {
            Host.Children.Add(Card("实时", $"点赞 {LiveService.Likes}   弹幕 {LiveService.Danmaku}   在线 {LiveService.Online}\n{LiveService.Watched}", state));
        }
        else
        {
            var now = LyricsService.Current;
            var shown = now.Lines.TakeLast(Math.Max(1, state.LyricLines)).ToList();
            if (shown.Count == 0) shown.Add(new LyricLine { Text = string.IsNullOrEmpty(now.Current) ? now.Label : now.Current });
            foreach (var line in shown.TakeLast(state.LyricLines))
            {
                var current = line.Text == now.Current || shown.Count == 1;
                Host.Children.Add(Lyric(current ? now.Current.Length > 0 ? now.Current : line.Text : line.Text, current, state));
            }
        }
    }

    public static Border Card(string name, string text, AppState state)
    {
        var ink = DarkText ? Parse("#1A1A1A") : Parse("#FFFFFF");
        if (!state.AdaptiveText) ink = Parse(state.TextColor);
        var panel = new StackPanel { Spacing = 2 };
        if (state.ShowAvatar)
            panel.Children.Add(new TextBlock { Text = name, Foreground = new SolidColorBrush(Parse(state.NameColor)), FontSize = Math.Max(12, state.FontSize - 2) });
        panel.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(ink), FontSize = state.FontSize, TextWrapping = TextWrapping.Wrap });
        var color = Parse(state.CardColor);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(state.CardOpacity * 255), color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(state.Corner),
            Padding = new Thickness(12, 8, 12, 8),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    static TextBlock Lyric(string text, bool current, AppState state)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(state.LyricFont),
            FontSize = current ? state.LyricSize : Math.Max(14, state.LyricSize - 8),
            Foreground = new SolidColorBrush(Parse(current ? state.LyricHighlight : state.LyricColor)),
            TextAlignment = state.LyricAlign == "left" ? TextAlignment.Left : state.LyricAlign == "right" ? TextAlignment.Right : TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        if (state.LyricShadow)
            block.Shadow = new ThemeShadow();
        return block;
    }

    public static Color Parse(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var n))
            return Color.FromArgb(255, 26, 26, 26);
        return Color.FromArgb(255, (byte)(n >> 16), (byte)(n >> 8), (byte)n);
    }
}
