import Foundation
import Testing
@testable import LeXiKit

// The in-process host the iOS app uses: Lexi.Embed compiled with NativeAOT (here for macOS), loaded
// with dlopen, speaking the same protocol through its C entry points and callback. The library runs
// one host per process, so one test covers the whole session.
@MainActor @Suite(.enabled(if: Fixture.embedded != nil && Fixture.dictionary != nil, "needs a published LexiEmbed library and the test dictionary"))
struct EmbeddedHost {
    @Test func queriesThroughTheEmbeddedLibrary() async throws {
        let library = try #require(Fixture.embedded)
        let user = FileManager.default.temporaryDirectory.appending(path: "lexi-embed-tests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: user, withIntermediateDirectories: true)
        try Data(#"{"online":false}"#.utf8).write(to: user.appending(path: "settings.json"))
        let transport = EmbeddedTransport(library: library, assets: library.deletingLastPathComponent().appending(path: "assets"),
                                          data: try #require(Fixture.dictionary), user: user)
        let store = LexiStore()
        await store.start(transport: transport)
        #expect(store.phase == .ready)
        await store.submit("walked")
        #expect(store.view.kind == .dictionary && store.view.local.first?.chinese == "步行")
        #expect(store.view.wordForms.first?.cells.first?.spelling == "walked → walk")
        await store.suggest("wal")
        #expect(store.suggestions.contains("walk"))
        await store.home()
        #expect(store.history.entries.first?.query == "walked")
        // Model keys reach Core for API services only, and nothing is written to the user folder.
        #expect(await store.setKey("anthropic", "synthetic-embedded-key"))
        #expect(!(await store.setKey("codex", "synthetic-embedded-key")))
        let written = FileManager.default.enumerator(atPath: user.path)?.compactMap { $0 as? String } ?? []
        #expect(!written.contains { (try? String(contentsOf: user.appending(path: $0), encoding: .utf8))?.contains("synthetic-embedded-key") == true })
        await store.stop()
    }
}
