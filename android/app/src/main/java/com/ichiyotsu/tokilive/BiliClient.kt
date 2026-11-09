package com.ichiyotsu.tokilive

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import okhttp3.*
import okio.ByteString
import okio.ByteString.Companion.toByteString
import org.json.JSONArray
import org.json.JSONObject
import java.net.URLEncoder
import java.nio.ByteBuffer
import java.security.MessageDigest
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import java.util.zip.Inflater

/** 一条直播间消息（与桌面端 tokilive 的卡片类型一一对应） */
data class LiveMsg(
    val id: Long, val type: String, val uname: String, val text: String = "", val medalName: String? = null, val medalLevel: Int = 0,
    val guard: Int = 0, val price: Int = 0, val gift: String = "", val num: Int = 0, val action: String = "", val emote: String? = null, val icon: String? = null,
)
data class LiveStats(val likes: Long = 0, val danmaku: Long = 0, val online: Long = 0, val watched: String = "")

private val ids = AtomicLong(1)
fun nextId() = ids.getAndIncrement()

/** B 站直播弹幕协议（protover 2 / zlib），移植自桌面端 bili.js */
class BiliClient(private val http: OkHttpClient = OkHttpClient.Builder().pingInterval(25, TimeUnit.SECONDS).build()) {
    private val ua = "Mozilla/5.0 (Linux; Android 15) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36"
    private val mixin = intArrayOf(46,47,18,2,53,8,23,32,15,50,10,31,58,3,45,35,27,43,5,49,33,9,42,19,29,28,14,39,12,38,41,13,37,48,7,16,24,55,40,61,26,17,0,1,60,51,30,4,22,25,54,21,56,59,6,63,57,62,11,36,20,34,44,52)
    val messages = MutableSharedFlow<LiveMsg>(extraBufferCapacity = 256)
    val stats = MutableStateFlow(LiveStats())
    val status = MutableStateFlow("未连接")
    private var ws: WebSocket? = null
    private var job: Job? = null
    var cookie: String = ""

    private fun get(url: String): JSONObject {
        val b = Request.Builder().url(url).header("User-Agent", ua).header("Referer", "https://live.bilibili.com/")
        if (cookie.isNotBlank()) b.header("Cookie", cookie)
        http.newCall(b.build()).execute().use { return JSONObject(it.body.string()) }
    }
    private fun md5(s: String) = MessageDigest.getInstance("MD5").digest(s.toByteArray()).joinToString("") { "%02x".format(it) }
    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8").replace("+", "%20")
    private fun wbi(params: Map<String, Any>): String {
        val nav = get("https://api.bilibili.com/x/web-interface/nav").getJSONObject("data").getJSONObject("wbi_img")
        val k = { u: String -> u.substringAfterLast('/').substringBefore('.') }
        val raw = k(nav.getString("img_url")) + k(nav.getString("sub_url"))
        val key = mixin.map { raw[it] }.joinToString("").take(32)
        val p = params + ("wts" to System.currentTimeMillis() / 1000)
        val q = p.keys.sorted().joinToString("&") { "${enc(it)}=${enc(p[it].toString().replace(Regex("[!'()*]"), ""))}" }
        return q + "&w_rid=" + md5(q + key)
    }

    fun start(scope: CoroutineScope, room: String) {
        stop(); job = scope.launch(Dispatchers.IO) {
            var retry = 0
            while (isActive) {
                try {
                    status.value = "正在解析房间号…"
                    val init = get("https://api.live.bilibili.com/room/v1/Room/room_init?id=$room")
                    if (init.optInt("code") != 0) { status.value = "直播间不存在"; return@launch }
                    val rid = init.getJSONObject("data").getLong("room_id")
                    var buvid = Regex("buvid3=([^;]+)").find(cookie)?.groupValues?.get(1)
                    if (buvid == null) { buvid = get("https://api.bilibili.com/x/frontend/finger/spi").getJSONObject("data").getString("b_3"); cookie = (if (cookie.isBlank()) "" else "$cookie; ") + "buvid3=$buvid" }
                    val info = get("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?" + wbi(mapOf("id" to rid, "type" to 0, "web_location" to "444.8"))).getJSONObject("data")
                    val h = info.getJSONArray("host_list").getJSONObject(0)
                    val uid = Regex("DedeUserID=(\\d+)").find(cookie)?.groupValues?.get(1)?.toLong() ?: 0L
                    val auth = JSONObject().put("uid", uid).put("roomid", rid).put("protover", 2).put("buvid", buvid).put("platform", "web").put("type", 2).put("key", info.getString("token"))
                    val closed = CompletableDeferred<Unit>()
                    status.value = "正在连接…"
                    ws = http.newWebSocket(Request.Builder().url("wss://${h.getString("host")}:${h.getInt("wss_port")}/sub").header("User-Agent", ua).build(), object : WebSocketListener() {
                        override fun onOpen(webSocket: WebSocket, response: Response) { webSocket.send(pack(7, auth.toString())) }
                        override fun onMessage(webSocket: WebSocket, bytes: ByteString) { onData(bytes.toByteArray()) ; if (status.value.startsWith("正在")) { retry = 0 } }
                        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) { closed.complete(Unit) }
                        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) { closed.complete(Unit) }
                    })
                    val hb = launch { while (isActive) { delay(30_000); ws?.send(pack(2, "[object Object]")) } }
                    closed.await(); hb.cancel()
                } catch (e: CancellationException) { throw e } catch (e: Exception) { status.value = "连接失败：${e.message}" }
                val s = minOf(60L, 1L shl minOf(retry++, 6)); status.value = "连接断开，$s 秒后重连…"; delay(s * 1000)
            }
        }
    }
    fun stop() { job?.cancel(); job = null; ws?.cancel(); ws = null; status.value = "未连接" }

