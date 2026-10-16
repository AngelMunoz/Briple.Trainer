module Briple.Trainer.Theme

open Avalonia.Media

let accentHex = "#2D7DD2"

let accent = SolidColorBrush(Color.Parse accentHex) :> IBrush

let text = SolidColorBrush(Color.Parse "#1A1A1A") :> IBrush

let muted = SolidColorBrush(Color.Parse "#6B6B6B") :> IBrush

let errorBrush = SolidColorBrush(Color.Parse "#C0392B") :> IBrush

let chip = SolidColorBrush(Color.Parse "#D8D8D8") :> IBrush

/// The OS UI font, like CSS `system-ui`: the platform FontManager
/// resolves it (Segoe UI on Windows, the system default on macOS/Linux).
let font = FontFamily.Default
