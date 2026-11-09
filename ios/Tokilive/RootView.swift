import SwiftUI

/// iPhone 用标签页；iPad 的 regular 宽度用 NavigationSplitView，对应 Split View / Stage Manager 里窗口变窄时退回紧凑布局。
struct RootView: View {
    @EnvironmentObject private var model: LiveModel
    @Environment(\.horizontalSizeClass) private var sizeClass
    @State private var tab = 0

    var body: some View {
        Group {
            if sizeClass == .regular {
                NavigationSplitView {
                    List(selection: $tab) {
                        Label("弹幕", systemImage: "text.bubble").tag(0)
                        Label("数据", systemImage: "chart.bar").tag(1)
                        Label("设置", systemImage: "gearshape").tag(2)
                    }
                    .navigationTitle("tokilive")
                } detail: {
                    detail
                }
            } else {
                TabView(selection: $tab) {
                    LiveScreen().tabItem { Label("弹幕", systemImage: "text.bubble") }.tag(0)
                    StatsScreen().tabItem { Label("数据", systemImage: "chart.bar") }.tag(1)
                    SettingsScreen().tabItem { Label("设置", systemImage: "gearshape") }.tag(2)
                }
            }
        }
        .tint(.purple)
    }

    @ViewBuilder private var detail: some View {
        switch tab {
        case 1: StatsScreen()
        case 2: SettingsScreen()
        default: LiveScreen()
        }
    }
}

struct LiveScreen: View {
    @EnvironmentObject private var model: LiveModel
    var body: some View {
        NavigationStack {
            VStack(spacing: 12) {
                HStack {
                    TextField("房间号", text: $model.room)
                        .keyboardType(.numberPad)
                        .textFieldStyle(.roundedBorder)
                    Button(model.connected ? "断开" : "连接") { model.toggle() }
                        .disabled(model.room.isEmpty && !model.connected)
                }
                Text(model.status).font(.footnote).foregroundStyle(.secondary)
                List(model.messages) { msg in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(msg.uname).font(.caption).foregroundStyle(.secondary)
                        Text(msg.text).font(.body)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            .padding()
            .navigationTitle("tokilive")
        }
    }
}

struct StatsScreen: View {
    @EnvironmentObject private var model: LiveModel
    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                stat("本场点赞", model.stats.likes)
                stat("弹幕", model.stats.danmaku)
                stat("当前在线", model.stats.online)
                Text(model.stats.watched.isEmpty ? "还没有收到看过人数" : model.stats.watched)
                    .foregroundStyle(.secondary)
                Spacer()
            }
            .padding()
            .navigationTitle("数据")
        }
    }
    private func stat(_ title: String, _ value: Int) -> some View {
        VStack(alignment: .leading) {
            Text(title).font(.headline)
            Text("\(value)").font(.largeTitle.weight(.bold))
        }
        .accessibilityElement(children: .combine)
    }
}

struct SettingsScreen: View {
    @EnvironmentObject private var model: LiveModel
    @Environment(\.colorScheme) private var colorScheme
    var body: some View {
        NavigationStack {
            Form {
                Section("外观") {
                    Picker("主题", selection: $model.theme) {
                        Text("跟随系统").tag(0)
                        Text("浅色").tag(1)
                        Text("深色").tag(2)
                    }
                }
                Section("账号") {
                    Text("扫码登录使用 B 站网页二维码，不收集 Apple 账号。")
                    if let image = model.qrImage {
                        image.resizable().scaledToFit().frame(width: 180, height: 180)
                        Text(model.qrText)
                    }
                    Button("获取登录二维码") { model.startQr() }
                }
                Section("更新") {
                    Text(model.updateMessage)
                    Button("检查公开版本") { model.checkUpdate() }
                    Text("不会在 App 里静默安装。新版本需要用 SideStore 重新签名安装。")
                        .font(.footnote)
                }
                Section("关于") {
                    Text("tokilive").font(.largeTitle.weight(.bold))
                    Text("1.2.0")
                    Text("iPhone 与 iPad 使用同一 Bundle ID：com.ichiyotsu.tokilive。不能像桌面端那样盖住其他 App。")
                        .font(.footnote)
                }
            }
            .navigationTitle("设置")
        }
        .preferredColorScheme(model.theme == 1 ? .light : model.theme == 2 ? .dark : nil)
        .accessibilityHint(colorScheme == .dark ? "深色" : "浅色")
    }
}
