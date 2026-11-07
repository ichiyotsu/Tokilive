package com.ichiyotsu.tokilive

import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

data class RemoteRelease(val version: String, val notes: String, val assetId: Long, val assetName: String, val digest: String)

class GithubUpdate(private val http: OkHttpClient = OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).callTimeout(60, TimeUnit.SECONDS).build()) {
    fun latest(token: String, current: String): RemoteRelease? {
        val response = get("https://api.github.com/repos/ichiyotsu/tokilive/releases?per_page=20", token, redirected = false, accept = "application/vnd.github+json")
        val body = response.use { if (it.code !in 200..299) error(UpdateLogic.redact("读取发行版失败 HTTP ${it.code}")) else it.body.string() }
        val array = JSONArray(body)
        var best: RemoteRelease? = null
        for (i in 0 until array.length()) {
            val rel = array.optJSONObject(i) ?: continue
            if (rel.optBoolean("draft") || rel.optBoolean("prerelease")) continue
            val version = rel.optString("tag_name").trim().removePrefix("v").removePrefix("V")
            if (!UpdateLogic.isStable(version) || !UpdateLogic.isNewer(version, current)) continue
            val asset = pick(rel.optJSONArray("assets")) ?: continue
            val digest = asset.optString("digest").substringAfter("sha256:", "").lowercase()
            val item = RemoteRelease(version, rel.optString("body"), asset.optLong("id"), asset.optString("name"), digest)
            if (best == null || UpdateLogic.isNewer(item.version, best.version)) best = item
        }
        return best
    }

    fun download(token: String, assetId: Long, dest: File, onProgress: (Long, Long) -> Unit): String {
        dest.parentFile?.mkdirs()
        val url = UpdateLogic.assetApi(assetId) ?: error("缺少资源编号")
        var next = url
        var redirected = false
        var tokenForCall: String? = token
        repeat(5) {
            val response = get(next, tokenForCall, redirected, accept = "application/octet-stream")
            val code = response.code
            if (code in listOf(301, 302, 303, 307, 308)) {
                val loc = response.header("Location") ?: error("重定向没有地址")
                if (!loc.startsWith("https://")) error("拒绝非 https 下载地址")
                response.close()
                next = loc
                redirected = true
                tokenForCall = null
                return@repeat
            }
            if (code !in 200..299) {
                response.close()
                error(UpdateLogic.redact("下载失败 HTTP $code"))
            }
            val total = response.body.contentLength()
            var read = 0L
            val digest = MessageDigest.getInstance("SHA-256")
            response.body.byteStream().use { input ->
                dest.outputStream().use { output ->
                    val buf = ByteArray(64 * 1024)
                    while (true) {
                        val n = input.read(buf)
                        if (n < 0) break
                        output.write(buf, 0, n)
                        digest.update(buf, 0, n)
                        read += n
                        onProgress(read, total)
                    }
                }
            }
            return digest.digest().joinToString("") { "%02x".format(it) }
        }
        error("重定向过多")
    }

    fun startDevice(clientId: String): JSONObject {
        val req = Request.Builder().url("https://github.com/login/device/code")
            .header("Accept", "application/json").header("User-Agent", "tokilive")
            .post("client_id=$clientId&scope=repo".toRequestBody("application/x-www-form-urlencoded".toMediaType())).build()
        http.newCall(req).execute().use { return JSONObject(it.body.string()) }
    }

    fun pollDevice(clientId: String, deviceCode: String): JSONObject {
        val req = Request.Builder().url("https://github.com/login/oauth/access_token")
            .header("Accept", "application/json").header("User-Agent", "tokilive")
            .post("client_id=$clientId&device_code=$deviceCode&grant_type=urn:ietf:params:oauth:grant-type:device_code".toRequestBody("application/x-www-form-urlencoded".toMediaType())).build()
        http.newCall(req).execute().use { return JSONObject(it.body.string()) }
    }

    private fun pick(assets: JSONArray?): JSONObject? {
        if (assets == null) return null
        val names = buildList { for (i in 0 until assets.length()) add(assets.getJSONObject(i).optString("name")) }
        val name = UpdateLogic.pickApk(names) ?: return null
        for (i in 0 until assets.length()) if (assets.getJSONObject(i).optString("name") == name) return assets.getJSONObject(i)
        return null
    }

    private fun get(url: String, token: String?, redirected: Boolean, accept: String) = http.newCall(
        Request.Builder().url(url).header("User-Agent", "tokilive").header("Accept", accept).apply {
            UpdateLogic.authorizationHeader(token, redirected)?.let { header("Authorization", it) }
        }.build()
    ).execute()
}
