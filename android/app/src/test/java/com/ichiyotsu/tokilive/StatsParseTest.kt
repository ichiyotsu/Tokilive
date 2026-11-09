package com.ichiyotsu.tokilive

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [31])
class StatsParseTest {
    @Test fun liveCounters() {
        val client = BiliClient()
        client.handle(JSONObject("""{"cmd":"DANMU_MSG","info":[[], "你好", [1, "甲", 0, 0, 0], [], [], [], 0, 0]}"""))
        client.handle(JSONObject("""{"cmd":"LIKE_INFO_V3_UPDATE","data":{"click_count":42}}"""))
        client.handle(JSONObject("""{"cmd":"ONLINE_RANK_COUNT","data":{"online_num":7}}"""))
        client.handle(JSONObject("""{"cmd":"WATCHED_CHANGE","data":{"text_large":"11人看过"}}"""))
        assertEquals(1L, client.stats.value.danmaku)
        assertEquals(42L, client.stats.value.likes)
        assertEquals(7L, client.stats.value.online)
        assertEquals("11人看过", client.stats.value.watched)
    }
}
