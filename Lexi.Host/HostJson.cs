using System.Text.Json;
using System.Text.Json.Serialization;
using Lexi.Core;

namespace Lexi.Host;

// Protocol messages as named types, so their JSON is generated at build time like Core's and the
// host also runs embedded under NativeAOT (iOS). Names and order match docs/host-protocol.md.
internal sealed record ViewEvent(string Event, QueryView Data);
internal sealed record HostReply(JsonElement? Id, object? Result);
internal sealed record HostFailure(JsonElement? Id, HostError Error);
internal sealed record HostError(string Code, string Message);
internal sealed record EmptyResult;
internal sealed record HostPaths(string Data, string User, string Assets);
internal sealed record InitializeResult(int Protocol, string Version, string Platform, HostPaths Paths, QueryView View, SettingsResult Settings);
internal sealed record ViewPosition(long Request, long Revision);
internal sealed record NavigateResult(QueryPage? Page, ViewPosition View);
internal sealed record SuggestResult(string[] Suggestions);
internal sealed record HistoryItem(string Query, bool Translation, DateTimeOffset QueriedAt, string Preview, string Kind);
internal sealed record HistoryResult(HistoryItem[] Entries, string Error);
internal sealed record RemoveHistoryResult(bool Removed, HistoryResult History);
internal sealed record ControlTexts(string OnlineLabel, string OnlineTooltip, string RegenerateTooltip);
internal sealed record SettingsResult(AppSettings Settings, ModelChoice[] Models, ModelChoice[] JudgeModels, ControlTexts Controls);
internal sealed record DictionariesResult(InstalledDictionary[] Dictionaries, string Folder);
internal sealed record LoginCommand(string FileName, string[] Arguments, string Path, string[] ClearEnvironment);
internal sealed record SpeechModelItem(string Provider, string Id, string Name, bool Local);
internal sealed record SpeechModelsResult(SpeechModelItem[] Models);
internal sealed record VoicesResult(SpeechVoice[] Voices);
internal sealed record CachedSpeechResult(string? Path, bool Local);
internal sealed record SpeechFileResult(string Path);
internal sealed record CancelResult(bool Cancelled);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ViewEvent))]
[JsonSerializable(typeof(HostReply))]
[JsonSerializable(typeof(HostFailure))]
[JsonSerializable(typeof(EmptyResult))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ViewPosition))]
[JsonSerializable(typeof(NavigateResult))]
[JsonSerializable(typeof(SuggestResult))]
[JsonSerializable(typeof(HistoryResult))]
[JsonSerializable(typeof(RemoveHistoryResult))]
[JsonSerializable(typeof(SettingsResult))]
[JsonSerializable(typeof(DictionariesResult))]
[JsonSerializable(typeof(LoginStatus))]
[JsonSerializable(typeof(LoginCommand))]
[JsonSerializable(typeof(SpeechModelsResult))]
[JsonSerializable(typeof(VoicesResult))]
[JsonSerializable(typeof(CachedSpeechResult))]
[JsonSerializable(typeof(SpeechFileResult))]
[JsonSerializable(typeof(CancelResult))]
[JsonSerializable(typeof(QueryView))]
[JsonSerializable(typeof(AppSettings))]
internal partial class HostJson : JsonSerializerContext;
