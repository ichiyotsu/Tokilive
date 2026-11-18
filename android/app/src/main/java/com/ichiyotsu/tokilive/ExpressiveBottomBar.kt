package com.ichiyotsu.tokilive

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.expandHorizontally
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkHorizontally
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.requiredSize
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.ExperimentalMaterial3ExpressiveApi
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationRail
import androidx.compose.material3.NavigationRailItem
import androidx.compose.material3.Text
import androidx.compose.material3.ripple
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.clipRect
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

/**
 * 悬浮导航栏：与 TokiStudio 0.1.10 同一套（单层毛玻璃胶囊 + 条目 56dp +
 * 选中实色 pill 标签弹簧展开）：
 *
 * - 颜色：全部取当前 MD3E ColorScheme 的 roles —— 选中 pill = primary /
 *   onPrimary，未选中图标 = onSurfaceVariant，涟漪 = primary 28%；
 * - 弹簧：不写死数值，全部映射到 MaterialTheme.motionScheme 的槽位；
 * - 形状：胶囊 = MD3E 的 full 角（50% 圆角）；条目 pill = CircleShape；
 * - 玻璃：只挂一层调用方给的 [glass]（内容源采样的 chrome —— 胶囊宽度
 *   随标签动，平台窗口背景采样在尺寸动画里边缘会发黑）。磨砂打开时
 *   胶囊不加粗描边；[solidStroke] 为真（玻璃关闭或采样不可用）时，外沿
 *   加一道与滚动标题色块同粗的强调色描边（莫奈悬浮底栏）；
 * - 高度：条目 56dp + 胶囊内边距。磨砂时上下各 8dp，总高 72dp。
 */
