module Briple.Trainer.UI.Tests.Leaks

// The lifetime guard: a page that leaves the route must release the store
// subscription it opened. Headless Avalonia keeps attached visuals through
// its top-level caches, so the deterministic boundary is the subscription,
// not a collected control.

open System
open System.Reactive.Subjects
open System.Threading.Tasks

open Avalonia.Controls
open Avalonia.VisualTree
open NXUI.Extensions

open Expecto
open MVUish.Avalonia

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
    uiTest "an athlete page releases its store subscription on leave"
    <| fun () -> task {
      let athlete = {
        Id = Guid.NewGuid()
        Profile = {
          Name = "Ana"
          Gender = Female
          Goals = [ Strength ]
        }
        ArchivedAt = ValueNone
      }

      let source = new BehaviorSubject<AthleteEntity list>([ athlete ])
      let _, culture = liveCulture es
      let lifetime = new TestLifetime()

      let! page =
        probeUi(fun () ->
          AthleteList(
            listEnv source culture,
            {
              Lead = ValueNone
              Lifetime = lifetime :> Navs.IDisposableBag
            },
            ignore
          ))

      let! window = mountWindowSized(page, 600.0, 400.0)

      let! rendered =
        waitFor(
          2000,
          (fun () -> page),
          fun root -> Option.isSome(rowButton(root, "Ana"))
        )

      Expect.isTrue rendered "the athlete row rendered before the leave"

      do!
        runUi(fun () ->
          window.Content <- null
          window.Close()
          (lifetime :> IDisposable).Dispose())

      let! stopped = waitFor(2000, (fun () -> source.HasObservers), not)

      Expect.isTrue stopped "the page left its store subscription behind"
    }

    uiTest "a disposed program completes the stream and freezes the model"
    <| fun () -> task {
      let harness = RecordingHarness<int, unit>()
      let block = TextBlock()

      let view model dispatch =
        block.Bind(TextBlock.TextProperty, Observable.map string model)
        |> ignore

        harness.View model dispatch |> ignore
        block

      let program =
        Program.mkSimple (fun () -> 0) (fun _ count -> count + 1) view

      let _, lifetime = Program.runWithLifecycle () program

      harness.Dispatch()

      let! raised = waitFor(2000, (fun () -> harness.Model), (=) 1)

      Expect.isTrue raised "the message reached the model"

      lifetime.Dispose()
      lifetime.Dispose()
      harness.Dispatch()
      do! Task.Delay 50

      Expect.isTrue harness.Completed "the model stream completed"

      Expect.equal
        harness.Model
        1
        "a message after dispose does not change the model"

      Expect.isTrue
        (String.IsNullOrEmpty block.Text)
        "the bound text reverted to its default"
    }
  ]
