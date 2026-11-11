# Tokilive

Tokilive 是一款哔哩哔哩直播弹幕工具，Windows、Android 和 iOS / iPadOS 各有一份客户端。Windows 上是可以锁定和改大小的弹幕悬浮窗，旁边有实时数据小窗；登录后可以把弹幕发回当前直播间，也可以把本机页面交给 OBS 或直播姬的浏览器源。手机端用来连接直播间，并查看弹幕、点赞和在线人数。三个平台的应用内更新都只对照 GitHub Releases，默认只提示正式版。

B站直播弹幕工具。当前开发版本 **1.2.0**。Windows、Android、iOS / iPadOS 的应用 ID 都是 `com.ichiyotsu.tokilive`。

| 平台 | 版本 |
| --- | --- |
| Windows | `windows/package.json` 的 `version` 1.2.0。安装包名为 `tokilive-1.2.0-setup.exe`、`tokilive-1.2.0-portable.exe`。 |
| Android | `versionName` 1.2.0，`versionCode` 3。 |
| iOS / iPadOS | `CFBundleShortVersionString` 1.2.0，`CFBundleVersion` 3。 |

已发布的 [v1.0.0](https://github.com/ichiyotsu/Tokilive/releases/tag/v1.0.0) 和 [v1.1.0](https://github.com/ichiyotsu/Tokilive/releases/tag/v1.1.0) 仍保留。1.2.0 还没有发布。1.0.0，以及 1.1.0 里那套私人仓库授权，都需要手动安装 1.2.0 一次。仓库现在是公开的，1.2.0 的更新检查不再要令牌。

签名钥匙不在这个仓库里。Android 正式包要在父机上用原来的钥匙签名。

## 版本规则

`X.Y.Z`（例如 `0.1.0`）是正式版。`X.Y.Z-bN`（例如 `0.1.1-b1`、`0.1.1-b2`）是测试版。tag 用 `v` 前缀，例如 `v0.1.1-b1`。

发布顺序是 `0.1.0` → `0.1.1-b1` → `0.1.1-b2` → … → `0.1.1`。测试版的 `X.Y.Z` 比已经发出的正式版超前几个，属于正常情况，并不表示那个正式版已经发布。同一个 `X.Y.Z` 上，测试版小于正式版：`0.1.1-b1` < `0.1.1`。`b` 后面的序号按数字比较，所以 `b2` < `b10`。

推送 `v*` tag 时，`.github/workflows/release.yml` 看 tag 里有没有 `-b`。带 `-b` 的发成 GitHub prerelease，并且不标成 latest。不带 `-b` 的发成正式 release，并标成 latest。这个工作流只设置发行类型，不在这里重新打包。

应用内更新默认只提供正式版。正在使用正式版的人不会收到 `X.Y.Z-bN`。上面的开发版本仍是 **1.2.0**。Android `versionCode` 按每次发布加 1（1.0.0 是 1，1.1.0 是 2，1.2.0 是 3），测试版也继续加，不跟着 `versionName` 回退。

## 安装已发布的版本

从 [Releases](https://github.com/ichiyotsu/Tokilive/releases) 下载。Windows 安装包可以选择目录，包括 D:。便携版不用安装。

## 从源码构建

```bash
cd windows && npm ci && npm test && npm run dist
cd android && ./gradlew :app:assembleRelease
```

没有 `TOKILIVE_KEYSTORE` 时，Android 打出来的是未签名包。变量是 `TOKILIVE_KEYSTORE`、`TOKILIVE_STORE_PASSWORD`、`TOKILIVE_KEY_ALIAS`、`TOKILIVE_KEY_PASSWORD`。

iOS 工程在 `ios/Tokilive.xcodeproj`。这台 Linux 环境不能编出 IPA。`.github/workflows/ios.yml` 在 macOS 上用 `xcodebuild` 打未签名归档，并打成 IPA 工件。工作流失败就说明当前托管镜像编不过，不能把源码 zip 伪装成 IPA。部署目标是 iOS 26.0。2026-11-03 的 [Xcode Releases](https://xcodereleases.com/) 记录了 Xcode 27.3 和 iOS SDK 27.3；工程没有调用未在文档中确认的 iOS 27 专有 API，因此可以在 iOS 27 上作为现有 SwiftUI 应用运行。

## Windows 说明

设置界面按 [WinUI 设计原则](https://learn.microsoft.com/zh-cn/windows/apps/design/design-principles) 安排导航、间距、键盘焦点、主题、高对比和高窄窗口，但进程仍是 Electron，不是原生 WinUI 3。原生迁移会丢掉现有的 CSS 主题和透明悬浮窗，所以没有改运行时。体积大头是 Electron 和内置字体（思源黑体、苹方等仍保留，因为它们是可选字体功能）。性能模式和进程结束功能已删除。

歌词只读 Windows 系统媒体会话（Windows 10 / 11 的 SMTC）。QQ 音乐和网易云如果向系统报告了正在播放的曲目，会优先标出来。不读取它们的本地数据库，也不注入进程。歌词来自你导入的 LRC / TTML，或 [LRCLIB](https://lrclib.net/docs.txt) 这个公开歌词库。没有时间轴就不会显示逐字效果。BetterLyrics 是 GPL-3.0，这里没有复制它的代码。

独占全屏游戏不经过桌面合成。实验性置顶只抬高 tokilive 自己的窗口，不注入游戏，也不保证盖住独占全屏。无边框窗口化更有机会。

## Android 说明

界面以 Material 3 Expressive 为主：主题使用 `MotionScheme.expressive()`，底栏是选中项才展开文字的胶囊，旁边是 18dp 圆角的独立操作按钮，并避开系统手势条。宽度达到 840dp（M3 expanded）时改用侧栏，手机竖屏仍用底栏。这些选择对照 [Material 3](https://developer.android.com/develop/ui/compose/designsystems/material3?hl=zh-cn)、[大屏](https://developer.android.com/media/implement/surfaces/large_screens?hl=zh-cn)，以及 Apache-2.0 的 [md3e-skill](https://github.com/mfskys/md3e-skill/tree/main) 里对导航栏、导航轨和形状策略的整理。没有复制该仓库的模板代码。底栏不是一条所有项都一样的通用胶囊。

`minSdk` 是 31（Android 12）。`compileSdk` / `targetSdk` 是 37，对应当时 sdkmanager 能安装的 `platforms;android-37` 系列。没有把未在平台包里出现的代号写成已支持。

## 用 SideStore 安装 iOS 版

下面只复述 SideStore 官方文档，链接以文档站为准。这里说的是 GitHub 仓库里的 `README.md`，不是 readme.com 那项服务。

官方入口：

- 文档首页：https://docs.sidestore.io/
- 准备：https://docs.sidestore.io/docs/installation/prerequisites
- 安装：https://docs.sidestore.io/docs/installation/install
- 常见问题：https://docs.sidestore.io/docs/faq
- 软件源：https://docs.sidestore.io/docs/advanced/app-sources
- SideStore 仓库：https://github.com/SideStore/SideStore

文档写明的前提（2026-11 查阅）：

- iPhone、iPad 或 iPod touch，有密码，系统为 iOS / iPadOS 15.0 或更高。第一次安装需要一台电脑：Windows 8 或更高、macOS High Sierra 或更高、较新的 Linux，或未受托管限制的 Chromebook。
- 一个 Apple 账号（文档也称为 Apple ID）。Wi-Fi，不能只用蜂窝网络。
- 安装 [LocalDevVPN](https://apps.apple.com/app/id1588445159)（App Store），或从 AltStore PAL 源安装。连接这个只连本机的 VPN。文档说明它不连远程服务器。每次安装、更新或刷新都要开着。
- 按电脑系统安装 [iloader](https://github.com/SideStore/SideStore/releases) 对应的安装包。Windows 另需苹果官网的 iTunes 或「Apple 设备」应用；文档写明不支持 32 位 Windows，也不支持 Windows 10 的 ARM 版。Linux 需要 `usbmuxd`。iloader 的问题文档指向 idevice Discord，不是 SideStore 服务器。

安装步骤（文档「Install」页）：

1. 用 USB 连接设备，在设备上信任这台电脑。
2. 打开 iloader，登录 Apple 账号。文档说明这个账号不必是设备上已登录的那个，并且区分大小写。
3. 选中设备，选择 Install SideStore (Stable)。
4. 在设备上打开设置，信任该开发者 App。iOS 16 及以上还要打开开发者模式并重启。
5. 打开 LocalDevVPN 并连接。打开 SideStore，登录刚才用的 Apple 账号。
6. 在「My Apps」里点 SideStore 旁边的天数，完成刷新。

签名有效期（文档 FAQ 与安装页）：

- 免费 Apple 账号：同时只能安装 3 个 App（含 SideStore 自己），一周最多 10 个不同的 App ID。安装页把刷新按钮写成「7 DAYS」，表示到期前的天数，点它刷新签名。过期后 App 打不开，需要再刷新，不是 tokilive 自己能续期。
- 付费的每年 99 美元 Apple Developer 账号：文档写明可以去掉 3 个 App 的限制，并把有效期变成 365 天。这是苹果账号的限制，不是 tokilive 的开关。

更新 tokilive：

- App 内只检查公开的 GitHub Release，并说明发现新版本。它不会静默安装 IPA，也不会向你要 Apple 账号。
- 未签名 IPA 若由上面的 GitHub Actions 打出，需要你在自己的电脑上用 SideStore / iloader 用你的 Apple 账号重新签名，再装到设备上。仓库里不放签名证书。
- 若以后发布 SideStore 源，添加地址的形式是文档里的 `sidestore://source?url=` 后面接源的 URL。用 AltStudio 生成的源要删掉默认的 `marketplaceID` 和 `Build`，否则 SideStore 会把它当成公证源并拒绝添加。源的格式以 https://docs.sidestore.io/docs/advanced/app-sources 为准。在没有发布经过校验的源之前，不要把任意链接当成官方 tokilive 源。

iOS 版不能像 Windows 悬浮窗那样盖住其他 App。后台时系统可以挂起连接。扫码登录用的是 B 站二维码，不收集 Apple ID。

## 许可

应用代码沿用仓库原有的 MIT 声明。字体文件自带目录里的许可。LRCLIB 按它的 API 文档使用，并带上 User-Agent。BetterLyrics 为 GPL-3.0，Lyricify 的站点只作产品参照，都没有把它们的代码或品牌资源放进本仓库。md3e-skill 为 Apache-2.0，只参考了其中对 Material 3 Expressive 的整理。
