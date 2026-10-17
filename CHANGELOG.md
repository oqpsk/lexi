# Changelog

## Unreleased

- Rewrite README.md as a product overview covering both platforms: lookup and translation, offline dictionaries, AI definitions and merging, subscriptions, speech, saved results, navigation and privacy.

- Add LeXi for macOS (`apple/`): a SwiftUI app for macOS 26+ that bundles the self-contained `Lexi.Host` and renders its views. It supports lookup, translation, suggestions, history, back/forward navigation, regeneration, the online switch, settings with the shared validation, dictionary management, official-client status and sign-in in Terminal, and system-voice speech. Liquid Glass is limited to the toolbar, search bar, footer controls and popovers; results use the normal background with pink as an accent. Keys are left to the input method while it composes text.
- Give the macOS app Apple's design language: a floating Liquid Glass query capsule, toolbar glass for navigation, history and settings, and grouped glass controls for regeneration and the online mode, with results scrolling beneath on grouped surfaces. Colors follow the system and the user's accent; pink appears only as the TL;DR mark. Settings use System Settings-style grouped forms with account status indicators; a General tab chooses system, light or dark appearance and lists the shortcuts. Each window is a single pane of Liquid Glass over the desktop, as in Control Center: light or dark glass that follows the appearance setting, takes on the wallpaper's color and becomes more opaque with Reduce Transparency. Settings controls sit centered beside their two-line labels. Parts of speech use the Windows pale gold (a deeper gold on light glass), and the etymology button shows the same Rosetta Stone as on Windows.
- Make the macOS query field ready the moment the window comes forward: it takes focus with the last query selected, and ⌘L does the same, as on Windows. Suggestions now hang below the query bar over the results instead of covering it.
- Add `apple/scripts/build-app.sh` to assemble `LeXi.app` with the host, model schemas, an icon generated from the brand mark and the license notices, and Swift tests for protocol decoding, view state, the real host and result layout. The macOS CI job runs them and builds the app.
- Add speech parity to LeXi for macOS: the 发音 settings tab offers system voices with an automatic choice, OpenAI, Grok and ElevenLabs models and voices, ElevenLabs account voice loading and a manual Voice ID, and a preview. Keys are saved in the current user's Keychain for LeXi only and passed to the host with each request that needs them. Saving replaces the Keychain item, so a rebuilt app that the old item no longer trusts can save the key again, and a refused read says so in the status bar. `LEXI_SIGN_IDENTITY` signs `build-app.sh` builds with a fixed identity so saved keys keep working across rebuilds. Online audio plays from the shared cache with `AVAudioPlayer`; generation, playback and errors appear in the status bar as on Windows.
- Move online speech generation and its audio cache into `Lexi.Core` (`SpeechAudio`): per text, provider, model and voice files written to a temporary file first, the offline rule and lazy key reading. The WPF window uses it and keeps SAPI generation and current-user key encryption. Unreachable speech services now report “无法连接语音服务，请检查网络后重试。” (ElevenLabs keeps its own wording) on both platforms instead of the system's network message.
- Add `speech.models`, `speech.voices`, `speech.elevenVoices`, `speech.cached` and `speech.synthesize` to `Lexi.Host`, with the `speech_failed` error code and speech validation in `settings.update`; the protocol version is unchanged. The host never keeps, logs or returns keys. `speech-check` and `host-check` cover the shared cache and the new methods with synthetic services.
- Local speech is identified per platform (`SpeechCatalog.Local`): `windows`/`sapi` on Windows, `macos`/`avspeech` on macOS. Settings saved with the other platform's local voice keep local speech with the automatic choice.

- Add `Lexi.Host`, a JSON-lines host for native front ends. It exposes query, translation, suggestions, history, settings, dictionaries and official-client status, streams every `QueryView` in order, supports cancelling individual requests, and keeps stdout to protocol messages only. It publishes as a self-contained single file with model schemas beside it. See docs/host-protocol.md.
- Share model choices, the “two different models” rule and the fixed Codex options through `ModelCatalog`, used by the Windows settings dialog and the host.
- Add `host-check`, covering streaming, cancellation, errors and settings validation in process, and the host executable's stdout and exit behavior; the macOS CI job also checks the published single-file host.

