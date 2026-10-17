import LeXiKit
import SwiftUI

// One dictionary or translation page, drawn exactly from the QueryView Lexi.Core published.
struct EntryView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController

    var body: some View {
        let view = store.view
        VStack(alignment: .leading, spacing: 16) {
            HeadingRow(view: view, store: store, speech: speech)
            if !view.wordForms.isEmpty { WordFormsView(rows: view.wordForms) }
            if view.quickRead.visible { QuickReadCard(quickRead: view.quickRead) }
            if view.kind == .translation {
                if let translation = view.translation { TranslationCard(translation: translation, store: store) }
            } else {
                if let answers = view.answers, !answers.empty { AnswersCard(answers: answers, store: store) }
                LocalSourcesView(view: view, store: store)
                OnlineSourceView(view: view, store: store)
            }
        }
    }
}

// The headword in Apple's serif, with pronunciation below and the speaker and etymology beside it.
struct HeadingRow: View {
    let view: QueryView
    @Bindable var store: LexiStore
    var speech: SpeechController
    @State private var showEtymology = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Text(view.heading)
                    .font(view.kind == .translation ? .system(size: 26, weight: .bold) : LexiTheme.lexical(34, weight: .bold))
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 4)
                HStack(spacing: 14) {
                    if !view.speechText.isEmpty {
                        let playing = speech.speakingID == "heading"
                        Button { speech.toggle(view.speechText, using: store) } label: {
                            Image(systemName: playing ? "stop.fill" : "speaker.wave.2.fill").font(.system(size: 19))
                        }
                        .accessibilityLabel(playing ? "停止合成发音" : "播放合成发音")
                    }
                    if !view.etymology.isEmpty {
                        Button { showEtymology = true } label: {
                            RosettaStone().stroke(style: StrokeStyle(lineWidth: 1.6, lineCap: .round, lineJoin: .round))
                                .frame(width: 17, height: 21).contentShape(.rect)
                        }
                        .accessibilityLabel("查看词源")
                    }
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
            }
            if !view.pronunciation.text.isEmpty {
                HStack(spacing: 8) {
                    Text(view.pronunciation.text)
                        .font(view.kind == .translation ? .system(size: 15) : LexiTheme.lexical(18))
                        .foregroundStyle(.secondary)
                    if !view.pronunciation.label.isEmpty {
                        Text(view.pronunciation.label.trimmingCharacters(in: .whitespaces))
                            .font(.caption2.weight(.semibold)).foregroundStyle(.secondary)
                            .padding(.horizontal, 6).padding(.vertical, 2)
                            .background(.quaternary, in: .capsule)
                            .accessibilityLabel(view.pronunciation.labelAttribution)
                    }
                }
            }
        }
        .sheet(isPresented: $showEtymology) { EtymologySheet(items: view.etymology) }
    }
}

struct EtymologySheet: View {
    let items: [Etymology]
    @Environment(\.dismiss) private var dismiss
    var body: some View {
        NavigationStack {
            List {
                ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                    VStack(alignment: .leading, spacing: 6) {
                        Text(item.caption).font(.caption).foregroundStyle(.secondary)
                        Text(item.text).textSelection(.enabled)
                        if !item.generated { SourceLink(item.linkLabel, url: item.sourceUrl) }
                    }
                    .padding(.vertical, 4)
                }
            }
            .navigationTitle("词源")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("完成") { dismiss() } } }
        }
        .presentationDetents([.medium, .large])
    }
}

struct WordFormsView: View {
    let rows: [WordFormRow]
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                FlowLayout(spacing: 14, lineSpacing: 4) {
                    ForEach(Array(row.cells.enumerated()), id: \.offset) { _, cell in
                        Text("\(Text(cell.label + "  ").font(.caption).foregroundStyle(.secondary))\(Text(cell.spelling).font(LexiTheme.lexical(16, weight: cell.matches ? .semibold : .regular)))")
                            .contextMenu { Button { copyToPasteboard(cell.copyText) } label: { Label("拷贝词形", systemImage: "doc.on.doc") } }
                    }
                }
            }
        }
    }
}

struct QuickReadCard: View {
    let quickRead: QuickRead
    var body: some View {
        HStack(alignment: .center, spacing: 12) {
            QuickReadBadge()
            Text(quickRead.text).font(.system(size: 17, weight: .medium)).textSelection(.enabled)
            if quickRead.showState { Text(quickRead.state).font(.caption).foregroundStyle(.secondary) }
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(LexiTheme.pink.opacity(0.14), in: .rect(cornerRadius: 20, style: .continuous))
        .accessibilityElement(children: .combine)
        .accessibilityLabel(quickRead.caption + "。" + quickRead.text)
    }
}

struct TranslationCard: View {
    let translation: TranslationResult
    @Bindable var store: LexiStore
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
                ForEach(Array(translation.paragraphs.enumerated()), id: \.offset) { index, paragraph in
                    SpeakableText(id: "translation:\(index)", text: paragraph, font: .system(size: 17), service: store)
                }
                if !translation.note.trimmingCharacters(in: .whitespaces).isEmpty {
                    Text(translation.note).font(.callout).foregroundStyle(.secondary)
                }
                if translation.complete {
                    Button { copyToPasteboard(translation.paragraphs.joined(separator: "\n\n")) } label: { Label("拷贝译文", systemImage: "doc.on.doc") }
                        .buttonStyle(.bordered).buttonBorderShape(.capsule).controlSize(.small)
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentGroup()
    }
}

// Touch screens have no hover, so a long press offers 朗读 (or 停止朗读) and 拷贝; the block being
// read is tinted pale gold, as on the desktop. VoiceOver offers the same action.
struct SpeakableText: View {
    let id: String
    let text: String
    let font: Font
    var style: AnyShapeStyle = AnyShapeStyle(.primary)
    let service: any SpeechService
    @Environment(SpeechController.self) private var speech: SpeechController?

    var body: some View {
        let playing = speech?.speakingID == id
        Text(text)
            .font(font)
            .foregroundStyle(style)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 6).padding(.vertical, 3)
            .background(LexiTheme.gold.opacity(playing ? 0.18 : 0), in: .rect(cornerRadius: 8, style: .continuous))
            .padding(.horizontal, -6).padding(.vertical, -3)
            .contentShape(.rect)
            .contextMenu {
                Button { toggle() } label: { Label(playing ? "停止朗读" : "朗读", systemImage: playing ? "stop.fill" : "speaker.wave.2") }
                Button { copyToPasteboard(text) } label: { Label("拷贝", systemImage: "doc.on.doc") }
            }
            .accessibilityAction(named: playing ? "停止朗读" : "朗读") { toggle() }
            .animation(.easeOut(duration: 0.2), value: playing)
    }

    private func toggle() {
        guard let speech else { return }
        if speech.speakingID == id { speech.stop("发音已停止"); return }
        speech.play(text, service.speechSelection, using: service, finished: "发音播放结束", id: id)
    }
}

struct SourceLink: View {
    let label: String
    let url: String
    init(_ label: String, url: String) { self.label = label; self.url = url }
    var body: some View {
        Button { openSource(url) } label: {
            HStack(spacing: 3) { Text(label); Image(systemName: "arrow.up.right").imageScale(.small) }
        }
        .font(.callout)
        .buttonStyle(.plain)
        .foregroundStyle(.tint)
    }
}
