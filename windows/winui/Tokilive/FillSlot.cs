using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tokilive;

/// <summary>
/// NavigationView 内容槽里的 ContentPresenter 用的是默认样式：
/// HorizontalAlignment=Left、VerticalAlignment=Top。它按子元素的期望尺寸
/// 排在左上角，不会拉开占满内容区。设置页因此缩成左上角一小块，旁边和下
/// 面是大片空白；窗口拉宽后期望尺寸和视口对不上，标题被裁掉、星号列也不
/// 跟着变。这里在测量时把期望尺寸报成整个内容槽，子页面在排列时铺满，
/// 滚动条才有真实视口。
/// </summary>
public sealed class FillSlot : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        var constraint = new Size(
            double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width,
            double.IsInfinity(available.Height) ? double.PositiveInfinity : available.Height);
        double w = 0, h = 0;
        foreach (var child in Children)
        {
            child.Measure(constraint);
            if (child.Visibility == Visibility.Collapsed) continue;
            w = Math.Max(w, child.DesiredSize.Width);
            h = Math.Max(h, child.DesiredSize.Height);
        }
        return new Size(
            double.IsInfinity(available.Width) ? w : available.Width,
            double.IsInfinity(available.Height) ? h : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var rect = new Rect(0, 0, final.Width, final.Height);
        foreach (var child in Children)
            child.Arrange(rect);
        return final;
    }
}
