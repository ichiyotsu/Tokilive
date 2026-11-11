package com.ichiyotsu.tokilive

enum class SignatureVerdict { MATCH, MISMATCH, UNSIGNED_PACKAGE, NO_BASELINE }

object UpdateLogic {
    const val PACKAGE_ID = "com.ichiyotsu.tokilive"
    const val FIRST_UPGRADE = "公开的 GitHub Releases。点一次「检查更新」。有新正式版时会下载并交给系统安装界面，不会静默安装。"

    private val tokenPattern = Regex("""(?:ghp_|gho_|ghu_|ghs_|ghr_|github_pat_)[A-Za-z0-9_]+""")

    fun redact(value: String?) = tokenPattern.replace(value ?: "", "[token]").replace(Regex("Bearer\\s+\\S+", RegexOption.IGNORE_CASE), "Bearer [token]")

    fun parse(input: String): Pair<List<Int>, List<String>>? {
        val raw = input.trim().removePrefix("v").removePrefix("V")
        val match = Regex("""^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?$""").matchEntire(raw) ?: return null
        val core = match.groupValues[1].split('.').map { it.toInt() }
        val pre = match.groupValues[2].takeIf { it.isNotEmpty() }?.split('.') ?: emptyList()
        return core to pre
    }

    fun compare(a: String, b: String): Int {
        val pa = parse(a) ?: return a.compareTo(b)
        val pb = parse(b) ?: return a.compareTo(b)
        val n = maxOf(pa.first.size, pb.first.size)
        for (i in 0 until n) {
            val d = (pa.first.getOrElse(i) { 0 }) - (pb.first.getOrElse(i) { 0 })
            if (d != 0) return d
        }
        if (pa.second.isEmpty() && pb.second.isNotEmpty()) return 1
        if (pa.second.isNotEmpty() && pb.second.isEmpty()) return -1
        val m = maxOf(pa.second.size, pb.second.size)
        for (i in 0 until m) {
            val left = pa.second.getOrNull(i) ?: return -1
            val right = pb.second.getOrNull(i) ?: return 1
            val d = compareId(left, right)
            if (d != 0) return d
        }
        return 0
    }

    private val betaId = Regex("""^b(\d+)$""")

    private fun compareId(a: String, b: String): Int {
        val ba = betaId.matchEntire(a)
        val bb = betaId.matchEntire(b)
        if (ba != null && bb != null) return ba.groupValues[1].toInt() - bb.groupValues[1].toInt()
        val aNum = a.toIntOrNull()?.takeIf { it.toString() == a }
        val bNum = b.toIntOrNull()?.takeIf { it.toString() == b }
        if (aNum != null && bNum != null) return aNum - bNum
        if (aNum != null) return -1
        if (bNum != null) return 1
        return a.compareTo(b)
    }

    fun isNewer(remote: String, current: String) = compare(remote, current) > 0

    fun isStable(version: String) = parse(version)?.second?.isEmpty() == true

    fun authorizationHeader(token: String?, redirected: Boolean): String? {
        if (redirected || token.isNullOrBlank()) return null
        return "Bearer $token"
    }

    fun assetApi(id: Long) = if (id > 0) "https://api.github.com/repos/ichiyotsu/Tokilive/releases/assets/$id" else null

    fun pickApk(names: List<String>) = names.firstOrNull { it.contains("android", true) && it.endsWith(".apk", true) }
        ?: names.firstOrNull { it.endsWith(".apk", true) }

    /** 从 tokilive-<版本>-android.apk 这类文件名里取版本；名字不合惯例时返回 null，调用方按 tag 版本处理。 */
    fun assetVersion(name: String): String? {
        val match = Regex("""^tokilive-(.+)-android\.apk$""", RegexOption.IGNORE_CASE).matchEntire(name.trim()) ?: return null
        return match.groupValues[1].takeIf { parse(it) != null }
    }

    fun signaturesCompatible(current: List<String>, incoming: List<String>): SignatureVerdict {
        if (incoming.isEmpty()) return SignatureVerdict.UNSIGNED_PACKAGE
        if (current.isEmpty()) return SignatureVerdict.NO_BASELINE
        return if (incoming.any { it in current }) SignatureVerdict.MATCH else SignatureVerdict.MISMATCH
    }

    fun constantTimeEquals(a: String, b: String): Boolean {
        if (a.length != b.length) return false
        var diff = 0
        for (i in a.indices) diff = diff or (a[i].code xor b[i].code)
        return diff == 0 && a.isNotEmpty()
    }

    /**
     * 只有校验、包名、签名都通过，并且用户已点过确认、系统允许安装未知来源时，才返回 OK。
     * 调用方拿到 OK 之后仍然必须交给系统安装界面，不能静默安装。
     */
    fun installDecision(
        checksumOk: Boolean,
        hasDigest: Boolean,
        packageName: String?,
        signature: SignatureVerdict,
        canInstallPackages: Boolean,
        userConfirmed: Boolean,
    ): String = when {
        !hasDigest -> "NO_CHECKSUM"
        !checksumOk -> "CHECKSUM"
        packageName != PACKAGE_ID -> "PACKAGE"
        signature == SignatureVerdict.MISMATCH -> "SIGNATURE"
        signature == SignatureVerdict.UNSIGNED_PACKAGE -> "UNSIGNED"
        signature == SignatureVerdict.NO_BASELINE -> "NO_BASELINE"
        !userConfirmed -> "NEED_CONFIRM"
        !canInstallPackages -> "UNKNOWN_SOURCES"
        else -> "OK"
    }

    fun decisionMessage(code: String) = when (code) {
        "OK" -> "校验通过。接下来会打开系统安装界面，需要你再次确认。不会静默安装。"
        "NO_CHECKSUM" -> "发行版没有 sha256，已拒绝下载安装。"
        "CHECKSUM" -> "校验和不匹配，安装包已放弃。"
        "PACKAGE" -> "这个包的应用 ID 不是 com.ichiyotsu.tokilive，已拒绝。"
        "SIGNATURE" -> "签名和当前安装不一致，已拒绝。"
        "UNSIGNED" -> "安装包没有签名，已拒绝。"
        "NO_BASELINE" -> "当前安装没有可对照的正式签名。请在持有同一把签名钥匙的电脑上安装。已停止。"
        "NEED_CONFIRM" -> "还没有确认安装。"
        "UNKNOWN_SOURCES" -> "系统还不允许这个应用安装软件包。请在接下来的设置里允许，然后再回来。"
        else -> "无法安装。"
    }
}
