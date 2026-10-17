# LeXi maintenance

Rules are grouped by where they apply. Shared behavior belongs to `Lexi.Core` and is identical on every platform; each front end follows its own platform's interface conventions. Parity means the same features and behavior, not the same appearance.

## Scope and workflow

- Keep the product focused on quick dictionary lookup and user-requested text translation. Windows (`Lexi.App`, WPF) and macOS (`apple/`, SwiftUI) are maintained together in this repository; iOS is out of scope.
- Use this repository as the canonical source. Work in a topic branch and record changes in CHANGELOG.md.
- Every PR states its affected platforms. A user-visible feature or behavior change updates [FEATURES.md](FEATURES.md) in the same PR, naming the other platform's equivalent or recording an explicit, temporary gap.
- Run scripts/Test.ps1 and scripts/Check-Privacy.ps1 before committing. On macOS without PowerShell, run the `dotnet` commands from scripts/Test.ps1 directly; the Windows CI job validates WPF. Live provider checks require the user's authorized local subscription and stay out of CI.
- Do not run computer-use or desktop UI automation tests unless the user explicitly requests them. Report build/program validation separately from observed UI behavior.
- Never commit credentials, OAuth codes, private account information, local absolute paths, userdata, query caches, logs, downloaded clients, binaries, or full dictionary data.
- Avoid unrelated refactors and new runtime dependencies. Do not add global shortcut hooks, selection translation, launchers, account systems or subscription services.
- Use LeXi / ξ for the public brand. Do not claim WPF or Windows services run on macOS, or that macOS features exist before they ship.
- Use direct Chinese explanations with the result first. Avoid “不是……而是……”, “本质上”, “换句话说”, “总的来说”, “值得注意的是”, “需要注意的是”, “当然”, “其实”, and repetitive conclusions.

## Shared behavior (Lexi.Core, all platforms)

