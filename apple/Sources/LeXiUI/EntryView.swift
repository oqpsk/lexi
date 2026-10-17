import LeXiKit
import SwiftUI

// One dictionary or translation page, drawn exactly from the QueryView Lexi.Core published.
public struct EntryView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController

    public init(store: LexiStore, speech: SpeechController) { self.store = store; self.speech = speech }

    public var body: some View {
        let view = store.view
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 10) {
                HeadingRow(view: view, store: store, speech: speech)
                if !view.wordForms.isEmpty { WordFormsView(rows: view.wordForms) }
            }
            if view.quickRead.visible { QuickReadCard(quickRead: view.quickRead) }
            if view.kind == .translation {
                if let translation = view.translation { TranslationCard(translation: translation) }
            } else {
                if let answers = view.answers, !answers.empty { AnswersCard(answers: answers, store: store) }
                LocalSourcesView(view: view, store: store)
                OnlineSourceView(view: view, store: store)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// The headword in the serif style of Apple's Dictionary, with pronunciation and actions beside it.
struct HeadingRow: View {
    let view: QueryView
    let store: LexiStore
    var speech: SpeechController
    @State private var showEtymology = false

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Text(view.heading)
                .font(view.kind == .translation ? .system(size: 28, weight: .bold) : LexiTheme.lexical(34, weight: .bold))
                .textSelection(.enabled)
            if !view.pronunciation.text.isEmpty {
                Text(view.pronunciation.text)
                    .font(view.kind == .translation ? .system(size: 15) : LexiTheme.lexical(18))
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
                    .help(view.pronunciation.attribution)
                if !view.pronunciation.label.isEmpty {
                    Text(view.pronunciation.label.trimmingCharacters(in: .whitespaces))
                        .font(.caption2.weight(.semibold)).foregroundStyle(.secondary)
                        .padding(.horizontal, 5).padding(.vertical, 1)
                        .background(.quaternary, in: .capsule)
                        .help(view.pronunciation.labelAttribution)
                        .accessibilityLabel(view.pronunciation.labelAttribution)
                }
            }
            HStack(spacing: 8) {
                if !view.speechText.isEmpty {
                    Button { speech.toggle(view.speechText, using: store) } label: {
                        Image(systemName: speech.speaking ? "stop.fill" : "speaker.wave.2.fill")
                    }
                    .help(speech.speaking ? "停止播放" : "播放合成发音")
                    .accessibilityLabel(speech.speaking ? "停止合成发音" : "播放合成发音")
                }
                if !view.etymology.isEmpty {
                    Button { showEtymology.toggle() } label: {
                        RosettaStone()
                            .stroke(style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
                            .frame(width: 16, height: 19)
                            .contentShape(.rect)
                    }
                        .help("词源")
                        .accessibilityLabel("查看词源")
                        .popover(isPresented: $showEtymology, arrowEdge: .bottom) { EtymologyPopover(items: view.etymology) }
                }
            }
            .buttonStyle(.borderless)
            .imageScale(.large)
            .foregroundStyle(.secondary)
        }
    }
}

struct EtymologyPopover: View {
    let items: [Etymology]
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                Text("词源").font(.headline)
                ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                    VStack(alignment: .leading, spacing: 4) {
                        if item.generated {
                            Text(item.text).textSelection(.enabled)
                            Text(item.caption).font(.caption).foregroundStyle(.secondary)
                        } else {
                            Text(item.caption).font(.caption).foregroundStyle(.secondary)
                            Text(item.text).textSelection(.enabled)
                            SourceLink(item.linkLabel, url: item.sourceUrl)
                        }
                    }
                }
            }
            .font(.system(size: 14))
            .padding(18)
            .frame(width: 380, alignment: .leading)
        }
        .frame(maxHeight: 420)
    }
}

struct WordFormsView: View {
    let rows: [WordFormRow]
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                FlowLayout(spacing: 16, lineSpacing: 4) {
                    ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                        Text("\(Text(cell.label + "  ").font(.caption).foregroundStyle(.secondary))\(Text(cell.spelling).font(LexiTheme.lexical(16, weight: cell.matches ? .semibold : .regular)))")
                            .help(cell.tooltip)
                            .contextMenu { Button("复制词形") { copyToPasteboard(cell.copyText) } }
                    }
                }
            }
        }
    }
}

// The one brand moment on the page: a short summary under the TL;DR mark.
struct QuickReadCard: View {
    let quickRead: QuickRead
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            QuickReadBadge()
            Text(quickRead.text).font(.system(size: 17, weight: .medium)).textSelection(.enabled).help(quickRead.tooltip)
            if quickRead.showState {
                Text(quickRead.state).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(LexiTheme.pink.opacity(0.12), in: .rect(cornerRadius: 18, style: .continuous))
        .help(quickRead.caption + "\n" + quickRead.explanation)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(quickRead.caption + "。" + quickRead.text)
    }
}

struct TranslationCard: View {
    let translation: TranslationResult
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !translation.error.isEmpty {
                Label(translation.error, systemImage: "exclamationmark.triangle").foregroundStyle(.secondary)
            } else {
                HStack(alignment: .firstTextBaseline) {
                    Text(translation.hint).font(.subheadline).foregroundStyle(.secondary)
                    Spacer()
                    Text(translation.source).font(.subheadline).foregroundStyle(.secondary)
                }
                ForEach(Array(translation.paragraphs.enumerated()), id: \.offset) { _, paragraph in
                    Text(paragraph).font(.system(size: 17)).lineSpacing(6).textSelection(.enabled)
                }
                if !translation.note.trimmingCharacters(in: .whitespaces).isEmpty {
                    Text(translation.note).font(.callout).foregroundStyle(.secondary)
                }
                if translation.complete {
                    Button { copyToPasteboard(translation.paragraphs.joined(separator: "\n\n")) } label: {
                        Label("复制译文", systemImage: "doc.on.doc")
                    }
                    .buttonStyle(.bordered)
                    .buttonBorderShape(.capsule)
                    .controlSize(.small)
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentGroup()
    }
}

// Standard macOS links in the accent color, marked with an arrow.
struct SourceLink: View {
    let label: String
    let url: String
    init(_ label: String, url: String) { self.label = label; self.url = url }
    var body: some View {
        Button { openSource(url) } label: { Label(label, systemImage: "arrow.up.right").labelStyle(TrailingIcon()) }
            .buttonStyle(.link)
            .font(.callout)
            .help(url)
    }
}

struct TrailingIcon: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 3) { configuration.title; configuration.icon.imageScale(.small) }
    }
}
