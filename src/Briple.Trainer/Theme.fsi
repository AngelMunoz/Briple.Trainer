module Briple.Trainer.Theme

// Visual tokens and the one app-level style layer, code-first over the
// FluentTheme as shipped. Classes: the type ramp (`title`, `subtitle`,
// `body-strong`, `body`, `caption`, `caption-strong`, `pane-title`),
// the tone classes (`secondary`, `danger`, `accent-text`), `accent`
// (primary action), `chrome` (flat in-bar buttons), `back` (the shell's
// 40x40 header lead), `row` (tappable content rows), `nav-item` (pane
// rows), `card` (content surface). Views assign classes; a reactive
// foreground or weight stays a binding. Precedence: these setters
// outrank the ControlTheme, so every property the theme animates by
// pseudo-class carries all its states here.

open Avalonia
open Avalonia.Media
open Avalonia.Styling

/// The single accent of the app.
val accentHex: string

/// The accent as a brush, created once.
val accent: IBrush

/// The accent one step down: the primary button's hover fill.
val accentHover: IBrush

/// The accent two steps down: the primary button's pressed fill.
val accentPressed: IBrush

/// An 8% wash: hover fill for flat surfaces.
val hoverFill: IBrush

/// The default text brush.
val text: IBrush

/// The muted text brush for secondary labels.
val muted: IBrush

/// The error text brush.
val errorBrush: IBrush

/// The accent at a wash: tonal fills for badges and selection states.
val accentTonal: IBrush

/// The error at a wash: InfoBar bands and failure fills.
val errorTonal: IBrush

/// The card surface fill over Mica.
val cardFill: IBrush

/// The OS UI font, like CSS `system-ui`: the platform FontManager
/// resolves it (Segoe UI on Windows, the system default on macOS/Linux).
val font: FontFamily

/// Re-tints the neutral tokens for a theme variant: the brushes are
/// live instances, so every consumer follows without rebinding.
val applyVariant: variant: ThemeVariant -> unit

/// The app's whole style layer. Install once, right after the
/// FluentTheme, in App.Initialize.
val appStyles: Styles
