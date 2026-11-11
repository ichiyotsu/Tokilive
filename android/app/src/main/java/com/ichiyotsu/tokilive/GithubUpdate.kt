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

class GithubUpdate(
    private val version: String = BuildConfig.VERSION_NAME,
    private val http: OkHttpClient = OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).callTimeout(60, TimeUnit.SECONDS).build(),
) {
    fun latest(current: String): RemoteRelease? {
        val response = get("https://api.github.com/repos/ichiyotsu/Tokilive/releases?per_page=20", accept = "application/vnd.github+json")
        val body = response.use {
            if (it.code == 403 || it.code == 429) error("GitHub API 访问受限（HTTP ${it.code}），请稍后再试")
            else if (it.code !in 200..299) error(UpdateLogic.redact("读取发行版失败 HTTP ${it.code}"))
            else it.body.string()
        }
        val array = JSONArray(body)
        var best: RemoteRelease? = null
        for (i in 0 until array.length()) {
            val rel = array.optJSONObject(i) ?: continue
            if (rel.optBoolean("draft") || rel.optBoolean("prerelease")) continue
            val version = rel.optString("tag_name").trim().removePrefix("v").removePrefix("V")
            if (!UpdateLogic.isStable(version) || !UpdateLogic.isNewer(version, current)) continue
            val asset = pick(rel.optJSONArray("assets")) ?: continue
            // tag 是全平台版本，某端可能仍是旧版（例如 v1.3.1 里附的 apk 仍是 1.3.0）。
            // 文件名里能读出版本时以它为准，避免下载一个和当前一样的包。
            val assetVersion = UpdateLogic.assetVersion(asset.optString("name"))
            if (assetVersion != null && !UpdateLogic.isNewer(assetVersion, current)) continue
            val digest = asset.optString("digest").substringAfter("sha256:", "").lowercase()
            val item = RemoteRelease(version, rel.optString("body"), asset.optLong("id"), asset.optString("name"), digest)
            if (best == null || UpdateLogic.isNewer(item.version, best.version)) best = item
        }
        return best
    }

    fun download(assetId: Long, dest: File, onProgress: (Long, Long) -> Unit): String {
        dest.parentFile?.mkdirs()
        val url = UpdateLogic.assetApi(assetId) ?: error("缺少资源编号")
        var next = url
        repeat(5) {
            val response = get(next, accept = "application/octet-stream")
            val code = response.code
            if (code in listOf(301, 302, 303, 307, 308)) {
                val loc = response.header("Location") ?: error("重定向没有地址")
                if (!loc.startsWith("https://")) error("拒绝非 https 下载地址")
                response.close()
                next = loc
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

    private fun pick(assets: JSONArray?): JSONObject? {
        if (assets == null) return null
        val names = buildList { for (i in 0 until assets.length()) add(assets.getJSONObject(i).optString("name")) }
        val name = UpdateLogic.pickApk(names) ?: return null
        for (i in 0 until assets.length()) if (assets.getJSONObject(i).optString("name") == name) return assets.getJSONObject(i)
        return null
    }

    private fun get(url: String, accept: String) = http.newCall(
        Request.Builder().url(url).header("User-Agent", "tokilive/$version (https://github.com/ichiyotsu/Tokilive)").header("Accept", accept).build()
    ).execute()
}