internal val BottomBarCapsuleHeight = 72.dp

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun ExpressiveBottomBar(
    selected: Int,
    onSelect: (Int) -> Unit,
    glass: Modifier,
    modifier: Modifier = Modifier,
    solidStroke: Boolean = false,
) {
    val entries = listOf("forum" to "弹幕", "monitoring" to "数据", "settings" to "设置")
    val shape = RoundedCornerShape(percent = 50)
    val inset = if (solidStroke) TokiChromeStroke.width + TokiChromeStroke.barGap else 8.dp
    // 尺寸动画与裁剪分离：胶囊的玻璃跟动画尺寸走（裁成胶囊形），
    // 条目行是撑起胶囊的唯一普通子项——matchParentSize 的子项不参与
    // Box 尺寸测量，两个子项都挂它会把胶囊压成 0（TokiStudio 0.1.7
    // 底栏消失的原因）。标签展开用快弹簧，收起用默认弹簧，切换时新旧
    // 两个标签同时在场，内容瞬时宽度会超过胶囊的动画宽度。裁剪护栏挂
    // 在条目行上：以行宽为界，超调只允许伸进与侧钮之间的 8dp 缝隙，
    // 再往外切掉——否则内容滑到侧钮的玻璃底下，右边看起来缺一块。
    // 护栏不挂在外层 Box 上：玻璃盒子不能被动画中的裁剪矩形切到，
    // 否则胶囊边缘在动画里会发黑。
    Box(
        modifier = modifier
            .animateContentSize(animationSpec = MaterialTheme.motionScheme.defaultSpatialSpec())
            .semantics { contentDescription = "主导航" },
        contentAlignment = Alignment.Center,
    ) {
        Box(
            Modifier
                .matchParentSize()
                .clip(shape)
                .then(glass)
                .then(
                    if (solidStroke) {
                        Modifier.border(TokiChromeStroke.width, MaterialTheme.colorScheme.primary, shape)
                    } else {
                        Modifier
                    },
                ),
        )
        Row(
            modifier = Modifier
                .padding(horizontal = inset, vertical = inset)
                .drawWithContent {
                    val slack = 8.dp.toPx()
                    clipRect(left = -slack, top = 0f, right = size.width + slack, bottom = size.height) {
                        this@drawWithContent.drawContent()
                    }
                },
            verticalAlignment = Alignment.CenterVertically,
        ) {
            entries.forEachIndexed { index, (icon, label) ->
                ExpressiveNavItem(icon, label, index == selected) { onSelect(index) }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
private fun ExpressiveNavItem(icon: String, label: String, selected: Boolean, onClick: () -> Unit) {
    val interaction = remember { MutableInteractionSource() }
    val haptic = LocalHapticFeedback.current
    val scheme = MaterialTheme.colorScheme
    TokiLongPressLabel(label) {
    Row(
        modifier = Modifier
            .height(56.dp)
            .clip(CircleShape)
            .background(if (selected) scheme.primary else Color.Transparent)
            .clickable(
                interactionSource = interaction,
                indication = ripple(color = scheme.primary.copy(alpha = 0.28f)),
                onClick = {
                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                    onClick()
                },
            )
            .pressScale(interaction)
            .padding(horizontal = 16.dp)
            .semantics { role = Role.Tab; this.selected = selected; contentDescription = label },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Symbol(icon, filled = selected, size = 22.dp, tint = if (selected) scheme.onPrimary else scheme.onSurfaceVariant)
        AnimatedVisibility(
            visible = selected,
            enter = expandHorizontally(animationSpec = MaterialTheme.motionScheme.fastSpatialSpec()) +
                fadeIn(animationSpec = MaterialTheme.motionScheme.defaultEffectsSpec()),
            exit = shrinkHorizontally(animationSpec = MaterialTheme.motionScheme.defaultSpatialSpec()) +
                fadeOut(animationSpec = MaterialTheme.motionScheme.defaultEffectsSpec()),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Spacer(Modifier.width(8.dp))
                Text(label, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, color = scheme.onPrimary, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
    }
}

/**
 * 底栏旁的独立圆形玻璃按钮：胶囊外右侧、与胶囊间隔 8dp 的一颗独立
 * 按钮（56dp 圆形玻璃：与胶囊条目同高，玻璃与胶囊同一层）。
 * 玻璃关闭时（莫奈悬浮底栏）：不画玻璃也不描边，整个圆填当前底栏
 * 边框色（primary），图标改 onPrimary。
 */
@Composable
fun ExpressiveAction(
    icon: String,
    description: String,
    onClick: () -> Unit,
    glass: Modifier,
    modifier: Modifier = Modifier,
    solidFill: Boolean = false,
) {
    val interaction = remember { MutableInteractionSource() }
    val haptic = LocalHapticFeedback.current
    // 布局和命中都锁在 56dp 圆里。模糊和按压缩放不能画出这个圆，
    // 否则动画过程中会盖住旁边的胶囊。可见间距仍由外层 8dp 决定。
    TokiLongPressLabel(description) {
    Box(modifier.requiredSize(56.dp).clipToBounds()) {
        Box(
            Modifier
                .fillMaxSize()
                .clip(CircleShape)
                .then(if (solidFill) Modifier.background(MaterialTheme.colorScheme.primary) else glass)
                .clickable(
                    interactionSource = interaction,
                    indication = ripple(color = MaterialTheme.colorScheme.primary.copy(alpha = 0.28f)),
                    onClick = {
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        onClick()
                    },
                )
                .pressScale(interaction)
                .semantics { contentDescription = description },
            contentAlignment = Alignment.Center,
        ) {
            Symbol(icon, filled = true, size = 24.dp, tint = if (solidFill) MaterialTheme.colorScheme.onPrimary else MaterialTheme.colorScheme.onSurface)
        }
    }
    }
}

/**
 * 宽屏（≥840dp，M3 expanded）用标准 M3 NavigationRail + NavigationRailItem，
 * 与 TokiStudio 0.1.10 同一套：栏宽 80dp、容器 surfaceContainer、选中指示为
 * 56×32dp secondaryContainer pill、图标 24dp、独立按钮放在 rail header（顶部）。
 * 窄屏仍用 [ExpressiveBottomBar]。
 */
@Composable
fun ExpressiveNavRail(
    selected: Int,
    onSelect: (Int) -> Unit,
    actionDescription: String,
    onAction: () -> Unit,
    actionIcon: String,
    glass: Modifier,
    modifier: Modifier = Modifier,
    solidFill: Boolean = false,
) {
    val entries = listOf("forum" to "弹幕", "monitoring" to "数据", "settings" to "设置")
    NavigationRail(
        modifier = modifier.fillMaxHeight(),
        containerColor = MaterialTheme.colorScheme.surfaceContainer,
        header = {
            ExpressiveAction(actionIcon, actionDescription, onAction, glass, solidFill = solidFill)
        },
    ) {
        entries.forEachIndexed { index, (icon, label) ->
            NavigationRailItem(
                selected = index == selected,
                onClick = { onSelect(index) },
                icon = { Symbol(icon, filled = index == selected, size = 24.dp) },
                label = { Text(label) },
            )
        }
    }
}
