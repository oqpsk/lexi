import Foundation
@testable import LeXiKit

// Views written by `Lexi.Tools host-check ... <views.json>`: real Lexi.Host output.
enum Fixture {
    static let repository = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
        .deletingLastPathComponent().deletingLastPathComponent()
    static let views: [String: QueryView] = {
        let data = try! Data(contentsOf: URL(fileURLWithPath: #filePath).deletingLastPathComponent().appending(path: "Fixtures/views.json"))
        return try! JSONDecoder().decode([String: QueryView].self, from: data)
    }()
    static func view(_ name: String) -> QueryView { views[name]! }

    // A host is available when LEXI_HOST names one or the published build exists in .cache.
    static var host: URL? {
        if let path = ProcessInfo.processInfo.environment["LEXI_HOST"], !path.isEmpty { return URL(fileURLWithPath: path) }
        let published = repository.appending(path: ".cache/host-publish/lexi-host")
        return FileManager.default.isExecutableFile(atPath: published.path) ? published : nil
    }
    // The embedded host library (Lexi.Embed) built for macOS, as iOS loads it: LEXI_EMBED or .cache/embed-publish.
    static var embedded: URL? {
        if let path = ProcessInfo.processInfo.environment["LEXI_EMBED"], !path.isEmpty { return URL(fileURLWithPath: path) }
        let published = repository.appending(path: ".cache/embed-publish/LexiEmbed.dylib")
        return FileManager.default.fileExists(atPath: published.path) ? published : nil
    }
    static var dictionary: URL? {
        let folder = repository.appending(path: ".cache/test-dictionary")
        return FileManager.default.fileExists(atPath: folder.appending(path: "lexicon.idx").path) ? folder : nil
    }
}
