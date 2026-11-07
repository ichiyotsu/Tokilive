package com.ichiyotsu.tokilive

import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import com.github.takahirom.roborazzi.captureRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [35], qualifiers = "w412dp-h915dp-xxhdpi")
class ScreenshotTest {
    @get:Rule val rule = createComposeRule()
    private fun shot(name: String, dark: Boolean, tab: Int) {
        val vm = LiveViewModel(RuntimeEnvironment.getApplication()); vm.demo(); vm.dynamicColor = false
        rule.setContent { TokiTheme(dark = dark, dynamic = false) { TokiApp(vm, tab) } }
        rule.mainClock.advanceTimeBy(1500)
        rule.onRoot().captureRoboImage("build/shots/$name.png")
    }
    @Test fun liveLight() = shot("v2-android-live-light", false, 0)
    @Test fun liveDark() = shot("v2-android-live-dark", true, 0)
    @Test fun stats() = shot("v2-android-stats-dark", true, 1)
    @Test fun settings() = shot("v2-android-settings-light", false, 2)
}
