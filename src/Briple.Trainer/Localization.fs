module Briple.Trainer.Localization

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open FSharp.Control.Reactive

open Briple.Trainer.Strings

// one dictionary, swapped in place: the resource observers see the
// change and update every bound control
let language = ResourceDictionary()

let install(culture: CultureInfo) : unit =
  match Application.Current with
  | null -> ()
  | app ->
    if not(app.Resources.MergedDictionaries.Contains language) then
      app.Resources.MergedDictionaries.Add language

    // update in place: no empty window where a key is missing, so a
    // concurrent reader never sees a raw key
    for key, value in Strings.all culture do
      language[key] <- value

let textFor(host: IResourceHost, key: string) : IObservable<string> =
  host.GetResourceObservable(key, Func<obj, obj>(fun value -> value))
  |> Observable.map(fun value ->
    match value with
    | :? string as text -> text
    | _ -> key)

let text(key: string) : IObservable<string> =
  match Application.Current with
  | null -> System.Reactive.Linq.Observable.Return key
  | app -> textFor(app, key)

let inline textBlock(key: string) : TextBlock = TextBlock().Text(text key)

let inline textBlockFor(host: IResourceHost, key: string) : TextBlock =
  TextBlock().Text(textFor(host, key))
