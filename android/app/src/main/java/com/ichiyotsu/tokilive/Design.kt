package com.ichiyotsu.tokilive

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Shapes
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * 设计数据：间距与形状。数值与 TokiStudio 0.1.10（TokiUI 设计数据层）
 * 同源 —— 其他 Toki App 复制这一层调用，见 TokiUI 的 DESIGN.md。
 */

/** MD3E 官方形状阶梯（material3 ShapeTokens，1.5.0）。 */
val TokiDesignShapes: Shapes = Shapes()

/**
 * 卡片形状：全 App 定死一档 —— 形状阶梯的 extraLarge（28dp 对称大圆角矩形）。
 * 功能界面不使用非对称圆角。
 */
val TokiCardShape: Shape = TokiDesignShapes.extraLarge

/** 间距：M3 的 4dp 基线网格阶梯；紧凑窗口的页面水平边距为 24dp。 */
object TokiSpacing {
    val xs: Dp = 4.dp
    val sm: Dp = 8.dp
    val md: Dp = 12.dp
    val lg: Dp = 16.dp
    val xl: Dp = 24.dp
    val xxl: Dp = 32.dp

    /** 页面水平边距（紧凑窗口 margin）：24dp。 */
    val pageMargin: Dp = xl

    /**
     * 大标题字形相对页面边距再往里的距离。滚动标题色块的文字内边距用它，
     * 这样标题和下面的正文左缘是同一条线。
     */
    val titleGlyphInset: Dp = lg

    /** 悬浮导航下方留给内容的安全间距（在实测底栏高度之外追加）。 */
    val contentGap: Dp = 16.dp

    /** 窗口宽度达到此值时，主导航从悬浮胶囊换成 NavigationRail。 */
    val expandedWindow: Dp = 840.dp
}