    private fun pack(op: Int, body: String): ByteString {
        val b = body.toByteArray(); val buf = ByteBuffer.allocate(16 + b.size)
        buf.putInt(16 + b.size).putShort(16).putShort(1).putInt(op).putInt(1).put(b); return buf.array().toByteString()
    }
    private fun inflate(b: ByteArray): ByteArray { val inf = Inflater(); inf.setInput(b); val out = java.io.ByteArrayOutputStream(); val tmp = ByteArray(8192)
        while (!inf.finished()) { val n = inf.inflate(tmp); if (n == 0 && (inf.needsInput() || inf.needsDictionary())) break; out.write(tmp, 0, n) }; inf.end(); return out.toByteArray() }
    internal fun onData(buf: ByteArray) {
        val bb = ByteBuffer.wrap(buf); var off = 0
        while (off + 16 <= buf.size) {
            val len = bb.getInt(off); val hl = bb.getShort(off + 4).toInt(); val ver = bb.getShort(off + 6).toInt(); val op = bb.getInt(off + 8)
            if (len < 16 || off + len > buf.size) break
            val body = buf.copyOfRange(off + hl, off + len); off += len
            when (op) {
                8 -> status.value = "已连接"
                3 -> {}
                5 -> if (ver == 2) runCatching { onData(inflate(body)) } else runCatching { handle(JSONObject(String(body))) }
            }
        }
    }
    private fun longField(o: JSONObject?, vararg keys: String): Long {
        if (o == null) return 0
        for (key in keys) if (o.has(key)) return o.optLong(key)
        return 0
    }
    private fun medal(o: JSONObject?): Pair<String?, Int> = if (o != null && o.optString("medal_name").isNotEmpty() && o.optInt("medal_level") > 0) o.getString("medal_name") to o.getInt("medal_level") else null to 0
    internal fun handle(m: JSONObject) {
        val cmd = m.optString("cmd").substringBefore(':')
        when (cmd) {
            "DANMU_MSG" -> { val i = m.getJSONArray("info"); val i0 = i.getJSONArray(0); val u = i.getJSONArray(2); val md = i.optJSONArray(3)
                val emo = i0.optJSONObject(13)?.optString("url")?.takeIf { it.isNotEmpty() }
                val name = i0.optJSONObject(15)?.optJSONObject("user")?.optJSONObject("base")?.optString("name")?.takeIf { it.isNotEmpty() } ?: u.getString(1)
                messages.tryEmit(LiveMsg(nextId(), "danmaku", name, i.getString(1), md?.takeIf { it.length() > 1 }?.optString(1), md?.optInt(0) ?: 0, i.optInt(7), emote = emo))
                stats.update { it.copy(danmaku = it.danmaku + 1) } }
            "SEND_GIFT" -> { val d = m.getJSONObject("data"); val (mn, ml) = medal(d.optJSONObject("medal_info"))
                messages.tryEmit(LiveMsg(nextId(), "gift", d.getString("uname"), medalName = mn, medalLevel = ml, gift = d.getString("giftName"), num = d.optInt("num"), action = d.optString("action", "投喂"), icon = d.optJSONObject("gift_info")?.optString("webp"))) }
            "SUPER_CHAT_MESSAGE" -> { val d = m.getJSONObject("data"); val (mn, ml) = medal(d.optJSONObject("medal_info"))
                messages.tryEmit(LiveMsg(nextId(), "superchat", d.getJSONObject("user_info").getString("uname"), d.getString("message"), mn, ml, price = d.optInt("price"))) }
            "GUARD_BUY" -> { val d = m.getJSONObject("data"); val lv = d.optInt("guard_level")
                messages.tryEmit(LiveMsg(nextId(), "guard", d.getString("username"), guard = lv, gift = d.optString("gift_name", listOf("", "总督", "提督", "舰长")[lv.coerceIn(0, 3)]), num = d.optInt("num"))) }
            "INTERACT_WORD", "INTERACT_WORD_V2" -> {
                val (uname, t) = if (cmd == "INTERACT_WORD_V2") decodeInteract(android.util.Base64.decode(m.getJSONObject("data").getString("pb"), 0)) else m.getJSONObject("data").let { it.getString("uname") to it.optInt("msg_type") }
                val (type, act) = when (t) { 1 -> "enter" to "进入直播间"; 2, 4 -> "follow" to "按下了关注"; 3 -> "share" to "分享了直播间"; 5 -> "follow" to "与主播互粉了"; else -> return }
                messages.tryEmit(LiveMsg(nextId(), type, uname, action = act)) }
            "LIKE_INFO_V3_CLICK" -> { val d = m.getJSONObject("data"); messages.tryEmit(LiveMsg(nextId(), "like", d.getString("uname"), action = d.optString("like_text", "点赞了"))) }
            "LIKE_INFO_V3_UPDATE" -> stats.update { it.copy(likes = longField(m.optJSONObject("data"), "click_count", "total_likes")) }
            "ONLINE_RANK_COUNT", "ONLINE_RANK_V2" -> m.optJSONObject("data")?.let { d -> stats.update { it.copy(online = longField(d, "online_count", "online_num", "count")) } }
            "WATCHED_CHANGE" -> m.optJSONObject("data")?.let { d ->
                val text = d.optString("text_large").ifBlank { d.optString("text_small") }.ifBlank { if (d.has("num")) d.optLong("num").toString() else "" }
                if (text.isNotBlank()) stats.update { it.copy(watched = text) }
            }
        }
    }
    /** 极简 protobuf：1 uid, 2 uname, 5 msg_type */
    private fun decodeInteract(b: ByteArray): Pair<String, Int> {
        var i = 0; var name = ""; var type = 0
        fun varint(): Long { var r = 0L; var s = 0; while (true) { val x = b[i++].toInt() and 0xff; r = r or ((x and 0x7f).toLong() shl s); s += 7; if (x and 0x80 == 0) return r } }
        while (i < b.size) { val k = varint().toInt(); val no = k shr 3
            when (k and 7) { 0 -> { val v = varint(); if (no == 5) type = v.toInt() }
                2 -> { val l = varint().toInt(); if (no == 2) name = String(b, i, l); i += l }
                1 -> i += 8; 5 -> i += 4; else -> break } }
        return name to type
    }

