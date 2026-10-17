import AppKit
import LeXiKit
import SwiftUI

extension EnvironmentValues {
    // The distance from a block's text back to the speaker column. Indented bodies (examples, other
    // wordings) add their indent, so every speaker in a card lines up in one column.
    @Entry var speechGutter: CGFloat = 26
}

// A definition or example that can be read aloud, the same way as on Windows. A speaker appears in
// the margin while the pointer is over the block and stays, as a stop button, while it plays; the
// block is tinted gold while it plays. Its context menu starts with 朗读 (朗读所选文字 for a
// selection), above the system's Look Up, Translate and Copy, and VoiceOver offers the same action.
struct Speakable: View {
    let id: String
    let text: String
    let look: TextLook
    let service: any SpeechService
    @Environment(SpeechController.self) private var speech: SpeechController?
    @Environment(\.speechGutter) private var gutter
    @State private var hovering = false

    private var playing: Bool { speech?.speakingID == id }

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            speaker.frame(width: gutter, height: look.lineHeight, alignment: .leading)
            BlockText(text: text, look: look, menuTitle: { selected in selected ? "朗读所选文字" : playing ? "停止朗读" : "朗读" }, read: read)
                .background {
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(LexiTheme.gold.opacity(playing ? 0.16 : 0))
                        .padding(.horizontal, -5).padding(.vertical, -3)
                }
        }
        .contentShape(.rect)
        .onHover { hovering = $0 }
        // The speaker column sits in the card's margin; the text stays where it was.
        .padding(.leading, -gutter)
        .animation(.easeOut(duration: 0.15), value: hovering)
        .animation(.easeOut(duration: 0.2), value: playing)
    }

    @ViewBuilder private var speaker: some View {
        if speech != nil {
            let shown = hovering || playing
            Button { read("") } label: {
                Image(systemName: playing ? "stop.fill" : "speaker.wave.2.fill")
                    .font(.system(size: 11, weight: .medium))
                    .frame(width: 18, height: 18)
                    .contentShape(.rect)
            }
            .buttonStyle(.borderless)
            .foregroundStyle(playing ? AnyShapeStyle(LexiTheme.gold) : AnyShapeStyle(.secondary))
            .opacity(shown ? 1 : 0)
            .allowsHitTesting(shown)
            .padding(.leading, 3)
            .help(playing ? "停止朗读" : "朗读")
            .accessibilityLabel(playing ? "停止朗读" : "朗读")
        }
    }

    // An empty selection reads the whole block, or stops it while it plays.
    private func read(_ selection: String) {
        guard let speech else { return }
        if selection.isEmpty && playing { speech.stop("发音已停止"); return }
        speech.play(selection.isEmpty ? text : selection, service.speechSelection, using: service, finished: "发音播放结束", id: id)
    }
}

// The typography of a block of result text, matching the SwiftUI fonts used elsewhere on the page.
struct TextLook {
    var font: NSFont
    var color: NSColor = .labelColor
    var lineSpacing: CGFloat = 0

    static func system(_ size: CGFloat, color: NSColor = .labelColor, lineSpacing: CGFloat = 0) -> TextLook {
        TextLook(font: .systemFont(ofSize: size), color: color, lineSpacing: lineSpacing)
    }
    // New York, as LexiTheme.lexical.
    static func lexical(_ size: CGFloat, italic: Bool = false) -> TextLook {
        let base = NSFont.systemFont(ofSize: size).fontDescriptor
        var descriptor = base.withDesign(.serif) ?? base
        if italic { descriptor = descriptor.withSymbolicTraits(.italic) }
        return TextLook(font: NSFont(descriptor: descriptor, size: size) ?? .systemFont(ofSize: size))
    }
    // Songti, as LexiTheme.chineseExample.
    static func chineseExample(_ size: CGFloat) -> TextLook {
        TextLook(font: NSFont(name: "Songti SC", size: size) ?? .systemFont(ofSize: size))
    }

