import Foundation
import SwiftUI
import CoreImage.CIFilterBuiltins

struct LiveMessage: Identifiable, Equatable {
    let id = UUID()
    var uname: String
    var text: String
}

struct LiveNumbers: Equatable {
    var likes = 0
    var danmaku = 0
    var online = 0
    var watched = ""
}

@MainActor
final class LiveModel: ObservableObject {
    @Published var room = ""
    @Published var connected = false
    @Published var status = "未连接"
    @Published var messages: [LiveMessage] = []
    @Published var stats = LiveNumbers()
    @Published var theme = 0
    @Published var qrImage: Image?
    @Published var qrText = ""
    @Published var updateMessage = "1.2.0。检查的是公开的 GitHub Releases。"
    private var socket: URLSessionWebSocketTask?
    private var cookie = ""

    func toggle() {
        if connected { stop() } else { start() }
    }

    func start() {
        let id = room.filter(\.isNumber)
        guard !id.isEmpty else { status = "请先填写房间号"; return }
        stop()
        connected = true
        status = "正在解析房间号…"
        Task {
            do {
                let rid = try await Self.roomID(id)
                let info = try await Self.danmuInfo(room: rid, cookie: cookie)
                status = "正在连接…"
                let task = URLSession.shared.webSocketTask(with: info.url)
                socket = task
                task.resume()
                let auth: [String: Any] = ["uid": 0, "roomid": rid, "protover": 0, "platform": "web", "type": 2, "key": info.token]
                let body = try JSONSerialization.data(withJSONObject: auth)
                try await task.send(.data(Self.pack(op: 7, body: body)))
                status = "已连接 · 房间 \(rid)"
                receive(task)
            } catch {
                status = "连接失败：\(error.localizedDescription)"
                connected = false
            }
        }
    }

    func stop() {
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connected = false
        status = "未连接"
    }

    func startQr() {
        qrText = "正在获取二维码…"
        Task {
            do {
                var req = URLRequest(url: URL(string: "https://passport.bilibili.com/x/passport-login/web/qrcode/generate")!)
                req.setValue("tokilive/1.2.0", forHTTPHeaderField: "User-Agent")
                let (data, _) = try await URLSession.shared.data(for: req)
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
                let payload = json?["data"] as? [String: Any]
                let url = payload?["url"] as? String ?? ""
                qrImage = Self.qr(url)
                qrText = "请用哔哩哔哩 App 扫码。确认后的 Cookie 只留在本机内存，不会写入仓库。"
            } catch {
                qrText = "获取二维码失败"
            }
        }
    }

