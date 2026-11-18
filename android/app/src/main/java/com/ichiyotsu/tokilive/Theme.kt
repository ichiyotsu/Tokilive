package com.ichiyotsu.tokilive

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.ExperimentalTextApi
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.*
import androidx.compose.ui.unit.Density
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.graphics.shapes.Morph
import androidx.graphics.shapes.RoundedPolygon
import androidx.graphics.shapes.toPath

val GoogleSans = FontFamily(Font(R.font.google_sans_400, FontWeight.Normal), Font(R.font.google_sans_500, FontWeight.Medium), Font(R.font.google_sans_700, FontWeight.Bold))

/** 是否处于深色（玻璃罩色等需要知道时读）。 */
val LocalDarkTheme = staticCompositionLocalOf { false }

/**
 * 字重只用两档：标题、标签用 FontWeight.Bold（wght 700）；正文用
 * FontWeight.Normal（wght 400）。层级靠字号和行距，不再在 regular 和
 * medium 之间混一档。与 TokiStudio 0.1.10 同一套规则。
 */
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
private fun Typography.withFont(f: FontFamily): Typography {
    fun TextStyle.bold() = copy(fontFamily = f, fontWeight = FontWeight.Bold)
    fun TextStyle.regular() = copy(fontFamily = f, fontWeight = FontWeight.Normal)
    return Typography(
        displayLarge = displayLarge.bold(),
        displayMedium = displayMedium.bold(),
        displaySmall = displaySmall.bold(),
        headlineLarge = headlineLarge.bold(),
        headlineMedium = headlineMedium.bold(),
        headlineSmall = headlineSmall.bold(),
        titleLarge = titleLarge.bold(),
        titleMedium = titleMedium.bold(),
        titleSmall = titleSmall.bold(),
        bodyLarge = bodyLarge.regular(),
        bodyMedium = bodyMedium.regular(),
        bodySmall = bodySmall.regular(),
        labelLarge = labelLarge.bold(),
        labelMedium = labelMedium.bold(),
        labelSmall = labelSmall.bold(),
        displayLargeEmphasized = displayLargeEmphasized.bold(),
        displayMediumEmphasized = displayMediumEmphasized.bold(),
        displaySmallEmphasized = displaySmallEmphasized.bold(),
        headlineLargeEmphasized = headlineLargeEmphasized.bold(),
        headlineMediumEmphasized = headlineMediumEmphasized.bold(),
        headlineSmallEmphasized = headlineSmallEmphasized.bold(),
        titleLargeEmphasized = titleLargeEmphasized.bold(),
        titleMediumEmphasized = titleMediumEmphasized.bold(),
        titleSmallEmphasized = titleSmallEmphasized.bold(),
        bodyLargeEmphasized = bodyLargeEmphasized.bold(),
        bodyMediumEmphasized = bodyMediumEmphasized.bold(),
        bodySmallEmphasized = bodySmallEmphasized.bold(),
        labelLargeEmphasized = labelLargeEmphasized.bold(),
        labelMediumEmphasized = labelMediumEmphasized.bold(),
        labelSmallEmphasized = labelSmallEmphasized.bold())
}

/**
 * 深色底色对比微调：背景与 surface 压暗 8%，surfaceContainer 阶梯从
 * 最低档的 3% 起、每档多抬一点、最高档 9%。叠黑/叠白只改变明度，
 * 色相与饱和度保持官方方案的原样。浅色模式不做任何改动。
 */
private fun ColorScheme.withDarkContrast(): ColorScheme {
    fun Color.darken(alpha: Float): Color = Color.Black.copy(alpha = alpha).compositeOver(this)
    fun Color.lighten(alpha: Float): Color = Color.White.copy(alpha = alpha).compositeOver(this)
    return copy(
        background = background.darken(0.08f),
        surface = surface.darken(0.08f),
        surfaceDim = surfaceDim.darken(0.08f),
        surfaceContainerLowest = surfaceContainerLowest.lighten(0.03f),
        surfaceContainerLow = surfaceContainerLow.lighten(0.04f),
        surfaceContainer = surfaceContainer.lighten(0.05f),
        surfaceContainerHigh = surfaceContainerHigh.lighten(0.07f),
        surfaceContainerHighest = surfaceContainerHighest.lighten(0.09f),
    )
}

