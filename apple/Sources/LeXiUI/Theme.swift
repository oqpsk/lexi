import AppKit
import SwiftUI

// LeXi for macOS follows Apple's design language rather than the Windows look: system colors that
// adapt to light, dark and the user's accent, continuous corners, Liquid Glass for the floating
// controls, and content on quiet grouped surfaces. Pink appears only as the brand TL;DR mark;
// parts of speech are pale gold, as on Windows.
public enum LexiTheme {
    // The one brand pink (#FFADD2). Solid pink fills always carry dark text.
    public static let pink = Color(nsColor: NSColor(srgbRed: 1, green: 0xAD / 255.0, blue: 0xD2 / 255.0, alpha: 1))
    // The Windows highlight gold (#DDBD82) on dark glass; light glass gets a deeper gold of the same
    // hue (#7D5A1A) so a part of speech stays readable.
    static let goldColor = NSColor(name: "LexiGold") { appearance in
        appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            ? NSColor(srgbRed: 0xDD / 255.0, green: 0xBD / 255.0, blue: 0x82 / 255.0, alpha: 1)
            : NSColor(srgbRed: 0x7D / 255.0, green: 0x5A / 255.0, blue: 0x1A / 255.0, alpha: 1)
    }
    public static let gold = Color(nsColor: goldColor)
    public static func lexical(_ size: CGFloat, weight: Font.Weight = .regular) -> Font { .system(size: size, weight: weight, design: .serif) }
    public static func chineseExample(_ size: CGFloat) -> Font { .custom("Songti SC", size: size) }
}

// Every LeXi window is a pane of Liquid Glass, like Control Center: the desktop shows through,
// blurred and refracted, as light or dark glass that follows the appearance (and takes on the
// wallpaper's color, as system glass does). Light windows keep regular glass: clear glass leaves
// text unreadable over a busy desktop. With Reduce Transparency the system makes it more opaque.
struct WindowGlass: NSViewRepresentable {
    func makeNSView(context: Context) -> WindowGlassView { WindowGlassView() }
    func updateNSView(_ view: WindowGlassView, context: Context) {}
}

final class WindowGlassView: NSView {
    let glass = NSGlassEffectView()

    override init(frame: NSRect) {
        super.init(frame: frame)
        glass.style = .regular
        glass.frame = bounds
        glass.autoresizingMask = [.width, .height]
        addSubview(glass)
    }

    required init?(coder: NSCoder) { nil }

    // The glass reaches the desktop only through a transparent window.
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard let window else { return }
        window.isOpaque = false
        window.backgroundColor = .clear
    }
}

extension View {
    // Glass behind the whole window, with the toolbar left clear so the same pane runs to the top.
    func glassWindow() -> some View {
        containerBackground(for: .window) { WindowGlass() }
            .toolbarBackgroundVisibility(.hidden, for: .windowToolbar)
    }
}

// Light, dark or the system appearance, for every LeXi window. Stored in the app's preferences.
public enum AppearanceChoice: String, CaseIterable, Identifiable, Sendable {
    case system, light, dark
    public static let key = "appearance"
    public var id: String { rawValue }
    var title: String { switch self { case .system: "跟随系统"; case .light: "浅色"; case .dark: "深色" } }
    @MainActor public static func apply(_ stored: String) {
        NSApp.appearance = switch AppearanceChoice(rawValue: stored) ?? .system {
        case .system: nil
        case .light: NSAppearance(named: .aqua)
        case .dark: NSAppearance(named: .darkAqua)
        }
    }
}

extension View {
    // A grouped content surface, like the groups in System Settings.
    func contentGroup(cornerRadius: CGFloat = 18) -> some View {
        background(.quinary, in: .rect(cornerRadius: cornerRadius, style: .continuous))
    }
}

// The TL;DR mark: a pale-pink rounded square with stacked dark letterforms.
struct QuickReadBadge: View {
    var body: some View {
        VStack(spacing: -2) {
            Text("TL"); Text("DR")
        }
        .font(.system(size: 9, weight: .black, design: .rounded))
        .foregroundStyle(.black)
        .frame(width: 22, height: 22)
        .background(LexiTheme.pink, in: .rect(cornerRadius: 6, style: .continuous))
        .accessibilityHidden(true)
    }
}

// The etymology mark from the Windows window: a Rosetta Stone, a slab with a broken top edge and
// lines of inscription, on the same 24-point grid (it spans x 5–19, y 2–21), scaled uniformly and
// centered. Stroke it like the symbols beside it.
struct RosettaStone: Shape {
    func path(in rect: CGRect) -> Path {
        let scale = min(rect.width / 14, rect.height / 19)
        let left = rect.midX - 7 * scale, top = rect.midY - 9.5 * scale
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: left + (x - 5) * scale, y: top + (y - 2) * scale) }
        var path = Path()
        path.addLines([point(5, 21), point(5, 7), point(8, 3), point(14, 2), point(19, 6), point(19, 21)])
        path.closeSubpath()
        // Inscription lines as (from x, to x, y).
        let lines: [(CGFloat, CGFloat, CGFloat)] = [(8, 11, 8), (13, 16, 8), (8, 16, 11), (8, 10, 14), (12, 16, 14), (8, 16, 17)]
        for (from, to, y) in lines {
            path.move(to: point(from, y))
            path.addLine(to: point(to, y))
        }
        return path
    }
}

// Wraps word forms and disclosure buttons onto as many lines as the width needs.
struct FlowLayout: Layout {
    var spacing: CGFloat = 8
    var lineSpacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        arrange(width: proposal.width ?? .infinity, subviews: subviews).size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (index, origin) in arrange(width: bounds.width, subviews: subviews).origins.enumerated() {
            subviews[index].place(at: CGPoint(x: bounds.minX + origin.x, y: bounds.minY + origin.y), proposal: .unspecified)
        }
    }

    private func arrange(width: CGFloat, subviews: Subviews) -> (origins: [CGPoint], size: CGSize) {
        var origins: [CGPoint] = []
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for subview in subviews {
            let size = subview.sizeThatFits(.unspecified)
            if x > 0, x + size.width > width { x = 0; y += line + lineSpacing; line = 0 }
            origins.append(CGPoint(x: x, y: y))
            x += size.width + spacing
            line = max(line, size.height)
            widest = max(widest, x - spacing)
        }
        return (origins, CGSize(width: widest, height: y + line))
    }
}

// Opens only https links, like the Windows window.
@MainActor func openSource(_ address: String) {
    guard let url = URL(string: address), url.scheme == "https" else { return }
    NSWorkspace.shared.open(url)
}

@MainActor func copyToPasteboard(_ text: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(text, forType: .string)
}
