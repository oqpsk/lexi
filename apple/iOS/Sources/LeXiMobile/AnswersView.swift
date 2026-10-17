import LeXiKit
import SwiftUI

// Model definitions with their sources, as Lexi.Core grouped and merged them.
struct AnswersCard: View {
    let answers: Answers
    @Bindable var store: LexiStore

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(answers.references.enumerated()), id: \.element.key) { index, reference in
                Row(first: index == 0) { ReferenceRow(reference: reference, store: store) }
            }
            ForEach(Array(answers.senses.enumerated()), id: \.element.key) { index, sense in
                Row(first: index + answers.references.count == 0) {
                    VStack(alignment: .leading, spacing: 5) {
                        SenseHeadingText(heading: sense.heading)
                        SenseContent(sense: sense, store: store)
                    }
                }
            }
            ForEach(Array(answers.notices.enumerated()), id: \.offset) { _, notice in
                Label(notice.text, systemImage: notice.warning ? "exclamationmark.triangle" : "info.circle")
                    .font(.callout).foregroundStyle(notice.warning ? AnyShapeStyle(.orange) : AnyShapeStyle(.secondary)).padding(.top, 10)
            }
            if !answers.notes.isEmpty {
                Disclosure(label: "补充说明", key: "lookup:notes", store: store) {
                    ForEach(answers.notes, id: \.self) { Text($0).font(.callout).foregroundStyle(.secondary) }
                }
                .padding(.top, 8)
            }
            if !answers.mergeWarning.isEmpty {
                Label(answers.mergeWarning, systemImage: "exclamationmark.triangle").font(.callout).foregroundStyle(.orange).padding(.top, 10)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentGroup()
    }
}

private struct Row<Content: View>: View {
    let first: Bool
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if !first { Divider().padding(.vertical, 14) }
            content
        }
    }
}

struct SenseHeadingText: View {
    let heading: SenseHeading
    var body: some View {
        var text = Text(heading.prefix + "  ").monospacedDigit().foregroundStyle(.tertiary)
        // The part of speech stands out in pale gold, as on Windows and macOS.
        text = Text("\(text)\(Text(heading.partOfSpeech).font(LexiTheme.lexical(17)).italic().foregroundStyle(LexiTheme.gold))")
        if !heading.form.isEmpty { text = Text("\(text)\(Text("  " + heading.form).font(LexiTheme.lexical(15)).italic())") }
        if !heading.usage.isEmpty { text = Text("\(text)\(Text("  " + heading.usage))") }
        if heading.abbreviation { text = Text("\(text)\(Text("  缩写"))") }
        if heading.provisional { text = Text("\(text)\(Text(" · 生成中"))") }
        return text.font(.subheadline).foregroundStyle(.secondary)
    }
}

struct SenseContent: View {
    let sense: Sense
    @Bindable var store: LexiStore
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            if !sense.expansion.isEmpty {
                Text(sense.expansion).font(LexiTheme.lexical(16)).foregroundStyle(.secondary)
            }
            SpeakableText(id: sense.key + ":chinese", text: sense.chinese, font: .system(size: 18), service: store)
            SpeakableText(id: sense.key + ":english", text: sense.english, font: .system(size: 16), style: AnyShapeStyle(.secondary), service: store)
            DisclosureBar(sense: sense, store: store)
        }
    }
}

// The three secondary disclosures of a sense, with their bodies below the buttons.
struct DisclosureBar: View {
    let sense: Sense
    @Bindable var store: LexiStore

