# 功能对照

LeXi 的 Windows 版（`Lexi.App`，WPF）与 macOS 版（`apple/`，SwiftUI）共用 `Lexi.Core` 的查询、缓存、合并、来源、进度与状态文字。Windows 直接引用 Core；macOS 通过内置的 `Lexi.Host`（[协议](docs/host-protocol.md)）调用同一份 Core。两端功能和行为一致，外观各自遵循平台规范。

改变用户可见的功能或行为时，在同一个 PR 中更新本表：写明共享实现、两端入口，或另一平台的临时缺口。平台专属功能写出对应实现；没有对应时写“不适用”并说明原因。

状态：**已实现**、**计划**（尚未发布，不对外宣称）、**不适用**。

## 查询与结果

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 自动判断查词、翻译与中英方向 | `Queries.IsText`、`Translation.Target`、`QuerySession.SubmitAsync` | 已实现：单一多行输入框，Enter 查询，Shift+Enter 换行 | 已实现：多行输入框，Return 查询，Shift-Return 或 Option-Return 换行 |
| 窗口来到前台即可输入 | 不适用：窗口行为 | 已实现：窗口激活时聚焦输入框并全选，Ctrl+L 同 | 已实现：窗口来到前台时聚焦输入框并全选上次的查询，⌘L 同 |
| 离线词典联合查询、简繁检索、词形匹配 | `OfflineLibrary`、`ChineseVariants`（内置 OpenCC 字表，各平台相同） | 已实现 | 已实现：词库位于 `~/Library/Application Support/LeXi/data` |
| 输入建议 | `QuerySession.Suggest` | 已实现：↑↓ 预览，Tab 或点击接受，Esc 还原 | 已实现：列表悬于输入框下方；↑↓ 预览，Tab 或点击接受，Esc 还原；输入法组字时按键留给输入法 |
| Wiktionary 在线来源与原文 | `OnlineDictionary`、`QueryView.Online` | 已实现：可展开原文与来源链接 | 已实现：可展开原文与来源链接 |
| 双模型查词、流式预览、失败隔离 | `DictionaryCoordinator`、`QuerySession` | 已实现 | 已实现 |
| 义项合并与其他表述 | `SenseMerging`、`AnswersView` | 已实现：折叠的“其他表述 / 用法细分” | 已实现：可展开的“其他表述 / 用法细分” |
| TL;DR 概览 | `QueryPresentation`（`QuickReadView`） | 已实现：粉色 TL;DR 徽标与状态 | 已实现：粉底深色 TL;DR 标记与状态 |
| 音标及来源、AI 音标标记 | `QueryPresentation`（`SavedPronunciation`） | 已实现 | 已实现 |
| 词形变化 | `QueryPresentation`（`WordFormRow`） | 已实现：右键复制词形 | 已实现：右键菜单复制词形 |
| 词源 | `EtymologyView` | 已实现：词条旁罗塞塔石碑按钮弹出 | 已实现：词条旁罗塞塔石碑按钮弹出（与 Windows 同一图形） |
| 专业参考摘要 | `SpecialistReferences`、`ReferenceView` | 已实现 | 已实现 |
| 查询阶段、结束状态与状态文字 | `QueryProgressState`、`QueryProgressTracker`、`QueryView.Status` | 已实现：输入框旁状态环，悬浮查看阶段；单行底栏 | 已实现：输入框旁状态指示，点击查看阶段；底栏状态 |
| 保存的结果与重新生成 | `SavedQueries`、`QuerySession.RegenerateAsync` | 已实现：底栏“重新生成” | 已实现：底栏“重新生成”，⌘R |
| 在线补充开关 | `QuerySession.SetOnline`、`ControlText` | 已实现：底栏两态开关 | 已实现：底部玻璃按钮，以图标和文字区分在线与离线 |
| 文本翻译与复制译文 | `Translation`、`TranslationView` | 已实现 | 已实现 |
| 查询历史 | `QueryHistory` | 已实现：主页列表，可单条删除 | 已实现：主页列表，可单条删除；⌘Y |
| 后退 / 前进 | `QueryNavigation`、`NavigationView` | 已实现：Alt+← / Alt+→ | 已实现：工具栏按钮，⌘[ / ⌘] |

