# WyspaFluent design system

Status: design direction approved; requested refinements implemented and verified. Updated 2 October 2026.

The user approved the shared WPF theme, shell, and representative Settings screen, then requested the refinements recorded below. This document remains the design reference for future UI work, as directed by the root `AGENTS.md`. All five main pages now share the page padding, title style, and content width; existing transcript layouts, overlays, and specialized interactions remain. This is not a claim that every dialog or window has been redesigned.

## Scope and references

The direction follows the supplied references in `Redesign/01-layout.png` through `Redesign/04-lists.png`: a quiet navigation rail, clear title hierarchy, grouped settings rows with subtle subsection surfaces, restrained Windows-accent selection, and compact native controls. Settings has seven groups in this order: Groq, Conversation, Audio & Capture, Look & Feel, Privacy, System, and Experimental. Experimental stays last. The shell retains Home, Audio Files, Conversation, YouTube, and Settings as five distinct destinations.

The visible window title and product name are `WyspaFluent`. The assembly and executable remain `Wyspa` and `Wyspa.exe` for compatibility with existing packaging, installation, and startup paths. Existing settings storage, Groq access, dictation and conversation workflows, and separate conversation/YouTube libraries remain in place. Refinements add routed navigation commands and an independently requested local input-level preview. Display-name changes preserve persisted identifiers and data formats: SmartListen continues to use `ActivationMode.AutoCapture`, `AutoCaptureService`, and the existing `AutoCapture*` properties internally.

## Theme architecture

The app targets .NET 10 WPF. `src/Wyspa.App/App.xaml` starts with `ThemeMode="System"` and merges `src/Wyspa.App/Themes/WyspaFluent.xaml`. That dictionary loads Microsoft's common Fluent dictionary at `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`. The use of the built-in theme and `ThemeMode` follows [Microsoft's WPF Fluent guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90). The runtime also includes the [Fluent improvements in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100).

`src/Wyspa.App/Services/ThemeService.cs` owns the app's semantic palette. It applies the saved `AppSettings.Theme` preference (`AppTheme.System`, `Light`, or `Dark`), responds to general, visual-style, and accessibility preference changes, applies matching native light/dark mode, and updates dynamic resources. System reads the Windows app theme preference; explicit Light and Dark choices remain fixed across Windows theme changes. Windows high contrast takes precedence. `ApplyTheme(bool)` also lets the native harness exercise both palettes without changing the user's Windows preferences.

Native theme changes go through `Application.Current.ThemeMode`. Replacing only a light/dark resource dictionary can leave instantiated native styles using old colors. The code API's `WPF0001` experimental opt-in is suppressed locally around this one assignment, rather than across the project. System is the default, including for existing settings files without a Theme field. The Theme dropdown in Look & Feel applies changes immediately and saves through the existing settings autosave flow. Startup applies the saved preference before showing the main window.

