package com.ichiyotsu.tokilive

import android.os.Build
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import dev.chrisbanes.haze.ExperimentalHazeApi
import dev.chrisbanes.haze.HazeFeatureFlags
import dev.chrisbanes.haze.HazeInput
import dev.chrisbanes.haze.HazePerformanceMode
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.blur.HazeBlurStyle
import dev.chrisbanes.haze.blur.HazeColorEffect
import dev.chrisbanes.haze.blur.hazeBlur

/**
 * 全局统一的玻璃（高斯模糊）参数与修饰符 —— 与 TokiStudio 0.1.10 同一套，
 * 全 App 只有这一处定义模糊，悬浮底栏与顶栏标题胶囊共用。
 *
 * - 模糊半径 **24dp**：落在 20–30dp 区间内。半径越大背景越糊、玻璃上的
 *   文字越容易发虚；24dp 仍有明确的高斯模糊感，同时底栏未选中图标/
 *   文字的边缘保持清楚。
 * - frost：深色仍是一层 20% 白霜，不加黑罩。浅色在白霜下多一层很轻的
 *   暖色暗罩，白霜也略加到 46%，让浅色玻璃比过透的白更沉一点，背后
 *   内容少透一些，但盖不住内容，仍然是玻璃而不是实色条。
 * - fallback（模糊不可用时兜底）：浅色 黑 30%、深色 黑 45%。正常路径不走它。
 * - noise 0.02：极轻的噪点，压掉模糊渐变的色带。
 *
 * 玻璃只画一层：消费方把修饰符挂在已裁好形状的容器上，不再叠加任何
 * 半透明 Surface / background —— 多层叠加正是模糊发闷、文字发虚的
 * 常见根源。选中态等需要强调的元素在玻璃之上另画实色，可读性不依赖
 * 模糊本身。
 */
val LocalHazeState = compositionLocalOf<HazeState?> { null }
val LocalBackdropBlur = compositionLocalOf { true }

/**
 * 窗口内背景采样是否可能成立。
 *
 * Compose [androidx.compose.ui.draw.blur] 和 Haze 的 [HazeInput.Content]
 * 都只模糊控件自己画出来的那一层，不能当作悬浮栏背后的实时模糊。
 * 底栏走 [HazeInput.Backdrop]：先采同一窗口里已经画好的像素，采不到时
 * 再回退到挂了 `hazeSource` 的内容根。底层仍是 API 31 的 RenderEffect。
 * minSdk 31 上条件为真；能力判断仍集中在这一处。
 */
@OptIn(ExperimentalHazeApi::class)
object TokiBlurCapability {
    init {
        // 必须在效果节点挂上之前打开。true 只是让原生窗口背景采样有资格，
        // 设备不支持时 Haze 会改用下面的内容根回退，不会退回 Modifier.blur。
        HazeFeatureFlags.isPlatformBackdropEnabled = true
    }

    val canSampleInWindow: Boolean
        get() = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
}

/**
 * 固定尺寸的玻璃表面（独立按钮等）。
 * 开启且设备能采样时是 Haze 背景模糊；否则是不透明的 surfaceContainer。
 * 尺寸会变的表面（悬浮底栏胶囊、顶栏标题胶囊）走内容源采样，见
 * [tokiContentChrome] 与 [TokiScrollChipBackdrop]。
 */
@Composable
fun tokiChrome(state: HazeState, backdropBlur: Boolean): Modifier {
    return if (backdropBlur && TokiBlurCapability.canSampleInWindow) {
        Modifier.tokiGlass(state).background(MaterialTheme.colorScheme.surfaceContainer.copy(alpha = 0.22f))
    } else {
        Modifier.background(MaterialTheme.colorScheme.surfaceContainer)
    }
}

/**
 * 尺寸会变的表面的 chrome（悬浮底栏胶囊）：与 [tokiChrome] 同一套
 * 处理（磨砂 + 22% 表面色，不可用时实色），只把采样换成内容源
 * （[tokiContentGlass]）——胶囊宽度随标签展开/收起连续变化，平台
 * 窗口背景采样在这种尺寸动画里边缘会发黑，内容源采样不会。
 */
@Composable
fun tokiContentChrome(state: HazeState, backdropBlur: Boolean): Modifier {
    return if (backdropBlur && TokiBlurCapability.canSampleInWindow) {
        Modifier.tokiContentGlass(state).background(MaterialTheme.colorScheme.surfaceContainer.copy(alpha = 0.22f))
    } else {
        Modifier.background(MaterialTheme.colorScheme.surfaceContainer)
    }
}

object TokiGlassParams {
    val blurRadius: Dp = 24.dp
    const val noise: Float = 0.02f

