package com.ichiyotsu.tokilive

import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import java.io.File
import java.security.MessageDigest

object ApkInstall {
    fun sha256(file: File): String = file.inputStream().use { input ->
        val digest = MessageDigest.getInstance("SHA-256")
        val buf = ByteArray(64 * 1024)
        while (true) {
            val n = input.read(buf)
            if (n < 0) break
            digest.update(buf, 0, n)
        }
        digest.digest().joinToString("") { "%02x".format(it) }
    }

    fun signingHex(info: android.content.pm.SigningInfo?): List<String> {
        if (info == null) return emptyList()
        val arr = if (info.hasMultipleSigners()) info.apkContentsSigners else info.signingCertificateHistory
        return arr.map { cert -> MessageDigest.getInstance("SHA-256").digest(cert.toByteArray()).joinToString("") { "%02x".format(it) } }
    }

    fun decide(context: Context, apk: File, expectedSha: String, userConfirmed: Boolean): String {
        val pm = context.packageManager
        val flags = PackageManager.GET_SIGNING_CERTIFICATES
        val archive = if (Build.VERSION.SDK_INT >= 28) pm.getPackageArchiveInfo(apk.path, flags) else null
        archive?.applicationInfo?.sourceDir = apk.path
        val incoming = signingHex(archive?.signingInfo)
        val currentInfo = if (Build.VERSION.SDK_INT >= 28) pm.getPackageInfo(context.packageName, flags) else null
        val current = signingHex(currentInfo?.signingInfo)
        val checksumOk = expectedSha.isNotBlank() && UpdateLogic.constantTimeEquals(sha256(apk), expectedSha.lowercase())
        return UpdateLogic.installDecision(
            checksumOk = checksumOk,
            hasDigest = expectedSha.isNotBlank(),
            packageName = archive?.packageName,
            signature = UpdateLogic.signaturesCompatible(current, incoming),
            canInstallPackages = pm.canRequestPackageInstalls(),
            userConfirmed = userConfirmed,
        )
    }

    fun openUnknownSources(context: Context) {
        val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + context.packageName))
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    }

    /** 交给系统安装界面。不使用 PackageInstaller 的免确认提交。 */
    fun launchSystemInstaller(context: Context, apk: File) {
        val uri = FileProvider.getUriForFile(context, context.packageName + ".fileprovider", apk)
        val intent = Intent(Intent.ACTION_VIEW)
            .setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    }
}
