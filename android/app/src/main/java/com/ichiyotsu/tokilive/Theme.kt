package com.ichiyotsu.tokilive

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.*
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

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
private fun Typography.withFont(f: FontFamily): Typography {
    fun TextStyle.g() = copy(fontFamily = f)
    return Typography(
        displayLarge = displayLarge.g(),
        displayMedium = displayMedium.g(),
        displaySmall = displaySmall.g(),
        headlineLarge = headlineLarge.g(),
        headlineMedium = headlineMedium.g(),
        headlineSmall = headlineSmall.g(),
        titleLarge = titleLarge.g(),
        titleMedium = titleMedium.g(),
        titleSmall = titleSmall.g(),
        bodyLarge = bodyLarge.g(),
        bodyMedium = bodyMedium.g(),
        bodySmall = bodySmall.g(),
        labelLarge = labelLarge.g(),
        labelMedium = labelMedium.g(),
        labelSmall = labelSmall.g(),
        displayLargeEmphasized = displayLargeEmphasized.g(),
        displayMediumEmphasized = displayMediumEmphasized.g(),
        displaySmallEmphasized = displaySmallEmphasized.g(),
        headlineLargeEmphasized = headlineLargeEmphasized.g(),
        headlineMediumEmphasized = headlineMediumEmphasized.g(),
        headlineSmallEmphasized = headlineSmallEmphasized.g(),
        titleLargeEmphasized = titleLargeEmphasized.g(),
        titleMediumEmphasized = titleMediumEmphasized.g(),
        titleSmallEmphasized = titleSmallEmphasized.g(),
        bodyLargeEmphasized = bodyLargeEmphasized.g(),
        bodyMediumEmphasized = bodyMediumEmphasized.g(),
        bodySmallEmphasized = bodySmallEmphasized.g(),
        labelLargeEmphasized = labelLargeEmphasized.g(),
        labelMediumEmphasized = labelMediumEmphasized.g(),
        labelSmallEmphasized = labelSmallEmphasized.g())
}

/** Material 3 Expressive 主题：动态取色 + 表现力动效 + Google Sans */
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun TokiTheme(dark: Boolean = isSystemInDarkTheme(), dynamic: Boolean = true, content: @Composable () -> Unit) {
    val ctx = LocalContext.current
    val scheme = when {
        dynamic -> if (dark) dynamicDarkColorScheme(ctx) else dynamicLightColorScheme(ctx)
        dark -> darkColorScheme(primary = Color(0xFFD0BCFF), onPrimary = Color(0xFF381E72), primaryContainer = Color(0xFF4F378B), onPrimaryContainer = Color(0xFFEADDFF),
            secondaryContainer = Color(0xFF4A4458), onSecondaryContainer = Color(0xFFE8DEF8), tertiaryContainer = Color(0xFF633B48), onTertiaryContainer = Color(0xFFFFD8E4),
            surface = Color(0xFF141218), surfaceContainer = Color(0xFF211F26), surfaceContainerHigh = Color(0xFF2B2930), surfaceContainerHighest = Color(0xFF36343B), surfaceContainerLow = Color(0xFF1D1B20))
        else -> lightColorScheme(primary = Color(0xFF6750A4), primaryContainer = Color(0xFFEADDFF), onPrimaryContainer = Color(0xFF21005D), secondaryContainer = Color(0xFFE8DEF8),
            tertiaryContainer = Color(0xFFFFD8E4), onTertiaryContainer = Color(0xFF31111D), surface = Color(0xFFFEF7FF), surfaceContainer = Color(0xFFF3EDF7),
            surfaceContainerHigh = Color(0xFFECE6F0), surfaceContainerHighest = Color(0xFFE6E0E9), surfaceContainerLow = Color(0xFFF7F2FA))
    }
    MaterialExpressiveTheme(colorScheme = scheme, motionScheme = MotionScheme.expressive(), typography = Typography().withFont(GoogleSans), content = content)
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
