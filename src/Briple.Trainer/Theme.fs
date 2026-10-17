module Briple.Trainer.Theme

#nowarn "3261" // the Style selector constructor annotates nullability

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions

/// The single accent of the app, in hex and as a live brush.
let accentHex = "#2D7DD2"

let accentBrush = SolidColorBrush(Color.Parse accentHex)

let accent = accentBrush :> IBrush

let accentHoverBrush = SolidColorBrush(Color.Parse "#2569B0")

/// The accent one step down: the primary button's hover fill.
let accentHover = accentHoverBrush :> IBrush

let accentPressedBrush = SolidColorBrush(Color.Parse "#1E5A97")

/// The accent two steps down: the primary button's pressed fill.
let accentPressed = accentPressedBrush :> IBrush

let hoverFillBrush = SolidColorBrush(Color.Parse "#14000000")

/// An 8% wash: hover fill for flat surfaces.
let hoverFill = hoverFillBrush :> IBrush

let textBrush = SolidColorBrush(Color.Parse "#1A1A1A")

/// The default text brush.
let text = textBrush :> IBrush

let mutedBrush = SolidColorBrush(Color.Parse "#6B6B6B")

/// The muted text brush for secondary labels.
let muted = mutedBrush :> IBrush

let errorSolid = SolidColorBrush(Color.Parse "#C0392B")

/// The error text brush.
let errorBrush = errorSolid :> IBrush

let accentTonalBrush = SolidColorBrush(Color.Parse "#DCE9F7")

/// The accent at a wash: tonal fills for badges and selection states.
let accentTonal = accentTonalBrush :> IBrush

let errorTonalBrush = SolidColorBrush(Color.Parse "#1AC0392B")

/// The error at a wash: InfoBar bands and failure fills.
let errorTonal = errorTonalBrush :> IBrush

let cardFillBrush = SolidColorBrush(Color.Parse "#B3FFFFFF")

/// The card surface fill over Mica.
let cardFill = cardFillBrush :> IBrush

/// The OS UI font, like CSS `system-ui`: the platform FontManager
/// resolves it (Segoe UI on Windows, the system default on macOS/Linux).
let font = FontFamily.Default

/// Re-tints the neutral tokens for a theme variant. The brushes are
/// live instances, so every consumer follows without rebinding.
let applyVariant(variant: ThemeVariant) : unit =
  let dark = variant = ThemeVariant.Dark

  textBrush.Color <- Color.Parse(if dark then "#FFFFFF" else "#1A1A1A")

  mutedBrush.Color <- Color.Parse(if dark then "#C9FFFFFF" else "#6B6B6B")

  hoverFillBrush.Color <- Color.Parse(if dark then "#14FFFFFF" else "#14000000")

  accentTonalBrush.Color <- Color.Parse(if dark then "#332D7DD2" else "#DCE9F7")

  cardFillBrush.Color <- Color.Parse(if dark then "#0DFFFFFF" else "#B3FFFFFF")

/// One app-level style: a selector plus its setters, as code.
let styleOf
  (build: Selector -> Selector, setters: (AvaloniaProperty * obj) array)
  : Style =
  Style(build).Setters(setters |> Array.map(fun (p, v) -> Setter(p, v)))

