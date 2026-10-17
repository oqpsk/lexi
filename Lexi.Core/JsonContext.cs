using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lexi.Core;

// Every type Core reads or writes as JSON, with its serializer generated at build time. Core then
// behaves the same where code cannot be generated at run time (iOS, through NativeAOT), and a type
// missing here fails in the shared checks on every platform instead of only on a device.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(AiEntry))]
[JsonSerializable(typeof(AiEnvelope))]
[JsonSerializable(typeof(LocalEntry))]
[JsonSerializable(typeof(OnlineEntry))]
[JsonSerializable(typeof(HistoryEntry[]))]
[JsonSerializable(typeof(SpecialistReference[]))]
[JsonSerializable(typeof(SavedDictionary))]
[JsonSerializable(typeof(SavedTranslation))]
[JsonSerializable(typeof(TranslationEntry))]
[JsonSerializable(typeof(TranslationEnvelope))]
[JsonSerializable(typeof(PronunciationEntry))]
[JsonSerializable(typeof(DictionaryMetadata))]
[JsonSerializable(typeof(SenseMergePlan))]
[JsonSerializable(typeof(QueryView))]
[JsonSerializable(typeof(ModelCacheKey))]
[JsonSerializable(typeof(DefinePrompt))]
[JsonSerializable(typeof(TranslationKey))]
[JsonSerializable(typeof(TranslationPrompt))]
[JsonSerializable(typeof(TranslationIdentity))]
[JsonSerializable(typeof(EtymologyIdentity))]
[JsonSerializable(typeof(SenseIdentity))]
[JsonSerializable(typeof(MergeIdentity))]
[JsonSerializable(typeof(MergePrompt))]
[JsonSerializable(typeof(SpeechCacheKey))]
[JsonSerializable(typeof(OpenAiSpeechBody))]
[JsonSerializable(typeof(XaiSpeechBody))]
[JsonSerializable(typeof(ElevenSpeechBody))]
internal partial class CoreJson : JsonSerializerContext;
