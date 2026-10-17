import LeXiKit
import SwiftUI

// The one screen. Results scroll beneath a floating Liquid Glass search capsule at the bottom,
// within thumb reach, with suggestions just above it; history fills the page before a query.
struct RootView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @State private var text = ""
    @State private var previous = ""
    @State private var programmatic = false
    @State private var showSettings = false
    @FocusState private var searching: Bool

    var body: some View {
        NavigationStack {
            ScrollView {
                content
                    .padding(.horizontal, 18).padding(.top, 4).padding(.bottom, 24)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollDismissesKeyboard(.interactively)
            .background(Color(uiColor: .systemGroupedBackground))
            // In the safe area, so the list sits just above the search capsule and the keyboard.
            .overlay(alignment: .bottom) { suggestions }
            .safeAreaBar(edge: .bottom) { searchBar }
            .navigationTitle(store.view.kind == .home ? "LeXi" : "")
            .navigationBarTitleDisplayMode(store.view.kind == .home ? .large : .inline)
            .toolbar { toolbar }
            .sheet(isPresented: $showSettings) { SettingsView(store: store, speech: speech) }
        }
        .onChange(of: store.view.request) { _, _ in pageChanged() }
        .onChange(of: store.view.status) { _, _ in speech.clearStatus() }
        // The lookup is ready the moment LeXi opens, as on the desktop. The field may join the
        // hierarchy a moment after the first appearance, so focus is asked for again on the next turn.
        .defaultFocus($searching, true)
        .task {
            await Task.yield()
            // For UI checks and screenshots: `-LexiSettings YES` opens settings at launch.
            if UserDefaults.standard.bool(forKey: "LexiSettings") { showSettings = true } else { searching = true }
        }
    }

    @ViewBuilder private var content: some View {
        switch store.phase {
        case .failed(let message):
            ContentUnavailableView("LeXi 无法查询", systemImage: "exclamationmark.triangle", description: Text(message))
        case .starting:
            ProgressView().frame(maxWidth: .infinity).padding(.top, 80)
        case .ready:
            if store.view.kind == .home {
                HistoryView(store: store) { entry in setText(entry.query); submit(entry.query) }
            } else {
                EntryView(store: store, speech: speech)
            }
        }
    }

    private var online: Bool { store.settings?.settings.online ?? true }
    private var canRegenerate: Bool { store.view.kind != .home && store.view.canRegenerate && online }
    private var status: String { speech.status.isEmpty ? store.view.status : speech.status }

    private var searchBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            if store.view.kind != .home && !status.isEmpty {
                Text(status).font(.footnote).foregroundStyle(.secondary).lineLimit(1).padding(.horizontal, 8)
                    .accessibilityLabel("查询状态：" + status)
            }
            HStack(spacing: 10) {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary).accessibilityHidden(true)
                TextField("查词或翻译", text: $text, axis: .vertical)
                    .lineLimit(1...4)
                    .font(.system(size: 18))
                    .focused($searching)
                    .submitLabel(.search)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .onChange(of: text) { old, value in edited(old, value) }
                    .accessibilityLabel("查询")
                if store.view.progress?.busy == true { ProgressView().controlSize(.small) }
                if !text.isEmpty {
                    Button { clear() } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary) }
                        .buttonStyle(.plain)
                        .accessibilityLabel("清除")
                }
            }
            .padding(.horizontal, 18).padding(.vertical, 13)
            .glassEffect(.regular.interactive(), in: .rect(cornerRadius: 26, style: .continuous))
        }
        .padding(.horizontal, 14).padding(.bottom, 6)
    }

    @ViewBuilder private var suggestions: some View {
        if searching && !store.suggestions.isEmpty {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(Array(store.suggestions.prefix(6).enumerated()), id: \.offset) { _, word in
                    Button { setText(word); submit(word) } label: {
                        Text(word).font(LexiTheme.lexical(18))
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 18).padding(.vertical, 11)
                            .contentShape(.rect)
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(.vertical, 6)
            .glassEffect(.regular, in: .rect(cornerRadius: 22, style: .continuous))
            .padding(.horizontal, 14).padding(.bottom, 10)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("输入建议")
        }
    }

    @ToolbarContentBuilder private var toolbar: some ToolbarContent {
        let navigation = store.view.navigation
        ToolbarItemGroup(placement: .topBarLeading) {
            Button { navigate(-1) } label: { Image(systemName: "chevron.backward") }
                .disabled(!navigation.canBack).accessibilityLabel("后退")
            Button { navigate(1) } label: { Image(systemName: "chevron.forward") }
                .disabled(!navigation.canForward).accessibilityLabel("前进")
        }
        ToolbarItemGroup(placement: .topBarTrailing) {
            Menu {
                if store.view.kind != .home {
                    Button { regenerate() } label: { Label("重新生成", systemImage: "arrow.clockwise") }.disabled(!canRegenerate)
                }
                Button { Task { await store.setOnline(!online) } } label: {
                    Label(store.settings?.controls.onlineLabel ?? "在线补充", systemImage: online ? "globe" : "network.slash")
                }
                Button { goHome() } label: { Label("查询历史", systemImage: "clock") }
            } label: { Image(systemName: "ellipsis") }
            .accessibilityLabel("更多操作")
            Button { showSettings = true } label: { Image(systemName: "gearshape") }.accessibilityLabel("设置")
        }
    }

    private func setText(_ value: String) { if text != value { programmatic = true; text = value } }

    // Return on the keyboard ends the query; pasted line breaks stay, for translation.
    private func edited(_ old: String, _ value: String) {
        if programmatic { programmatic = false; return }
        if value.hasSuffix("\n") && String(value.dropLast()) == old {
            setText(old); submit(old); return
        }
        if value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            store.clearSuggestions()
            if store.view.kind != .home { Task { await store.home() } }
            return
        }
        Task { await store.suggest(value) }
    }

    private func submit(_ value: String) {
        guard !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        speech.stop()
        store.clearSuggestions()
        searching = false
        Task { await store.submit(value) }
    }

    private func clear() {
        setText("")
        store.clearSuggestions()
        Task { await store.home() }
        searching = true
    }

    private func regenerate() { guard canRegenerate else { return }; speech.stop(); Task { await store.regenerate() } }

    private func goHome() { speech.stop(); setText(""); Task { await store.home() } }

    private func navigate(_ direction: Int) {
        Task {
            guard let page = await store.navigate(direction) else { return }
            if page.query.isEmpty { setText("") }
        }
    }

    // A new page (from a link, history or back/forward) puts the keyboard away so the result shows.
    private func pageChanged() {
        speech.stop()
        if store.view.kind != .home { setText(store.view.input); searching = false }
    }
}

