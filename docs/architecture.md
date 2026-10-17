# 平台与仓库

LeXi 使用一个仓库维护 Windows 与 macOS 版本，共享词典格式、查询与缓存规则、模型协议和回归用例。两端的功能和行为一致，外观各自遵循平台规范；对照见 [FEATURES.md](../FEATURES.md)。

## 分层

- `Lexi.Core`：唯一的逻辑实现。包括离线索引、来源数据、模型协调、词形解析、翻译、缓存、保存的结果、历史与前进／后退记录，以及查询流程本身。`QuerySession` 执行一次查询的全部步骤，每一步发布完整的 `QueryView`：进度阶段、状态文字、义项行、来源、词形、音标、词源、TL;DR 与翻译。界面只负责渲染这些值。
- `Lexi.App`：Windows 的 WPF 界面。负责布局、焦点、滚动、展开状态、窗口材质、任务栏和托盘、本地发音（SAPI）与密钥保护（当前用户加密）。WPF 仍保留原有的内部入口，检查程序通过它们驱动 Core 的展示模型。
- `Lexi.Tools`：词库导入与非交互检查，包括共享查询流程（`session-check`）、平台服务（`platform-check`）和 Host 协议（`host-check`）。
- `Lexi.Host`：以 JSON lines 暴露 Core 的查询、翻译、建议、历史、设置与在线发音，流式推送 `QueryView`，支持取消。协议见 [host-protocol.md](host-protocol.md)。macOS 应用内置它的自包含单文件发布版本。
- `Lexi.Embed`：同一个 `HostServer` 以 NativeAOT 编译成动态库，供不能启动进程的平台（iOS）在应用内调用，C 接口 `lexi_start` / `lexi_send` / `lexi_close` 逐行收发与 `lexi-host` 相同的消息。Core 与 Host 的 JSON 在编译时生成（`CoreJson`、`HostJson`），不依赖运行时反射，因此能在 NativeAOT 下运行。
- `apple/iOS`：iOS 的 SwiftUI 应用（模拟器预览），共用 `LeXiKit`，内置 `Lexi.Embed`；模型通过用户自己的 OpenAI 或 Anthropic API Key 访问。
- `apple/`：macOS 的 SwiftUI 应用，最低 macOS 26，内置单文件发布的 `Lexi.Host`。`LeXiKit` 启动 Host 并保存最新视图，`LeXiUI` 渲染视图；Liquid Glass 只用于工具栏、控件和弹出层。构建与检查见 [apple/README.md](../apple/README.md)。

[WPF 仅支持 Windows](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)。macOS 与 iOS 不用 Swift 重写逻辑，而是通过 `Lexi.Host`（进程）或 `Lexi.Embed`（应用内）调用同一份 Core。

## Core 中的平台服务

- 数据目录（`LexiPaths`）：Windows 保留程序目录下的 `userdata` 与 `data`；macOS 使用 `~/Library/Application Support/LeXi`，词库位于其中的 `data`；其他系统遵循 XDG。
- 官方客户端发现（`ToolLocator`）：从访达打开的应用拿不到 shell 的 PATH，因此除 PATH 外还检查 `~/.local/bin`、`/opt/homebrew/bin`、`/usr/local/bin`、npm 全局目录及 nvm、fnm、Volta 等目录；都没有时，使用 ChatGPT 桌面应用内置的 Codex 和 Claude 桌面应用下载并校验过的 Claude Code。npm 安装的入口脚本通过同一前缀下的 `node` 启动，并把找到的目录加入子进程 PATH。Windows 仍查找 `codex.exe`、Codex 应用目录、`claude.exe` 与 npm 的 `cli.js`。
- 简繁检索键（`ChineseVariants`）：内置 OpenCC 字表，各平台生成相同的检索键，不再依赖 Windows 的 `LCMapStringEx`。一个简体字对应多个繁体字时，旧词库会依次尝试有限数量的组合。

在线发音的模型、声线、校验、生成、缓存命名与离线规则在 Core（`SpeechCatalog`、`SpeechClient`、`SpeechAudio`），两端共用。本机声线、播放与密钥存储属于各自界面：Windows 使用 SAPI 与当前用户加密，macOS 使用系统声线与钥匙串，密钥只随单次请求传给 Host。

## 升级兼容

公开品牌为 **LeXi / ξ**。既有 `Lexi.exe` 文件名、项目/程序集标识、IPC 和 AppUserModelID 暂时保留，保护现有任务栏入口与运行数据。品牌调整不迁移或清空词库、设置、历史缓存及官方客户端登录。

升级后可运行 `scripts/Refresh-TaskbarIcon.ps1 -Executable <安装目录>/Lexi.exe` 刷新已固定的 LeXi 图标。脚本只修改目标程序路径匹配的任务栏快捷方式，将图标指向按内容命名的独立资源，保留启动参数与工作目录。可用 `-RecoveryDirectory` 保留旧快捷方式；不会重启 Explorer 或清空系统图标缓存。