    var lineHeight: CGFloat { NSLayoutManager().defaultLineHeight(for: font) }

    var attributes: [NSAttributedString.Key: Any] {
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineSpacing = lineSpacing
        return [.font: font, .foregroundColor: color, .paragraphStyle: paragraph]
    }
}

// Selectable, wrapping result text in an AppKit text view. SwiftUI's selectable Text replaces any
// context menu with the system text menu, so the 朗读 item is added to that menu here instead.
struct BlockText: NSViewRepresentable {
    let text: String
    let look: TextLook
    let menuTitle: (_ selected: Bool) -> String
    let read: (_ selection: String) -> Void

    func makeNSView(context: Context) -> BlockTextView {
        let view = BlockTextView(usingTextLayoutManager: false)
        view.isEditable = false
        view.isSelectable = true
        view.drawsBackground = false
        view.textContainerInset = .zero
        view.textContainer?.lineFragmentPadding = 0
        view.textContainer?.widthTracksTextView = false
        view.isVerticallyResizable = false
        view.isHorizontallyResizable = false
        return view
    }

    func updateNSView(_ view: BlockTextView, context: Context) {
        view.show(text, look)
        view.menuTitle = menuTitle
        view.read = read
    }

    // The size the text needs at the proposed width. SwiftUI asks with several trial widths, so this
    // lays out a separate copy of the text and never touches the view's own container.
    func sizeThatFits(_ proposal: ProposedViewSize, nsView view: BlockTextView, context: Context) -> CGSize? {
        let width = proposal.width.flatMap { $0.isFinite ? $0 : nil }
        let used = BlockTextView.measure(NSAttributedString(string: text, attributes: look.attributes), width: width ?? 10_000)
        return CGSize(width: width ?? ceil(used.width), height: ceil(used.height))
    }
}

final class BlockTextView: NSTextView {
    var menuTitle: (Bool) -> String = { _ in "朗读" }
    var read: (String) -> Void = { _ in }
    private var shown: (String, NSFont, NSColor, CGFloat)?

    // The text always wraps at the width SwiftUI gives the view, including when that width is
    // unchanged from an earlier layout.
    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        textContainer?.containerSize = NSSize(width: newSize.width, height: .greatestFiniteMagnitude)
    }

    static func measure(_ text: NSAttributedString, width: CGFloat) -> CGSize {
        let storage = NSTextStorage(attributedString: text)
        let container = NSTextContainer(size: NSSize(width: width, height: .greatestFiniteMagnitude))
        container.lineFragmentPadding = 0
        let layout = NSLayoutManager()
        layout.addTextContainer(container)
        storage.addLayoutManager(layout)
        layout.ensureLayout(for: container)
        return layout.usedRect(for: container).size
    }

    func show(_ text: String, _ look: TextLook) {
        guard shown.map({ $0 != (text, look.font, look.color, look.lineSpacing) }) ?? true else { return }
        shown = (text, look.font, look.color, look.lineSpacing)
        textStorage?.setAttributedString(NSAttributedString(string: text, attributes: look.attributes))
        setAccessibilityCustomActions([NSAccessibilityCustomAction(name: "朗读") { [weak self] in self?.read(""); return true }])
    }

    // The selection is read before the system menu is built, which may select the clicked word.
    override func menu(for event: NSEvent) -> NSMenu? {
        let range = selectedRange()
        let selection = range.length > 0 ? (string as NSString).substring(with: range) : ""
        let menu = super.menu(for: event) ?? NSMenu()
        let item = NSMenuItem(title: menuTitle(!selection.isEmpty), action: #selector(readFromMenu(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = selection
        menu.insertItem(.separator(), at: 0)
        menu.insertItem(item, at: 0)
        return menu
    }

    @objc private func readFromMenu(_ item: NSMenuItem) { read(item.representedObject as? String ?? "") }
}
