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
    private var reconnectTask: Task<Void, Never>?
    private var reconnectAttempts = 0
    private var connectionEpoch = 0
    private var qrTask: Task<Void, Never>?
    private var qrPollTask: Task<Void, Never>?
    private var cookie = ""

    func toggle() {
        if connected { stop() } else { start() }
    }

    func start() {
        let id = room.filter(\.isNumber)
        guard !id.isEmpty else { status = "请先填写房间号"; return }
        connectionEpoch += 1
        let epoch = connectionEpoch
        beat?.cancel()
        beat = nil
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connected = true
        stats = LiveNumbers()
        status = "正在解析房间号…"
        Task {
            var roomResolved = false
            do {
                let rid = try await Self.roomID(id)
                roomResolved = true
                guard epoch == connectionEpoch else { return }
                let info = try await Self.danmuInfo(room: rid, cookie: cookie)
                guard epoch == connectionEpoch else { return }
                status = "正在连接…"
                let task = URLSession.shared.webSocketTask(with: info.url)
                socket = task
                task.resume()
                let auth: [String: Any] = ["uid": authUID, "roomid": rid, "protover": 2, "platform": "web", "type": 2, "key": info.token]
                let body = try JSONSerialization.data(withJSONObject: auth)
                try await task.send(.data(Self.pack(op: 7, body: body)))
                guard epoch == connectionEpoch else { return }
                status = "已连接 · 房间 \(rid)"
                reconnectAttempts = 0
                beat?.cancel()
                beat = Task { [weak self] in
                    while !Task.isCancelled {
                        try? await Task.sleep(for: .seconds(30))
                        await self?.sendBeat()
                    }
                }
                receive(task)
            } catch {
                guard epoch == connectionEpoch else { return }
                connected = false
                if roomResolved {
                    scheduleReconnect()
                } else {
                    status = "连接失败：\(error.localizedDescription)"
                }
            }
        }
    }

    func stop() {
        connectionEpoch += 1
        reconnectTask?.cancel()
        reconnectTask = nil
        reconnectAttempts = 0
        qrTask?.cancel()
        qrTask = nil
        qrPollTask?.cancel()
        qrPollTask = nil
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
        qrTask?.cancel()
        qrPollTask?.cancel()
        qrPollTask = nil
        qrText = "正在获取二维码…"
        qrTask = Task {
            do {
                var req = URLRequest(url: URL(string: "https://passport.bilibili.com/x/passport-login/web/qrcode/generate")!)
                req.setValue("tokilive/1.3.0", forHTTPHeaderField: "User-Agent")
                let (data, _) = try await URLSession.shared.data(for: req)
                guard !Task.isCancelled else { return }
                let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
                let payload = json?["data"] as? [String: Any]
                let url = payload?["url"] as? String ?? ""
                let key = payload?["qrcode_key"] as? String ?? ""
                guard !key.isEmpty, !url.isEmpty else { qrText = "获取二维码失败"; return }
                qrImage = Self.qr(url)
                qrText = "请用哔哩哔哩 App 扫码。确认后的 Cookie 只留在本机内存，不会写入仓库。"
                pollQr(key: key)
            } catch {
                if !Task.isCancelled { qrText = "获取二维码失败" }
            }
        }
    }

    /// 每 2 秒轮询扫码状态：86101 未扫码继续等；86090 已扫未确认；86038 过期停掉；
    /// 0 成功时从 Set-Cookie 取 cookie（只放内存），随后停掉轮询。
    private func pollQr(key: String) {
        qrPollTask?.cancel()
        qrPollTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(2))
                guard !Task.isCancelled, let self else { return }
                let code: Int
                let jar: String?
                do {
                    (code, jar) = try await Self.pollQrOnce(key: key)
                } catch {
                    continue
                }
                switch code {
                case 0:
                    if let jar, !jar.isEmpty { self.cookie = jar }
                    self.qrText = "登录成功"
                    return
                case 86090:
                    self.qrText = "已扫码，请在手机上确认"
                case 86038:
                    self.qrText = "二维码已过期，请重新获取"
                    return
                default:
                    break
                }
            }
        }
    }

    /// 轮询一次。返回 (code, cookie)：code 为 0 时 cookie 是 Set-Cookie 拼成的串。
    private static func pollQrOnce(key: String) async throws -> (Int, String?) {
        var req = URLRequest(url: URL(string: "https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=\(key)")!)
        req.setValue("tokilive/1.3.0", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: req)
        let json = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let payload = json?["data"] as? [String: Any]
        let code = payload?["code"] as? Int ?? -1
        guard code == 0 else { return (code, nil) }
        let fields = (response as? HTTPURLResponse)?.allHeaderFields as? [String: String] ?? [:]
        var jar = HTTPCookie.cookies(withResponseHeaderFields: fields, for: req.url!)
            .map { "\($0.name)=\($0.value)" }
            .joined(separator: "; ")
        if jar.isEmpty {
            jar = fields.compactMap { key, value in
                key.lowercased() == "set-cookie" ? value.split(separator: ";", maxSplits: 1).first.map(String.init) : nil
            }.joined(separator: "; ")
        }
        return (0, jar.isEmpty ? nil : jar)
    }

    /// 从内存 cookie 里取 DedeUserID，作为鉴权 uid（与 Android / Windows 一致）。
    private var authUID: Int {
        guard let start = cookie.range(of: "DedeUserID=")?.upperBound else { return 0 }
        return Int(cookie[start...].prefix(while: \.isNumber)) ?? 0
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
                guard task === self.socket else { return }
                switch result {
                case .success(let message):
                    switch message {
                    case .string(let text): self.ingest(text)
                    case .data(let data): self.unpack(data)
                    @unknown default: break
                    }
                    if self.connected { self.receive(task) }
                case .failure:
                    if self.connected { self.handleDisconnect() }
                }
            }
        }
    }

    /// socket 出错或被服务端关闭：停心跳、断开状态、指数退避重连。
    private func handleDisconnect() {
        beat?.cancel()
        beat = nil
        socket?.cancel(with: .goingAway, reason: nil)
        socket = nil
        connected = false
        scheduleReconnect()
    }

    /// 指数退避重连：2s、4s、8s… 上限 60s（与 Android 端一致）。
    private func scheduleReconnect() {
        reconnectTask?.cancel()
        let delay = min(60, 2 << min(reconnectAttempts, 5))
        reconnectAttempts += 1
        status = "连接断开，\(delay) 秒后重连…"
        reconnectTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(delay))
            guard !Task.isCancelled else { return }
            self?.start()
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
        } else if name == "INTERACT_WORD", let data = json["data"] as? [String: Any] {
            addInteract(uname: data["uname"] as? String ?? "", type: data["msg_type"] as? Int ?? 0)
        } else if name == "INTERACT_WORD_V2", let data = json["data"] as? [String: Any], let pb = data["pb"] as? String, let raw = Data(base64Encoded: pb) {
            let (uname, type) = Self.decodeInteract(raw)
            addInteract(uname: uname, type: type)
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

    /// 进场 / 关注：msg_type 1 进场，2 / 4 关注，5 互粉（与 Android 端一致；3 分享 iOS 没有对应开关，不显示）。
    private func addInteract(uname: String, type: Int) {
        switch type {
        case 1: add("enter", uname, "进入直播间")
        case 2, 4: add("follow", uname, "按下了关注")
        case 5: add("follow", uname, "与主播互粉了")
        default: break
        }
    }

    /// 极简 protobuf：1 uid，2 uname，5 msg_type（与 Android 端 decodeInteract 对齐）。
    private static func decodeInteract(_ bytes: Data) -> (String, Int) {
        var index = bytes.startIndex
        var name = ""
        var type = 0
        func varint() -> UInt64 {
            var result: UInt64 = 0
            var shift = 0
            while index < bytes.endIndex, shift < 64 {
                let byte = bytes[index]
                index = bytes.index(after: index)
                result |= UInt64(byte & 0x7f) << shift
                shift += 7
                if byte & 0x80 == 0 { return result }
            }
            return result
        }
        while index < bytes.endIndex {
            let key = varint()
            let field = Int(key >> 3)
            switch key & 7 {
            case 0:
                let value = varint()
                if field == 5 { type = Int(clamping: value) }
            case 2:
                let length = varint()
                guard length <= UInt64(bytes.distance(from: index, to: bytes.endIndex)) else { return (name, type) }
                let count = Int(length)
                if field == 2 {
                    name = String(decoding: bytes[index..<bytes.index(index, offsetBy: count)], as: UTF8.self)
                }
                index = bytes.index(index, offsetBy: count)
            case 1:
                index = bytes.index(index, offsetBy: 8, limitedBy: bytes.endIndex) ?? bytes.endIndex
            case 5:
                index = bytes.index(index, offsetBy: 4, limitedBy: bytes.endIndex) ?? bytes.endIndex
            default:
                return (name, type)
            }
        }
        return (name, type)
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
