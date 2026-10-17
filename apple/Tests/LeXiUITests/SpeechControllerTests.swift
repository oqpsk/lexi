import Foundation
import Testing
@testable import LeXiKit
@testable import LeXiUI

// The speech flow with a synthetic host and key store. No case reaches audio output: each ends in
// an error, a stop, or a file that is not audio.
@MainActor @Suite struct SpeechFlow {
    final class Keys: SpeechKeys, @unchecked Sendable {
        var values: [String: String] = [:]
        func read(_ provider: String) throws -> String? { values[provider] }
        func save(_ provider: String, _ value: String) throws { values[provider] = value }
        func remove(_ provider: String) throws { values[provider] = nil }
        func hasSaved(_ provider: String) -> Bool { values[provider] != nil }
    }

    @MainActor final class Host: SpeechService {
        var speechSelection = SpeechSelection(provider: "openai", model: "gpt-4o-mini-tts", voice: "coral")
        var cache = SpeechCache(path: nil, local: false)
        var probeFailure: HostError?
        var answer: Result<String, HostError> = .failure(HostError(code: "speech_failed", message: "请在设置 → 发音的在线语音连接中填写所选服务的 API Key。"))
        var hold = false
        var held: CheckedContinuation<String, Error>?
        var keys: [String] = []
        var statusWhileGenerating = ""
        weak var controller: SpeechController?

        func cachedSpeech(_ text: String, _ selection: SpeechSelection) async throws -> SpeechCache {
            if let probeFailure { throw probeFailure }
            return cache
        }
        func synthesize(_ text: String, _ selection: SpeechSelection, key: String) async throws -> String {
            keys.append(key)
            statusWhileGenerating = controller?.status ?? ""
            if hold { return try await withCheckedThrowingContinuation { held = $0 } }
            return try answer.get()
        }
    }

    private func make(_ keys: Keys = Keys()) -> (SpeechController, Host) {
        let controller = SpeechController(keys: keys)
        let host = Host()
        host.controller = controller
        return (controller, host)
    }

    @Test func showsCoreMessagesForMissingCachedAudio() async {
        let (speech, host) = make()
        host.probeFailure = HostError(code: "speech_failed", message: "离线模式下没有此声线的缓存发音。")
        speech.toggle("word", using: host)
        #expect(speech.speaking)
        await speech.work?.value
        #expect(!speech.speaking && speech.status == "离线模式下没有此声线的缓存发音。" && host.keys.isEmpty)
    }

    @Test func generatesWithTheTypedKeyBeforeTheSavedOne() async {
        let keys = Keys()
        keys.values["openai"] = "synthetic-saved"
        let (speech, host) = make(keys)
        speech.play("word", host.speechSelection, key: "synthetic-typed", using: host, finished: "试听结束")
        await speech.work?.value
        speech.toggle("word", using: host)
        await speech.work?.value
        #expect(host.keys == ["synthetic-typed", "synthetic-saved"])
        #expect(host.statusWhileGenerating == "正在生成发音…")
        #expect(!speech.speaking && speech.status == "请在设置 → 发音的在线语音连接中填写所选服务的 API Key。")
    }

    @Test func reportsAudioThatCannotPlay() async throws {
        let file = FileManager.default.temporaryDirectory.appending(path: "lexi-not-audio-\(UUID().uuidString).mp3")
        try Data("not audio".utf8).write(to: file)
        defer { try? FileManager.default.removeItem(at: file) }
        let (speech, host) = make()
        host.cache = SpeechCache(path: file.path, local: false)
        speech.toggle("word", using: host)
        await speech.work?.value
        #expect(host.keys.isEmpty && !speech.speaking && speech.status == "音频无法播放，请检查系统音频输出。")
    }

    @Test func missingSystemVoiceIsReported() async {
        let (speech, host) = make()
        host.cache = SpeechCache(path: nil, local: true)
        host.speechSelection = SpeechSelection(provider: "macos", model: "avspeech", voice: "io.github.oqpsk.lexi.no-such-voice")
        speech.toggle("word", using: host)
        await speech.work?.value
        #expect(!speech.speaking && speech.status == "没有匹配的系统声线，请在设置 → 发音中另选声线或在线服务。")
    }

    @Test func stoppingDropsTheLateResult() async throws {
        let (speech, host) = make()
        host.hold = true
        speech.toggle("word", using: host)
        while host.held == nil { await Task.yield() }
        speech.toggle("word", using: host)
        #expect(!speech.speaking && speech.status == "发音已停止")
        host.held?.resume(returning: "/nonexistent/late.mp3")
        await speech.work?.value
        #expect(!speech.speaking && speech.status == "发音已停止")
        speech.clearStatus()
        #expect(speech.status.isEmpty)
    }

    // Each speaker controls only its own item: another one switches to it, the same one stops.
    @Test func speakersSwitchBetweenItemsAndStopOnlyTheirOwn() async throws {
        let (speech, host) = make()
        host.hold = true
        speech.toggle("serendipity", using: host)
        while host.held == nil { await Task.yield() }
        #expect(speech.speakingID == "heading")
        let first = host.held
        host.held = nil
        speech.toggle("A happy accident.", id: "sense:english", using: host)
        #expect(speech.speaking && speech.speakingID == "sense:english")
        first?.resume(returning: "/nonexistent/late.mp3")
        while host.held == nil { await Task.yield() }
        #expect(speech.speakingID == "sense:english", "the replaced headword request does not end the definition")
        speech.toggle("A happy accident.", id: "sense:english", using: host)
        #expect(!speech.speaking && speech.speakingID == nil && speech.status == "发音已停止")
        host.held?.resume(returning: "/nonexistent/late.mp3")
        await speech.work?.value
        #expect(speech.speakingID == nil)
    }

    @Test func elevenLabsChoicesKeepSavedVoices() {
        let george = SpeechVoice(id: "JBFqnCBsd6RMkjVDRZzb", name: "George")
        let saved = SpeechVoice(id: "synthetic_private_voice", name: "Synthetic private voice")
        #expect(SpeechChoices.eleven(loaded: nil, saved: nil, initial: [george]) == [george])
        #expect(SpeechChoices.eleven(loaded: [], saved: nil, initial: [george]).isEmpty)
        #expect(SpeechChoices.eleven(loaded: [], saved: saved, initial: [george]) == [saved])
        #expect(SpeechChoices.eleven(loaded: [saved], saved: saved, initial: [george]) == [saved])
    }

    @Test func systemVoicesStartWithAutomaticChoice() {
        let choices = SystemVoices.choices()
        #expect(choices.first?.id == "auto")
        #expect(SystemVoices.resolve("io.github.oqpsk.lexi.no-such-voice", for: "word") == nil)
        #expect(SystemVoices.containsChinese("你好 world") && !SystemVoices.containsChinese("hello"))
        if choices.count > 1 { #expect(SystemVoices.resolve(choices[1].id, for: "word")?.identifier == choices[1].id) }
    }
}
