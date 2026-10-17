import AVFoundation
import LeXiKit
import SwiftUI

@main
struct LeXiMobileApp: App {
    @State private var store = LexiStore()
    @State private var speech = SpeechController()
    @AppStorage(Appearance.key) private var appearance = Appearance.system.rawValue

    var body: some Scene {
        WindowGroup {
            RootView(store: store, speech: speech)
                .environment(speech)
                .preferredColorScheme(Appearance(rawValue: appearance)?.scheme)
                .task { await start() }
                // lexi://lookup?q=word looks a word up, from Shortcuts, other apps or a link.
                .onOpenURL { url in
                    guard url.scheme == "lexi", let query = URLComponents(url: url, resolvingAgainstBaseURL: false)?
                        .queryItems?.first(where: { $0.name == "q" })?.value, !query.isEmpty else { return }
                    Task { await store.submit(query) }
                }
        }
    }

    private func start() async {
        guard store.phase == .starting else { return }
        // Speech plays even with the ring/silent switch on, like other dictionary apps.
        try? AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio)
        guard let transport = EmbeddedTransport.app() else {
            store.failToStart("未找到内置的查询服务。请重新安装 LeXi。")
            return
        }
        await store.start(transport: transport)
        await ModelKeys.sendAll(to: store)
        // For UI checks and screenshots: `-LexiLookup <text>` looks something up at launch.
        if let query = UserDefaults.standard.string(forKey: "LexiLookup"), !query.isEmpty { await store.submit(query) }
    }
}

// API keys live in the Keychain; the host gets them in memory only, at start and after each change.
enum ModelKeys {
    static let providers = ["openai", "anthropic"]
    @MainActor static func sendAll(to store: LexiStore, keys: any SpeechKeys = KeychainSpeechKeys()) async {
        for provider in providers { await store.setKey(provider, (try? keys.read(provider)) ?? "") }
    }
}

enum Appearance: String, CaseIterable, Identifiable {
    case system, light, dark
    static let key = "appearance"
    var id: String { rawValue }
    var title: String { switch self { case .system: "跟随系统"; case .light: "浅色"; case .dark: "深色" } }
    var scheme: ColorScheme? { switch self { case .system: nil; case .light: .light; case .dark: .dark } }
}