- Move the query lifecycle into `Lexi.Core`. `QuerySession` now performs routing, source and cache reads, model streaming, merging, saved-view restoration, translation, history and navigation, and publishes a complete `QueryView` at each step: progress stage, status text, sense rows with provenance and citations, sources, word forms, pronunciation, etymology, TL;DR and translation. The WPF window only renders these views, keeping its existing disclosure, scroll and source-expansion behavior. Settings changes mark the kept result before cancelling, so the order no longer depends on the UI thread.
- Derive simplified/traditional search keys from embedded OpenCC character tables on every platform instead of Windows `LCMapStringEx`. Older traditional-only indexes are searched with a bounded set of candidate spellings (for example 车钟 → 車鐘). Displayed dictionary text is unchanged.
- Find Codex and Claude Code without relying on the login shell PATH: besides PATH, check `~/.local/bin`, Homebrew, `/usr/local/bin`, npm global prefixes and Node version managers; run npm entry scripts through the matching `node` and pass the found folders to the client. Without a command-line install, use the Codex CLI bundled with the ChatGPT desktop app and the newest verified macOS Claude Code kept by the Claude desktop app (never its Linux sandbox build). Windows discovery is unchanged. The missing-Codex message no longer assumes `codex.exe`.
- Choose data folders per platform: Windows keeps the portable `userdata`/`data` folders; macOS uses `~/Library/Application Support/LeXi`.
- Add `session-check` and `platform-check`, a cross-platform `Lexi.Shared.slnf`, and a macOS CI job. `scripts/Test.ps1` runs the shared checks on macOS and Linux and the WPF checks on Windows.
- Add [FEATURES.md](FEATURES.md) for Windows/macOS parity, an affected-platforms section in the PR template, and split AGENTS.md into shared behavior and per-platform interface rules.

## 0.9.16

- Focus the search input immediately on startup, taskbar activation and explicit restore, with an input-priority retry after WPF layout and focus restoration. Declare the input as the window's logical focus target and align native keyboard focus only while the main window is already foreground and enabled.
- Select existing text for replacement on activation without reselecting text typed before a deferred retry. Cancel pending focus work on mouse interaction, deactivation, minimization and close; keep background windows and modal settings from redirecting keyboard input.

## 0.9.15

- Read ElevenLabs account voices on the first dropdown expansion, reuse that list within the settings dialog, and provide an explicit “刷新声线列表” action. Keep loading and connection errors beside the voice selector; invalidate results on key edits, reject stale responses, and preserve saved/custom choices without inventing voices for empty accounts.
- Explain Secret Key versus API Key ID beside the masked input and report the provider's `api_key_id_used_as_api_key` error for both voice discovery and synthesis. Classify known service codes without displaying raw provider messages, credentials or account details.

## 0.9.14

- Add ElevenLabs speech using its official API, explicit model/voice selection, on-demand paginated account voice loading and an optional manual Voice ID. Keep saved voice names, provide the API-key dashboard link, mask and encrypt keys locally, and preserve provider/model/voice audio cache separation.
- Keep Windows local SAPI selected on upgrade. Clarify provider access and credits in speech settings, show only the selected provider's key panel, and sanitize ElevenLabs authentication, quota and voice errors. Voice lookup and synthesis happen only on explicit clicks; no account credentials are imported.

## 0.9.13

- Move the quick-read text up by 2 DIP to center its visible letterforms beside the TL/DR badge. Preserve the existing 19-DIP type, 28-DIP line spacing, selectable text, badge size and equal horizontal gaps; the correction applies to single-line and wrapped summaries.

## 0.9.12

- Reduce the TL/DR badge from 28 to 20 DIP to sit closer to the summary's 19-DIP type size, with proportionally smaller vector letterforms. Account for the text box's internal leading inset so the visible gaps on either side of the badge are both 12 DIP; keep vertical centering and the existing summary text size.

## 0.9.11