    private var hasExamples: Bool { !sense.example.trimmingCharacters(in: .whitespaces).isEmpty || !sense.exampleChinese.trimmingCharacters(in: .whitespaces).isEmpty }
    private var examplesKey: String { sense.key + ":examples" }
    private var sourcesKey: String { sense.key + ":sources" }
    private var alternativesKey: String { sense.key + ":alternatives" }
    private var sourcesOpen: Bool { store.isOpen(sourcesKey, default: store.sourcesOpenByDefault(sense)) }
    private var alternativesOpen: Bool { store.isOpen(alternativesKey, default: store.alternativesOpenByDefault(sense)) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            FlowLayout(spacing: 8) {
                if hasExamples { DisclosureButton(label: "例句", open: store.isOpen(examplesKey)) { store.setOpen(examplesKey, $0) } }
                DisclosureButton(label: sense.sourceLabel, open: sourcesOpen) { store.setOpen(sourcesKey, $0) }
                if !sense.alternatives.isEmpty { DisclosureButton(label: sense.alternativesLabel, open: alternativesOpen) { store.setOpen(alternativesKey, $0) } }
            }
            .padding(.top, 4)
            if hasExamples && store.isOpen(examplesKey) {
                DetailBody {
                    if !sense.example.isEmpty { SpeakableText(id: sense.key + ":example", text: sense.example, font: LexiTheme.lexical(16).italic(), service: store) }
                    if !sense.exampleChinese.isEmpty { SpeakableText(id: sense.key + ":exampleChinese", text: sense.exampleChinese, font: LexiTheme.chineseExample(16), service: store) }
                }
            }
            if sourcesOpen {
                DetailBody {
                    Text(sense.provenance).font(.callout).foregroundStyle(.secondary)
                    Text(sense.originalLabels).font(.callout).foregroundStyle(.secondary)
                    if !sense.citations.isEmpty {
                        FlowLayout(spacing: 12) { ForEach(sense.citations) { citation in SourceLink(citation.label, url: citation.url) } }
                    }
                    ForEach(Array(sense.briefs.enumerated()), id: \.offset) { _, brief in
                        Text(brief.caption).font(.callout).foregroundStyle(.secondary)
                        Text(brief.chinese).textSelection(.enabled)
                        Text(brief.english).textSelection(.enabled)
                    }
                    if !sense.sourceNote.isEmpty { Text(sense.sourceNote).font(.callout).foregroundStyle(.secondary) }
                    if !sense.alternativesNote.isEmpty { Text(sense.alternativesNote).font(.callout).foregroundStyle(.secondary) }
                }
            }
            if !sense.alternatives.isEmpty && alternativesOpen {
                DetailBody {
                    ForEach(sense.alternatives) { alternative in
                        VStack(alignment: .leading, spacing: 4) {
                            SenseHeadingText(heading: alternative.heading)
                            SenseContent(sense: alternative, store: store)
                        }
                        .padding(.bottom, 4)
                    }
                }
            }
        }
    }
}

struct ReferenceRow: View {
    let reference: Reference
    @Bindable var store: LexiStore
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(reference.caption).font(.callout).foregroundStyle(.secondary)
            if !reference.expansion.isEmpty { Text(reference.expansion).font(LexiTheme.lexical(16)).foregroundStyle(.secondary) }
            SpeakableText(id: reference.key + ":chinese", text: reference.chinese, font: .system(size: 18), service: store)
            SpeakableText(id: reference.key + ":english", text: reference.english, font: .system(size: 16), style: AnyShapeStyle(.secondary), service: store)
            Disclosure(label: "参考资料", key: reference.key, store: store) {
                Text(reference.sourceNote).font(.callout).foregroundStyle(.secondary)
                SourceLink(reference.sourceName, url: reference.sourceUrl)
            }
        }
    }
}

struct Disclosure<Content: View>: View {
    let label: String
    let key: String
    @Bindable var store: LexiStore
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            DisclosureButton(label: label, open: store.isOpen(key)) { store.setOpen(key, $0) }
            if store.isOpen(key) { DetailBody { content } }
        }
    }
}