## 设置与连接

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 查询与模型设置 | `AppSettings`、`ModelCatalog`（模型选项与校验）、`QuerySession.SettingsSaved` | 已实现 | 已实现：系统设置风格的分组表单，修改即生效，校验与提示同 Windows |
| 离线词典启停与信息 | `DictionaryCatalog` | 已实现：设置 → 离线词典 | 已实现：设置 → 离线词典；安装词库需 PowerShell 7 运行同一安装脚本 |
| 官方客户端登录与状态 | `ProviderConnection`、`ToolLocator`（不依赖 shell PATH） | 已实现：设置 → 账号连接 | 已实现：在“终端”中打开官方登录，完成后点击“检查状态” |
| 本地发音 | `SpeechCatalog.Local`（本机声线模型与校验） | 已实现：Windows SAPI 声线，可选“按文字语言自动选择”；生成 WAV 后按同一规则缓存 | 已实现：系统声线（`AVSpeechSynthesizer`），可选“按文字语言自动选择”；直接朗读，不生成缓存文件 |
| 在线发音：OpenAI、Grok、ElevenLabs | `SpeechCatalog`（模型、声线、校验与提示）、`SpeechClient`、`ElevenLabsSpeech` | 已实现：设置 → 发音选择模型与声线，可试听 | 已实现：设置 → 发音选择模型与声线，可试听；通过 Host 的 `speech.*` 方法调用同一份 Core |
| ElevenLabs 账号声线与 Voice ID | `SpeechClient.ElevenVoicesAsync`、`SpeechAudio.ElevenVoicesAsync`（离线时不读取） | 已实现：首次展开声线列表时读取，可刷新；“指定其他声线”填写 Voice ID | 已实现：点击“读取声线列表”读取，可刷新（SwiftUI 选择器没有展开时机）；“指定其他声线”填写 Voice ID |
| 朗读释义与例句 | `SpeechAudio` 与 `SpeechCatalog.Validate`（同一份缓存与长度规则，每次最多 4096/5000 字） | 已实现：鼠标移到释义、英文释义、例句或其译文上，左侧边距浮现扬声器，播放中变为停止按钮；右键菜单首项“朗读”，有选中文字时为“朗读所选文字”；播放中的段落以淡金色底标出；英文例句斜体，中文译文正体；扬声器与正文首行的字形中心对齐，正文到按钮的悬停区域连续覆盖折叠内容缩进 | 已实现：悬停扬声器、逐段朗读与停止、朗读所选文字；右键菜单在系统的查询、翻译与拷贝之上加入“朗读”；VoiceOver 提供“朗读”操作 |
| 发音缓存与离线规则 | `SpeechAudio`：按文字、服务、模型、声线分别保存，写完整后才保留；离线时只播放已缓存的在线发音 | 已实现：`MediaPlayer` 播放 | 已实现：`AVAudioPlayer` 播放 Host 返回的 MP3 |
| 语音密钥 | `SpeechClient`（格式校验与错误提示）；Core 不保存密钥 | 已实现：Windows 当前用户加密；未保存时可用 `OPENAI_API_KEY` 等环境变量 | 已实现：存入当前用户的钥匙串，只供 LeXi 使用，每次请求随调用传给 Host，Host 不保存。临时签名的应用重新构建后需重新保存一次，或用固定签名身份构建（apple/README.md）。不读取环境变量：从访达打开的应用拿不到 shell 环境 |

## 平台专属

