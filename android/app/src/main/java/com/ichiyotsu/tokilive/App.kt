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
import androidx.compose.ui.input.nestedscroll.nestedScroll
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import coil3.compose.AsyncImage
import com.google.zxing.BarcodeFormat
import com.google.zxing.qrcode.QRCodeWriter
import dev.chrisbanes.haze.*
import dev.chrisbanes.haze.materials.ExperimentalHazeMaterialsApi

@OptIn(ExperimentalMaterial3ExpressiveApi::class, ExperimentalHazeMaterialsApi::class, ExperimentalMaterial3Api::class)
@Composable
fun TokiApp(vm: LiveViewModel, initialTab: Int = 0) {
    var tab by rememberSaveable { mutableIntStateOf(initialTab) }
    val haze = rememberHazeState()
    BoxWithConstraints(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface)) {
        val expanded = maxWidth >= 840.dp
        // 底栏占的高度：系统手势/导航条 + 12dp 间隙 + 64dp 胶囊，再留 12dp 余量，
        // 让最后一条消息完整露在底栏上方。手势导航约 104dp，三键导航约 136dp，
        // 比原来写死的 140dp 更贴底栏，内容一滚就经过底栏、模糊看得见。
        val density = LocalDensity.current
        val bottomInset = with(density) {
            maxOf(WindowInsets.navigationBars.getBottom(this), WindowInsets.systemGestures.getBottom(this)).toDp()
        }
        val liveBottomPad = bottomInset + BottomBarCapsuleHeight + 24.dp
        val pages = @Composable {
            Box(Modifier.fillMaxSize().hazeSource(haze)) {
                val ms = MaterialTheme.motionScheme
                val eIn = ms.defaultEffectsSpec<Float>(); val sIn = ms.defaultSpatialSpec<androidx.compose.ui.unit.IntOffset>(); val eOut = ms.fastEffectsSpec<Float>()
                AnimatedContent(tab, transitionSpec = {
                    (fadeIn(eIn) + slideInVertically(sIn) { it / 12 }) togetherWith fadeOut(eOut)
                }, label = "tab") { t: Int ->
                    when (t) { 0 -> LiveScreen(vm, if (expanded) 24.dp else liveBottomPad); 1 -> StatsScreen(vm); else -> SettingsScreen(vm) }
                }
            }
        }
        if (expanded) {
            Row(Modifier.fillMaxSize()) {
                ExpressiveNavRail(tab, { tab = it }, if (vm.connected) "断开直播" else "连接或预览", { if (vm.room.isBlank()) vm.demo() else vm.toggle() }, if (vm.connected) "stop" else "play_arrow")
                Box(Modifier.weight(1f)) { pages() }
            }
        } else {
            Box(Modifier.fillMaxSize()) {
                pages()
                ExpressiveBottomBar(tab, { tab = it }, if (vm.connected) "断开直播" else "连接或预览", { if (vm.room.isBlank()) vm.demo() else vm.toggle() }, if (vm.connected) "stop" else "play_arrow", haze, Modifier.align(Alignment.BottomCenter))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class, ExperimentalMaterial3Api::class)
@Composable
fun LiveScreen(vm: LiveViewModel, bottomPad: androidx.compose.ui.unit.Dp = 140.dp) {
    val status by vm.client.status.collectAsState()
    val sb = TopAppBarDefaults.exitUntilCollapsedScrollBehavior()
    val list = rememberLazyListState()
    LaunchedEffect(vm.messages.size) { if (vm.messages.isNotEmpty()) list.animateScrollToItem(vm.messages.size - 1) }
    Scaffold(Modifier.nestedScroll(sb.nestedScrollConnection), containerColor = Color.Transparent, topBar = {
        LargeFlexibleTopAppBar(title = { Text("tokilive", fontWeight = FontWeight.Bold) }, subtitle = { Text(if (vm.connected) "房间 ${vm.room} · $status" else "弹幕预览") },
            scrollBehavior = sb, colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface, scrolledContainerColor = MaterialTheme.colorScheme.surfaceContainer))
    }) { pad ->
        LazyColumn(state = list, contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = pad.calculateTopPadding() + 4.dp, bottom = bottomPad), verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxSize()) {
            item(key = "room") { RoomCard(vm, status) }
            items(vm.messages, key = { it.id }) { m -> Box(Modifier.animateItem()) { MsgCard(m) } }
        }
    }
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun RoomCard(vm: LiveViewModel, status: String) {
    Surface(shape = RoundedCornerShape(28.dp), color = MaterialTheme.colorScheme.surfaceContainer, modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp)) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(vm.room, { vm.room = it.filter(Char::isDigit) }, Modifier.weight(1f), label = { Text("房间号") }, singleLine = true, shape = RoundedCornerShape(16.dp),
                leadingIcon = { Symbol("live_tv") }, keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), enabled = !vm.connected)
            if (vm.connected && status.startsWith("正在")) LoadingIndicator(Modifier.size(48.dp))
            else Button(onClick = { vm.toggle() }, shapes = ButtonDefaults.shapes(), modifier = Modifier.height(56.dp)) { Text(if (vm.connected) "断开" else "连接") }
        }
    }
}

