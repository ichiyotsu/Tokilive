# tokilive

B 站直播弹幕工具。当前版本 **1.2.0**。应用 ID / Bundle ID 都是 `com.ichiyotsu.tokilive`。

| 平台 | 版本字段 |
| --- | --- |
| Windows | `windows/package.json` 的 `version`：1.2.0 |
| Android | `versionName` 1.2.0，`versionCode` 3 |
| iOS / iPadOS | `CFBundleShortVersionString` 1.2.0，`CFBundleVersion` 3（`ios/Tokilive/Info.plist`） |

已发布的 1.0.0 和 1.1.0 仍在 Releases 里。1.2.0 还没有发布。从 1.0.0 或 1.1.0 升到 1.2.0 需要手动安装一次。签名钥匙不在这个仓库里。

```bash
cd windows && npm ci && npm test && npm run dist
cd android && ./gradlew :app:assembleRelease
```

安装包文件名带版本号：`tokilive-1.2.0-setup.exe`、`tokilive-1.2.0-portable.exe`。Android 正式签名使用环境变量 `TOKILIVE_KEYSTORE`、`TOKILIVE_STORE_PASSWORD`、`TOKILIVE_KEY_ALIAS`、`TOKILIVE_KEY_PASSWORD`。没有这些变量时打出来的是未签名包。
