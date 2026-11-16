using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WindowsBrush = Windows.UI.Composition.CompositionColorBrush;

namespace Tokilive;

/// <summary>
/// WinUI 的窗口默认会铺一层不透明的主题底。悬浮窗要像 1.0.0 的 Electron
/// 窗口那样真透明（只看见卡片），就把系统底刷成全透明，再配合
/// DwmExtendFrameIntoClientArea。不要再加 WS_EX_LAYERED：WinUI 的交换链
/// 不会走 UpdateLayeredWindow，分层窗口会整块涂黑。
/// ICompositionSupportsSystemBackdrop.SystemBackdrop 要的是
/// Windows.UI.Composition 的画刷，不是 Microsoft.UI.Composition 那份。
/// </summary>
sealed class TransparentBackdrop : SystemBackdrop
{
    WindowsBrush? _brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        try
        {
            var compositor = new Windows.UI.Composition.Compositor();
            _brush = compositor.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
            connectedTarget.SystemBackdrop = _brush;
        }
        catch
        {
            // 透明底设不上时窗口仍能显示。启动冒烟测试不能因为这里失败而退出。
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try { disconnectedTarget.SystemBackdrop = null; } catch { /* 窗口正在关 */ }
        _brush?.Dispose();
        _brush = null;
    }
}