    func checkUpdate() {
        updateMessage = "正在检查…"
        Task {
            var req = URLRequest(url: URL(string: "https://api.github.com/repos/ichiyotsu/tokilive/releases?per_page=10")!)
            req.setValue("tokilive/1.2.0 (https://github.com/ichiyotsu/tokilive)", forHTTPHeaderField: "User-Agent")
            req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            do {
                let (data, response) = try await URLSession.shared.data(for: req)
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard code == 200, let list = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
                    updateMessage = "检查失败：HTTP \(code)"
                    return
                }
                let latest = list.compactMap { $0["tag_name"] as? String }.first { !$0.contains("-") } ?? ""
                let version = latest.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
                if Self.isNewer(version, than: "1.2.0") {
                    updateMessage = "发现 \(version)。请到 GitHub Releases 下载后，用 SideStore 重新签名安装。本 App 不会静默安装 IPA。"
                } else {
                    updateMessage = "已经是 1.2.0，或没有更新的正式版。"
                }
            } catch {
                updateMessage = "检查失败：\(error.localizedDescription)"
            }
        }
    }

    private func receive(_ task: URLSessionWebSocketTask) {
        task.receive { [weak self] result in
            Task { @MainActor in
                guard let self else { return }
                if case .success(let message) = result, case .string(let text) = message {
                    self.ingest(text)
                }
                if self.connected { self.receive(task) }
            }
        }
    }

    private func ingest(_ text: String) {
        guard let data = text.data(using: .utf8),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let cmd = json["cmd"] as? String else { return }
        let name = cmd.split(separator: ":").first.map(String.init) ?? cmd
        if name == "DANMU_MSG", let info = json["info"] as? [Any], info.count > 2 {
            let body = info[1] as? String ?? ""
            let user = info[2] as? [Any]
            let uname = user?.dropFirst().first as? String ?? "观众"
            messages.append(LiveMessage(uname: uname, text: body))
            if messages.count > 200 { messages.removeFirst(messages.count - 200) }
            stats.danmaku += 1
        } else if name == "LIKE_INFO_V3_UPDATE", let data = json["data"] as? [String: Any] {
            stats.likes = data["click_count"] as? Int ?? stats.likes
        } else if name == "ONLINE_RANK_COUNT" || name == "ONLINE_RANK_V2", let data = json["data"] as? [String: Any] {
            stats.online = data["online_count"] as? Int ?? data["online_num"] as? Int ?? data["count"] as? Int ?? stats.online
        } else if name == "WATCHED_CHANGE", let data = json["data"] as? [String: Any] {
            stats.watched = data["text_large"] as? String ?? stats.watched
        }
    }

    static func isNewer(_ remote: String, than current: String) -> Bool {
        let a = remote.split(separator: ".").map { Int($0) ?? 0 }
        let b = current.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(a.count, b.count) {
            let l = i < a.count ? a[i] : 0
            let r = i < b.count ? b[i] : 0
            if l != r { return l > r }
        }
        return false
    }

    private struct Danmu { let url: URL; let token: String }
    private static func roomID(_ id: String) async throws -> Int {
        let url = URL(string: "https://api.live.bilibili.com/room/v1/Room/room_init?id=\(id)")!
        let (data, _) = try await URLSession.shared.data(from: url)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let dataObj = json?["data"] as? [String: Any]
        guard let rid = dataObj?["room_id"] as? Int else { throw URLError(.badServerResponse) }
        return rid
    }
    private static func danmuInfo(room: Int, cookie: String) async throws -> Danmu {
        var req = URLRequest(url: URL(string: "https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id=\(room)&type=0")!)
        if !cookie.isEmpty { req.setValue(cookie, forHTTPHeaderField: "Cookie") }
        let (data, _) = try await URLSession.shared.data(for: req)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let dataObj = json?["data"] as? [String: Any]
        let hosts = dataObj?["host_list"] as? [[String: Any]]
        let host = hosts?.first?["host"] as? String ?? ""
        let port = hosts?.first?["wss_port"] as? Int ?? 443
        let token = dataObj?["token"] as? String ?? ""
        guard let url = URL(string: "wss://\(host):\(port)/sub") else { throw URLError(.badURL) }
        return Danmu(url: url, token: token)
    }
    private static func pack(op: Int, body: Data) -> Data {
        var out = Data(count: 16 + body.count)
        func put(_ value: UInt32, at offset: Int) {
            var be = value.bigEndian
            withUnsafeBytes(of: &be) { out.replaceSubrange(offset..<offset + 4, with: $0) }
        }
        put(UInt32(16 + body.count), at: 0)
        out[4] = 0; out[5] = 16; out[6] = 0; out[7] = 1
        put(UInt32(op), at: 8)
        put(1, at: 12)
        out.replaceSubrange(16..<out.count, with: body)
        return out
    }
    private static func qr(_ text: String) -> Image? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(text.utf8)
        guard let output = filter.outputImage else { return nil }
        let scaled = output.transformed(by: CGAffineTransform(scaleX: 8, y: 8))
        let context = CIContext()
        guard let cg = context.createCGImage(scaled, from: scaled.extent) else { return nil }
        return Image(decorative: cg, scale: 1)
    }
}