struct DisclosureButton: View {
    let label: String
    let open: Bool
    let toggle: (Bool) -> Void
    var body: some View {
        Button { withAnimation(.snappy) { toggle(!open) } } label: {
            HStack(spacing: 4) {
                Image(systemName: "chevron.right").imageScale(.small).rotationEffect(.degrees(open ? 90 : 0))
                Text(label)
            }
            .font(.subheadline)
        }
        .buttonStyle(.bordered)
        .buttonBorderShape(.capsule)
        .controlSize(.small)
        .tint(.secondary)
        .accessibilityLabel(label)
        .accessibilityValue(open ? "已展开" : "已收起")
    }
}

struct DetailBody<Content: View>: View {
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 5) { content }
            .padding(.leading, 12)
            .overlay(alignment: .leading) { Capsule().fill(.quaternary).frame(width: 3) }
            .padding(.vertical, 2)
    }
}

// Original dictionary text: shown directly, then kept in one collapsed group once definitions exist.
struct LocalSourcesView: View {
    let view: QueryView
    @Bindable var store: LexiStore
    var body: some View {
        if !view.localHint.isEmpty {
            Label(view.localHint, systemImage: "books.vertical").font(.callout).foregroundStyle(.secondary)
        } else if !view.local.isEmpty {
            if store.localWrapped {
                DisclosureGroup(isExpanded: $store.localExpanded) { cards.padding(.top, 8) } label: {
                    Text("离线词典原文 · \(view.local.count) 部").font(.headline)
                }
                .tint(.primary)
            } else {
                VStack(alignment: .leading, spacing: 8) {
                    Text("离线词典原文").font(.headline)
                    cards
                }
            }
        }
    }
    private var cards: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(view.local.enumerated()), id: \.element.id) { index, source in
                if index > 0 { Divider().padding(.vertical, 12) }
                LocalSourceCard(source: source)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .contentGroup()
    }
}

struct LocalSourceCard: View {
    let source: LocalSource
    @State private var showOriginal = false
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Text(source.name).font(.subheadline.weight(.semibold))
                Spacer()
                SourceLink("词典来源", url: source.sourceUrl)
            }
            if !source.headword.isEmpty { Text(source.headword).font(.callout).foregroundStyle(.secondary) }
            if !source.chinese.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { Text(source.chinese).font(.system(size: 16)).textSelection(.enabled) }
            if !source.english.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { Text(source.english).font(.system(size: 16)).foregroundStyle(.secondary).textSelection(.enabled) }
            if !source.original.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                DisclosureGroup("完整原始条目", isExpanded: $showOriginal) { Text(source.original).font(.callout).textSelection(.enabled).padding(.top, 4) }
                    .font(.callout).tint(.secondary)
            }
            Text(source.attribution).font(.caption).foregroundStyle(.tertiary)
        }
    }
}

struct OnlineSourceView: View {
    let view: QueryView
    @Bindable var store: LexiStore
    var body: some View {
        if let online = view.online {
            DisclosureGroup(isExpanded: $store.onlineExpanded) {
                VStack(alignment: .leading, spacing: 5) {
                    ForEach(Array(online.senses.enumerated()), id: \.offset) { index, sense in
                        if index > 0 { Spacer().frame(height: 8) }
                        Text(sense.label).font(.subheadline).foregroundStyle(.secondary)
                        Text(sense.definition).font(.system(size: 16)).textSelection(.enabled)
                        ForEach(Array(sense.examples.enumerated()), id: \.offset) { _, example in
                            Text(example.text).font(example.chinese ? LexiTheme.chineseExample(15) : LexiTheme.lexical(15)).italic()
                                .foregroundStyle(.secondary).textSelection(.enabled)
                        }
                    }
                    HStack {
                        Text(online.footer).font(.caption).foregroundStyle(.tertiary)
                        Spacer()
                        SourceLink("Wiktionary", url: online.url)
                    }
                    .padding(.top, 10)
                }
                .padding(18)
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentGroup()
                .padding(.top, 8)
            } label: {
                Text(online.header).font(.headline)
            }
            .tint(.primary)
        } else if !view.onlineHint.isEmpty {
            Label(view.onlineHint, systemImage: "globe").font(.callout).foregroundStyle(.secondary)
        }
    }
}