- Keep the TL/DR mark consistent for local excerpts, model previews and final summaries. Replace font-rendered labels with a compact two-by-two vector badge using the shared pale pink and dark letterforms; center it vertically beside single-line and wrapped summary text. Keep provisional and partial-result status separate, with source/core-selection explanations in the tooltip and accessible caption.

## 0.9.10

- Preview the selected search suggestion in the input while keeping the caret at its end. Up/down browse the original candidates without launching a lookup; typing immediately edits the selected word. Clicking or pressing Tab accepts a suggestion for editing, Enter searches, and Escape restores the input from before browsing. Keep suggestion previews out of query history and saved results.

## 0.9.9

- Increase the title's size and use the main text color for clearer contrast on acrylic.
- Put the quick-read guide beside the first line of the summary: a stacked TL/DR mark for selected core meanings, or an excerpt label for unranked results. Keep provisional/partial state visible inline and move the full explanation into its tooltip and accessible name.
- Preserve safe failure categories for sense merging and show the cause, retained results and retry action in progress details. Mark the failed stage explicitly instead of marking every stage as ended. Legacy failed views remain readable without inventing a cause; no new model calls or automatic cache refresh.

## 0.9.8

- Restore saved window bounds after initial WPF/chrome layout, preserving the startup snapshot until restoration succeeds. Keep early startup geometry from overwriting the saved position.
- Theme the tray menu with the application's dark surface, readable text, rounded hover states and pale-pink selection text.
- Make scrollbars visible on acrylic using neutral-gray grips, with brighter hover/drag states and a wider hit area.
- Keep the loose shell icon in a separate content path so it cannot shadow the embedded WPF icon; validate this during program checks.
- Refresh matching LeXi taskbar shortcuts with a content-addressed packaged icon, avoiding stale executable-icon caches without resetting Explorer or unrelated shortcuts.

## 0.9.7

- Remember main-window size and position automatically, including the normal bounds behind minimization/maximization. Persist separately from preferences and recover safely from missing or invalid state.
- Use a pure-black logo background with a pale-pink rounded-square outline across the application, tray and repository brandmark.
- Treat specialist interests as a coverage checklist, not an assumed context. Preserve general definitions and their native domain; distinguish an industry application from a separate meaning in generation and merging. Remove domain-tag promotion. Existing saved queries stay unchanged until explicit regeneration.

## 0.9.6

- Unify pink accents around the logo's pale pink (#FFADD2). Use the same color for primary buttons and text selection with dark readable text; derive translucent cards and disclosure states from that color. Align the upper-right actions into two consistent columns.
- Persist completed dictionary views, including final grouping, provider attribution, pronunciation, morphology and etymology. Revisit them without network requests or automatic expiry, including after model/lookup settings change. Only explicit regeneration replaces saved content; incomplete streaming is never saved, and failed refreshes retain the previous saved result.
- Give complete translations the same manual-refresh behavior and retain the producing model's identity. Reuse valid legacy caches without automatic age/version refresh, while keeping dictionary and translation identities separate.

## 0.9.5

- Add session back/forward navigation, Alt+Left/Right shortcuts, destination tooltips and scroll restoration. Preserve query case, translation paragraphs and routing, reuse the existing cache pipeline, and keep recent-history deletion separate from the visit trail. A new query after going back clears the forward branch; refreshes do not duplicate visits.
- Separate window controls from lookup controls. A compact top toolbar holds navigation, settings and close, with a continuous labeled drag area; progress and Enter remain alongside the input.
- Make X/Alt+F4 configurable as minimize or exit, defaulting to taskbar minimization for Win+number access. Add a Windows notification-area icon with open, settings and unconditional exit actions; dispose it when the application exits. Esc still minimizes and Ctrl+Q still quits. Keep this integration in the Windows frontend.

## 0.9.4

- Put conventional Chinese translations first in quick-read glosses and full definitions. Supply the equivalent as structured metadata, retain it when the summary budget is tight, and keep the existing core-meaning selection. Refresh old model caches once online while retaining them as offline/initial fallbacks; no additional summary request.
- Replace startup examples with local query history. Keep the 30 most recent word/text submissions, preserve case and translation paragraphs, support one-click requery and individual deletion, and persist history independently of definition caches. Clear the search box or select the footer xi to return to history.