/// The app's whole style layer, code-first over the FluentTheme as
/// shipped. One owner (this module), one install (App.Initialize), a
/// closed class vocabulary — no view styles anything by itself.
///
/// Precedence rule: these setters outrank the theme's ControlTheme, so
/// any property the theme animates by pseudo-class and we touch here
/// gets every state spelled out (resting, :pointerover, :pressed) or we
/// do not touch it.
let appStyles: Styles =
  let styles = Styles()

  // the type ramp: hierarchy from size and weight, as classes
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("title")),
      [|
        TextBlock.FontSizeProperty, box 28.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("subtitle")),
      [|
        TextBlock.FontSizeProperty, box 20.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("body-strong")),
      [|
        TextBlock.FontSizeProperty, box 14.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("body")),
      [| TextBlock.FontSizeProperty, box 14.0 |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("caption")),
      [| TextBlock.FontSizeProperty, box 12.0 |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("caption-strong")),
      [|
        TextBlock.FontSizeProperty, box 12.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("pane-title")),
      [|
        TextBlock.FontSizeProperty, box 18.0
        TextBlock.FontWeightProperty, box FontWeight.SemiBold
      |]
    )
  )

  // tone classes for static text: the token in one place, not on each view
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("secondary")),
      [| TextBlock.ForegroundProperty, muted |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("danger")),
      [| TextBlock.ForegroundProperty, errorBrush |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<TextBlock>().Class("accent-text")),
      [| TextBlock.ForegroundProperty, accent |]
    )
  )

  // the primary action: the accent fill the "accent" class always
  // promised (primaryButton has carried the class since the beginning;
  // this is its first consumer)
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("accent")),
      [|
        Button.BackgroundProperty, accent
        Button.ForegroundProperty, Brushes.White
        Button.BorderThicknessProperty, Thickness(0.0)
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("accent").Class(":pointerover")),
      [| Button.BackgroundProperty, accentHover |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("accent").Class(":pressed")),
      [| Button.BackgroundProperty, accentPressed |]
    )
  )

  // flat chrome: transparent at rest, a subtle wash on hover, no border
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("chrome")),
      [|
        Button.BackgroundProperty, Brushes.Transparent
        Button.BorderThicknessProperty, Thickness(0.0)
        Button.PaddingProperty, Thickness(8.0, 4.0, 8.0, 4.0)
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("chrome").Class(":pointerover")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  // the flat chrome carries every state itself: a disabled command must
  // not borrow the theme's filled disabled look
  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("chrome").Class(":pressed")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("chrome").Class(":disabled")),
      [|
        Button.BackgroundProperty, Brushes.Transparent
        Button.ForegroundProperty, muted
      |]
    )
  )

  // the shell's back control: an icon-only 40x40 flat button in the
  // page header
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("back")),
      [|
        Button.WidthProperty, 40.0
        Button.HeightProperty, 40.0
        Button.PaddingProperty, Thickness(0.0)
        Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center
      |]
    )
  )

  // content rows (athletes, plans): the whole row is tappable content,
  // flat at rest, washed on hover, rounded like the theme
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("row")),
      [|
        Button.BackgroundProperty, Brushes.Transparent
        Button.BorderThicknessProperty, Thickness(0.0)
        Button.CornerRadiusProperty, CornerRadius(4.0)
        Button.PaddingProperty, Thickness(12.0, 8.0, 12.0, 8.0)
        // the row fills the items control; the content grid fills the
        // row, so the trailing glyph lands on one shared edge
        Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch
        Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch
        Button.VerticalContentAlignmentProperty, VerticalAlignment.Center
        Button.MinHeightProperty, 64.0
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("row").Class(":pointerover")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("row").Class(":pressed")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("row").Class(":disabled")),
      [|
        Button.BackgroundProperty, Brushes.Transparent
        Button.ForegroundProperty, muted
      |]
    )
  )

  // pane navigation rows: taller than content rows, same flat language
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Button>().Class("nav-item")),
      [|
        Button.BackgroundProperty, Brushes.Transparent
        Button.BorderThicknessProperty, Thickness(0.0)
        Button.CornerRadiusProperty, CornerRadius(4.0)
        Button.HeightProperty, 40.0
      |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("nav-item").Class(":pointerover")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  styles.Add(
    styleOf(
      (fun selector ->
        selector.OfType<Button>().Class("nav-item").Class(":pressed")),
      [| Button.BackgroundProperty, hoverFill |]
    )
  )

  // the card container: the Fluent content surface
  styles.Add(
    styleOf(
      (fun selector -> selector.OfType<Border>().Class("card")),
      [|
        Border.BackgroundProperty, cardFill
        Border.CornerRadiusProperty, CornerRadius(4.0)
        Border.PaddingProperty, Thickness(16.0)
      |]
    )
  )

  styles
