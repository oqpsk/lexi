# Lexi.Host 协议

`lexi-host` 把 `Lexi.Core` 的查询、翻译、建议、历史、设置与在线发音提供给原生前端：macOS 应用启动它作为子进程；iOS 不能启动进程，同一个 `HostServer` 以 NativeAOT 编译进应用内的库（`Lexi.Embed`），按同样的消息逐行收发。前端不重新实现查询逻辑，只渲染 Host 发来的视图。

## 启动

```
lexi-host [--data <词库目录>] [--user <用户数据目录>] [--assets <模型说明与结构目录>]
```

未指定时使用 `LexiPaths`：macOS 为 `~/Library/Application Support/LeXi`（词库在其中的 `data`），资源目录为程序旁的 `assets`。`--version` 输出版本号。参数错误时退出码为 2，用户目录无法创建时为 3。

标准输入和标准输出各传输 UTF-8 JSON，每行一条消息。标准输出只包含协议消息；诊断信息写入标准错误，不包含查询文本、账号或密钥。标准输入关闭或收到 `shutdown` 后，Host 结束正在进行的请求并退出。

## 消息

请求必须带 `id`（数字或字符串）和 `method`，`params` 可省略：

```json
{"id": 7, "method": "query.submit", "params": {"text": "serendipity"}}
```

每个请求恰好得到一条响应，`result` 与 `error` 二选一。多个请求可以并行，响应顺序不保证与请求一致：

```json
{"id": 7, "result": {"request": 3, "revision": 41}}
{"id": 8, "error": {"code": "invalid_settings", "message": "请选择两个不同的查询模型。"}}
```

错误代码：`parse_error`、`invalid_request`、`method_not_found`、`invalid_params`、`invalid_settings` 与 `speech_failed`（`message` 是 Core 的中文提示，可直接显示）、`cancelled`、`internal_error`（只包含错误类别）。无法确定 `id` 的错误使用 `"id": null`。

查询状态通过事件推送，每次 Core 发布新视图时发送一条：

```json
{"event": "view", "data": { "request": 3, "revision": 41, "kind": "dictionary", "...": "..." }}
```

`data` 是完整的 `QueryView`（字段见 `Lexi.Core/QueryViews.cs`），字段名为 camelCase，枚举为 camelCase 字符串。`revision` 在整个会话中递增；前端丢弃不大于已显示 `revision` 的视图。`request` 变化表示新的查询页面。`progress` 为 `null` 表示没有进行中的查询；`progressGeneration` 变化表示新的进度周期。视图中的文字、来源说明、阶段和状态都已由 Core 决定，前端不再推断。

## 方法

| 方法 | 参数 | 结果 |
|---|---|---|
| `initialize` | — | `protocol`、`version`、`platform`、`paths`、当前 `view`、`settings`（同 `settings.get`）。等待离线词库加载 |
| `query.submit` | `text`，可选 `force`、`scroll` | 查询结束后返回 `{request, revision}`；过程通过 `view` 事件推送 |
| `query.regenerate` | — | 跳过已保存结果重新查询当前页面 |
| `query.navigate` | `direction`（-1 或 1），可选 `scroll` | `{page, view}`；`page` 为 `null` 表示已到边界 |
| `query.home` | 可选 `scroll` | 返回查询历史页面 |
| `query.cancel` | — | 停止当前查询，保留已显示内容 |
| `query.view` | — | 当前 `QueryView` |
| `suggest` | `text` | `{suggestions}` |
| `history.list` | — | `{entries: [{query, translation, queriedAt, preview, kind}], error}` |
| `history.remove` | `query`、`translation` | `{removed, history}` |
| `settings.get` | — | `{settings, models, judgeModels, controls}`；`controls` 为在线开关与重新生成的共享文字 |
| `settings.update` | `settings`（只含要修改的字段） | 与 Windows 设置相同的校验（修改 `speech*` 字段时也校验发声模型与声线），保存后返回 `settings.get` 的内容 |
| `settings.setOnline` | `online` | 切换在线补充并返回设置 |
| `dictionaries.list` | — | `{dictionaries, folder}` |
| `connection.status` | `provider`（`codex` 或 `claude`），可选 `codexPath` | `{installed, signedIn, message}`，只含经过清理的摘要 |
| `connection.loginCommand` | `provider`，可选 `codexPath` | `{fileName, arguments, path, clearEnvironment}` 或 `null`。前端仅在用户点击后在终端中运行，并清除 `clearEnvironment` 中的变量 |
| `speech.models` | — | `{models: [{provider, id, name, local}]}`；第一项是本机系统声线（`local` 为 `true`），其余为在线模型 |
| `speech.voices` | `provider`、`model` | `{voices: [{id, name}]}`：该模型的在线声线；本机声线由前端从系统读取，这里为空 |
| `speech.elevenVoices` | 可选 `key` | `{voices}`：ElevenLabs 账号可用声线。离线模式下不发请求 |
| `speech.cached` | `text`、`provider`、`model`、`voice` | 按 Windows 规则校验文字与声线，返回 `{path, local}`：已缓存的音频文件路径或 `null`。`local` 为 `true` 时由前端用系统声线朗读。不生成音频 |
| `speech.synthesize` | `text`、`provider`、`model`、`voice`，可选 `key` | `{path}`：缓存中的 MP3 路径，缺失时用在线服务生成。已缓存时不需要 `key`；离线模式下缺失则返回“离线模式下没有此声线的缓存发音。” |
| `keys.set` | `provider`（`openai` 或 `anthropic`），`key`（空字符串表示清除） | `{}`。只在没有官方客户端的平台（iOS）使用：查词、翻译和义项判断经由该服务的 API |
| `cancel` | `id` | 取消仍在运行的请求，`{cancelled}` |
| `shutdown` | — | 等待其余请求结束后最后响应，然后退出 |

## 发音

发音只在用户点击后请求。音频保存在用户数据目录的 `speech-cache`，每段文字、服务、模型和声线各有一个文件（`SpeechOptions.CacheKey`），写完整后才出现，与 Windows 共用同一份 `SpeechAudio`。前端播放返回的文件；本机系统声线由前端直接朗读，不经过缓存。

在线服务的 API Key 由前端从系统的安全存储读取，只随需要它的那一次请求传入。Host 不保存、不记录、不在响应或事件中返回密钥，`speech_failed` 的提示也不包含密钥或服务返回的原文。

## 模型 API

iOS 没有 Codex 与 Claude Code 客户端，查询模型改用用户自己的 OpenAI 或 Anthropic API Key（`ApiDictionary`）：同一份系统说明、JSON 结构与校验，OpenAI 使用 Responses API 的严格结构化输出，Anthropic 使用强制调用的工具。`initialize` 返回的 `platform` 为 `ios` 时，`settings` 中的模型选项只有这两种服务。前端在启动和修改密钥后用 `keys.set` 传入密钥；Host 只在本次运行的内存中保留，不写入设置或缓存，也不在任何响应中返回。错误提示只包含应用自己的说明，不含服务返回的原文。

## 兼容

不兼容的变化提高 `HostServer.Protocol`。前端在 `initialize` 后检查 `protocol`，不一致时提示重新安装。新增字段和方法不改变版本号；前端忽略未知字段。

`host-check` 在进程内用合成模型检查流式视图、取消、错误和设置，并分别以 `dotnet` 和单文件发布形式启动 Host 检查标准输出与退出行为。
