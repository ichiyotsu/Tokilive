import SwiftUI

@main
struct TokiliveApp: App {
    @StateObject private var model = LiveModel()
    var body: some Scene {
        WindowGroup {
            RootView()
                .environmentObject(model)
        }
    }
}