/**
 * 全 App 页面底色：surface 家族内部的纵向台阶 —— surfaceContainer
 * → surface → background 的同色系内部过渡；深色模式同一套 role 自动是
 * 深色系内部过渡。深色下台阶的对比由 [ColorScheme.withDarkContrast]
 * 统一抬起，这里不另写数值。
 */
@Composable
fun tokiPageBackground(): Brush {
    val scheme = MaterialTheme.colorScheme
    return Brush.verticalGradient(listOf(scheme.surfaceContainer, scheme.surface, scheme.background))
}

/** Material 3 Expressive 主题：动态取色 + 表现力动效 + Google Sans */
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun TokiTheme(dark: Boolean = isSystemInDarkTheme(), dynamic: Boolean = true, content: @Composable () -> Unit) {
    val ctx = LocalContext.current
    val base = when {
        dynamic -> if (dark) dynamicDarkColorScheme(ctx) else dynamicLightColorScheme(ctx)
        dark -> darkColorScheme(primary = Color(0xFFD0BCFF), onPrimary = Color(0xFF381E72), primaryContainer = Color(0xFF4F378B), onPrimaryContainer = Color(0xFFEADDFF),
            secondaryContainer = Color(0xFF4A4458), onSecondaryContainer = Color(0xFFE8DEF8), tertiaryContainer = Color(0xFF633B48), onTertiaryContainer = Color(0xFFFFD8E4),
            surface = Color(0xFF141218), surfaceContainer = Color(0xFF211F26), surfaceContainerHigh = Color(0xFF2B2930), surfaceContainerHighest = Color(0xFF36343B), surfaceContainerLow = Color(0xFF1D1B20))
        else -> lightColorScheme(primary = Color(0xFF6750A4), primaryContainer = Color(0xFFEADDFF), onPrimaryContainer = Color(0xFF21005D), secondaryContainer = Color(0xFFE8DEF8),
            tertiaryContainer = Color(0xFFFFD8E4), onTertiaryContainer = Color(0xFF31111D), surface = Color(0xFFFEF7FF), surfaceContainer = Color(0xFFF3EDF7),
            surfaceContainerHigh = Color(0xFFECE6F0), surfaceContainerHighest = Color(0xFFE6E0E9), surfaceContainerLow = Color(0xFFF7F2FA))
    }
    val scheme = if (dark) base.withDarkContrast() else base
    // MaterialExpressiveTheme 不提供 LocalContentColor：不补这一层，
    // 无显式颜色的 Text 会默认黑色，深色下看不见。
    CompositionLocalProvider(LocalDarkTheme provides dark, LocalContentColor provides scheme.onBackground) {
        MaterialExpressiveTheme(colorScheme = scheme, motionScheme = MotionScheme.expressive(), shapes = TokiDesignShapes, typography = Typography().withFont(GoogleSans), content = content)
    }
}

/** Material Symbols Rounded（可变字体，FILL 轴随选中状态动画） */
@OptIn(ExperimentalTextApi::class)
private val symbolFonts = HashMap<Int, FontFamily>()
@OptIn(ExperimentalTextApi::class)
private fun symbolFont(fill: Int, wght: Int) = symbolFonts.getOrPut(fill * 1000 + wght) {
    FontFamily(Font(R.font.material_symbols, variationSettings = FontVariation.Settings(FontVariation.Setting("FILL", fill.toFloat()), FontVariation.weight(wght))))
}
@Composable
fun Symbol(name: String, modifier: Modifier = Modifier, filled: Boolean = false, size: Dp = 24.dp, tint: Color = LocalContentColor.current, weight: Int = 400) {
    val f by animateFloatAsState(if (filled) 1f else 0f, label = "fill")
    Text(name, modifier, color = tint, fontSize = size.value.sp, lineHeight = size.value.sp, fontFamily = symbolFont(if (f > .5f) 1 else 0, weight))
}

/** 形状变形：两个 MaterialShapes 多边形之间插值 */
class MorphShape(private val morph: Morph, private val progress: Float, private val rotation: Float = 0f) : Shape {
    private val m = android.graphics.Matrix()
    override fun createOutline(size: Size, layoutDirection: LayoutDirection, density: Density): Outline {
        val p = morph.toPath(progress, android.graphics.Path())
        m.reset(); m.setScale(size.width, size.height); m.postRotate(rotation, size.width / 2, size.height / 2); p.transform(m)
        return Outline.Generic(p.asComposePath())
    }
}
fun morphOf(a: RoundedPolygon, b: RoundedPolygon) = Morph(a, b)
