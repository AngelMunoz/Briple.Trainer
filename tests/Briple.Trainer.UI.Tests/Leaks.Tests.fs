module Briple.Trainer.UI.Tests.Leaks

// The lifetime guard: a page that leaves the route must release the store
// subscription it opened. Headless Avalonia keeps attached visuals through
// its top-level caches, so the deterministic boundary is the subscription,
// not a collected control.

open System
open System.Reactive.Subjects

open Avalonia.Controls
open Avalonia.VisualTree

open Expecto

open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.UI.Tests.Harness

let private es = Language.toCulture Es

let private listEnv
  (athletes: BehaviorSubject<AthleteEntity list>)
  (culture: PageCulture)
  : AthleteListEnv = {
  Observe = fun () -> athletes :> IObservable<_>
  Snapshot = fun () -> athletes.Value
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let private rowButton(root: Control, text: string) : Button option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Button as button when hasText(button, text) -> Some button
    | _ -> None)

let tests =
  testList "leaks" [
    testCase "an athlete page releases its store subscription on leave"
    <| fun _ ->
      let athlete = {
        Id = Guid.NewGuid()
        Profile = {
          Name = "Ana"
          Gender = Female
          Goals = [ Strength ]
        }
      }

      let source = new BehaviorSubject<AthleteEntity list>([ athlete ])
      let _, culture = liveCulture es
      let lifetime = new TestLifetime()

      let page =
        probeUi(fun () ->
          AthleteList(
            listEnv source culture,
            {
              Lead = ValueNone
              Lifetime = lifetime :> Navs.IDisposableBag
            },
            ignore
          ))

      let window =
        probeUi(fun () ->
          let window = Window(Content = page, Width = 600.0, Height = 400.0)
          window.Show() |> ignore
          window)

      let rendered =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> Option.isSome(rowButton(root, "Ana"))
        )

      Expect.isTrue rendered "the athlete row rendered before the leave"

      onUi(fun () ->
        window.Content <- null
        window.Close()
        (lifetime :> IDisposable).Dispose())

      settle()

      Expect.isFalse
        source.HasObservers
        "the page left its store subscription behind"
  ]