struct HistoryView: View {
    @Bindable var store: LexiStore
    let open: (HistoryEntry) -> Void

    var body: some View {
        if store.history.entries.isEmpty {
            ContentUnavailableView {
                Label("查一个词", systemImage: "character.book.closed")
            } description: {
                Text(store.history.error.isEmpty ? "在下方输入单词、短语或一段文字。查过的内容会留在这里。" : store.history.error)
            }
            .padding(.top, 40)
        } else {
            VStack(alignment: .leading, spacing: 10) {
                Text("最近查询").font(.subheadline.weight(.semibold)).foregroundStyle(.secondary).padding(.horizontal, 4)
                VStack(spacing: 0) {
                    ForEach(Array(store.history.entries.enumerated()), id: \.element.id) { index, entry in
                        if index > 0 { Divider().padding(.leading, 18) }
                        Button { open(entry) } label: {
                            HStack(spacing: 12) {
                                Text(entry.preview)
                                    .font(entry.translation ? .system(size: 17) : LexiTheme.lexical(19))
                                    .lineLimit(1).foregroundStyle(.primary)
                                Spacer(minLength: 8)
                                Text(entry.kind).font(.caption).foregroundStyle(.secondary)
                            }
                            .padding(.horizontal, 18).padding(.vertical, 13)
                            .contentShape(.rect)
                        }
                        .buttonStyle(.plain)
                        .contextMenu {
                            Button(role: .destructive) { Task { await store.remove(entry) } } label: { Label("从历史中删除", systemImage: "trash") }
                        }
                    }
                }
                .contentGroup()
            }
        }
    }
}
