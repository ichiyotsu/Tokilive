package com.ichiyotsu.tokilive

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** 令牌只经过 Android Keystore。不要把明文写进日志或 Compose 状态。 */
object TokenStore {
    private const val KEY = "tokilive.github"
    private const val PREF = "tokilive.auth"
    private const val FIELD = "blob"

    fun save(context: Context, token: String) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val iv = cipher.iv
        val enc = cipher.doFinal(token.toByteArray(Charsets.UTF_8))
        val blob = ByteArray(iv.size + enc.size)
        iv.copyInto(blob)
        enc.copyInto(blob, iv.size)
        context.getSharedPreferences(PREF, Context.MODE_PRIVATE).edit().putString(FIELD, Base64.encodeToString(blob, Base64.NO_WRAP)).apply()
    }

    fun read(context: Context): String? {
        val raw = context.getSharedPreferences(PREF, Context.MODE_PRIVATE).getString(FIELD, null) ?: return null
        return try {
            val blob = Base64.decode(raw, Base64.NO_WRAP)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, blob.copyOfRange(0, 12)))
            String(cipher.doFinal(blob.copyOfRange(12, blob.size)), Charsets.UTF_8)
        } catch (_: Exception) { null }
    }

    fun clear(context: Context) {
        context.getSharedPreferences(PREF, Context.MODE_PRIVATE).edit().remove(FIELD).apply()
    }

    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(KEY, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(KeyGenParameterSpec.Builder(KEY, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        return gen.generateKey()
    }
}
