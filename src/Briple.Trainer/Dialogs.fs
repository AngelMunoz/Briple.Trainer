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
  /// Styles the accept button as a destructive action.
  Danger: bool
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
  let acceptButton =
    let button =
      Button(Content = request.Accept).OnClickHandler(fun _ _ -> answer true)

    if request.Danger then
      button.Classes.Add("danger")

    button

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
              TextBlock().Text(request.Title).Classes("subtitle"),
              TextBlock().Text(request.Body).TextWrapping(TextWrapping.Wrap),
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .HorizontalAlignment(HorizontalAlignment.Right)
                .Children(
                  Button(Content = request.Cancel)
                    .OnClickHandler(fun _ _ -> answer false),
                  acceptButton
                )
            )
        )
    )

/// Asks the user: true accepts, false cancels — or Escape.
let confirmAsync(request: ConfirmRequest) : Async<bool> =
  Async.FromContinuations(fun (onOk, _, _) ->
    pending.OnNext(ValueSome(request, onOk)))

/// The overlay layer the shell mounts last; it shows only while a
/// request is pending. The content is a binding, not a subscription.
/// Each host is a new control, so a test window can release it cleanly.
let host() : Control =
  ContentControl()
    .IsVisible(pending |> Observable.map ValueOption.isSome)
    .Content(
      (pending
       |> Observable.map(fun current ->
         match current with
         | ValueSome(request, _) -> dialogView request
         | ValueNone -> Grid()))
        .ToBinding()
    )
    .OnKeyDownHandler(
      (fun _ args ->
        match args.Key with
        | Key.Escape -> answer false
        | _ -> ()),
      RoutingStrategies.Tunnel
    )
