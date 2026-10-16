module Briple.Trainer.Components.Fields

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open Briple.Trainer.Theme

let inline title(text: string) : Control =
  TextBlock()
    .Text(text)
    .FontFamily(font)
    .FontSize(22.0)
    .FontWeight(FontWeight.Bold)
    .Margin(0.0, 0.0, 0.0, 12.0)

let inline label(text: string) : Control =
  TextBlock().Text(text).FontFamily(font).FontSize(12.0).Foreground(muted)

let inline field
  (labelText: string, [<InlineIfLambda>] input: unit -> Control)
  : Control =
  StackPanel().Spacing(4.0).Margin(0.0, 8.0).Children(label labelText, input())

let inline button
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  Button()
    .Content(text)
    .FontFamily(font)
    .CornerRadius(0.0)
    .Padding(12.0, 6.0)
    .OnClickHandler(fun _ _ -> onClick())

let inline primaryButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Button =
  let flat = button(text, onClick)

  flat.Background(accent).Foreground(Brushes.White)

let inline chipButton
  (text: string, [<InlineIfLambda>] onClick: unit -> unit, isOn: bool)
  : Button =
  let flat = button(text, onClick)

  if isOn then
    flat.Background(accent).Foreground(Brushes.White)
  else
    flat.Background(chip).Foreground(Brushes.Black)

let inline choiceChips
  (
    options: string list,
    selected: string,
    [<InlineIfLambda>] onPick: string -> unit
  ) : Control =
  StackPanel().Orientation(Orientation.Horizontal).Spacing(6.0).Children [|
    for option in options ->
      chipButton(option, (fun () -> onPick option), (option = selected))
      :> Control
  |]

let inline multiChips
  (
    options: string list,
    selected: string list,
    [<InlineIfLambda>] onToggle: string -> unit
  ) : Control =
  StackPanel().Orientation(Orientation.Horizontal).Spacing(6.0).Children [|
    for option in options ->
      chipButton(
        option,
        (fun () -> onToggle option),
        (List.contains option selected)
      )
      :> Control
  |]

let inline reactiveChoiceChips
  (
    options: string list,
    selected: IObservable<string>,
    [<InlineIfLambda>] onPick: string -> unit
  ) : Control =
  let rows: Control array = [|
    for option in options ->
      let row = button(option, (fun () -> onPick option))

      row
        .Background(
          selected
          |> Observable.map(fun current ->
            if current = option then accent else chip)
        )
        .Foreground(
          selected
          |> Observable.map(fun current ->
            if current = option then
              Brushes.White :> IBrush
            else
              Brushes.Black :> IBrush)
        )
  |]

  StackPanel().Orientation(Orientation.Horizontal).Spacing(6.0).Children(rows)

let inline reactiveMultiChips
  (
    options: string list,
    selected: IObservable<string list>,
    [<InlineIfLambda>] onToggle: string -> unit
  ) : Control =
  let rows: Control array = [|
    for option in options ->
      let row = button(option, (fun () -> onToggle option))

      row
        .Background(
          selected
          |> Observable.map(fun current ->
            if List.contains option current then accent else chip)
        )
        .Foreground(
          selected
          |> Observable.map(fun current ->
            if List.contains option current then
              Brushes.White :> IBrush
            else
              Brushes.Black :> IBrush)
        )
  |]

  StackPanel().Orientation(Orientation.Horizontal).Spacing(6.0).Children(rows)

let inline entry
  (
    placeholder: string,
    text: string,
    [<InlineIfLambda>] onChanged: string -> unit
  ) : TextBox =
  TextBox()
    .FontFamily(font)
    .CornerRadius(0.0)
    .Padding(8.0, 4.0)
    .Text(text)
    .PlaceholderText(placeholder)
    .OnText(fun _ observed ->
      observed
      |> Observable.map(fun value ->
        value |> Option.ofObj |> Option.defaultValue "")
      |> Observable.subscribe onChanged
      |> ignore)

let inline page(titleText: string, content: Control) : Control =
  ScrollViewer()
    .Content(
      StackPanel().Margin(24.0).Spacing(8.0).Children(title titleText, content)
    )