    /**
     * 磨砂罩色。深色维持一层薄白霜，不再加黑罩。浅色先铺一层很轻的暖色暗罩，
     * 再盖略强一点的白霜：比原先 40% 纯白更有存在感，也稍深，但两层加起来
     * 仍然透得见背后的内容。
     */
    fun tints(dark: Boolean): List<HazeColorEffect> = if (dark) {
        listOf(HazeColorEffect.tint(Color.White.copy(alpha = 0.20f)))
    } else {
        listOf(
            HazeColorEffect.tint(Color(0xFF2A2622).copy(alpha = 0.16f)),
            HazeColorEffect.tint(Color.White.copy(alpha = 0.46f)),
        )
    }

    /** 兜底色：模糊这条路径不可用时，退化为这层纯色 tint。 */
    fun fallback(dark: Boolean): Color = Color.Black.copy(alpha = if (dark) 0.45f else 0.30f)
}

/** 全 App 共用的模糊样式。 */
internal fun tokiBlurStyle(dark: Boolean): HazeBlurStyle = HazeBlurStyle {
    blurEnabled(true)
    blurRadius(TokiGlassParams.blurRadius)
    noiseFactor(TokiGlassParams.noise)
    backgroundColor(Color.Transparent)
    colorEffects(TokiGlassParams.tints(dark))
    fallbackColorEffect(HazeColorEffect.tint(TokiGlassParams.fallback(dark)))
}

/**
 * 把当前容器变成一层玻璃：模糊其身后由 [state] 收集的内容（内容根挂
 * `Modifier.hazeSource(state)`）。调用方负责先裁形状（如胶囊的全圆角），
 * 本修饰符不画任何额外的实色背景。
 */
@Composable
fun Modifier.tokiGlass(state: HazeState): Modifier {
    val dark = LocalDarkTheme.current
    return hazeBlur(
        input = HazeInput.Backdrop(state),
        style = tokiBlurStyle(dark),
        // 胶囊面积很小，用全分辨率采样，避免降采样把身后的内容糊成一块平色。
        performanceMode = HazePerformanceMode.Quality,
    )
}

/**
 * 内容源玻璃：与 [tokiGlass] 同一套参数（半径/tint/fallback/noise 一个
 * 不改），只换了输入 —— [HazeInput.Sources] 直接消费 `hazeSource` 录制
 * 的内容根。给尺寸会变的表面用（顶栏标题胶囊、悬浮底栏）：平台窗口
 * 背景采样在图层尺寸连续变化时，边缘会出现黑边或整块消失；
 * 内容源采样由 Haze 自己重绘，不经过平台 RenderEffect 的尺寸动画，
 * 尺寸变化时模糊始终完整。调用方先裁形状，再挂本修饰符。
 */
@Composable
fun Modifier.tokiContentGlass(state: HazeState): Modifier {
    val dark = LocalDarkTheme.current
    return hazeBlur(
        input = HazeInput.Sources(state),
        style = tokiBlurStyle(dark),
        performanceMode = HazePerformanceMode.Quality,
    )
}

/**
 * 实心底栏描边的粗细。玻璃打开时底栏和滚动标题都不加这道描边。
 * 玻璃关闭或采样不可用时，底栏与滚动标题沿用同一道 [width]。
 */
object TokiChromeStroke {
    val width: Dp = 3.dp
    val barGap: Dp = 6.dp
}

/**
 * 大标题 / 返回键的色块：默认就带磨砂，不随滚动出现或消失。
 *
 * 旧实现按各页上报的滚动位置，用一个 cover 动画把磨砂淡入淡出：
 * 上报、动画、透明度图层三处接力，先后在多个版本上出现「滑动后磨砂
 * 消失」。重写后色块不再有任何条件：能采样时始终是与底栏相同的内容
 * 源磨砂（尺寸变化也不黑边），标题由调用方画在这一层上面；不能采样
 * 时改为不透明 `surfaceContainer`，并加上与实心底栏同粗的强调色描边。
 * 磨砂上再铺一层很轻的表面色与一道细描边，让标题在任何背景上都有
 * 足够对比。
 *
 * [hazeState] 与 [backdropBlur] 默认读组合局部变量（App 骨架提供）。
 */
@Composable
fun TokiScrollChipBackdrop(
    shape: Shape,
    backdropBlur: Boolean = LocalBackdropBlur.current,
    modifier: Modifier = Modifier,
    hazeState: HazeState? = LocalHazeState.current,
) {
    val scheme = MaterialTheme.colorScheme
    val frosted = backdropBlur && TokiBlurCapability.canSampleInWindow
    val state = hazeState
    Box(
        modifier
            .clip(shape)
            .then(
                if (frosted && state != null) {
                    Modifier.tokiContentGlass(state)
                        .background(scheme.surfaceContainer.copy(alpha = 0.24f))
                        .border(1.dp, scheme.outlineVariant.copy(alpha = 0.45f), shape)
                } else if (!frosted) {
                    Modifier
                        .background(scheme.surfaceContainer)
                        .border(TokiChromeStroke.width, scheme.primary, shape)
                } else {
                    Modifier.background(scheme.surfaceContainer.copy(alpha = 0.24f))
                },
            ),
    )
}
