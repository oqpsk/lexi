import Foundation
import Observation

// The app's single source of truth: the latest QueryView from Lexi.Host plus front-end state
// (disclosures, original-source expansion) that Windows also keeps per window and per lookup.
@MainActor @Observable
public final class LexiStore {
    public enum Phase: Equatable { case starting, ready, failed(String) }
    public static let supportedProtocol = 1

    public private(set) var phase: Phase = .starting
    public private(set) var view: QueryView = .starting
    public private(set) var settings: SettingsPayload?
    public private(set) var history: HistoryList = .empty
    public private(set) var suggestions: [String] = []
    public private(set) var dictionaries: DictionaryList?
    public private(set) var version = ""
    // Disclosures the reader opened or closed in this lookup, keyed by Core sense keys plus
    // ":examples", ":sources" or ":alternatives". Untouched disclosures use their default.
    public var details: [String: Bool] = [:]
    // Original dictionary text: shown directly, then collapsed once definitions arrive.
    public private(set) var localWrapped = false
    public var localExpanded = false
    public var onlineExpanded = true
    private var onlineCollapsible = true
    private var client: HostClient?
    private var suggestionRequest = 0

    public init() {}

#if os(macOS)
    public func start(executable: URL, arguments: [String]) async {
        await start(transport: ProcessTransport(executable: executable, arguments: arguments))
    }
#endif

    public func start(transport: any HostTransport) async {
        let client = HostClient(transport: transport) { [weak self] event in
            Task { @MainActor in self?.handle(event) }
        }
        self.client = client
        do {
            try await client.start()
            let info: HostInfo = try await client.call("initialize")
            guard info.protocolVersion == Self.supportedProtocol else {
                phase = .failed("查询服务版本不匹配，请重新安装 LeXi。"); return
            }
            version = info.version; settings = info.settings
            receive(info.view)
            phase = .ready
            await refreshHistory()
        } catch {
            phase = .failed("无法启动查询服务：\(error.localizedDescription)")
        }
    }

    public func stop() async { await client?.stop() }

    // Model API keys for platforms without the subscription clients (iOS), from the Keychain.
    // The host keeps them in memory only (keys.set); an empty key removes it.
    @discardableResult public func setKey(_ provider: String, _ key: String) async -> Bool {
        (try? await client?.call("keys.set", ["provider": provider, "key": key], as: Nothing.self)) != nil
    }
    private struct Nothing: Decodable, Sendable {}
    public func failToStart(_ message: String) { phase = .failed(message) }

    private func handle(_ event: HostEvent) {
        switch event {
        case .view(let next): receive(next)
        case .exited: if phase != .starting { phase = .failed("查询服务已停止，请重新打开 LeXi。") }
        }
    }

    // Views can arrive faster than they are drawn; only newer revisions replace what is shown.
    public func receive(_ next: QueryView) {
        guard next.revision > view.revision else { return }
        let previous = view
        let fresh = next.request != previous.request
        view = next
        if fresh {
            if next.kind != .home { details.removeAll() }
            else { Task { await refreshHistory() } }
        }
        // Same rules as the Windows window: a redrawn source section starts expanded only when no
        // definitions are shown, collapses once definitions arrive, and reopens when none do.
        if fresh || previous.local != next.local { localWrapped = false; localExpanded = false }
        if fresh || previous.online != next.online { onlineExpanded = next.answers?.empty ?? true; onlineCollapsible = true }
        guard fresh || previous.answers != next.answers else { return }
        switch next.answers?.originals {
        case .collapse:
            if onlineCollapsible { onlineExpanded = false; onlineCollapsible = false }
            if !localWrapped, !next.local.isEmpty { localWrapped = true; localExpanded = false }
        case .expand: localExpanded = true; onlineExpanded = true
        default: break
        }
    }

    public func isOpen(_ key: String, default initial: Bool = false) -> Bool { details[key] ?? initial }
    public func setOpen(_ key: String, _ open: Bool) { details[key] = open }
    // A sense's references stay open when the reader was reading the matching fallback brief.
    public func sourcesOpenByDefault(_ sense: Sense) -> Bool { sense.referenceKeys.contains { details[$0] == true } }
    // Alternatives stay open when the reader was inside one of them before merging.
    public func alternativesOpenByDefault(_ sense: Sense) -> Bool {
        sense.alternatives.contains { alternative in details.contains { $0.value && $0.key.hasPrefix(alternative.key + ":") } }
    }

