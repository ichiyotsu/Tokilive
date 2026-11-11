package com.ichiyotsu.tokilive

import android.app.Application
import android.content.Context
import androidx.compose.runtime.*
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class LiveViewModel(app: Application) : AndroidViewModel(app) {
    private val prefs = app.getSharedPreferences("tokilive", Context.MODE_PRIVATE)
    val client = BiliClient().also { it.cookie = prefs.getString("cookie", "") ?: "" }
    val messages = mutableStateListOf<LiveMsg>()
    var room by mutableStateOf(prefs.getString("room", "") ?: "")
    var connected by mutableStateOf(false); private set
    var themeMode by mutableIntStateOf(prefs.getInt("theme", 0))   // 0 跟随系统 1 浅色 2 深色
    var dynamicColor by mutableStateOf(prefs.getBoolean("dynamic", true))
    var maxItems by mutableIntStateOf(prefs.getInt("max", 200))
    val show = mutableStateMapOf<String, Boolean>().apply { for (t in listOf("danmaku", "gift", "superchat", "guard", "enter", "follow", "like", "share")) put(t, prefs.getBoolean("show.$t", t != "enter")) }
    var qr by mutableStateOf<String?>(null); var qrText by mutableStateOf("")
    val loggedIn get() = client.cookie.contains("SESSDATA")

    init { viewModelScope.launch { client.messages.collect { m -> if (show[m.type] != false) { messages.add(m); if (messages.size > maxItems) messages.removeRange(0, messages.size - maxItems) } } } }
    fun save() = prefs.edit().putString("room", room).putInt("theme", themeMode).putBoolean("dynamic", dynamicColor).putInt("max", maxItems).putString("cookie", client.cookie)
        .apply { show.forEach { (k, v) -> putBoolean("show.$k", v) } }.apply()
    fun toggle() { if (connected) { client.stop(); connected = false } else if (room.isNotBlank()) { save(); messages.clear(); client.start(viewModelScope, room.filter(Char::isDigit)); connected = true } }
    fun demo() { messages.clear(); messages.addAll(demoMessages()); client.stats.value = LiveStats(51162, 1234, 10289, "11.9万人看过") }
    fun startQr() = viewModelScope.launch {
        qrText = "正在获取二维码…"
        runCatching { withContext(Dispatchers.IO) { client.qrGenerate() } }.onSuccess { (url, key) -> qr = url; qrText = "请用哔哩哔哩 App 扫码"
            while (qr != null) { delay(2000); val (c, ck) = withContext(Dispatchers.IO) { runCatching { client.qrPoll(key) }.getOrDefault(-1 to null) }
                when (c) { 0 -> { client.cookie = ck ?: ""; save(); qrText = "登录成功"; delay(800); qr = null }; 86090 -> qrText = "已扫码，请在手机上确认"; 86038 -> { qrText = "二维码已过期"; return@onSuccess } } } }
            .onFailure { qrText = "获取二维码失败：${it.message}" }
    }
    fun logout() { client.cookie = ""; save() }

    var updateMessage by mutableStateOf(UpdateLogic.FIRST_UPGRADE)
    var updateProgress by mutableIntStateOf(-1)
    var updateBusy by mutableStateOf(false)
    private var pending: RemoteRelease? = null
    private var apkFile: java.io.File? = null

    fun checkUpdate() = viewModelScope.launch {
        updateBusy = true
        updateMessage = "正在检查…"
        val result = withContext(Dispatchers.IO) { runCatching { GithubUpdate().latest(BuildConfig.VERSION_NAME) } }
        val rel = result.getOrElse { updateBusy = false; updateMessage = UpdateLogic.redact(it.message); return@launch }
        if (rel == null) { updateBusy = false; updateMessage = "已经是最新正式版 ${BuildConfig.VERSION_NAME}。"; return@launch }
        pending = rel
        if (rel.digest.length != 64) { updateBusy = false; updateMessage = UpdateLogic.decisionMessage("NO_CHECKSUM"); return@launch }
        val dest = java.io.File(getApplication<Application>().cacheDir, "updates/tokilive-update.apk")
        updateProgress = 0
        updateMessage = "正在下载 ${rel.version}…"
        val downloaded = withContext(Dispatchers.IO) {
            runCatching { GithubUpdate().download(rel.assetId, dest) { read, total ->
                viewModelScope.launch { updateProgress = if (total > 0) ((read * 100) / total).toInt() else 0 }
            } }
        }
        updateBusy = false
        downloaded.onSuccess { sha ->
            if (!UpdateLogic.constantTimeEquals(sha, rel.digest)) { dest.delete(); updateMessage = UpdateLogic.decisionMessage("CHECKSUM"); return@onSuccess }
            apkFile = dest
            updateProgress = 100
            installUpdate()
        }.onFailure { dest.delete(); updateMessage = UpdateLogic.redact(it.message) }
    }
    fun downloadUpdate() = viewModelScope.launch {
        val rel = pending ?: run { updateMessage = "先检查更新。"; return@launch }
        if (rel.digest.length != 64) { updateMessage = UpdateLogic.decisionMessage("NO_CHECKSUM"); return@launch }
        val dest = java.io.File(getApplication<Application>().cacheDir, "updates/tokilive-update.apk")
        updateBusy = true
        updateProgress = 0
        val result = withContext(Dispatchers.IO) {
            runCatching { GithubUpdate().download(rel.assetId, dest) { read, total ->
                viewModelScope.launch { updateProgress = if (total > 0) ((read * 100) / total).toInt() else 0 }
            } }
        }
        updateBusy = false
        result.onSuccess { sha ->
            if (!UpdateLogic.constantTimeEquals(sha, rel.digest)) { dest.delete(); updateMessage = UpdateLogic.decisionMessage("CHECKSUM"); return@onSuccess }
            apkFile = dest
            updateProgress = 100
            updateMessage = "已校验 ${rel.assetName}。还没有安装。"
        }.onFailure { dest.delete(); updateMessage = UpdateLogic.redact(it.message) }
    }
    fun installUpdate() {
        val file = apkFile ?: run { updateMessage = "还没有已校验的安装包。"; return }
        val digest = pending?.digest ?: ""
        val code = ApkInstall.decide(getApplication(), file, digest, userConfirmed = true)
        updateMessage = UpdateLogic.decisionMessage(code)
        when (code) {
            "OK" -> ApkInstall.launchSystemInstaller(getApplication(), file)
            "UNKNOWN_SOURCES" -> ApkInstall.openUnknownSources(getApplication())
        }
    }
}