    // ---- 扫码登录 ----
    fun qrGenerate(): Pair<String, String> { val d = get("https://passport.bilibili.com/x/passport-login/web/qrcode/generate").getJSONObject("data"); return d.getString("url") to d.getString("qrcode_key") }
    fun qrPoll(key: String): Pair<Int, String?> {
        val r = http.newCall(Request.Builder().url("https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=$key").header("User-Agent", ua).build()).execute()
        r.use { val d = JSONObject(it.body.string()).getJSONObject("data"); val c = d.getInt("code")
            if (c != 0) return c to null
            val jar = it.headers("Set-Cookie").map { s -> s.substringBefore(';') }.joinToString("; "); return 0 to jar }
    }
}

/** 演示数据：预览与截图用 */
fun demoMessages(): List<LiveMsg> = listOf(
    LiveMsg(nextId(), "guard", "用户 ID", medalName = "粉丝团", medalLevel = 22, guard = 3, gift = "舰长", num = 1, action = "开通了"),
    LiveMsg(nextId(), "superchat", "用户 ID", "输入的文本输入的文本输入的文本", "粉丝团", 24, price = 30),
    LiveMsg(nextId(), "danmaku", "用户 ID", "输入的文本输入的文本", "粉丝团", 24, guard = 3),
    LiveMsg(nextId(), "danmaku", "用户 ID", "输入的文本", "粉丝团", 22),
    LiveMsg(nextId(), "gift", "用户 ID", medalName = "粉丝团", medalLevel = 22, gift = "小礼物", num = 1, action = "投喂"),
    LiveMsg(nextId(), "enter", "用户 ID", medalName = "粉丝团", medalLevel = 22, action = "进入直播间"),
    LiveMsg(nextId(), "follow", "さくら Sakura", action = "按下了关注"),
    LiveMsg(nextId(), "like", "用户 ID", action = "点赞了"),
    LiveMsg(nextId(), "danmaku", "さくら Sakura", "こんにちは！今日の配信も楽しみです ✨", "粉丝团", 18),
)
