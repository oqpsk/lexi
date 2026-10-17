import AppKit
import SwiftUI
import Testing
@testable import LeXiKit
@testable import LeXiUI

// Definitions and examples are AppKit text views inside SwiftUI. At every width, including after
// the window narrows and widens again, each must wrap at its own frame width and fit inside its
// frame; otherwise a wrapped line runs into the row below.
// Serialized, and without spinning the run loop, so no other test runs in the middle of a layout.
@MainActor @Suite(.serialized) struct BlockTextLayout {
    struct Page: View {
        let width: CGFloat
        static let chinese = "函数式的；指以数学函数定义语义、没有副作用的编程方式，强调可组合、可推理，并且便于测试与并行执行。"
        static let english = "Describing programming whose semantics are defined through mathematical functions without side effects, so that programs compose and can be reasoned about."
        var body: some View {
            VStack(alignment: .leading, spacing: 4) {
                BlockText(text: Self.chinese, look: .system(17, lineSpacing: 3), menuTitle: { _ in "朗读" }, read: { _ in })
                BlockText(text: Self.english, look: .system(15, color: .secondaryLabelColor, lineSpacing: 2), menuTitle: { _ in "朗读" }, read: { _ in })
                BlockText(text: "A short line.", look: .lexical(15, italic: true), menuTitle: { _ in "朗读" }, read: { _ in })
            }
            .frame(width: width, alignment: .leading)
        }
    }

    @Test func wrappedTextFitsItsFrameAtEveryWidth() throws {
        _ = NSApplication.shared
        // One hosting view throughout, so the same text views are laid out again at each width.
        let host = NSHostingView(rootView: Page(width: 320))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 700), styleMask: [.titled], backing: .buffered, defer: false)
        // ARC owns the window; AppKit must not release it again on close.
        window.isReleasedWhenClosed = false
        defer { window.close() }
        window.contentView = host
        func texts(_ view: NSView) -> [BlockTextView] { (view as? BlockTextView).map { [$0] } ?? view.subviews.flatMap(texts) }
        var lines: [CGFloat: Int] = [:]
        for value in [320.0, 540.0, 760.0, 540.0, 320.0] {
            host.rootView = Page(width: value)
            host.layoutSubtreeIfNeeded()
            let views = texts(host)
            #expect(views.count == 3)
            var bottom = -CGFloat.infinity
            for view in views.sorted(by: { $0.frame.minY > $1.frame.minY }) {
                let container = try #require(view.textContainer), layout = try #require(view.layoutManager)
                layout.ensureLayout(for: container)
                let used = layout.usedRect(for: container)
                #expect(abs(container.containerSize.width - view.frame.width) < 0.5, "wraps at its own width (\(value))")
                #expect(abs(view.frame.width - value) < 0.5, "fills the column (\(value))")
                #expect(used.height <= view.frame.height + 0.5, "fits its frame (\(value)): \(used.height) > \(view.frame.height)")
                bottom = max(bottom, view.frame.maxY)
            }
            let english = try #require(views.first { $0.string == Page.english })
            let layout = try #require(english.layoutManager)
            var count = 0, index = 0
            while index < layout.numberOfGlyphs { var range = NSRange(); layout.lineFragmentRect(forGlyphAt: index, effectiveRange: &range); index = NSMaxRange(range); count += 1 }
            lines[value] = count
        }
        #expect((lines[320] ?? 0) > (lines[760] ?? 0) && (lines[760] ?? 0) >= 2, "narrow columns wrap into more lines: \(lines)")
    }

    // The real result page, with definitions long enough to wrap, in a window that is resized.
    @Test func resultDefinitionsFitTheirFramesInTheEntryView() throws {
        _ = NSApplication.shared
        let file = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent()
            .appending(path: "LeXiKitTests/Fixtures/views.json")
        var views = try #require(JSONSerialization.jsonObject(with: Data(contentsOf: file)) as? [String: Any])
        var view = try #require(views["dictionary"] as? [String: Any])
        var answers = try #require(view["answers"] as? [String: Any])
        var senses = try #require(answers["senses"] as? [[String: Any]])
        senses[0]["chinese"] = Page.chinese
        senses[0]["english"] = Page.english
        answers["senses"] = senses; view["answers"] = answers; views["dictionary"] = view
        let query = try JSONDecoder().decode(QueryView.self, from: JSONSerialization.data(withJSONObject: view))
        let store = LexiStore()
        store.receive(query)
        let speech = SpeechController()
        let host = NSHostingView(rootView: ScrollView { EntryView(store: store, speech: speech).environment(speech).padding(24) })
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 720, height: 700), styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        defer { window.close() }
        window.contentView = host
        func texts(_ view: NSView) -> [BlockTextView] { (view as? BlockTextView).map { [$0] } ?? view.subviews.flatMap(texts) }
        for width in [720.0, 560.0, 1000.0, 720.0] {
            window.setContentSize(NSSize(width: width, height: 700))
            host.layoutSubtreeIfNeeded()
            let blocks = texts(host).filter { $0.string == Page.chinese || $0.string == Page.english }
            #expect(blocks.count == 2)
            for block in blocks {
                let container = try #require(block.textContainer), layout = try #require(block.layoutManager)
                layout.ensureLayout(for: container)
                #expect(abs(container.containerSize.width - block.frame.width) < 0.5, "wraps at its own width in a \(width)-point window")
                #expect(layout.usedRect(for: container).height <= block.frame.height + 0.5, "fits its frame in a \(width)-point window")
            }
        }
    }
}