## 0.9.3

- Show first-result latency and total query duration in the footer. Include routing, cached/offline results, model completion, sense judgment and pronunciation; apply the same timing to translation and terminal failures.
- Select core meanings independently of technical coverage and display order. Reuse the existing sense judge to select overview IDs, place those meanings first, and keep every original definition accessible. New model responses identify core senses without an additional request; legacy unranked output is labeled as an excerpt until judged.
- Version comparison caches so existing definition caches receive the improved overview selection without regenerating both models. Keep short summaries focused instead of filling a second slot with a peripheral meaning.

## 0.9.2

- Use a uniformly rounded TL;DR card without a left accent rule. Preserve the existing tint, spacing and lookup behavior.

## 0.9.1

- Add a compact pink-tinted TL;DR above definitions. Reuse short glosses from the same definition request, with complete-text fallbacks for old caches and offline entries; keep preview, partial and final states explicit without another model call.
- Move etymology into a Rosetta-stone-inspired vector control beside the headword. Open a selectable, scrollable popover with source attribution, keyboard dismissal and bounded dimensions; retain content and reader position during model updates.
- Remove the standalone specialist-reference section. Keep uncovered professional meanings as attributed rows in the definition card, and place covered reference summaries inside the corresponding sense's reference disclosure.

## 0.9.0

- Preserve query case across online lookup, model requests and caches. Resolve exact-case and lowercase Wiktionary pages separately; keep abbreviation expansions visible and prevent merging unrelated expansions or attaching lowercase homograph inflections.
- Prioritize established cryptoasset, blockchain, cryptography and security meanings without dropping common usages. Add 12 attributed reference briefs with aliases, available offline, and general specialist coverage in both model requests. Raise the per-model sense budget to six and version definition/merge caches.
- Reject damaged IPA escape sequences, normalize stress and vowel length, and display slash-delimited transcriptions. Read expanded IPA templates from Wiktionary so valid online pronunciations can replace damaged local entries.
- Add a collapsed etymology disclosure with attributed Wiktionary excerpts and concise Chinese model explanations. Source etymology can join the existing definition request; unsourced historical claims are discouraged and model explanation remains identified.
- Use serif italic examples, rename source disclosures to “参考资料”, and give secondary controls persistent surfaces, borders and expanded/focus states. Preserve disclosure state and clear metadata between queries and translations.

## 0.8.2

- Return structured morphology alongside each model's definitions, covering noun/verb/adjective/adverb uses introduced after initial dictionary lookup. Update forms when each model completes; keep partial output out of the display and cache, and preserve complete data during streaming refreshes.
- Fill missing recorded forms with model-supplied morphology while retaining its own provenance. Prefer recorded spellings and the configured primary model when proposals differ; retain other proposals in the form tooltip. Late online dictionary data can take precedence without dropping the remaining model enrichment.
- Keep completed morphology in the model-specific cache for offline use. Preserve legacy definitions as an immediate/offline fallback and refresh them once online to obtain the new fields. Translation and sense-merge model selection are unchanged; morphology adds no separate model request.

## 0.8.1

- Remove redundant word-form and bilingual-definition headings. Use warm English serif italic parts of speech, upright serif word forms and serif examples, with a Song-style Chinese fallback. Align word-form labels and spellings on a shared text baseline and combine identical past/participle spellings.
- Read expanded English headword inflections from Wiktionary alongside pronunciation. Combine online verb forms with offline noun data, retain source attribution and cache forms for offline use without guessing endings or adding model calls. Query counts remain in progress details.
- Update the xi icon and in-app mark to a charcoal-black background with pink lettering.

## 0.8.0

