package com.ichiyotsu.tokilive

import android.graphics.Bitmap
import androidx.compose.animation.*
import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import coil3.compose.AsyncImage
import com.google.zxing.BarcodeFormat
import com.google.zxing.qrcode.QRCodeWriter
import dev.chrisbanes.haze.hazeSource
import dev.chrisbanes.haze.rememberHazeState

@OptIn(ExperimentalMaterial3ExpressiveApi::class, ExperimentalMaterial3Api::class)
@Composable
fun TokiApp(vm: LiveViewModel, initialTab: Int = 0) {
    var tab by rememberSaveable { mutableIntStateOf(initialTab) }
    val backdropBlur = true
    val frostedChrome = backdropBlur && TokiBlurCapability.canSampleInWindow
    val hazeState = rememberHazeState()
    val glass = tokiChrome(hazeState, backdropBlur)
    // 底栏胶囊的玻璃单独走内容源采样：胶囊宽度随标签展开/收起连续
    // 变化，平台窗口背景采样在尺寸动画里边缘会发黑（见 Glass.kt）。
    val barGlass = tokiContentChrome(hazeState, backdropBlur)
    val density = LocalDensity.current
    val navBarInset = with(density) { WindowInsets.navigationBars.getBottom(density).toDp() }
    var measuredBarTotal by remember { mutableStateOf<Dp?>(null) }
    var topBarHeight by remember { mutableStateOf(0.dp) }
    val barTotal = measuredBarTotal ?: (BottomBarCapsuleHeight + 8.dp + navBarInset)
    val bottomContentPadding: Dp = barTotal + TokiSpacing.contentGap
    val titles = listOf("tokilive", "直播间数据", "设置")
    val actionDescription = if (vm.connected) "断开直播" else "连接或预览"
    val onAction = { if (vm.room.isBlank()) vm.demo() else vm.toggle() }
    val actionIcon = if (vm.connected) "stop" else "play_arrow"

    BoxWithConstraints(Modifier.fillMaxSize()) {
        val expanded = maxWidth >= TokiSpacing.expandedWindow
        val contentBottom = if (expanded) navBarInset + TokiSpacing.contentGap else bottomContentPadding
        Row(Modifier.fillMaxSize()) {
            if (expanded) {
                ExpressiveNavRail(tab, { tab = it }, actionDescription, onAction, actionIcon, glass, solidFill = !frostedChrome)
            }
            Box(Modifier.weight(1f).fillMaxHeight()) {
                CompositionLocalProvider(LocalHazeState provides hazeState, LocalBackdropBlur provides backdropBlur) {
                    Scaffold(
                        contentWindowInsets = WindowInsets(0, 0, 0, 0),
                        topBar = {
                            OverlayTitleHeader(
                                title = titles[tab],
                                onBack = null,
                                onHeightMeasured = { topBarHeight = it },
                                backdropBlur = backdropBlur,
                                hazeState = hazeState,
                            )
                        },
                    ) { _ ->
                        Box(Modifier.fillMaxSize()) {
                            val ms = MaterialTheme.motionScheme
                            val eIn = ms.defaultEffectsSpec<Float>(); val sIn = ms.defaultSpatialSpec<androidx.compose.ui.unit.IntOffset>(); val eOut = ms.fastEffectsSpec<Float>()
                            AnimatedContent(tab, transitionSpec = {
                                (fadeIn(eIn) + slideInHorizontally(sIn) { it / 14 }) togetherWith fadeOut(eOut)
                            }, modifier = Modifier.fillMaxSize().hazeSource(hazeState).background(tokiPageBackground()), label = "tab") { t: Int ->
                                when (t) {
                                    0 -> LiveScreen(vm, topBarHeight, contentBottom)
                                    1 -> StatsScreen(vm, topBarHeight, contentBottom)
                                    else -> SettingsScreen(vm, topBarHeight, contentBottom)
                                }
                            }
                            if (!expanded) {
                                Row(
                                    modifier = Modifier
                                        .align(Alignment.BottomCenter)
                                        .onSizeChanged { size -> measuredBarTotal = with(density) { size.height.toDp() } }
                                        .navigationBarsPadding()
                                        .padding(bottom = 8.dp),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                                    verticalAlignment = Alignment.CenterVertically,
                                ) {
                                    ExpressiveBottomBar(tab, { tab = it }, barGlass, solidStroke = !frostedChrome)
                                    ExpressiveAction(actionIcon, actionDescription, onAction, glass, solidFill = !frostedChrome)
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class, ExperimentalMaterial3Api::class)
@Composable
fun LiveScreen(vm: LiveViewModel, topPadding: Dp, bottomPadding: Dp) {
    val status by vm.client.status.collectAsState()
    val list = rememberLazyListState()
    LaunchedEffect(vm.messages.size) { if (vm.messages.isNotEmpty()) list.animateScrollToItem(vm.messages.size - 1) }
    LazyColumn(state = list, contentPadding = PaddingValues(start = TokiSpacing.pageMargin, end = TokiSpacing.pageMargin, top = topPadding + TokiSpacing.sm, bottom = bottomPadding), verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxSize()) {
        item(key = "room") { RoomCard(vm, status) }
        // 房间卡与首条消息之间 16dp（卡片间距）；消息与消息之间 8dp 保留，
        // 直播流密度是有意的选择。
        item(key = "gap") { Spacer(Modifier.height(TokiSpacing.sm)) }
        items(vm.messages, key = { it.id }) { m -> Box(Modifier.animateItem()) { MsgCard(m) } }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun RoomCard(vm: LiveViewModel, status: String) {
    Surface(shape = TokiCardShape, color = MaterialTheme.colorScheme.surfaceContainerHigh, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(TokiSpacing.xl), verticalArrangement = Arrangement.spacedBy(TokiSpacing.sm)) {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(vm.room, { vm.room = it.filter(Char::isDigit) }, Modifier.weight(1f), label = { Text("房间号") }, singleLine = true, shape = RoundedCornerShape(16.dp),
                    leadingIcon = { Symbol("live_tv") }, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), enabled = !vm.connected)
                if (vm.connected && status.startsWith("正在")) LoadingIndicator(Modifier.size(48.dp))
                else Button(onClick = { vm.toggle() }, shapes = ButtonDefaults.shapes(), modifier = Modifier.height(56.dp)) { Text(if (vm.connected) "断开" else "连接", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold) }
            }
            Text(if (vm.connected) "房间 ${vm.room} · $status" else "弹幕预览", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

// ---------- 卡片：灵动岛式分组，间距统一 ----------
@Composable
private fun MedalChip(name: String, level: Int, dim: Boolean = false) {
    val c = TokiLiveChatColors
    val bg = if (dim) c.medalBgDim else c.medalBg; val lv = if (dim) c.medalLevelBgDim else c.medalLevelBg; val fg = if (dim) c.medalFgDim else c.medalFg
    Row(Modifier.clip(CircleShape).background(bg).border(2.dp, lv, CircleShape).padding(start = 8.dp, end = 2.dp, top = 2.dp, bottom = 2.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(TokiSpacing.sm)) {
        Text(name, color = fg, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold)
        Text("lv.$level", color = Color.White, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold, modifier = Modifier.clip(CircleShape).background(lv).padding(horizontal = TokiSpacing.sm, vertical = 2.dp))
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MsgCard(m: LiveMsg) {
    val cs = MaterialTheme.colorScheme
    val (bg, line, fg) = when (m.type) {
        "superchat" -> Triple(cs.primary, cs.primary, cs.onPrimary)
        "guard" -> Triple(cs.primaryContainer, cs.primary, cs.onPrimaryContainer)
        "gift" -> Triple(TokiLiveChatColors.giftBg, TokiLiveChatColors.giftLine, TokiLiveChatColors.giftFg)
        "enter" -> Triple(cs.surface, cs.onSurface, cs.onSurface)
        "follow", "like", "share" -> Triple(cs.tertiaryContainer, cs.tertiaryContainer, cs.onTertiaryContainer)
        else -> Triple(cs.surfaceContainerHighest, cs.outlineVariant, cs.onSurface)
    }
    // 长消息用全 App 定死的卡片形状 28dp；短消息的圆形是聊天气泡例外，
    // 纵向内边距 12dp 也是气泡密度例外（卡片标准是 24dp），不是漏改。
    val shape = if (m.type == "superchat" || m.emote != null || m.text.length > 18) TokiCardShape else CircleShape
    Surface(shape = shape, color = bg, contentColor = fg, border = BorderStroke(if (m.type == "danmaku") 1.dp else 2.dp, line)) {
        Column(Modifier.padding(horizontal = TokiSpacing.xl, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp), itemVerticalAlignment = Alignment.CenterVertically) {
                if (m.type == "like") Symbol("thumb_up", filled = true, size = 20.dp)
                if (m.type == "share") Symbol("share", filled = true, size = 20.dp)
                Text(m.uname, style = MaterialTheme.typography.labelLargeEmphasized, color = fg.copy(alpha = if (m.type == "danmaku") .62f else 1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                m.medalName?.let { MedalChip(it, m.medalLevel, dim = m.type == "gift" || m.type == "guard") }
                if (m.guard > 0 && m.type == "danmaku") Box(Modifier.size(20.dp).clip(CircleShape).background(TokiLiveChatColors.guardRed), contentAlignment = Alignment.Center) { Text("舰", color = Color.White, style = MaterialTheme.typography.labelSmall, fontWeight = FontWeight.Bold) }
                when (m.type) {
                    "superchat" -> { Spacer(Modifier.weight(1f)); Text("+${m.price}", style = MaterialTheme.typography.titleMediumEmphasized) }
                    "danmaku" -> if (m.emote == null && m.text.length <= 18) Text(m.text, style = MaterialTheme.typography.bodyLargeEmphasized)
                    "gift" -> { Text("${m.action}了", color = fg.copy(.7f), style = MaterialTheme.typography.bodyLarge); Text("${m.gift} ×${m.num}", style = MaterialTheme.typography.bodyLargeEmphasized)
                        m.icon?.takeIf { it.isNotEmpty() }?.let { AsyncImage(it, null, Modifier.size(20.dp)) } }
                    "guard" -> { Text("开通了", color = cs.primary, style = MaterialTheme.typography.bodyLarge); Text("${m.gift} ${m.num} 个月", style = MaterialTheme.typography.bodyLargeEmphasized) }
                    else -> Text(m.action, style = MaterialTheme.typography.bodyLargeEmphasized)
                }
            }
            if (m.type == "superchat" || (m.type == "danmaku" && m.emote == null && m.text.length > 18)) Text(m.text, style = MaterialTheme.typography.bodyLargeEmphasized)
            m.emote?.let { AsyncImage(it, null, Modifier.height(56.dp).padding(start = 8.dp)) }
        }
    }
}

// ---------- 数据 ----------
@OptIn(ExperimentalMaterial3ExpressiveApi::class, ExperimentalMaterial3Api::class)
@Composable
fun StatsScreen(vm: LiveViewModel, topPadding: Dp, bottomPadding: Dp) {
    val st by vm.client.stats.collectAsState()
    val inf = rememberInfiniteTransition(label = "m")
    val p by inf.animateFloat(0f, 1f, infiniteRepeatable(tween(1600, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "p")
    val rot by inf.animateFloat(0f, 360f, infiniteRepeatable(tween(12000, easing = LinearEasing)), label = "r")
    val shapes = remember { listOf(morphOf(MaterialShapes.Cookie9Sided, MaterialShapes.Clover4Leaf), morphOf(MaterialShapes.Sunny, MaterialShapes.SoftBurst), morphOf(MaterialShapes.Pill, MaterialShapes.Cookie6Sided)) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = TokiSpacing.pageMargin).padding(top = topPadding + TokiSpacing.sm, bottom = bottomPadding), verticalArrangement = Arrangement.spacedBy(TokiSpacing.lg)) {
        val cs = MaterialTheme.colorScheme
        listOf(Triple("本场点赞", st.likes, "favorite"), Triple("弹幕", st.danmaku, "chat_bubble"), Triple("当前在线", st.online, "group")).forEachIndexed { i, (label, v, ic) ->
            val (c, on) = listOf(cs.primaryContainer to cs.onPrimaryContainer, cs.secondaryContainer to cs.onSecondaryContainer, cs.tertiaryContainer to cs.onTertiaryContainer)[i]
            Surface(shape = TokiCardShape, color = c, contentColor = on, modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(TokiSpacing.xl), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(TokiSpacing.lg)) {
                    Box(Modifier.size(64.dp).clip(MorphShape(shapes[i], p, rot)).background(on), contentAlignment = Alignment.Center) { Symbol(ic, filled = true, tint = c, size = 24.dp) }
                    Column { Text(label, style = MaterialTheme.typography.titleMedium)
                        AnimatedContent(v, transitionSpec = { (slideInVertically { it } + fadeIn()) togetherWith (slideOutVertically { -it } + fadeOut()) }, label = "n") { Text("%,d".format(it), style = MaterialTheme.typography.displayMediumEmphasized) } }
                }
            }
        }
        if (st.watched.isNotEmpty()) Text(st.watched, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

// ---------- 设置 ----------
/** 设置分组：surface 阶梯上的实色容器。 */
@Composable
private fun SettingsGroup(content: @Composable () -> Unit) {
    Card(
        Modifier.fillMaxWidth().padding(vertical = TokiSpacing.sm),
        shape = TokiCardShape,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh),
    ) { content() }
}

/** 设置行：64dp 高，20dp 主色图标，标题 titleMedium 粗体 + 可选的 bodyMedium 说明。 */
@Composable
private fun SettingRow(icon: String, title: String, note: String? = null, trailing: @Composable () -> Unit = {}, onClick: (() -> Unit)? = null) {
    Row(
        Modifier.fillMaxWidth().height(64.dp).then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier).padding(horizontal = TokiSpacing.xl),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Symbol(icon, size = 20.dp, tint = MaterialTheme.colorScheme.primary)
        Spacer(Modifier.size(TokiSpacing.md))
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
            if (note != null) Text(note, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        trailing()
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun SettingsScreen(vm: LiveViewModel, topPadding: Dp, bottomPadding: Dp) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = TokiSpacing.pageMargin).padding(top = topPadding + TokiSpacing.sm, bottom = bottomPadding)) {
        SectionTitle("账号")
        SettingsGroup {
            SettingRow("qr_code_scanner", if (vm.loggedIn) "已登录" else "扫码登录", "登录后可显示完整用户名", trailing = { if (vm.loggedIn) TextButton({ vm.logout() }) { Text("退出") } }, onClick = { if (!vm.loggedIn) vm.startQr() })
        }
        SectionTitle("外观")
        SettingsGroup {
            SettingRow("dark_mode", "主题", trailing = {
                Row(horizontalArrangement = Arrangement.spacedBy(ButtonGroupDefaults.ConnectedSpaceBetween)) {
                    listOf("系统", "浅色", "深色").forEachIndexed { i, l -> ToggleButton(checked = vm.themeMode == i, onCheckedChange = { vm.themeMode = i; vm.save() },
                        shapes = when (i) { 0 -> ButtonGroupDefaults.connectedLeadingButtonShapes(); 2 -> ButtonGroupDefaults.connectedTrailingButtonShapes(); else -> ButtonGroupDefaults.connectedMiddleButtonShapes() },
                        contentPadding = PaddingValues(horizontal = 12.dp)) { Text(l) } }
                } })
            SettingRow("palette", "动态取色", "跟随系统壁纸颜色（Material You）", trailing = { Switch(vm.dynamicColor, { vm.dynamicColor = it; vm.save() }, thumbContent = { if (vm.dynamicColor) Symbol("check", size = 16.dp) }) })
        }
        SectionTitle("显示的消息")
        SettingsGroup {
            listOf("danmaku" to ("弹幕" to "chat"), "gift" to ("礼物" to "redeem"), "superchat" to ("醒目留言" to "paid"), "guard" to ("上舰" to "military_tech"),
                "enter" to ("进场" to "login"), "follow" to ("关注" to "person_add"), "like" to ("点赞" to "thumb_up")).forEach { (k, v) ->
                SettingRow(v.second, v.first, trailing = { Switch(vm.show[k] != false, { vm.show[k] = it; vm.save() }) })
            }
        }
        SectionTitle("关于")
        SettingsGroup {
            Column(Modifier.padding(TokiSpacing.xl), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(BuildConfig.VERSION_NAME, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                Text("com.ichiyotsu.tokilive", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        }
        SectionTitle("更新")
        UpdateCard(vm)
    }
    vm.qr?.let { url ->
        val bmp = remember(url) { val m = QRCodeWriter().encode(url, BarcodeFormat.QR_CODE, 512, 512); Bitmap.createBitmap(512, 512, Bitmap.Config.ARGB_8888).apply { for (x in 0 until 512) for (y in 0 until 512) setPixel(x, y, if (m[x, y]) -0x1000000 else -1) } }
        AlertDialog(onDismissRequest = { vm.qr = null }, confirmButton = { TextButton({ vm.qr = null }) { Text("关闭") } }, icon = { Symbol("qr_code_2") }, title = { Text("扫码登录") },
            text = { Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) { Image(bmp.asImageBitmap(), null, Modifier.size(220.dp).clip(RoundedCornerShape(16.dp))); Spacer(Modifier.height(12.dp)); Text(vm.qrText) } })
    }
}

@Composable
private fun UpdateCard(vm: LiveViewModel) {
    Card(shape = TokiCardShape, colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerHigh), modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(TokiSpacing.xl), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(vm.updateMessage, style = MaterialTheme.typography.bodyMedium)
            Text("公开的 GitHub Releases。核对 SHA-256、包名和签名后，打开系统安装界面。", style = MaterialTheme.typography.bodySmall)
            if (vm.updateProgress >= 0) LinearProgressIndicator(progress = { vm.updateProgress / 100f }, modifier = Modifier.fillMaxWidth())
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button({ vm.checkUpdate() }, enabled = !vm.updateBusy, shapes = ButtonDefaults.shapes(), modifier = Modifier.height(56.dp)) { Text("检查更新", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold) }
                if (vm.updateReady) Button({ vm.installUpdate() }, enabled = !vm.updateBusy, shapes = ButtonDefaults.shapes(), modifier = Modifier.height(56.dp)) { Text("安装", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold) }
            }
        }
    }
}