| 功能 | Windows | macOS |
|---|---|---|
| 用户数据位置（`LexiPaths`） | 程序目录 `userdata/`、`data/`，便于升级保留 | `~/Library/Application Support/LeXi`（含 `data/`） |
| 窗口位置记忆 | `WindowPlacement` 保存正常位置与最大化状态 | 系统窗口恢复 |
| 外观（浅色 / 深色） | 固定深色 | 设置 → 通用 → 外观：跟随系统、浅色或深色 |
| 查询历史入口 | 底栏 ξ 标志 | 工具栏“查询历史”按钮、⌘Y |
| 关闭与后台 | 托盘图标；关闭按钮可在“设置 → 窗口”中设为最小化或退出 | 关闭窗口不退出，点击 Dock 图标重新打开；不提供关闭行为设置 |
| 单实例 | 互斥量与命名管道激活已有窗口 | macOS 应用默认单实例，单一查询窗口 |
| 设计语言 | Windows 11 Acrylic 深色浮窗，粉色点缀 | Apple 设计语言：Liquid Glass 查询胶囊、工具栏与底部控件，结果在其下滚动；窗口本身是一整块 Liquid Glass（如控制中心），透出桌面并随深浅色变为浅色或深色玻璃，开启“减少透明度”时更不透明；系统颜色与强调色，跟随深浅色；粉色只用于 TL;DR，词性用与 Windows 相同的淡金色（浅色下加深） |
| 快捷键 | Ctrl+L、Ctrl+,、Ctrl+Q；Esc 收起建议或最小化 | ⌘L、⌘R、⌘[、⌘]、⌘Y、⌘,、⌘Q；Esc 收起建议 |
| 查询逻辑接入 | 直接引用 `Lexi.Core` | 内置 `Lexi.Host`（JSON lines） |
| 构建与分发 | `scripts/Build.ps1`，可生成自包含版本 | `apple/scripts/build-app.sh`，按构建机器架构生成本机签名的应用；Developer ID 签名与公证计划中 |
| 发音状态 | 底栏显示生成、播放与错误状态 | 底栏显示生成、播放与错误状态；扬声器按钮显示播放或停止 |
| 任务栏图标刷新 | `Refresh-TaskbarIcon.ps1` | 不适用：Dock 图标随应用包更新 |

## iOS（模拟器预览）

iOS 应用（`apple/iOS`）目前只在模拟器中运行：安装到设备或上架需要 Apple 开发者账号。iOS 不能启动进程，`Lexi.Core` 以 NativeAOT 库（`Lexi.Embed`）运行在应用内，收发与 macOS 相同的 Host 协议。界面遵循 iOS 的设计规范，功能与两个桌面版对齐；下表只列出 iOS 的状态与差别。

| 功能 | iOS | 说明 |
|---|---|---|
| 查词、翻译、输入建议、历史、后退 / 前进、重新生成、在线补充 | 已实现 | 共用 Core；搜索框在屏幕底部，Return 查询，粘贴的换行保留给翻译；`lexi://lookup?q=…` 直接查词 |
| 模型访问 | 已实现：OpenAI 与 Anthropic API Key | 没有官方客户端，不能使用 ChatGPT / Claude 订阅；密钥存入钥匙串，只在内存中交给 Core（`keys.set`），按服务商的 API 计费 |
| 双模型、义项合并、TL;DR、来源与原文、词形、音标、词源 | 已实现 | 同一份 Core 规则；义项判断可用任一 API 服务 |
| 模型流式预览 | 计划 | API 路径一次返回完整结果 |
| 朗读释义与例句 | 已实现：长按选择“朗读”，播放中的段落以淡金色底标出 | 触屏没有悬停，因此不显示边距扬声器 |
| 发音 | 已实现：本机声线、OpenAI 语音 | Grok、ElevenLabs 计划 |
| 离线词库 | 已实现：读取数据目录中已安装的词库 | 应用内下载与安装计划 |
| 外观 | 已实现：跟随系统、浅色、深色 | |
| 设备安装与分发 | 计划 | 需要 Apple 开发者账号 |
