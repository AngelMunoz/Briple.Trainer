module Briple.Trainer.Theme

// Visual tokens of the Fluent language: one accent plus its tonal wash,
// the OS UI font, the system Mica behind the window. The tokens ride on
// the control factories in Components.Fields; no global style dictionary
// is needed for the flat look.

open Avalonia.Media

/// The single accent of the app.
val accentHex: string

/// The accent as a brush, created once.
val accent: IBrush

/// The default text brush.
val text: IBrush

/// The muted text brush for secondary labels.
val muted: IBrush

/// The error text brush.
val errorBrush: IBrush

/// The background of an unselected chip.
val chip: IBrush

/// The accent at a wash: tonal fills for badges and selection states.
val accentTonal: IBrush

/// The OS UI font, like CSS `system-ui`: the platform FontManager
/// resolves it (Segoe UI on Windows, the system default on macOS/Linux).
val font: FontFamily
