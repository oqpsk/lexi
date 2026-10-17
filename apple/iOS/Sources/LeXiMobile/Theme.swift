import SwiftUI
import UIKit

// The same visual language as LeXi for macOS, adapted to touch: system colors, continuous corners,
// Liquid Glass for the floating controls, results on quiet grouped surfaces. Pink marks only TL;DR;
// parts of speech are pale gold, as on Windows and macOS.
enum LexiTheme {
    static let pink = Color(red: 1, green: 0xAD / 255, blue: 0xD2 / 255)
    // #DDBD82 on dark surfaces, a deeper gold of the same hue (#7D5A1A) on light ones.
    static let gold = Color(UIColor { traits in
        traits.userInterfaceStyle == .dark
            ? UIColor(red: 0xDD / 255, green: 0xBD / 255, blue: 0x82 / 255, alpha: 1)
            : UIColor(red: 0x7D / 255, green: 0x5A / 255, blue: 0x1A / 255, alpha: 1)
    })
    static func lexical(_ size: CGFloat, weight: Font.Weight = .regular) -> Font { .system(size: size, weight: weight, design: .serif) }
    static func chineseExample(_ size: CGFloat) -> Font { .custom("Songti SC", size: size) }
}

extension View {
    // A grouped content surface.
    func contentGroup(cornerRadius: CGFloat = 22) -> some View {
        background(Color(uiColor: .secondarySystemGroupedBackground), in: .rect(cornerRadius: cornerRadius, style: .continuous))
    }
}

// The TL;DR mark: a pale-pink rounded square with stacked dark letterforms.
struct QuickReadBadge: View {
    var body: some View {
        VStack(spacing: -2) { Text("TL"); Text("DR") }
            .font(.system(size: 9, weight: .black, design: .rounded))
            .foregroundStyle(.black)
            .frame(width: 22, height: 22)
            .background(LexiTheme.pink, in: .rect(cornerRadius: 6, style: .continuous))
            .accessibilityHidden(true)
    }
}

// The etymology mark shared with Windows and macOS: a Rosetta Stone on the same 24-point grid.
struct RosettaStone: Shape {
    func path(in rect: CGRect) -> Path {
        let scale = min(rect.width / 14, rect.height / 19)
        let left = rect.midX - 7 * scale, top = rect.midY - 9.5 * scale
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: left + (x - 5) * scale, y: top + (y - 2) * scale) }
        var path = Path()
        path.addLines([point(5, 21), point(5, 7), point(8, 3), point(14, 2), point(19, 6), point(19, 21)])
        path.closeSubpath()
        let lines: [(CGFloat, CGFloat, CGFloat)] = [(8, 11, 8), (13, 16, 8), (8, 16, 11), (8, 10, 14), (12, 16, 14), (8, 16, 17)]
        for (from, to, y) in lines { path.move(to: point(from, y)); path.addLine(to: point(to, y)) }
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

@MainActor func openSource(_ address: String) {
    guard let url = URL(string: address), url.scheme == "https" else { return }
    UIApplication.shared.open(url)
}

@MainActor func copyToPasteboard(_ text: String) { UIPasteboard.general.string = text }