// ---------- 卡片：灵动岛式分组，间距统一 ----------
@Composable
private fun MedalChip(name: String, level: Int, dim: Boolean = false) {
    val bg = if (dim) Color(0xFF5C4A16) else Color(0xFFFF5FC8); val lv = if (dim) Color(0xFF3A2E0A) else Color(0xFF4A0A30); val fg = if (dim) Color(0xFFF3E4B0) else Color(0xFF2A0A1E)
    Row(Modifier.clip(CircleShape).background(bg).border(1.5.dp, lv, CircleShape).padding(start = 8.dp, end = 2.dp, top = 2.dp, bottom = 2.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(name, color = fg, fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Text("lv.$level", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold, modifier = Modifier.clip(CircleShape).background(lv).padding(horizontal = 7.dp, vertical = 1.dp))
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun MsgCard(m: LiveMsg) {
    val cs = MaterialTheme.colorScheme
    val (bg, line, fg) = when (m.type) {
        "superchat" -> Triple(cs.primary, cs.primary, cs.onPrimary)
        "guard" -> Triple(cs.primaryContainer, cs.primary, cs.onPrimaryContainer)
        "gift" -> Triple(Color(0xFFFFE3A6), Color(0xFF4A3A00), Color(0xFF2A2000))
        "enter" -> Triple(cs.surface, cs.onSurface, cs.onSurface)
        "follow", "like", "share" -> Triple(cs.tertiaryContainer, cs.tertiaryContainer, cs.onTertiaryContainer)
        else -> Triple(cs.surfaceContainerHighest, cs.outlineVariant, cs.onSurface)
    }
    val shape = if (m.type == "superchat" || m.emote != null || m.text.length > 18) RoundedCornerShape(24.dp) else CircleShape
    Surface(shape = shape, color = bg, contentColor = fg, border = BorderStroke(if (m.type == "danmaku") 1.dp else 2.dp, line)) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(4.dp), itemVerticalAlignment = Alignment.CenterVertically) {
                if (m.type == "like") Symbol("thumb_up", filled = true, size = 18.dp)
                if (m.type == "share") Symbol("share", filled = true, size = 18.dp)
                Text(m.uname, style = MaterialTheme.typography.labelLargeEmphasized, color = fg.copy(alpha = if (m.type == "danmaku") .62f else 1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                m.medalName?.let { MedalChip(it, m.medalLevel, dim = m.type == "gift" || m.type == "guard") }
                if (m.guard > 0 && m.type == "danmaku") Box(Modifier.size(18.dp).clip(CircleShape).background(Color(0xFFE5484D)), contentAlignment = Alignment.Center) { Text("舰", color = Color.White, fontSize = 10.sp, fontWeight = FontWeight.Bold) }
                when (m.type) {
                    "superchat" -> { Spacer(Modifier.weight(1f)); Text("+${m.price}", style = MaterialTheme.typography.titleMediumEmphasized) }
                    "danmaku" -> if (m.emote == null && m.text.length <= 18) Text(m.text, style = MaterialTheme.typography.bodyLargeEmphasized)
                    "gift" -> { Text("${m.action}了", color = fg.copy(.7f), style = MaterialTheme.typography.bodyLarge); Text("${m.gift} ×${m.num}", style = MaterialTheme.typography.bodyLargeEmphasized)
                        m.icon?.takeIf { it.isNotEmpty() }?.let { AsyncImage(it, null, Modifier.size(22.dp)) } }
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
fun StatsScreen(vm: LiveViewModel) {
    val st by vm.client.stats.collectAsState()
    val inf = rememberInfiniteTransition(label = "m")
    val p by inf.animateFloat(0f, 1f, infiniteRepeatable(tween(1600, easing = FastOutSlowInEasing), RepeatMode.Reverse), label = "p")
    val rot by inf.animateFloat(0f, 360f, infiniteRepeatable(tween(12000, easing = LinearEasing)), label = "r")
    val shapes = remember { listOf(morphOf(MaterialShapes.Cookie9Sided, MaterialShapes.Clover4Leaf), morphOf(MaterialShapes.Sunny, MaterialShapes.SoftBurst), morphOf(MaterialShapes.Pill, MaterialShapes.Cookie6Sided)) }
    Column(Modifier.fillMaxSize().statusBarsPadding().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(top = 24.dp, bottom = 140.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("直播间数据", style = MaterialTheme.typography.displaySmallEmphasized, modifier = Modifier.padding(start = 8.dp, bottom = 12.dp))
        val cs = MaterialTheme.colorScheme
        listOf(Triple("本场点赞", st.likes, "favorite"), Triple("弹幕", st.danmaku, "chat_bubble"), Triple("当前在线", st.online, "group")).forEachIndexed { i, (label, v, ic) ->
            val (c, on) = listOf(cs.primaryContainer to cs.onPrimaryContainer, cs.secondaryContainer to cs.onSecondaryContainer, cs.tertiaryContainer to cs.onTertiaryContainer)[i]
            Surface(shape = RoundedCornerShape(if (i == 0) 32.dp else 12.dp, if (i == 0) 32.dp else 12.dp, if (i == 2) 32.dp else 12.dp, if (i == 2) 32.dp else 12.dp), color = c, contentColor = on, modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(20.dp)) {
                    Box(Modifier.size(64.dp).clip(MorphShape(shapes[i], p, rot)).background(on), contentAlignment = Alignment.Center) { Symbol(ic, filled = true, tint = c, size = 28.dp) }
                    Column { Text(label, style = MaterialTheme.typography.titleMedium)
                        AnimatedContent(v, transitionSpec = { (slideInVertically { it } + fadeIn()) togetherWith (slideOutVertically { -it } + fadeOut()) }, label = "n") { Text("%,d".format(it), style = MaterialTheme.typography.displayMediumEmphasized) } }
                }
            }
        }
        if (st.watched.isNotEmpty()) Text(st.watched, style = MaterialTheme.typography.bodyLarge, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 8.dp))
    }
}

// ---------- 设置 ----------
@Composable
private fun Group(content: @Composable ColumnScope.() -> Unit) = Column(Modifier.clip(RoundedCornerShape(24.dp)), verticalArrangement = Arrangement.spacedBy(2.dp), content = content)
@Composable
private fun Item(title: String, sub: String? = null, icon: String, trailing: @Composable () -> Unit = {}, onClick: (() -> Unit)? = null) {
    ListItem(headlineContent = { Text(title) }, supportingContent = sub?.let { { Text(it) } }, leadingContent = { Symbol(icon) }, trailingContent = trailing,
        colors = ListItemDefaults.colors(containerColor = MaterialTheme.colorScheme.surfaceContainer), modifier = Modifier.clip(RoundedCornerShape(4.dp)).then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier))
}

@OptIn(ExperimentalMaterial3ExpressiveApi::class)
@Composable
fun SettingsScreen(vm: LiveViewModel) {
    Column(Modifier.fillMaxSize().statusBarsPadding().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(top = 24.dp, bottom = 140.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("设置", style = MaterialTheme.typography.displaySmallEmphasized, modifier = Modifier.padding(start = 8.dp, bottom = 12.dp))
        Text("账号", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 16.dp, top = 8.dp))
        Group {
            Item(if (vm.loggedIn) "已登录" else "扫码登录", "登录后可显示完整用户名", "qr_code_scanner", trailing = { if (vm.loggedIn) TextButton({ vm.logout() }) { Text("退出") } }, onClick = { if (!vm.loggedIn) vm.startQr() })
        }
        Text("外观", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 16.dp, top = 8.dp))
        Group {
            Item("主题", null, "dark_mode", trailing = {
                Row(horizontalArrangement = Arrangement.spacedBy(ButtonGroupDefaults.ConnectedSpaceBetween)) {
                    listOf("系统", "浅色", "深色").forEachIndexed { i, l -> ToggleButton(checked = vm.themeMode == i, onCheckedChange = { vm.themeMode = i; vm.save() },
                        shapes = when (i) { 0 -> ButtonGroupDefaults.connectedLeadingButtonShapes(); 2 -> ButtonGroupDefaults.connectedTrailingButtonShapes(); else -> ButtonGroupDefaults.connectedMiddleButtonShapes() },
                        contentPadding = PaddingValues(horizontal = 12.dp)) { Text(l) } }
                } })
            Item("动态取色", "跟随系统壁纸颜色（Material You）", "palette", trailing = { Switch(vm.dynamicColor, { vm.dynamicColor = it; vm.save() }, thumbContent = { if (vm.dynamicColor) Symbol("check", size = 16.dp) }) })
        }
        Text("显示的消息", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 16.dp, top = 8.dp))
        Group {
            listOf("danmaku" to ("弹幕" to "chat"), "gift" to ("礼物" to "redeem"), "superchat" to ("醒目留言" to "paid"), "guard" to ("上舰" to "military_tech"),
                "enter" to ("进场" to "login"), "follow" to ("关注" to "person_add"), "like" to ("点赞" to "thumb_up")).forEach { (k, v) ->
                Item(v.first, null, v.second, trailing = { Switch(vm.show[k] != false, { vm.show[k] = it; vm.save() }) })
            }
        }
        Text("关于", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 16.dp, top = 8.dp))
        Text(BuildConfig.VERSION_NAME, style = MaterialTheme.typography.displaySmallEmphasized, modifier = Modifier.padding(start = 8.dp))
        Text("com.ichiyotsu.tokilive", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(start = 8.dp, bottom = 8.dp))
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
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("更新", style = MaterialTheme.typography.titleSmall, color = MaterialTheme.colorScheme.primary, modifier = Modifier.padding(start = 16.dp, top = 8.dp))
        Surface(shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surfaceContainer, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(vm.updateMessage, style = MaterialTheme.typography.bodyMedium)
                Text("公开的 GitHub Releases。核对 SHA-256、包名和签名后，打开系统安装界面。", style = MaterialTheme.typography.bodySmall)
                if (vm.updateProgress >= 0) LinearProgressIndicator(progress = { vm.updateProgress / 100f }, modifier = Modifier.fillMaxWidth())
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton({ vm.checkUpdate() }, enabled = !vm.updateBusy) { Text("检查更新") }
                    if (vm.updateReady) TextButton({ vm.installUpdate() }, enabled = !vm.updateBusy) { Text("安装") }
                }
            }
        }
    }
}
