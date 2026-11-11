import Foundation
import SwiftUI
import CoreImage.CIFilterBuiltins
import Compression

struct LiveMessage: Identifiable, Equatable {
    let id = UUID()
    var type = "danmaku"
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
    @Published var show: [String: Bool] = ["danmaku": true, "gift": true, "superchat": true, "guard": true, "enter": true, "follow": true, "like": true]
    @Published var updateMessage = "1.3.0。检查的是公开的 GitHub Releases。"

    func binding(_ key: String) -> Binding<Bool> {
        Binding(get: { self.show[key] ?? true }, set: { self.show[key] = $0 })
    }
    private var socket: URLSessionWebSocketTask?
    private var beat: Task<Void, Never>?
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
                let auth: [String: Any] = ["uid": 0, "roomid": rid, "protover": 2, "platform": "web", "type": 2, "key": info.token]
                let body = try JSONSerialization.data(withJSONObject: auth)
                try await task.send(.data(Self.pack(op: 7, body: body)))
                status = "已连接 · 房间 \(rid)"
                beat?.cancel()
                beat = Task { [weak self] in
                    while !Task.isCancelled {
                        try? await Task.sleep(for: .seconds(30))
                        await self?.sendBeat()
                    }
                }
                receive(task)
            } catch {
                status = "连接失败：\(error.localizedDescription)"
                connected = false
            }
        }
    }

    func stop() {
        beat?.cancel()
        beat = nil
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connected = false
        status = "未连接"
    }

    private func sendBeat() async {
        guard let socket else { return }
        try? await socket.send(.data(Self.pack(op: 2, body: Data())))
    }

    func startQr() {
        qrText = "正在获取二维码…"
        Task {
            do {
                var req = URLRequest(url: URL(string: "https://passport.bilibili.com/x/passport-login/web/qrcode/generate")!)
                req.setValue("tokilive/1.3.0", forHTTPHeaderField: "User-Agent")
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
            var req = URLRequest(url: URL(string: "https://api.github.com/repos/ichiyotsu/Tokilive/releases?per_page=20")!)
            req.setValue("tokilive/1.3.0 (https://github.com/ichiyotsu/Tokilive)", forHTTPHeaderField: "User-Agent")
            req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            do {
                let (data, response) = try await URLSession.shared.data(for: req)
                let code = (response as? HTTPURLResponse)?.statusCode ?? 0
                guard code == 200, let list = try JSONSerialization.jsonObject(with: data) as? [[String: Any]] else {
                    updateMessage = "检查失败：HTTP \(code)"
                    return
                }
                let current = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.3.0"
                let version = Self.latestStable(list)
                if Self.isNewer(version, than: current) {
                    updateMessage = "发现 \(version)。请到 GitHub Releases 下载后，用 SideStore 重新签名安装。本 App 不会静默安装 IPA。"
                } else {
                    updateMessage = "已经是 \(current)，或没有更新的正式版。"
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
                if case .success(let message) = result {
                    switch message {
                    case .string(let text): self.ingest(text)
                    case .data(let data): self.unpack(data)
                    @unknown default: break
                    }
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
            add("danmaku", uname, body)
            stats.danmaku += 1
        } else if name == "SEND_GIFT", let data = json["data"] as? [String: Any] {
            add("gift", data["uname"] as? String ?? "", "\((data["giftName"] as? String) ?? "") x\((data["num"] as? Int) ?? 1)")
        } else if name == "SUPER_CHAT_MESSAGE", let data = json["data"] as? [String: Any] {
            let user = data["user_info"] as? [String: Any]
            add("superchat", user?["uname"] as? String ?? "", data["message"] as? String ?? "")
        } else if name == "GUARD_BUY", let data = json["data"] as? [String: Any] {
            add("guard", data["username"] as? String ?? "", data["gift_name"] as? String ?? "上舰")
        } else if name == "LIKE_INFO_V3_CLICK", let data = json["data"] as? [String: Any] {
            add("like", data["uname"] as? String ?? "", data["like_text"] as? String ?? "点赞了")
        } else if name == "LIKE_INFO_V3_UPDATE", let data = json["data"] as? [String: Any] {
            stats.likes = data["click_count"] as? Int ?? stats.likes
        } else if name == "ONLINE_RANK_COUNT" || name == "ONLINE_RANK_V2", let data = json["data"] as? [String: Any] {
            stats.online = data["online_count"] as? Int ?? data["online_num"] as? Int ?? data["count"] as? Int ?? stats.online
        } else if name == "WATCHED_CHANGE", let data = json["data"] as? [String: Any] {
            stats.watched = data["text_large"] as? String ?? stats.watched
        }
    }

    private func add(_ type: String, _ name: String, _ text: String) {
        guard show[type] != false else { return }
        messages.append(LiveMessage(type: type, uname: name, text: text))
        if messages.count > 200 { messages.removeFirst(messages.count - 200) }
    }

    private func unpack(_ data: Data) {
        var offset = 0
        while offset + 16 <= data.count {
            let len = Int(be32(data, offset))
            let header = Int(be16(data, offset + 4))
            let ver = be16(data, offset + 6)
            let op = be32(data, offset + 8)
            guard len >= 16, offset + len <= data.count else { return }
            let body = data.subdata(in: (offset + header)..<(offset + len))
            offset += len
            if op == 5, ver == 0, let text = String(data: body, encoding: .utf8) { ingest(text) }
            else if op == 5, ver == 2, let inflated = inflate(body) { unpack(inflated) }
        }
    }

    private func be32(_ data: Data, _ offset: Int) -> UInt32 {
        (UInt32(data[offset]) << 24) | (UInt32(data[offset + 1]) << 16) | (UInt32(data[offset + 2]) << 8) | UInt32(data[offset + 3])
    }
    private func be16(_ data: Data, _ offset: Int) -> UInt16 { (UInt16(data[offset]) << 8) | UInt16(data[offset + 1]) }

    private func inflate(_ data: Data) -> Data? {
        let capacity = 1 << 20
        let dst = UnsafeMutablePointer<UInt8>.allocate(capacity: capacity)
        defer { dst.deallocate() }
        let count = data.withUnsafeBytes { raw -> Int in
            guard let base = raw.bindMemory(to: UInt8.self).baseAddress else { return 0 }
            return compression_decode_buffer(dst, capacity, base, data.count, nil, COMPRESSION_ZLIB)
        }
        return count > 0 ? Data(bytes: dst, count: count) : nil
    }

    static func latestStable(_ releases: [[String: Any]]) -> String {
        var best = ""
        for item in releases {
            if jsonBool(item["draft"]) || jsonBool(item["prerelease"]) { continue }
            guard let tag = item["tag_name"] as? String else { continue }
            var version = tag.trimmingCharacters(in: .whitespacesAndNewlines)
            if version.hasPrefix("v") || version.hasPrefix("V") { version.removeFirst() }
            guard isStable(version) else { continue }
            if best.isEmpty || compare(version, best) > 0 { best = version }
        }
        return best
    }

    private static func jsonBool(_ value: Any?) -> Bool {
        if let value = value as? Bool { return value }
        if let value = value as? NSNumber { return value.boolValue }
        return false
    }

    static func isStable(_ version: String) -> Bool {
        guard let parsed = parseVersion(version) else { return false }
        return parsed.pre.isEmpty
    }

    static func isNewer(_ remote: String, than current: String) -> Bool {
        compare(remote, current) > 0
    }

    static func compare(_ left: String, _ right: String) -> Int {
        guard let a = parseVersion(left), let b = parseVersion(right) else {
            if left == right { return 0 }
            return left < right ? -1 : 1
        }
        let n = max(a.core.count, b.core.count)
        for i in 0..<n {
            let d = (i < a.core.count ? a.core[i] : 0) - (i < b.core.count ? b.core[i] : 0)
            if d != 0 { return d }
        }
        if a.pre.isEmpty && !b.pre.isEmpty { return 1 }
        if !a.pre.isEmpty && b.pre.isEmpty { return -1 }
        let m = max(a.pre.count, b.pre.count)
        for i in 0..<m {
            if i >= a.pre.count { return -1 }
            if i >= b.pre.count { return 1 }
            let d = compareIdentifier(a.pre[i], b.pre[i])
            if d != 0 { return d }
        }
        return 0
    }

    private static func parseVersion(_ input: String) -> (core: [Int], pre: [String])? {
        var raw = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if raw.hasPrefix("v") || raw.hasPrefix("V") { raw.removeFirst() }
        let pattern = #"^(\d+(?:\.\d+)*)(?:-([0-9A-Za-z.-]+))?$"#
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return nil }
        let range = NSRange(raw.startIndex..<raw.endIndex, in: raw)
        guard let match = regex.firstMatch(in: raw, range: range),
              let coreRange = Range(match.range(at: 1), in: raw) else { return nil }
        let pieces = raw[coreRange].split(separator: ".")
        let core = pieces.compactMap { Int($0) }
        if core.count != pieces.count { return nil }
        var pre: [String] = []
        let preNS = match.range(at: 2)
        if preNS.location != NSNotFound, let preRange = Range(preNS, in: raw) {
            pre = raw[preRange].split(separator: ".").map(String.init)
            if pre.isEmpty { return nil }
        }
        return (core, pre)
    }

    private static func betaNumber(_ id: String) -> Int? {
        guard id.count > 1, id.first == "b" else { return nil }
        let digits = id.dropFirst()
        guard digits.allSatisfy({ $0.isNumber }), let n = Int(digits) else { return nil }
        return n
    }

    private static func compareIdentifier(_ a: String, _ b: String) -> Int {
        if let ba = betaNumber(a), let bb = betaNumber(b) {
            if ba == bb { return 0 }
            return ba < bb ? -1 : 1
        }
        func numeric(_ s: String) -> Int? {
            guard !s.isEmpty, s.allSatisfy({ $0.isNumber }), let n = Int(s), String(n) == s else { return nil }
            return n
        }
        let na = numeric(a)
        let nb = numeric(b)
        if let na, let nb {
            if na == nb { return 0 }
            return na < nb ? -1 : 1
        }
        if na != nil { return -1 }
        if nb != nil { return 1 }
        if a == b { return 0 }
        return a < b ? -1 : 1
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
