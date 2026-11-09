package com.ichiyotsu.tokilive

import android.provider.Settings
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.spring
import androidx.compose.animation.expandHorizontally
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemGestures
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.ripple
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.IntSize
import androidx.compose.ui.unit.dp
import dev.chrisbanes.haze.HazeState
import dev.chrisbanes.haze.hazeEffect
import dev.chrisbanes.haze.materials.ExperimentalHazeMaterialsApi
import dev.chrisbanes.haze.materials.HazeMaterials

/**
 * 底部导航按 Material 3 Expressive 的选中展开来做：未选中只有图标，选中项用弹簧展开出文字，
 * 操作按钮脱离胶囊，圆角 18dp。手势条和导航条谁更高就避让谁。
 * 视觉关系参考用户自己的 TokiNote 浮动底栏。TokiNote 源码是 GPL-3.0，这里是重新写的，没有复制它的源码。
 */
@OptIn(ExperimentalHazeMaterialsApi::class)
@Composable
fun ExpressiveBottomBar(
    selected: Int,
    onSelect: (Int) -> Unit,
    actionDescription: String,
    onAction: () -> Unit,
    actionIcon: String,
    haze: HazeState,
    modifier: Modifier = Modifier,
) {
    val entries = listOf("forum" to "弹幕", "monitoring" to "数据", "settings" to "设置")
    val density = LocalDensity.current
    val insetPx = maxOf(WindowInsets.navigationBars.getBottom(density), WindowInsets.systemGestures.getBottom(density))
    val inset = with(density) { insetPx.toDp() } + 12.dp
    val reduce = rememberAnimatorScale() == 0f
    Row(
        modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = inset).semantics { contentDescription = "主导航" },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp, Alignment.CenterHorizontally),
    ) {
        Row(
            Modifier
                .animateContentSize(if (reduce) snap() else spring(dampingRatio = 0.72f, stiffness = 380f))
                .height(64.dp)
                .clip(RoundedCornerShape(28.dp))
                .hazeEffect(haze, HazeMaterials.thin(MaterialTheme.colorScheme.surfaceContainer))
                .background(MaterialTheme.colorScheme.surfaceContainer.copy(alpha = 0.72f))
                .padding(horizontal = 6.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            entries.forEachIndexed { index, (icon, label) ->
                ExpressiveNavItem(icon, label, index == selected, reduce) { onSelect(index) }
            }
        }
        ExpressiveAction(actionIcon, actionDescription, onAction)
    }
}

@Composable
private fun rememberAnimatorScale(): Float {
    val view = LocalView.current
    return remember(view) {
        runCatching {
            Settings.Global.getFloat(view.context.contentResolver, Settings.Global.ANIMATOR_DURATION_SCALE, 1f)
        }.getOrDefault(1f)
    }
}

@Composable
private fun Modifier.pressScale(interaction: MutableInteractionSource, reduce: Boolean): Modifier {
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (!reduce && pressed) 0.92f else 1f, spring(dampingRatio = 0.82f, stiffness = 380f), label = "press")
    return graphicsLayer { scaleX = scale; scaleY = scale }
}

@Composable
private fun ExpressiveNavItem(icon: String, label: String, selected: Boolean, reduce: Boolean, onClick: () -> Unit) {
    val interaction = remember { MutableInteractionSource() }
    val haptic = LocalHapticFeedback.current
    val fade = if (reduce) snap<Float>() else spring<Float>(dampingRatio = 0.6f, stiffness = 440f)
    val grow = if (reduce) snap<IntSize>() else spring(dampingRatio = 0.6f, stiffness = 440f)
    Row(
        Modifier
            .height(52.dp)
            .clip(RoundedCornerShape(24.dp))
            .background(if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surface.copy(alpha = 0f))
            .clickable(interaction, ripple(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.28f))) {
                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                onClick()
            }
            .pressScale(interaction, reduce)
            .padding(horizontal = 14.dp)
            .semantics { role = Role.Tab; this.selected = selected; contentDescription = label },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Symbol(icon, filled = selected, size = 22.dp, tint = if (selected) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurfaceVariant)
        AnimatedVisibility(visible = selected, enter = expandHorizontally(grow) + fadeIn(fade), exit = shrinkHorizontally(grow) + fadeOut(fade)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.width(8.dp))
                Text(label, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onPrimary, maxLines = 1)
            }
        }
    }
}

/**
 * 宽屏（≥840dp，M3 expanded）用侧栏，而不是把手机底栏拉宽。
 * 选中项用 16dp 圆角的主色块，和未选中的图标形成形状对比，对应 MD3 Expressive 的形状策略。
 * 窄屏仍用 [ExpressiveBottomBar]。来源：m3.material.io navigation rail / navigation bar，以及 Apache-2.0 的 md3e-skill 设计策略说明，没有复制其模板代码。
 */
@Composable
fun ExpressiveNavRail(
    selected: Int,
    onSelect: (Int) -> Unit,
    actionDescription: String,
    onAction: () -> Unit,
    actionIcon: String,
    modifier: Modifier = Modifier,
) {
    val entries = listOf("forum" to "弹幕", "monitoring" to "数据", "settings" to "设置")
    Column(
        modifier.width(96.dp).fillMaxSize().padding(vertical = 12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        entries.forEachIndexed { index, (icon, label) ->
            val on = index == selected
            Column(
                Modifier
                    .height(64.dp)
                    .fillMaxWidth()
                    .padding(horizontal = 8.dp)
                    .clip(RoundedCornerShape(if (on) 16.dp else 8.dp))
                    .background(if (on) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface)
                    .clickable { onSelect(index) }
                    .semantics { role = androidx.compose.ui.semantics.Role.Tab; this.selected = on; contentDescription = label },
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Symbol(icon, filled = on, size = 22.dp)
                Text(label, style = MaterialTheme.typography.labelMedium, maxLines = 1)
            }
        }
        Spacer(Modifier.weight(1f))
        ExpressiveAction(actionIcon, actionDescription, onAction)
    }
}

@Composable
private fun ExpressiveAction(icon: String, description: String, onClick: () -> Unit) {
    Surface(
        onClick = onClick,
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.primary,
        contentColor = MaterialTheme.colorScheme.onPrimary,
        modifier = Modifier.size(64.dp).semantics { contentDescription = description },
    ) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
            Symbol(icon, filled = true, size = 28.dp, tint = MaterialTheme.colorScheme.onPrimary)
        }
    }
}
