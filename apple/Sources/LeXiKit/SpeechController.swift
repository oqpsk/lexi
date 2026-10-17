import AVFoundation
import Observation

// Speech starts only on an explicit click and never changes provider. Lexi.Core checks the text
// and voice first; system voices then speak directly, and online voices play the MP3 that Core
// cached for this text, provider, model and voice, generating it only when it is missing.
@MainActor @Observable
public final class SpeechController: NSObject {
    public private(set) var speaking = false
    // What is being read: "heading" for the headword, otherwise a definition or example's key.
    public private(set) var speakingID: String?
    // Generation, playback and error messages, worded as on Windows.
    public private(set) var status = ""
    @ObservationIgnored private let keys: any SpeechKeys
    @ObservationIgnored private let synthesizer = AVSpeechSynthesizer()
    @ObservationIgnored private var utterance: AVSpeechUtterance?
    @ObservationIgnored private var player: AVAudioPlayer?
    @ObservationIgnored private(set) var work: Task<Void, Never>?
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var finished = ""

    public init(keys: any SpeechKeys = KeychainSpeechKeys()) {
        self.keys = keys
        super.init()
        synthesizer.delegate = self
    }

    // A speaker button: speaks with the saved choice, or stops when it is already reading this item.
    public func toggle(_ text: String, id: String = "heading", using service: any SpeechService) {
        if speaking && speakingID == id { stop("发音已停止"); return }
        play(text, service.speechSelection, using: service, finished: "发音播放结束", id: id)
    }

    // key is one typed in settings but not saved yet; otherwise the Keychain's key is used,
    // and only when the audio still has to be generated.
    public func play(_ text: String, _ selection: SpeechSelection, key: String? = nil, using service: any SpeechService,
                     finished: String, id: String? = nil) {
        stop()
        let request = generation
        speaking = true
        speakingID = id
        self.finished = finished
        work = Task { await run(text, selection, key, service, request) }
    }

    public func stop(_ message: String = "") {
        generation += 1
        work?.cancel()
        utterance = nil
        if synthesizer.isSpeaking { synthesizer.stopSpeaking(at: .immediate) }
        player?.stop(); player = nil
        speaking = false
        speakingID = nil
        status = message
    }

    // A newer query status replaces the last speech message, as in the Windows status bar.
    public func clearStatus() { status = "" }

    private func run(_ text: String, _ selection: SpeechSelection, _ key: String?, _ service: any SpeechService, _ request: Int) async {
        do {
            let cache = try await service.cachedSpeech(text, selection)
            guard request == generation else { return }
            if cache.local { try speak(text, voice: selection.voice); return }
            var path = cache.path
            if path == nil {
                status = "正在生成发音…"
                let secret = try key ?? keys.read(selection.provider) ?? ""
                path = try await service.synthesize(text, selection, key: secret)
                guard request == generation else { return }
            }
            try play(file: path ?? "")
        } catch {
            if request == generation { end(Self.message(error)) }
        }
    }

    private func speak(_ text: String, voice: String) throws {
        guard let chosen = SystemVoices.resolve(voice, for: text) else { throw SpeechFailure.noSystemVoice }
        let next = AVSpeechUtterance(string: text)
        next.voice = chosen
        utterance = next
        status = "正在播放合成发音…"
        synthesizer.speak(next)
    }

    private func play(file path: String) throws {
        guard let audio = try? AVAudioPlayer(contentsOf: URL(fileURLWithPath: path)) else { throw SpeechFailure.playback }
        audio.delegate = self
        player = audio
        status = "正在播放合成发音…"
        guard audio.play() else { player = nil; throw SpeechFailure.playback }
    }

    private func end(_ message: String) {
        utterance = nil; player = nil
        speaking = false
        speakingID = nil
        status = message
    }

    // Callbacks for stopped or replaced audio arrive late; only the current one ends playback.
    fileprivate func ended(_ source: AnyObject, succeeded: Bool) {
        guard source === utterance || source === player else { return }
        end(succeeded ? finished : SpeechFailure.playback.message)
    }

    public static func message(_ error: any Error) -> String {
        (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
    }
}

extension SpeechController: AVSpeechSynthesizerDelegate, AVAudioPlayerDelegate {
    nonisolated public func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didFinish utterance: AVSpeechUtterance) {
        let source = Delivered(object: utterance)
        Task { @MainActor in self.ended(source.object, succeeded: true) }
    }
    nonisolated public func speechSynthesizer(_ synthesizer: AVSpeechSynthesizer, didCancel utterance: AVSpeechUtterance) {
        let source = Delivered(object: utterance)
        Task { @MainActor in self.ended(source.object, succeeded: true) }
    }
    nonisolated public func audioPlayerDidFinishPlaying(_ player: AVAudioPlayer, successfully flag: Bool) {
        let source = Delivered(object: player)
        Task { @MainActor in self.ended(source.object, succeeded: flag) }
    }
    nonisolated public func audioPlayerDecodeErrorDidOccur(_ player: AVAudioPlayer, error: (any Error)?) {
        let source = Delivered(object: player)
        Task { @MainActor in self.ended(source.object, succeeded: false) }
    }
}

// Carries a delegate's object to the main actor only to compare its identity.
private struct Delivered: @unchecked Sendable { let object: AnyObject }

struct SpeechFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
    static let noSystemVoice = SpeechFailure(message: "没有匹配的系统声线，请在设置 → 发音中另选声线或在线服务。")
    static let playback = SpeechFailure(message: "音频无法播放，请检查系统音频输出。")
}

public enum SystemVoices {
    // "auto" first, as on Windows, then the installed voices by language and name.
    public static func choices() -> [SpeechVoice] {
        let voices = AVSpeechSynthesisVoice.speechVoices()
            .sorted { ($0.language, $0.name) < ($1.language, $1.name) }
            .map { SpeechVoice(id: $0.identifier, name: "\($0.name) · \($0.language)") }
        return [SpeechVoice(id: "auto", name: voices.isEmpty ? "本机没有可用声线" : "按文字语言自动选择")] + voices
    }

    // "auto" follows the text's language: a Chinese voice for Chinese text, otherwise English.
    public static func resolve(_ id: String, for text: String) -> AVSpeechSynthesisVoice? {
        guard id == "auto" else { return AVSpeechSynthesisVoice(identifier: id) }
        let chinese = containsChinese(text)
        return AVSpeechSynthesisVoice(language: chinese ? "zh-CN" : "en-US")
            ?? AVSpeechSynthesisVoice.speechVoices().first { $0.language.hasPrefix(chinese ? "zh" : "en") }
    }

    static func containsChinese(_ text: String) -> Bool {
        text.unicodeScalars.contains { (0x3400...0x9FFF).contains($0.value) }
    }
}

public enum SpeechChoices {
    // ElevenLabs: a saved or typed voice stays selectable before the account list is read.
    // loaded is nil until the list has been read; an empty account list stays empty.
    public static func eleven(loaded: [SpeechVoice]?, saved: SpeechVoice?, initial: [SpeechVoice]) -> [SpeechVoice] {
        let voices = loaded ?? initial
        guard let saved, !saved.id.isEmpty, !voices.contains(where: { $0.id == saved.id }) else { return voices }
        return [saved] + voices
    }
}
