import LeXiKit
import SwiftUI

// iOS has no official subscription clients, so models are reached with the user's own API keys.
// Keys stay in the Keychain on this device and reach Lexi.Core in memory only.
struct SettingsView: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @Environment(\.dismiss) private var dismiss
    @AppStorage(Appearance.key) private var appearance = Appearance.system.rawValue

    var body: some View {
        NavigationStack {
            Form {
                KeysSection(store: store)
                if let payload = store.settings { ModelSection(store: store, payload: payload) }
                SpeechSection(store: store, speech: speech)
                DictionarySection(store: store)
                Section("外观") {
                    Picker("外观", selection: $appearance) {
                        ForEach(Appearance.allCases) { Text($0.title).tag($0.rawValue) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                }
                Section {
                    LabeledContent("版本", value: store.version.isEmpty ? "—" : store.version)
                    SourceLink("词库与许可", url: "https://github.com/oqpsk/LeXi/blob/main/THIRD_PARTY_NOTICES.md")
                } footer: {
                    Text("LeXi 不需要注册，不收集使用数据。只有查 Wiktionary、调用你选的模型和合成在线语音时才会联网。")
                }
            }
            .navigationTitle("设置")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar { ToolbarItem(placement: .confirmationAction) { Button("完成") { dismiss() } } }
            .task { await store.refreshSettings(); await store.refreshDictionaries() }
        }
    }
}

// One secure field per service. The OpenAI key also serves OpenAI speech.
struct KeysSection: View {
    @Bindable var store: LexiStore
    @State private var inputs: [String: String] = [:]
    @State private var saved: Set<String> = []
    @State private var message = ""
    private let keys = KeychainSpeechKeys()

    var body: some View {
        Section {
            ForEach(ModelKeys.providers, id: \.self) { provider in
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text(provider == "openai" ? "OpenAI" : "Anthropic").font(.body.weight(.medium))
                        Spacer()
                        Label(saved.contains(provider) ? "已保存" : "未填写", systemImage: saved.contains(provider) ? "checkmark.circle.fill" : "circle")
                            .labelStyle(.titleAndIcon).font(.caption)
                            .foregroundStyle(saved.contains(provider) ? AnyShapeStyle(.green) : AnyShapeStyle(.secondary))
                    }
                    SecureField(saved.contains(provider) ? "留空保留已有密钥" : "粘贴 API Key",
                                text: Binding(get: { inputs[provider] ?? "" }, set: { inputs[provider] = $0 }))
                        .textInputAutocapitalization(.never).autocorrectionDisabled()
                        .onSubmit { save(provider) }
                    HStack {
                        Button("保存") { save(provider) }.disabled((inputs[provider] ?? "").trimmingCharacters(in: .whitespaces).isEmpty)
                        Spacer()
                        Button("清除", role: .destructive) { remove(provider) }.disabled(!saved.contains(provider))
                    }
                    .buttonStyle(.borderless)
                }
                .padding(.vertical, 4)
            }
            if !message.isEmpty { Label(message, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
        } header: {
            Text("模型服务")
        } footer: {
            Text("iOS 上没有官方客户端，LeXi 用你自己的 API Key 访问 OpenAI 与 Anthropic，按服务商的 API 计费，与 ChatGPT、Claude 订阅分开。密钥只保存在本机钥匙串。OpenAI 的密钥也用于 OpenAI 在线语音。")
        }
        .onAppear { saved = Set(ModelKeys.providers.filter { keys.hasSaved($0) }) }
    }

    private func save(_ provider: String) {
        let value = (inputs[provider] ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return }
        do {
            try keys.save(provider, value)
            inputs[provider] = nil; saved.insert(provider); message = ""
            Task { await store.setKey(provider, value) }
        } catch { message = SpeechController.message(error) }
    }

    private func remove(_ provider: String) {
        do {
            try keys.remove(provider)
            saved.remove(provider); message = ""
            Task { await store.setKey(provider, "") }
        } catch { message = SpeechController.message(error) }
    }
}

struct ModelSection: View {
    @Bindable var store: LexiStore
    let payload: SettingsPayload
    @State private var error: String?

    var body: some View {
        let settings = payload.settings
        Section {
            Toggle("在线补充", isOn: binding(settings.online, "online"))
            Toggle("自动补充双语释义", isOn: binding(settings.autoAi, "autoAi"))
            picker("主要模型", payload.models, selected: settings.provider + ":" + settings.model) { ["provider": .string($0.provider), "model": .string($0.id)] }
            Toggle("双模型同时查词", isOn: binding(settings.parallelLookup, "parallelLookup"))
            if settings.parallelLookup {
                picker("第二个模型", payload.models, selected: secondaryKey(settings)) { ["secondaryProvider": .string($0.provider), "secondaryModel": .string($0.id)] }
            }
            Toggle("自动整理同类义项", isOn: binding(settings.mergeSenses, "mergeSenses"))
            if settings.mergeSenses {
                picker("义项判断模型", payload.judgeModels, selected: judgeKey(settings)) { ["judgeModel": .string($0.id)] }
            }
            if let error { Label(error, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
        } header: {
            Text("查询")
        } footer: {
            Text("两个模型独立查词，先返回的释义先显示；缺少某个服务的密钥时，只有那个模型无法查询。")
        }
    }

    private func secondaryKey(_ settings: AppSettings) -> String {
        settings.secondaryProvider.isEmpty
            ? (settings.provider == "anthropic" ? "openai:gpt-6-luna" : "anthropic:claude-haiku-5-5")
            : settings.secondaryProvider + ":" + settings.secondaryModel
    }
    private func judgeKey(_ settings: AppSettings) -> String {
        (settings.judgeModel.hasPrefix("claude") ? "anthropic:" : "openai:") + settings.judgeModel
    }

    private func binding(_ value: Bool, _ key: String) -> Binding<Bool> {
        Binding(get: { value }, set: { apply([key: .bool($0)]) })
    }

    private func picker(_ title: String, _ choices: [ModelChoice], selected: String, change: @escaping (ModelChoice) -> [String: SettingValue]) -> some View {
        Picker(title, selection: Binding(get: { selected }, set: { key in
            if let choice = choices.first(where: { $0.key == key }) { apply(change(choice)) }
        })) {
            ForEach(choices, id: \.key) { Text($0.label).tag($0.key) }
        }
    }

    private func apply(_ changes: [String: SettingValue]) { Task { error = await store.update(changes) } }
}

struct SpeechSection: View {
    @Bindable var store: LexiStore
    var speech: SpeechController
    @State private var models: [SpeechModel] = []
    @State private var voices: [SpeechVoice] = []
    @State private var message: String?

    var body: some View {
        Section {
            if let settings = store.settings?.settings, !models.isEmpty {
                Picker("发音模型", selection: Binding(get: { settings.speechProvider + ":" + settings.speechModel }, set: { key in
                    guard let model = models.first(where: { $0.key == key }) else { return }
                    Task { await choose(model) }
                })) { ForEach(models, id: \.key) { Text($0.name).tag($0.key) } }
                Picker("声线", selection: Binding(get: { settings.speechVoice }, set: { id in
                    guard let voice = voices.first(where: { $0.id == id }), let model = models.first(where: { $0.key == settings.speechProvider + ":" + settings.speechModel }) else { return }
                    Task { message = await store.update(["speechProvider": .string(model.provider), "speechModel": .string(model.id), "speechVoice": .string(voice.id), "speechVoiceName": .string(voice.name)]) }
                })) { ForEach(voices) { Text($0.name).tag($0.id) } }
                Button { speech.toggle("Hello, this is LeXi.", id: "preview", using: store) } label: {
                    Label(speech.speakingID == "preview" ? "停止" : "试听", systemImage: speech.speakingID == "preview" ? "stop.fill" : "play.fill")
                }
                if speech.speakingID == "preview" || !speech.status.isEmpty, !speech.status.isEmpty {
                    Text(speech.status).font(.callout).foregroundStyle(.secondary)
                }
            } else {
                ProgressView()
            }
            if let message { Label(message, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.red).font(.callout) }
        } header: {
            Text("发音")
        } footer: {
            Text("本机声线离线可用；在线语音按服务商的 API 计费，生成过的发音会缓存。在 Mac 或 Windows 上设置的 Grok、ElevenLabs 密钥不会同步到这里。")
        }
        .task { await load() }
    }

    private func load() async {
        models = await store.speechModels().filter { $0.local || $0.provider == "openai" }
        guard let settings = store.settings?.settings, let model = models.first(where: { $0.provider == settings.speechProvider && $0.id == settings.speechModel }) ?? models.first else { return }
        voices = await choices(model)
    }

    private func choices(_ model: SpeechModel) async -> [SpeechVoice] {
        model.local ? SystemVoices.choices() : await store.speechVoices(provider: model.provider, model: model.id)
    }

    private func choose(_ model: SpeechModel) async {
        speech.stop()
        voices = await choices(model)
        guard let voice = voices.first else { return }
        message = await store.update(["speechProvider": .string(model.provider), "speechModel": .string(model.id),
                                      "speechVoice": .string(voice.id), "speechVoiceName": .string(voice.name)])
    }
}

struct DictionarySection: View {
    @Bindable var store: LexiStore
    var body: some View {
        Section {
            if let list = store.dictionaries, !list.dictionaries.isEmpty {
                ForEach(list.dictionaries) { dictionary in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(dictionary.name)
                        Text("\(dictionary.languages) · \(dictionary.entryCount) 条").font(.caption).foregroundStyle(.secondary)
                    }
                }
            } else {
                Text("尚未安装离线词库").foregroundStyle(.secondary)
            }
        } header: {
            Text("离线词库")
        } footer: {
            Text("没有离线词库时，LeXi 仍可查 Wiktionary 与模型释义。iOS 版暂不能下载词库。")
        }
    }
}
