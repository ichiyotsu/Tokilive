# tokilive

B 站直播弹幕工具，1.1.0。Windows 桌面端和 Android 端放在同一个仓库里。

- `windows/`：Electron。设置页、悬浮窗、便携版和 NSIS 安装包。
- `android/`：Jetpack Compose。应用 ID 仍是 `com.ichiyotsu.tokilive`。

1.0.0 的发行版还在 Releases 里，没有被这次改动替换。1.0.0 本身没有应用内更新，升到 1.1.0 需要手动安装一次。签名用的钥匙不在这个仓库里。

```bash
cd windows && npm ci && npm test && npm run dist
cd android && ./gradlew :app:assembleRelease
```

Android 正式签名使用环境变量 `TOKILIVE_KEYSTORE`、`TOKILIVE_STORE_PASSWORD`、`TOKILIVE_KEY_ALIAS`、`TOKILIVE_KEY_PASSWORD`。没有这些变量时打出来的是未签名包。