- Rebrand the product and repository as LeXi, with a pink Greek xi (ξ) mark and multi-size application icon. Replace the detailed README with a concise product introduction and move operational details to the usage guide.
- Add a model-account connection page and a shortcut beside the primary model selector. Check local Codex/Claude sign-in status, open their official interactive OAuth login on request, and retain Codex executable configuration. Never read credential files or display raw account output.
- Keep online/offline switch geometry fixed and use equal-length labels.
- Highlight readable Chinese parts of speech, localize useful usage labels and remove neutral boilerplate. Move model provenance details into source disclosures without equating missing dictionary evidence with an incorrect definition; retain all citations and original labels.
- Display recorded word forms next to the headword: verb tenses and participles, noun plurals and adjective comparisons. Resolve inflected queries through their original lemma even when the inflected spelling has its own dictionary entry; preserve alternative spellings and avoid guessed endings.
- Document a single-repository macOS direction with a shared core and platform-specific UI/services. macOS remains future work.

## 0.7.0

- Consolidate query timing and status into a single bottom bar. Give regeneration a persistent button surface and border, and use an accessible two-state online switch with a thumb, clear labels and keyboard focus. Remove the passive dictionary count and redundant footer text.
- Add five official NAER offline terminology datasets: computer science, cross-strait computing translations, electrical engineering, physics and mathematics. Pin source checksums and retain attribution, original wording and simplified lookup aliases.
- Preserve and index both regional translations in cross-strait terminology; label them separately. Add optional dictionary IDs to the installer so existing installations can add selected sources without replacing other dictionaries.
- Verify regional lookup aliases and original text, accessible switch states and compact bottom-bar layout without opening or automating desktop windows.

## 0.6.1

- Unify search-row controls into evenly spaced 32-DIP targets with 18-DIP vector icons and consistent strokes. Remove the isolated Enter-button background and divider.
- Replace the four-dot diamond with a quiet status ring: an indeterminate arc while working, a check on completion and a warning symbol for incomplete results. Preserve full stage details on hover/focus and reduced-motion behavior.
- Increase IPA from 14 to 20 DIP and lay out the headword, phonetics and speech button in a shared inline text flow. Keep the headword and IPA on one baseline, retain wrapping for long terms, and separate the smaller AI provenance label from the phonetic text.

## 0.6.0

- Compare senses within each model as well as across models. Group confident facets of one lexical usage, prefer the most comprehensive original wording, and preserve all original definitions and attribution under expandable details. Keep materially different usages separate and invalidate older merge plans without discarding model results.
- Restore phonetics independently of model definitions: prefer offline pronunciation, fetch and cache English Wiktionary IPA when missing, and label model-only IPA as unverified. Reject late pronunciation updates from canceled queries.
- Add explicitly triggered pronunciation with Windows local voices by default, plus OpenAI and Grok TTS model/voice settings and preview. Encrypt user-entered cloud API keys with Windows current-user protection, cache complete audio by text/model/voice, and support cancellation without switching providers. Cloud TTS requires separate API credentials and billing.
- Move query progress into a four-dot diamond beside the search input. Show dictionary, supplement, merge and completion details on hover or keyboard focus, retain incomplete-result states, and stop animation when results settle.
- Add account-free IPA, TTS protocol, credential-encryption, cancellation, grouping and compact-progress checks. Local voice validation writes a WAV file without playing audio; cloud endpoints use synthetic responses in automated tests.

## 0.5.2

- Use a pink accent and rose text-selection highlight across the main window and settings, retaining readable selected text over Acrylic.
- Add a fixed, compact query-stage strip for dictionary lookup, model supplementation, sense merging and completion. Keep live counts labeled as preview candidates until the entire query finishes; explain that merging can reduce the count.
- Distinguish stable completion from model/source failures and incomplete merging. Stop the loading indicator on every terminal path, retain available definitions, and prevent late callbacks from reviving or overwriting a finished/newer query. Translation has its own generation/completion states, including cache and offline exits.
- Add account-free lifecycle checks for the 2-to-6-to-4 result flow, failure states, stale updates, contrast and minimum-window layout. Keep definition sizes and expandable secondary information unchanged.

## 0.5.1

- Increase dictionary result density while retaining 20-DIP Chinese definitions, 18-DIP English definitions and existing line spacing. Combine part of speech, register and provenance into one wrapping line, and place pronunciation beside the headword.
- Hide examples, detailed citations, alternate wording and model notes behind compact, keyboard-accessible disclosure controls. Keep core definitions, uncertainty, incomplete-output labels and failures visible.
- Preserve each disclosure's open or closed state during streaming and merging, including an open example from the faster model when it moves under alternate wording. Reset details for a new lookup.
- Add non-displaying checks for result height, unchanged typography, selectable examples, complete citations and disclosure state. The five-sense fixture uses about 59% less result height at the default window width.

