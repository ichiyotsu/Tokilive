package com.ichiyotsu.tokilive

import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.ripple
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import dev.chrisbanes.haze.HazeState

/**
 * 按压缩放：幅度 0.92，动画规格不写死弹簧常数，直接读当前主题的
 * MotionScheme.fastSpatialSpec() —— 切换 expressive/standard 或开减少
 * 动效时，全 App 的按压手感随主题一起变。
 */
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun Modifier.pressScale(
    interactionSource: MutableInteractionSource = remember { MutableInteractionSource() },
    scale: Float = 0.92f,
): Modifier {
    val pressed by interactionSource.collectIsPressedAsState()
    val s by animateFloatAsState(
        targetValue = if (pressed) scale else 1f,
        animationSpec = MaterialTheme.motionScheme.fastSpatialSpec(),
        label = "pressScale",
    )
    return this.graphicsLayer {
        scaleX = s
        scaleY = s
        clip = true
    }
}

/** 分组标题：titleLarge 粗体 + 可选的 bodySmall 说明。 */
@Composable
fun SectionTitle(title: String, note: String? = null) {
    Column(Modifier.fillMaxWidth().padding(top = TokiSpacing.xl, bottom = TokiSpacing.sm)) {
        Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
        if (note != null) Text(note, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/**
 * 全 App 唯一的顶栏：
 * 标题只用一个文字角色（headlineMedium、粗体），返回键是 56dp 圆 chip。
 * 返回键与标题默认就带同一块磨砂（或实色退路），不随滚动出现或消失；
 * 两者间距固定 12dp。能采样时色块只做与底栏相同的磨砂，标题直接放在
 * 磨砂上，不再叠实色。不能采样时是不透明表面加与实心底栏同粗的描边。
 * 字始终用 onSurface。
 */
@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun OverlayTitleHeader(
    title: String,
    onBack: (() -> Unit)?,
    onHeightMeasured: (Dp) -> Unit,
    backdropBlur: Boolean,
    hazeState: HazeState,
    modifier: Modifier = Modifier,
) {
    val density = LocalDensity.current
    val content = MaterialTheme.colorScheme.onSurface
    val titleShape = RoundedCornerShape(percent = 50)
    Row(
        modifier
            .fillMaxWidth()
            .onSizeChanged { onHeightMeasured(with(density) { it.height.toDp() }) }
            .statusBarsPadding()
            .heightIn(min = 64.dp)
            .padding(horizontal = TokiSpacing.pageMargin),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (onBack != null) {
            TokiScrollBackChip(
                backdropBlur = backdropBlur,
                hazeState = hazeState,
                onClick = onBack,
            )
            Spacer(Modifier.width(TokiSpacing.md))
        }
        Box(
            Modifier
                .weight(1f, fill = false)
                .heightIn(min = 56.dp)
                .clip(titleShape),
            contentAlignment = Alignment.CenterStart,
        ) {
            TokiScrollChipBackdrop(
                shape = titleShape,
                backdropBlur = backdropBlur,
                hazeState = hazeState,
                modifier = Modifier.matchParentSize(),
            )
            Text(
                title,
                modifier = Modifier.padding(horizontal = TokiSpacing.titleGlyphInset),
                style = MaterialTheme.typography.headlineMedium,
                fontWeight = FontWeight.Bold,
                color = content,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

/** 返回键自己的大圆角色块，高度和标题胶囊对齐。默认就带磨砂（与标题同一套），不随滚动变化。 */
@Composable
fun TokiScrollBackChip(
    backdropBlur: Boolean,
    hazeState: HazeState,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val interaction = remember { MutableInteractionSource() }
    val content = MaterialTheme.colorScheme.onSurface
    Box(
        modifier
            .size(56.dp)
            .clip(CircleShape)
            .clickable(interactionSource = interaction, indication = ripple()) { onClick() }
            .pressScale(interaction),
        contentAlignment = Alignment.Center,
    ) {
        TokiScrollChipBackdrop(
            shape = CircleShape,
            backdropBlur = backdropBlur,
            hazeState = hazeState,
            modifier = Modifier.matchParentSize(),
        )
        Symbol("arrow_back", size = 24.dp, tint = content)
    }
}
