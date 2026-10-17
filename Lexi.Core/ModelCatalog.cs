namespace Lexi.Core;

public sealed record ModelChoice(string Label, string Id, string Provider = "codex")
{ public override string ToString() => Label; }

// Model options and settings rules shared by every settings screen.
public static class ModelCatalog
{
    // Where the official subscription clients cannot run (iOS), models are reached with the user's
    // own API keys (OpenAI, Anthropic) instead of Codex and Claude Code.
    public static bool ApiOnly { get; } = OperatingSystem.IsIOS();
    public static bool IsApi(string provider) => ApiKeys.Supports(provider);

    public static ModelChoice[] Choices(AppSettings settings) => Choices(settings, ApiOnly);
    public static ModelChoice[] Choices(AppSettings settings, bool api)
    {
        var choices = api
            ? new List<ModelChoice> { new("GPT-6.1 Sol · OpenAI API", "gpt-6.1-sol", "openai"), new("GPT-6 Luna · OpenAI API", "gpt-6-luna", "openai"),
                new("Claude Haiku 5.5 · Anthropic API", "claude-haiku-5-5", "anthropic"), new("Claude Sonnet 5.5 · Anthropic API", "claude-sonnet-5-5", "anthropic") }
            : new List<ModelChoice> { new("Claude Haiku · 流式显示", "haiku", "claude"), new("GPT-6 Luna · Fast", "gpt-6-luna"),
                new("GPT-6.1 Sol · Fast", "gpt-6.1-sol"), new("Codex 默认 · Fast", "") };
        // Keep a previously saved model selectable even when it is no longer offered by default.
        foreach (var selected in new[] { LookupModel.Primary(settings), LookupModel.Secondary(settings), Judge(settings, api) })
            if (IsApi(selected.Provider) == api && !choices.Any(x => x.Id == selected.Model && x.Provider == selected.Provider))
                choices.Add(new(selected.Name, selected.Model, selected.Provider));
        return choices.ToArray();
    }
    // The sense judge runs through the Codex subscription, or through either API service.
    public static ModelChoice[] JudgeChoices(ModelChoice[] choices) => choices.Where(c => c.Provider != "claude").ToArray();
    public static LookupModel Judge(AppSettings settings) => Judge(settings, ApiOnly);
    public static LookupModel Judge(AppSettings settings, bool api) =>
        !api ? new("codex", settings.JudgeModel) : new(settings.JudgeModel.StartsWith("claude", StringComparison.Ordinal) ? "anthropic" : "openai", settings.JudgeModel);

    public static string? Validate(AppSettings settings) => Validate(settings, ApiOnly);
    public static string? Validate(AppSettings settings, bool api)
    {
        string[] providers = api ? ["openai", "anthropic"] : ["codex", "claude"];
        if (!providers.Contains(settings.Provider) || settings.SecondaryProvider.Length > 0 && !providers.Contains(settings.SecondaryProvider))
            return "请选择受支持的模型服务。";
        if (api && new[] { settings.Model, settings.SecondaryModel, settings.JudgeModel }.Any(m => m.Length > 0 && !ValidModelId(m)) ||
            api && (settings.Model.Length == 0 || settings.JudgeModel.Length == 0))
            return "请填写有效的模型 ID。";
        if (settings.ParallelLookup && LookupModel.Primary(settings) == LookupModel.Secondary(settings)) return "请选择两个不同的查询模型。";
        return null;
    }
    public static bool ValidModelId(string id) => id.Length is > 0 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');

    // API platforms start from an OpenAI model with an Anthropic second opinion; desktop settings
    // copied over keep nothing that needs a subscription client.
    public static void UseApiDefaults(AppSettings settings)
    {
        if (!IsApi(settings.Provider) || !ValidModelId(settings.Model)) { settings.Provider = "openai"; settings.Model = "gpt-6-luna"; }
        if (settings.SecondaryProvider.Length > 0 && (!IsApi(settings.SecondaryProvider) || !ValidModelId(settings.SecondaryModel)))
        { settings.SecondaryProvider = ""; settings.SecondaryModel = ""; }
        if (!ValidModelId(settings.JudgeModel)) settings.JudgeModel = "gpt-6-luna";
    }

    // Codex always runs with low reasoning on the fast tier; Claude disables extra thinking itself.
    public static void ApplyFixedOptions(AppSettings settings) { settings.ReasoningEffort = "low"; settings.ServiceTier = "fast"; }
}
