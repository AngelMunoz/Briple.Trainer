module Briple.Trainer.Dialogs

open System
open System.Reactive.Subjects

open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions

open Briple.Trainer.Theme

/// What a confirmation asks: title, body, the two answers.
[<Struct>]
type ConfirmRequest = {
  Title: string
  Body: string
  Accept: string
  Cancel: string
}

// the pending request and its reply; one at a time
let pending =
  new BehaviorSubject<(ConfirmRequest * (bool -> unit)) voption>(ValueNone)

let answer(value: bool) =
  match pending.Value with
  | ValueSome(_, reply) ->
    pending.OnNext ValueNone
    reply value
  | ValueNone -> ()

let cardBackground() : IBrush =
  match Application.Current with
  | null -> Brushes.White
  | app ->
    if app.ActualThemeVariant = ThemeVariant.Dark then
      SolidColorBrush(Color.FromRgb(32uy, 32uy, 32uy)) :> IBrush
    else
      Brushes.White

let dialogView(request: ConfirmRequest) : Control =
  Border()
    .Background(SolidColorBrush(Color.FromArgb(153uy, 0uy, 0uy, 0uy)))
    .Child(
      Border()
        .Background(cardBackground())
        .CornerRadius(8.0)
        .Padding(24.0)
        .MaxWidth(440.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .HorizontalAlignment(HorizontalAlignment.Center)
        .Child(
          StackPanel()
            .Spacing(16.0)
            .Children(
              TextBlock()
                .Text(request.Title)
                .FontSize(20.0)
                .FontWeight(FontWeight.SemiBold)
                .FontFamily(font),
              TextBlock()
                .Text(request.Body)
                .FontFamily(font)
                .TextWrapping(TextWrapping.Wrap),
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .HorizontalAlignment(HorizontalAlignment.Right)
                .Children(
                  Button(Content = request.Cancel, FontFamily = font)
                    .OnClickHandler(fun _ _ -> answer false),
                  Button(Content = request.Accept, FontFamily = font)
                    .OnClickHandler(fun _ _ -> answer true)
                )
            )
        )
    )

/// Asks the user: true accepts, false cancels — or Escape.
let inline confirmAsync(request: ConfirmRequest) : Async<bool> =
  Async.FromContinuations(fun (onOk, _, _) ->
    pending.OnNext(ValueSome(request, onOk)))

/// The overlay layer the shell mounts last; it shows only while a
/// request is pending.
let host: Control =
  let layer =
    ContentControl().IsVisible(pending |> Observable.map ValueOption.isSome)

  // NXUI's fluent Content would store the observable itself as the
  // content, so the swap is an explicit subscription
  pending
  |> Observable.subscribe(fun current ->
    layer.Content <-
      match current with
      | ValueSome(request, _) -> dialogView request
      | ValueNone -> Grid())
  |> ignore

  layer.OnKeyDownHandler(
    (fun _ args ->
      match args.Key with
      | Key.Escape -> answer false
      | _ -> ()),
    RoutingStrategies.Tunnel
  )
  |> ignore

  layer
