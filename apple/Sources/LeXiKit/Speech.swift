import Foundation
import Security

// Speech choices and audio files come from Lexi.Core through the host (docs/host-protocol.md).
// API keys stay in the Keychain and travel to the host only with the request that needs them.
public struct SpeechModel: Decodable, Equatable, Sendable, Hashable {
    public let provider: String
    public let id: String
    public let name: String
    public let local: Bool
    public var key: String { provider + ":" + id }
    public init(provider: String, id: String, name: String, local: Bool) { self.provider = provider; self.id = id; self.name = name; self.local = local }
}
public struct SpeechVoice: Decodable, Equatable, Sendable, Hashable, Identifiable {
    public let id: String
    public let name: String
    public init(id: String, name: String) { self.id = id; self.name = name }
}
public struct SpeechSelection: Equatable, Sendable {
    public let provider: String
    public let model: String
    public let voice: String
    public init(provider: String, model: String, voice: String) { self.provider = provider; self.model = model; self.voice = voice }
}
// The cached audio for a text and voice, if any. Local voices have no file: the app speaks them itself.
public struct SpeechCache: Decodable, Equatable, Sendable {
    public let path: String?
    public let local: Bool
    public init(path: String?, local: Bool) { self.path = path; self.local = local }
}

@MainActor public protocol SpeechService: AnyObject, Sendable {
    var speechSelection: SpeechSelection { get }
    // Checks the text and voice with Core's rules and returns any cached audio; never generates.
    func cachedSpeech(_ text: String, _ selection: SpeechSelection) async throws -> SpeechCache
    func synthesize(_ text: String, _ selection: SpeechSelection, key: String) async throws -> String
}

public protocol SpeechKeys: Sendable {
    func read(_ provider: String) throws -> String?
    func save(_ provider: String, _ value: String) throws
    func remove(_ provider: String) throws
    func hasSaved(_ provider: String) -> Bool
}

public struct SpeechKeyError: Error, LocalizedError {
    // The keychain refused this copy of LeXi. An item trusts only the build that saved it, and a
    // build signed ad hoc (apple/scripts/build-app.sh) is a new app to the keychain after every rebuild.
    public var refused = false
    public init(refused: Bool = false) { self.refused = refused }
    public var errorDescription: String? {
        refused ? "钥匙串不允许这一版 LeXi 读取之前保存的密钥。请在设置 → 发音中重新粘贴 API Key 并保存。"
            : "语音密钥无法从钥匙串读取或保存，请在设置中重新填写。"
    }
}

// Speech keys the user entered for LeXi, stored as generic passwords in the current user's login
// keychain. A new item's access list trusts only the app that created it, and items do not sync.
// Subscription credentials of the official clients are never read.
public struct KeychainSpeechKeys: SpeechKeys {
    // Speech services, plus Anthropic for model access on iOS (the OpenAI key serves both there).
    public static let providers = ["openai", "xai", "elevenlabs", "anthropic"]
    private let service: String
    public init(service: String = "io.github.oqpsk.lexi.speech") { self.service = service }

    public func read(_ provider: String) throws -> String? {
        var query = try item(provider)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw Self.failure(status) }
        guard let data = result as? Data, let value = String(data: data, encoding: .utf8) else { throw SpeechKeyError() }
        return value
    }

    public func save(_ provider: String, _ value: String) throws {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { try remove(provider); return }
        // Replaces the item instead of updating it: an item saved by an earlier build may not trust
        // this one, and the new item trusts the build that saved it.
        try remove(provider)
        var add = try item(provider)
        add[kSecValueData as String] = Data(trimmed.utf8)
        add[kSecAttrLabel as String] = "LeXi 发音 · " + Self.title(provider)
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw Self.failure(status) }
    }

    public func remove(_ provider: String) throws {
        let status = SecItemDelete(try item(provider) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw Self.failure(status) }
    }

    // Asks only whether the item exists, so it never requests access to the secret.
    public func hasSaved(_ provider: String) -> Bool {
        guard let query = try? item(provider) else { return false }
        return SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess
    }

    public static func title(_ provider: String) -> String {
        switch provider {
        case "openai": "OpenAI API Key"
        case "xai": "Grok / xAI API Key"
        case "anthropic": "Anthropic API Key"
        default: "ElevenLabs API Key"
        }
    }

    private static func failure(_ status: OSStatus) -> SpeechKeyError {
        SpeechKeyError(refused: [errSecAuthFailed, errSecUserCanceled, errSecNoAccessForItem, errSecInvalidOwnerEdit].contains(status))
    }

    private func item(_ provider: String) throws -> [String: Any] {
        guard Self.providers.contains(provider) else { throw SpeechKeyError() }
        return [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: provider]
    }
}
