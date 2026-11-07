package com.ichiyotsu.tokilive

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class UpdateLogicTest {
    @Test fun versionOrder() {
        assertTrue(UpdateLogic.isNewer("1.1.0", "1.0.0"))
        assertFalse(UpdateLogic.isNewer("1.0.0", "1.1.0"))
        assertFalse(UpdateLogic.isNewer("v1.1.0", "1.1.0"))
        assertTrue(UpdateLogic.compare("1.2.0", "1.1.10") > 0)
        assertTrue(UpdateLogic.isStable("1.1.0"))
        assertFalse(UpdateLogic.isStable("1.1.0-beta"))
    }

    @Test fun privateDownloadDropsAuthAndNeedsAssetId() {
        assertEquals("Bearer abc", UpdateLogic.authorizationHeader("abc", redirected = false))
        assertNull(UpdateLogic.authorizationHeader("abc", redirected = true))
        assertNull(UpdateLogic.authorizationHeader("", redirected = false))
        assertEquals("https://api.github.com/repos/ichiyotsu/tokilive/releases/assets/9", UpdateLogic.assetApi(9))
        assertEquals("tokilive-1.1.0-android.apk", UpdateLogic.pickApk(listOf("tokilive-1.1.0-setup.exe", "tokilive-1.1.0-android.apk")))
    }

    @Test fun installRequiresChecksumSignatureAndConfirmation() {
        assertEquals(SignatureVerdict.MATCH, UpdateLogic.signaturesCompatible(listOf("aa"), listOf("aa")))
        assertEquals(SignatureVerdict.MISMATCH, UpdateLogic.signaturesCompatible(listOf("aa"), listOf("bb")))
        assertEquals(SignatureVerdict.NO_BASELINE, UpdateLogic.signaturesCompatible(emptyList(), listOf("aa")))
        assertEquals(SignatureVerdict.UNSIGNED_PACKAGE, UpdateLogic.signaturesCompatible(listOf("aa"), emptyList()))
        assertEquals("NO_CHECKSUM", UpdateLogic.installDecision(false, false, UpdateLogic.PACKAGE_ID, SignatureVerdict.MATCH, true, true))
        assertEquals("CHECKSUM", UpdateLogic.installDecision(false, true, UpdateLogic.PACKAGE_ID, SignatureVerdict.MATCH, true, true))
        assertEquals("PACKAGE", UpdateLogic.installDecision(true, true, "other.app", SignatureVerdict.MATCH, true, true))
        assertEquals("SIGNATURE", UpdateLogic.installDecision(true, true, UpdateLogic.PACKAGE_ID, SignatureVerdict.MISMATCH, true, true))
        assertEquals("NEED_CONFIRM", UpdateLogic.installDecision(true, true, UpdateLogic.PACKAGE_ID, SignatureVerdict.MATCH, true, false))
        assertEquals("UNKNOWN_SOURCES", UpdateLogic.installDecision(true, true, UpdateLogic.PACKAGE_ID, SignatureVerdict.MATCH, false, true))
        assertEquals("OK", UpdateLogic.installDecision(true, true, UpdateLogic.PACKAGE_ID, SignatureVerdict.MATCH, true, true))
        assertTrue(UpdateLogic.decisionMessage("OK").contains("系统安装"))
        val sample = "github_pat_" + "abcdefghijklmnopqrstuvwxyz"
        assertFalse(UpdateLogic.redact(sample).contains("abcdefghij"))
    }
}