    // ---- Queries ----
    public func submit(_ text: String, force: Bool = false, scroll: Double = 0) async {
        suggestions = []
        _ = try? await client?.call("query.submit", Submit(text: text, force: force, scroll: scroll)) as SubmitResult?
        await refreshHistoryIfHome()
    }
    public func regenerate() async { _ = try? await client?.call("query.regenerate") as SubmitResult? }
    public func navigate(_ direction: Int, scroll: Double = 0) async -> QueryPage? {
        let result = try? await client?.call("query.navigate", Navigate(direction: direction, scroll: scroll)) as NavigateResult?
        return result?.page
    }
    public func home(scroll: Double = 0) async {
        suggestions = []
        _ = try? await client?.call("query.home", Scroll(scroll: scroll)) as SubmitResult?
        await refreshHistory()
    }
    public func cancelQuery() async { _ = try? await client?.call("query.cancel") as SubmitResult? }

    // Candidates are discarded when a newer keystroke has already asked again.
    public func suggest(_ text: String) async {
        suggestionRequest += 1
        let request = suggestionRequest
        let result = try? await client?.call("suggest", ["text": text]) as Suggestions?
        if request == suggestionRequest { suggestions = result?.suggestions ?? [] }
    }
    public func clearSuggestions() { suggestionRequest += 1; suggestions = [] }

    // ---- History ----
    public func refreshHistory() async {
        if let list = try? await client?.call("history.list") as HistoryList? { history = list }
    }
    private func refreshHistoryIfHome() async { if view.kind == .home { await refreshHistory() } }
    public func remove(_ entry: HistoryEntry) async {
        if let result = try? await client?.call("history.remove", RemoveHistory(query: entry.query, translation: entry.translation)) as HistoryRemoval? {
            history = result.history
        }
    }

    // ---- Settings ----
    public func setOnline(_ online: Bool) async {
        if let payload = try? await client?.call("settings.setOnline", ["online": online]) as SettingsPayload? { settings = payload }
    }
    // Returns the shared validation message when Lexi.Core rejects the change.
    @discardableResult
    public func update(_ changes: [String: SettingValue]) async -> String? {
        guard let client else { return nil }
        do { settings = try await client.call("settings.update", ["settings": changes]); return nil }
        catch let error as HostError { return error.message }
        catch { return error.localizedDescription }
    }
    public func refreshSettings() async {
        if let payload = try? await client?.call("settings.get") as SettingsPayload? { settings = payload }
    }
    public func refreshDictionaries() async {
        if let list = try? await client?.call("dictionaries.list") as DictionaryList? { dictionaries = list }
    }
    public func connectionStatus(_ provider: String) async -> LoginStatus? {
        try? await client?.call("connection.status", ["provider": provider]) as LoginStatus?
    }
    public func loginCommand(_ provider: String) async -> LoginCommand? {
        guard let client else { return nil }
        return try? await client.callOptional("connection.loginCommand", ["provider": provider], as: LoginCommand.self)
    }

    // ---- Speech ----
    public var speechSelection: SpeechSelection {
        let settings = settings?.settings
        return SpeechSelection(provider: settings?.speechProvider ?? "", model: settings?.speechModel ?? "", voice: settings?.speechVoice ?? "")
    }
    public func speechModels() async -> [SpeechModel] {
        (try? await client?.call("speech.models") as SpeechModels?)?.models ?? []
    }
    public func speechVoices(provider: String, model: String) async -> [SpeechVoice] {
        (try? await client?.call("speech.voices", ["provider": provider, "model": model]) as SpeechVoices?)?.voices ?? []
    }
    // The key is the one typed in settings or read from the Keychain; the host does not keep it.
    public func elevenVoices(key: String) async throws -> [SpeechVoice] {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.elevenVoices", ["key": key], as: SpeechVoices.self).voices
    }
    public func cachedSpeech(_ text: String, _ selection: SpeechSelection) async throws -> SpeechCache {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.cached", SpeechRequest(text: text, selection, key: nil))
    }
    public func synthesize(_ text: String, _ selection: SpeechSelection, key: String) async throws -> String {
        guard let client else { throw HostError.exited }
        return try await client.call("speech.synthesize", SpeechRequest(text: text, selection, key: key), as: SpeechFile.self).path
    }

    private struct Submit: Encodable, Sendable { let text: String; let force: Bool; let scroll: Double }
    private struct Navigate: Encodable, Sendable { let direction: Int; let scroll: Double }
    private struct Scroll: Encodable, Sendable { let scroll: Double }
    private struct RemoveHistory: Encodable, Sendable { let query: String; let translation: Bool }
    private struct SpeechModels: Decodable, Sendable { let models: [SpeechModel] }
    private struct SpeechVoices: Decodable, Sendable { let voices: [SpeechVoice] }
    private struct SpeechFile: Decodable, Sendable { let path: String }
    private struct SpeechRequest: Encodable, Sendable {
        let text: String, provider: String, model: String, voice: String, key: String?
        init(text: String, _ selection: SpeechSelection, key: String?) {
            self.text = text; provider = selection.provider; model = selection.model; voice = selection.voice; self.key = key
        }
    }
}

extension LexiStore: SpeechService {}