Semantic resource names describe purpose, following the [Fluent design-token approach](https://fluent2.microsoft.design/design-tokens). These are app-owned values, not a claim that each name is an official Fluent token.

### Palette

| Resource | Light | Dark | Role |
| --- | --- | --- | --- |
| `AppBackgroundBrush` | `#EFEFEF` | `#1E1E1E` | Main page surface |
| `ChromeBrush` | `#E9E9E9` | `#202020` | App header and navigation surface |
| `PanelBrush` | `#FCFCFC` | `#2B2B2B` | Group and card surface |
| `PanelAltBrush` | `#F4F4F4` | `#343434` | Alternate and hover surface |
| `InputBrush` | `#FFFFFF` | `#242424` | App input-surface token |
| `InkBrush` | `#171717` | `#F7F7F7` | Primary text |
| `MutedBrush` | `#505050` | `#D1D1D1` | Secondary text and inactive toggle detail |
| `LineBrush` | `#D1D1D1` | `#555555` | Dividers and surface borders |
| `AccentBrush` | Windows accent | Windows accent | Selection, active toggles, native accent roles |
| `AccentDarkBrush` | Contrast-adjusted accent | Contrast-adjusted accent | Links and focused text-control borders |
| `AccentSoftBrush` | Accent tint on page surface | Accent tint on page surface | Selected navigation background |
| `AccentTextBrush` | Black/white by contrast | Black/white by contrast | Text or thumb on accent fill |
| `WarnBrush` | `#9D4C20` | `#FFBE90` | Warning text |
| `SelectedTextBrush` | Black/white by contrast | Black/white by contrast | Selected-text foreground |

The contrast refinement strengthens primary and secondary text, subtly deepens the page/navigation backgrounds, separates subsection surfaces, and makes shared borders/dividers more visible in both palettes. Fonts, spacing, native interaction states, and Windows high-contrast precedence retain their existing treatment. ThemeService applies the Windows accent before the main window is shown.

`LogoGradientBrush` remains a compatibility resource with a solid Windows-accent value. Despite its historical name, it is not a gradient. Raster branding assets are unchanged.

`ThemeService` maps the app accent to the native Fluent accent-button backgrounds and borders, accent-fill roles, slider thumbs, progress foreground, and selected ComboBox indicator. Accent-button foreground uses `AccentTextBrush`. Hover and pressed button fills are derived from the accent with 8% and 16% contrast-preserving blends. Hyperlinks and focused text-control borders use `AccentDarkBrush`. Other native state resources continue to come from the Fluent theme.

In high contrast, surfaces use `SystemColors.WindowBrush`; text, dividers, and warnings use `WindowTextBrush`; active accents use `HighlightBrush`; accent text uses `HighlightTextBrush`; links use `HotTrackBrush`; and the soft selection surface uses `ControlBrush`. Native `ThemeMode.System` is used in that case. This mapping is implemented; visual high-contrast acceptance remains pending.

### Type, spacing, and geometry

Dimensions below are WPF device-independent units. The font stack is `Segoe UI Variable, Segoe UI`, with `Segoe Fluent Icons, Segoe MDL2 Assets` for font icons. The 28/14/12 hierarchy is consistent with the Windows roles in [Fluent typography guidance](https://fluent2.microsoft.design/typography); the specific spacing choices adapt [Fluent layout guidance](https://fluent2.microsoft.design/layout) to the existing desktop app.

| Token or pattern | Value |
| --- | --- |
| Page title | 28, semibold |
| Body and field labels | 14 |
| Secondary text | 12, regular; 17 line height |
| Section title | 16, semibold |
| Standard control minimum height | 32 |
| Surface corner radius | 4 |
| Page padding | Left 24, top 20, right 24, bottom 24 |
| Settings row padding | Horizontal 16, vertical 12 |
| Card padding | 16 |
| Settings subsection | 16 padding, 1-unit border, 4 radius; 12-unit gap between sibling subsections |
| Group header | Minimum height 72; horizontal 16, vertical 14 padding |
| Navigation row | Minimum height 40; horizontal 12, vertical 10 padding |
| Navigation selection indicator | 3 wide, 16 high |
| Toggle track and thumb | 40 × 20 track; 12 × 12 thumb |

## Native and custom component boundaries

| Component | Implementation |
| --- | --- |
| Standard and primary buttons | Microsoft `DefaultButtonStyle` / `AccentButtonStyle`, with app spacing and minimum height |
| Text, password, and selection inputs | Native Fluent TextBox, PasswordBox, and ComboBox templates |
| Ordinary checkboxes, scroll viewers, and links | Native Fluent templates |
| Navigation | Custom `NavigationShell` / `NavigationItem` templates over real TabControl / TabItem controls |
| About footer | Native Fluent button using `NavigationFooterButton`, with an official Octicons GitHub vector mark |
| Settings group | Custom `SettingsGroup` template over an Expander, with a full-width ToggleButton header |
| Settings subsection | `SettingsSubsection` Border style based on `Card`, using `PanelAltBrush` and `LineBrush` |
| Adaptive field row | `SettingsField` template over HeaderedContentControl |
| Settings switch | `SettingsToggle` template over CheckBox; retains CheckBox toggle and automation semantics |

Custom templates are limited to the shell and grouped settings patterns. The controls still use existing bindings and commands. Native input templates keep their own focus, hover, pressed, disabled, selection, and popup behavior. WPF has no built-in ToggleSwitch, so the switch appearance is implemented on a CheckBox with visible On/Off state text, pointer feedback, disabled opacity, and a native focus visual.

The standard Windows title bar and caption controls remain native. `NativeWindowStyler` continues to request OS dark-caption, corner, and backdrop attributes where supported. No replacement minimize, maximize, or close controls are introduced, and the WPF client captures do not validate the OS-rendered caption or backdrop.

## Approved refinements and behavior

### Settings presentation and naming

Within Settings, the scoped `Card` style derives from `SettingsSubsection`. Subsections use `PanelAltBrush`, a thin `LineBrush` border, a 4-unit radius, and 16-unit padding to separate related controls without introducing another heavy visual layer. Sibling subsections use a 12-unit gap. The reusable style defaults to `Margin="0,8,0,12"`; individual placements adjust the outer spacing. Ordinary field rows continue to use thin dividers.

Use these exact visible names: **Audio & Capture**, **Input Device**, **SmartListen**, **SmartListen Threshold**, and **Look & Feel**. Keep **Experimental** as the final Settings group. Transcription and summary model selection stays under Groq; experimental rewrite and action controls retain their existing bindings in Experimental.

### Theme and Overlay

Look & Feel contains Theme, Windows notifications and Overlay first, followed by the existing Basic Cleanup and Text Insertion subsections. The Theme dropdown offers **Dark**, **Light**, and **System**. Overlay was moved out of System, which now contains startup and update settings. Its existing opacity binding and autosave behavior are preserved.

`ThemeService.ApplyPreference` stores the active appearance choice; `RefreshTheme` resolves it when Windows settings change. Windows high contrast retains priority. App resources update existing controls immediately, and theme notifications refresh native main-window/conversation-overlay styling and the recording overlay. The recording overlay uses the selected palette for its panel and text while retaining its configured panel opacity. The System theme reader is injectable for deterministic native harness checks without changing the user's Windows preferences.

### Home, shortcuts, and About

Home replaces its Scratchpad section with Getting Started: create or sign in to a Groq account, create an API key, connect Wyspa, and select/check the input before dictating. It links to the official Groq Console and API Keys pages and provides buttons to the existing Groq and Audio & Capture groups. Existing dictation controls remain on Home.

`src/Wyspa.App/AppNavigationCommands.cs` defines routed commands for Groq, Audio & Capture, and Conversation settings. The Conversation page's settings button selects Settings, expands the actual Conversation group, brings its header into view, and focuses that header. The Home shortcuts use the same group-opening behavior. These routes expose existing configuration rather than duplicate it.

The bottom-left **About** button stays anchored in the navigation footer and opens the repository at [DracoManX69/Wyspa on GitHub](https://github.com/DracoManX69/Wyspa). Its accessible name is “About Wyspa on GitHub.” The button retains the native Fluent template and uses the official Octicons GitHub mark. The MIT license is stored at `src/Wyspa.App/Assets/Octicons-LICENSE.txt` and copied to build and publish output. `AppNavigationCommands.RepositoryUri` is the single source for the destination.

### Local input-level preview

The meter under SmartListen Threshold shows local levels whenever it is visible in the Settings viewport, including when SmartListen is disabled, another activation mode is selected, or no Groq key is present. `AutoCaptureService.SetLevelPreviewAsync` requests the shared local level monitor independently of the saved listening setting. Previewing levels does not enable speech-triggered capture, create an audio file, upload audio, or change saved settings.

The preview request is released when the meter scrolls out of view, its group collapses, another tab is selected, or the window is hidden, minimized, or closed. Releasing that request preserves monitoring needed by enabled background SmartListen. The selected Input Device is applied to the shared monitor; active recording and conversation suspension guards take priority. Speech-triggered recording retains its activation-mode, listening-enabled, key, and capture-state guards, so preview levels alone cannot start recording.

Device failures show a local input-level error near the meter. Returning to the preview or applying a device/settings change can retry the monitor. Keep these visibility, capture, and background-listening rules together when changing the meter or navigation.

## Responsive and accessibility patterns

The main window starts at 1180 × 860 and retains a 560 × 480 minimum. `MainWindow.IsCompactLayout` is a dependency property updated on resize; it becomes true below 960 units of window width. The rail changes from 232 to 160 units while keeping text labels. Home, Audio Files, Conversation, YouTube, and Settings all use `PagePadding`, `PageTitle`, and a maximum content width of 1040, with vertical scrolling.

`SettingsField` places a label and description beside a 300-unit control column in the wide layout. In compact mode, the control moves below the label and fills the available column. Labels and descriptions wrap. Other existing Settings layouts remain in their current groups and are checked at minimum size; this checkpoint does not claim a new adaptive layout for every legacy panel.

Navigation retains TabControl selection and keyboard semantics. Group headers are actual expand/collapse controls. Settings inputs and sliders have explicit automation names, group headers expose their names, and connection/model status text uses polite live-setting metadata. The templates reference the native focus visual. These implementation choices support accessibility but do not substitute for keyboard-only or screen-reader acceptance testing.

## Validation and review evidence

The final v0.8 automated suite reported **109 passed, 0 skipped, 0 failed** after enabling the native FLAC integration tests. Settings coverage includes theme persistence and compatibility with older settings files that omit Theme.

A native Windows WPF harness loads the actual views with isolated settings and note files, simulated microphone and Groq services, fixture text, and fake capture. Earlier coverage includes all five navigation destinations, all seven Settings groups, task-specific model filtering, refresh and selection persistence, text-field focus traversal and autosave on focus loss, nonempty UI Automation peer names, model and toggle autosave, independent note libraries, and simulated start/pause/resume/stop.

The final refinement run in `artifacts/fluent-refined-final` passed with exit code 0 and a zero-byte `binding-errors.txt`. It additionally verified:

- All three Settings shortcuts, expansion and header navigation, the final Settings group order, and the About command's repository target and routing. The external browser was not launched.
- Simulated microphone levels through the actual meter binding with SmartListen disabled and no key, with no recording started; selected-device changes; and preview release/reacquisition across hiding, minimizing, scrolling, tab changes, and group collapse.
- Capture suspension, preservation of enabled background SmartListen, and input-monitor failure/retry handling.

Current captures include Home at normal and minimum size, the refined Audio & Capture section in light, dark, and narrow layouts, and the native model-dropdown popup. Earlier theme evidence remains in `artifacts/fluent-qa-theme` and `artifacts/fluent-verification`.

The preceding subsection/SmartListen refinement received a fresh finish review with **SHIP** for its requested changes, with no material visual, accessibility, or behavior findings after inspecting the wide/narrow and light/dark captures, source, and harness. The reviewer did not independently rerun tests or exercise a physical microphone, external browser, or live Groq request. Physical microphone use and new live transcription remain untested in this refinement.

The subsequent Look & Feel/theme run in `artifacts/fluent-theme-qa` passed with exit code 0 and no binding errors. It exercised Dark/Light/System through the actual dropdown, autosave and settings reload, a fresh theme-service startup, explicit preferences surviving simulated Windows theme changes, System following those changes, the moved opacity control's persistence, and dark recording-overlay color/opacity. Captures include wide light/dark Look & Feel, minimum-size controls, the native theme dropdown, and the dark recording overlay. These checks inject Windows theme values; they do not change the host's Windows preferences or prove receipt of an actual OS appearance notification.

The contrast-only refinement was checked in `artifacts/fluent-contrast-qa` using actual light/dark and narrow WPF renders. The existing native harness passed, including theme selection/persistence, with an empty binding-error log. Secondary text against subsection surfaces increased from 5.82:1 to 7.33:1 in Light and from 7.27:1 to 8.15:1 in Dark (computed from the opaque semantic colors; this is not a full accessibility audit). The runnable preview was republished after this palette change.

Desktop computer-use capture failed twice during the initial checkpoint. The available images use WPF `RenderTargetBitmap` and show rendered client content. They do not establish OS caption rendering, desktop composition, raw keyboard interaction, screen-reader announcements, high-contrast appearance, or DPI/multi-monitor behavior. Those remain manual acceptance checks.

Build the framework-dependent preview from the repository root in WSL:

```sh
dotnet publish src/Wyspa.App/Wyspa.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/WyspaFluent
```

Publishing succeeded and the refinement preview was refreshed at `artifacts/WyspaFluent/Wyspa.exe`. It needs a compatible Windows .NET 10 Desktop Runtime; the verification host has 10.0.11 installed. The subsequent v0.8 release packages this design with an installer that installs a missing compatible runtime automatically. See `docs/V8_VALIDATION.md` for release evidence and manual acceptance limits.

## Guidance for subsequent UI work

v0.9 adds **Stream Mode** as an independent `SettingsToggle` in the existing Capture & Shortcuts subsection. It uses the shared on/off, focus, automation and autosave treatment; its supporting text wraps using `BodyText`. It does not add an activation-mode enum value or rename persisted settings. See `STREAM_MODE.md` for behavior and validation limits.

Continue from this approved design system. Reuse the shared semantic palette, native Fluent controls, page geometry, Settings subsection treatment, and routed Settings shortcuts. Keep conversation and YouTube workflows separate and preserve persisted identifiers when changing display labels. Further changes to specialized dialogs, transcript layouts, or overlays should be scoped explicitly and checked against their existing behavior; these refinements do not establish complete acceptance of every app surface.

### v0.9.1 Stream Fix and processing status

Experimental remains the final group. Stream Fix uses the existing Card/SettingsToggle/BodyText resources and persists separately from Stream Mode. Its toggle is enabled only when Stream Mode is on; helper text explains conservative dictation-only proofreading, verified passage replacement across supported editors and clipboard fallback. The selected Tone Re-write model is shared without reusing its prompts. The recording overlay retains its state colors, adds a processing animation independent of microphone levels, and has room for a two-line processing/fallback message. Listening and Transcribing states never use the notification auto-hide timer.

### v0.9.2 speech activity and completion

The existing recorder overlay animates throughout active listening and processing. Captured speech takes priority over request state and uses the existing red (191,63,63); quiet listening/pending work uses the existing green (56,137,89). Activity decays after 250 ms without a voiced callback, and captured audio is tracked separately from idle microphone monitoring. Completed delivery hides the overlay before native observer and file cleanup; nonbusy notices retain their timeout but have no waveform bars.

### v0.9.3 Windows appearance and notifications

The Windows accent replaces the previously fixed teal palette. ThemeService reads `SystemColors.AccentColor`, maps it to selection, toggle and native accent roles, derives tinted selection surfaces and readable link shades, and chooses black/white accent foregrounds by contrast. Colour, desktop, visual-style and general Windows preference changes refresh resources on the UI dispatcher. Light/Dark/System preference and high-contrast precedence remain intact. The compatibility LogoGradientBrush follows the accent; raster branding assets are unchanged.

The header has a one-unit bottom LineBrush border and the navigation rail a one-unit right border. Look & Feel adds Windows notifications, default On for compatibility, persisted as WindowsNotificationsEnabled. The central tray notification gate reads the current setting before every notification. Off suppresses all Wyspa Windows notifications; in-app feedback and the recorder overlay remain available.

### v0.9.4 help and neutral chrome

Header, navigation and scratchpad chrome use neutral greys (#E9E9E9 Light, #202020 Dark); theme/accent state still comes from Windows. The raster app icon keeps its branding. Status waveform colours retain their separate recording/processing meaning.

All 27 previous question-mark Border adornments are now HelpButton controls. Each has a 24-unit transparent hit surface around a 16-unit glyph, a keyboard focus indicator, a descriptive automation name and nonempty HelpText. Empty/whitespace help collapses the icon. Hover, click, Enter and Space expose the same wrapped help. Escape, focus loss, clicking elsewhere, window deactivation, unloading or the 20-second timeout dismiss it. Built-in WPF tooltip service handles hover; the button adds explicit invocation and dismissal. See [WPF tooltip guidance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/tooltip-overview).

Control hints are concise explanations of behaviour, consequences and prerequisites. All authored settings inputs/actions have guidance, including disabled controls. Straightforward controls use a direct tooltip and existing inline description; question marks are retained for longer section/concept explanations rather than added to every row. Hints also cover the main actions in Audio Files, Conversation, YouTube, Scratchpad and the conversation overlay. Common timing is 400 ms initial delay, 100 ms between hints and 20 seconds to read; popups wrap within 380 units and use theme-aware neutral surfaces/text. Help icons use the same timing policy.
