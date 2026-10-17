import AppKit
import Foundation
import SwiftUI
import Testing
@testable import LeXiKit
@testable import LeXiUI

// Builds and lays out the result views from real host output without showing a window,
// simulating input or taking screenshots.
@MainActor @Suite struct RenderSmoke {
    static let views: [String: QueryView] = {
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "LeXiKitTests/Fixtures/views.json")
        return try! JSONDecoder().decode([String: QueryView].self, from: Data(contentsOf: file))
    }()

    @Test(arguments: ["dictionary", "translation", "preview", "streaming"])
    func entryLaysOut(_ name: String) throws {
        let store = LexiStore()
        store.receive(Self.views[name]!)
        for width in [540.0, 720.0, 1080.0] {
            let renderer = ImageRenderer(content: EntryView(store: store, speech: SpeechController()).frame(width: width).padding())
            let image = try #require(renderer.nsImage)
            #expect(image.size.width >= width && image.size.height > 40)
        }
    }

    @Test func pinkAccentKeepsDarkTextReadable() throws {
        let pink = try #require(NSColor(LexiTheme.pink).usingColorSpace(.sRGB))
        #expect(abs(pink.redComponent - 1) < 0.01 && abs(pink.greenComponent - 0xAD / 255.0) < 0.01 && abs(pink.blueComponent - 0xD2 / 255.0) < 0.01)
        #expect((Self.luminance(pink) + 0.05) / 0.05 >= 7, "black text on the solid pink badge")
    }

    // Parts of speech are small italic text, so the gold needs 4.5:1 on typical light and dark glass.
    @Test func goldPartOfSpeechStaysReadable() throws {
        func gold(_ name: NSAppearance.Name) -> NSColor {
            var color = NSColor.clear
            NSAppearance(named: name)!.performAsCurrentDrawingAppearance { color = LexiTheme.goldColor.usingColorSpace(.sRGB)! }
            return color
        }
        func contrast(_ a: NSColor, _ b: NSColor) -> Double {
            let (x, y) = (Self.luminance(a), Self.luminance(b))
            return (max(x, y) + 0.05) / (min(x, y) + 0.05)
        }
        let dark = gold(.darkAqua)
        #expect(abs(dark.redComponent - 0xDD / 255.0) < 0.01 && abs(dark.greenComponent - 0xBD / 255.0) < 0.01 && abs(dark.blueComponent - 0x82 / 255.0) < 0.01, "the Windows gold")
        #expect(contrast(gold(.aqua), NSColor(srgbRed: 0.93, green: 0.93, blue: 0.94, alpha: 1)) >= 4.5, "deeper gold on light glass")
        #expect(contrast(dark, NSColor(srgbRed: 0.15, green: 0.15, blue: 0.16, alpha: 1)) >= 4.5, "pale gold on dark glass")
    }

    private static func luminance(_ c: NSColor) -> Double {
        func linear(_ v: CGFloat) -> Double { let x = Double(v); return x <= 0.04045 ? x / 12.92 : pow((x + 0.055) / 1.055, 2.4) }
        return 0.2126 * linear(c.redComponent) + 0.7152 * linear(c.greenComponent) + 0.0722 * linear(c.blueComponent)
    }
}