- `Lexi.Core` is the only implementation of lookup, translation, caching, merging, provenance, progress and status wording. Front ends render `QueryView` and never re-derive these decisions; macOS reaches the same code through `Lexi.Host` rather than a Swift reimplementation. New behavior lands in Core first, with offline checks in `Lexi.Tools`.
- Platform differences in Core are limited to services: data folders (`LexiPaths`), official client discovery (`ToolLocator`), and OS speech and key storage, which stay in each front end. Chinese variant keys use the embedded table on every platform.
- Delegate authentication to the official provider CLI. Do not read or copy its tokens. Do not alter global provider settings. Client discovery cannot depend on the login shell PATH.
- Preserve source citations and uncertainty labels. Partial output must remain visibly provisional and must not enter the cache.
- Keep offline lookup functional without online providers. Preserve user settings during updates.
- Keep dictionary and translation caches separate. Preserve case and paragraph boundaries in translations. Never automatically switch models.
- Dictionary lookup may run two explicitly configured models concurrently, followed by the configured Codex sense judge; translation uses only the primary model. Keep model caches and evidence identities separate. Show the first result before merging, retain every original variant, and never upgrade provenance based on model agreement. Uncertain or contradictory senses must remain separate; judge failure must not discard definitions.
- Compare senses within each model as well as across models. Group facets only within the same lexical usage, prefer the most comprehensive original wording, and keep every original definition accessible. Preserve distinct referents, political versus general insults, literal versus figurative usages and parts of speech. Version comparison caches when judgment semantics change.
- Prefer sourced pronunciation over model IPA, label model-only IPA as unverified, and retain pronunciation attribution.
- Speech is explicitly triggered by the user, defaults to the system's local synthesis, and never automatically changes provider. Definitions, English definitions, examples and their translations can be read aloud the same way on both platforms: a speaker in the card's left margin on hover (a stop button while playing), 朗读 / 朗读所选文字 first in the context menu, and a pale gold tint on the block being read. Each speaker controls only its own item. Cloud speech uses separate user-entered API keys protected by the operating system for the current user; never reuse or inspect subscription OAuth credentials. Keep audio and keys out of Git and release packages.
- Route words, phrases and text automatically, including translation direction. Do not add query-mode or target-language selectors to the main interface.
- Treat examples, detailed sources and alternate wording as expandable secondary information. Omit redundant word-form and bilingual-definition headings; use plain Chinese usage labels and omit neutral boilerplate. Keep model supplementation identified quietly in the source disclosure, with full provenance inside; lack of supporting excerpts must not look like an incorrect-definition verdict. Preserve disclosure state during updates.
- Model completion is not whole-query completion: only finalize after merging returns or an explicit terminal exit. Label live counts as previews, distinguish incomplete outcomes, and reject stale updates without inventing percentages or time estimates.
- Place model-only IPA provenance in a separate label without removing its uncertainty explanation.
- Keep the online mode a two-state choice and regeneration an explicit action. Keep dictionary management in settings rather than displaying a passive footer count.
- Preserve regional terminology as separate labeled source text and index both variants. Terminology pairs do not verify a generated explanatory definition. Add selected dictionaries without replacing unrelated installed sources.
- Retain original dictionary text and attribution. MOE entries must stay in their original form and must not be supplied as AI rewriting evidence.
- Display recorded word forms outside the generated-definition card. Resolve inflected entries to their lemma even when an exact entry exists; merge recorded online headword forms with local data and cache them for offline use. Completed model answers may supplement missing forms only for the parts of speech they actually define. Keep model morphology identified, never infer suffixes in application code, never upgrade its provenance through model agreement, and retain conflicting proposals without displacing recorded spellings. Preserve legacy cache fallback and isolate late updates from subsequent queries; keep noun and verb uses distinct.
- OAuth entry points launch the official client only on an explicit user click. Check status through official commands, display sanitized summaries, never log account identifiers or read credential files. Keep sign-in effects separate from saving application settings.
- Preserve case in dictionary request and cache identities, with case-insensitive keys confined to the local index. Keep abbreviations, their full expansions, and ordinary homographs distinct in morphology and merging.
- Cover known cryptoasset, blockchain, cryptography and information-security meanings without inventing them or treating user interests as query context. Give standalone core definitions first; distinguish industry application examples from separate lexical meanings and use accurate native-field tags. Do not rank senses by favored-domain tags. Reference briefs are original attributed summaries; keep them visible if generated output omits their cited meaning. New terms must continue to use general coverage rather than word-specific application branches.
- Reject malformed IPA escapes; render sourced and model IPA with consistent delimiters. Etymology remains collapsed, with source excerpts separate from model explanation; do not promote mnemonics or model agreement into historical evidence. Etymology belongs in a keyboard-accessible headword popover, not a standalone result row.
- Keep quick-read glosses tied to completed displayed senses, with concise complete-text fallback for legacy/offline data. Never add a separate summary model call or turn truncation into a new definition. Keep preview/partial status explicit. Specialist fallback rows remain until a compatible completed sense cites their evidence; retain the brief in that sense’s reference disclosure.
- Restore completed query views without network or automatic age/settings refresh. Only explicit regeneration replaces saved results. Preserve original model/source provenance across setting changes, separate translation identities, and never replace a good saved view with failed regeneration or streaming previews.
- Use one pale-pink base color (#FFADD2) for all pink UI states, with opacity variants for backgrounds and dark text on solid pink fills.

## Windows interface (Lexi.App, WPF)

- Preserve existing executable, IPC, AppUserModelID and userdata identities for upgrade compatibility. Windows keeps the portable `userdata` and `data` folders beside the program.
- Use vector geometry for UI icons. Keep selected text legible and exclude interactive controls from window dragging; system backdrops must retain an opaque fallback.
- Keep bilingual definitions readable at 20/18 DIP. Use English serif italic parts of speech in the warm accent, upright serif spellings, italic English serif examples and upright Chinese serif example translations. Keep mixed-font word-form labels and spellings on one typographic baseline.
- Keep search-row controls aligned with consistent icon and hit-target sizes. Use a compact indeterminate status ring with hover and keyboard-accessible stage details, a completion check and a distinct incomplete-state symbol.
- Keep IPA legible at 20 DIP on the same text baseline as the headword, with model-only provenance in a smaller adjacent label; allow long headings to wrap.
- Keep query status in the single bottom bar, regeneration visibly styled as a button and online mode as an accessible two-state switch.
- Local speech uses Windows SAPI voices; speech keys use Windows current-user data protection.
- Remember the main window's normal placement independently of query settings. Debounce movement/resizing writes, flush on close/minimize, restore visible normal/maximized state, and let the native placement API account for monitor changes. Keep the startup snapshot immutable and defer writes until layout/chrome restoration completes. Keep corrupt state harmless.

## macOS interface (apple/, SwiftUI)

- SwiftUI with Liquid Glass, minimum macOS 26. Follow the macOS Human Interface Guidelines for menus, shortcuts, settings, windows and accessibility; do not imitate WPF layouts.
- Use Apple's design language, not the Windows look: the same features and information, laid out with macOS 26 conventions. Toolbar glass items for back/forward, history and settings; a floating Liquid Glass query capsule; grouped glass controls at the bottom for regeneration and the online mode; results as content scrolling beneath them on grouped surfaces with continuous corners.
- System colors throughout, adapting to light, dark and the user's accent, on windows that are a single pane of Liquid Glass over the desktop, as in Control Center (`glassWindow()`); the glass takes on the wallpaper's color by design. Settings → 通用 offers system, light or dark appearance for all LeXi windows. Pink appears only as the brand TL;DR mark and card; parts of speech use the Windows pale gold (deeper on light glass) and etymology uses the Windows Rosetta Stone icon; controls, links, sense numbers and state symbols use system styles. Settings follow System Settings: grouped forms, short labels with secondary explanations.
- The app bundles a self-contained `Lexi.Host` and talks to it over JSON lines. Render Host views as received and reject stale revisions. Incompatible protocol changes raise `HostServer.Protocol` and update docs/host-protocol.md and host-check in the same PR.
- User data lives in `~/Library/Application Support/LeXi`. Local speech uses system voices; speech keys use the Keychain.
- Keep window state with the system's restoration. The app is a regular windowed app; closing the window does not quit it.
- Keys belong to the input method while it is composing text. Text that Core marks as accent stays readable: pale pink is a fill or outline in light appearance, never body text.
- Build with apple/scripts/build-app.sh and check with `swift test --package-path apple`. Regenerate the protocol fixture with host-check when QueryView changes.
