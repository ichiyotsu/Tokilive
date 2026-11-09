package com.ichiyotsu.tokilive

import kotlinx.coroutines.*
import org.junit.Assume
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

/** 真实直播间连通性（设置 -Plive=7777 时运行） */
@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class LiveRoomTest {
    @Test fun realRoom() = runBlocking {
        val room = System.getProperty("live") ?: ""; Assume.assumeTrue(room.isNotEmpty())
        val c = BiliClient(); val got = mutableListOf<LiveMsg>()
        val scope = CoroutineScope(Dispatchers.IO); val j = scope.launch { c.messages.collect { got.add(it) } }
        c.start(scope, room); delay(20_000); c.stop(); j.cancel()
        println("LIVE status=${c.status.value} msgs=${got.size} types=${got.groupingBy { it.type }.eachCount()} stats=${c.stats.value} sample=${got.take(3)}")
        check(got.isNotEmpty())
    }
}