## 0.5.0

- Query two configurable dictionary models concurrently, showing the first available definition immediately; retain the other model's results when one fails. Keep text translation on the selected primary model.
- Add configurable Codex subscription-based semantic comparison after lookup. Group confidently equivalent senses, keep distinct or uncertain meanings, prefer the primary wording and retain every original variant with its own source labels in expandable sections.
- Separate complete-definition caches by model and evidence, cache validated comparison plans, reject mismatched or incomplete judgments, and preserve all definitions on judge failure or timeout.
- Improve coverage of contemporary Internet meanings and keep slang evidence ahead of older senses when preparing dictionary context. Preserve original offline and online dictionary text in expandable source sections.
- Add account-free concurrency, cancellation, cache and semantic-grouping regression checks, non-displaying result/settings checks, and an opt-in live comparison runner.

## 0.4.2

- Suppress DWM's system-accent caption fill on full-client Acrylic windows, including after system theme changes, while retaining resizing and background material.
- Enlarge dictionary definitions, examples, translations, search input and supporting text; give multiline results more line spacing.
- Reduce the dark overlay on Acrylic and result cards, with brighter secondary text and the existing opaque fallback.
- Check native caption/resize flags and the Acrylic-or-opaque invariant using an HWND that is never displayed or activated.

## 0.4.1

- Replace private-use icon-font glyphs and disclosure arrows with vector geometry so global text styles cannot turn controls into missing-glyph boxes.
- Use non-adorner text selection, an explicit contrasting selected-text brush and a translucent highlight for editable and read-only text.
- Allow dragging from non-interactive areas throughout both windows while preserving selection, buttons, menus, tabs and scrollbar interaction.
- Add native Windows 11 Desktop Acrylic behind translucent surfaces, retaining opaque text and a solid fallback when transparency, composition or system support is unavailable.
- Add 15 presentation regression checks for icons, selection contrast, drag exclusions and glass availability without displaying windows or automating the desktop.

## 0.4.0

- Replace the main window and settings with a compact Raycast-inspired dark WPF design, custom window chrome, consistent controls and quieter source styling.
- Remove query-mode and translation-language selectors and the input helper row; automatically route terms, short sentences and paragraphs without switching models.
- Detect the dominant language in mixed Chinese/English text, preserve known dictionary phrases and add automatic-routing regression checks.
- Add Ctrl+, for settings and non-displaying WPF resource construction checks; retain taskbar activation, resizing, text selection and keyboard access.

## 0.3.0

- Add MOE revised Mandarin dictionary, Open English WordNet, CC-CEDICT and NAER maritime/shipbuilding terminology installers, with provenance and license handling.
- Query multiple local dictionaries with per-source settings and simplified/traditional lookup keys; preserve original text.
- Support Chinese Wiktionary entries and pass eligible offline evidence to the selected model; keep modern English slang lookup online.
- Add multiline Chinese/English text translation, automatic/manual mode selection, target language selection and separate case-sensitive translation caching, without automatic model switching.
- Isolate outdated definition caches, avoid caching unknown terms, retry malformed Claude output once with the same model, and add 24 dictionary/translation regression checks.
- Update pinned GitHub Actions to Node.js 24 releases to remove deprecated runtime warnings.

## 0.2.0 — 2026-10-16

- Windows WPF desktop dictionary with offline ECDICT lookup and inflection matching.
- Wiktionary evidence, bilingual AI definitions, examples and provenance labels.
- Claude Haiku streaming output and prepared workers; Codex Low + Fast option.
- Local cache, cancellation handling and independent context for each lookup.
- Installed offline dictionary settings and consistent Microsoft YaHei UI fonts.
- Native taskbar behavior, single-instance activation and keyboard navigation.
- Repository build scripts, offline regression checks, privacy scanning and Windows CI.
