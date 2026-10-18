module Briple.Trainer.Components.MemoryReview

open System
open System.Globalization

open Avalonia.Controls
open Avalonia.Layout
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type Event =
  | Confirmed of id: Guid * memory: Memory
  | Discarded of discardedId: Guid

[<Struct>]
type Props = {
  Culture: PageCulture
  Id: Guid
  Memory: Memory
}

type Model = {
  Selected: Delta list
  Scope: MemoryScope
}

type Msg =
  | Toggle of Delta
  | SetScope of MemoryScope
  | Accept
  | Discard

let targetName target =
  match target with
  | Exercise name -> name
  | Day id -> id

let labelText (culture: CultureInfo) (label: Label) =
  match label with
  | Light -> get(culture, Keys.labelLight)
  | Heavy -> get(culture, Keys.labelHeavy)
  | Pain -> get(culture, Keys.labelPain)
  | NoEquipment -> get(culture, Keys.labelNoEquipment)

/// The localized rendering of the origin: data in, prose out.
let originText (culture: CultureInfo) (memory: Memory) =
  match memory.Origin with
  | Mark mark ->
    getf(
      culture,
      Keys.markText,
      [
        targetName mark.Target :> obj
        labelText culture mark.Label :> obj
        mark.Date.ToString("d", culture) :> obj
      ]
    )
  | Exemption exemption ->
    getf(
      culture,
      Keys.exemptionText,
      [ exemption.Technique :> obj; targetName exemption.Target :> obj ]
    )

let inline withSign(change: int) =
  if change >= 0 then $"+{change}" else $"{change}"

let deltaText (culture: CultureInfo) (delta: Delta) =
  let rirLabel = get(culture, Keys.planRir)
  let repsLabel = get(culture, Keys.planReps)

  match delta with
  | Rir change -> $"{rirLabel} {withSign change}"
  | Reps change -> $"{repsLabel} {withSign change}"
  | Sets change -> $"sets {withSign change}"
  | Load percentage -> "load " + withSign percentage + "%"
  | PatternVariant name -> name
  | Duration minutes -> $"{withSign minutes} {get(culture, Keys.deltaDuration)}"
  | Zone change -> $"{get(culture, Keys.deltaZone)} {withSign change}"
  | Modality name -> $"{get(culture, Keys.deltaModality)} {name}"

let init(props: Props) =
  {
    Selected = props.Memory.Deltas
    Scope = props.Memory.Scope
  },
  Cmd.none

/// The update closes over `onEvent`: the confirm and discard decisions are
/// one-shot outputs, not model transitions.
let update (props: Props) (onEvent: Event -> unit) (msg: Msg) (model: Model) =
  match msg with
  | Toggle delta ->
    let selected =
      if List.contains delta model.Selected then
        List.except [ delta ] model.Selected
      else
        model.Selected @ [ delta ]

    { model with Selected = selected }, Cmd.none

  | SetScope scope -> { model with Scope = scope }, Cmd.none

  | Accept ->
    let memory = {
      props.Memory with
          Deltas = model.Selected
          Scope = model.Scope
    }

    onEvent(Event.Confirmed(props.Id, memory))
    model, Cmd.none

  | Discard ->
    onEvent(Event.Discarded props.Id)
    model, Cmd.none

type MemoryReview(props: Props, [<InlineIfLambda>] onEvent: Event -> unit) as this
  =
  inherit UserControl()

  let culture = props.Culture

  do

    let view (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      let optionRow(delta: Delta) : Control =
        let selected =
          model |> Observable.map(fun m -> List.contains delta m.Selected)

        let text =
          culture.Changed
          |> Observable.map(fun current -> deltaText current delta)

        checkRowObs(text, selected, fun () -> dispatch(Toggle delta))

      // how far the confirmed memory travels: this plan or the athlete
      let scopeRow =
        radioRowKeys(
          [ Plan, Keys.memoryScopePlan; Athlete, Keys.memoryScopeAthlete ],
          $"memory-scope-{props.Id}",
          props.Memory.Scope,
          SetScope >> dispatch
        )

      // the proposal rides a card: the Fluent content surface

      Border()
        .Margin(0.0, 0.0, 0.0, 8.0)
        .Child(
          StackPanel()
            .Spacing(6.0)
            .Children(
              TextBlock()
                .Text(
                  culture.Changed
                  |> Observable.map(fun current ->
                    originText current props.Memory)
                ),
              StackPanel().Spacing(4.0).Children [|
                for delta in props.Memory.Deltas -> optionRow delta
              |],
              scopeRow,
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(8.0)
                .Children(
                  iconButtonKey(
                    Keys.memoriesConfirm,
                    Icons.check,
                    fun () -> dispatch Accept
                  ),
                  iconButtonKey(
                    Keys.memoriesDiscard,
                    Icons.delete,
                    fun () -> dispatch Discard
                  )
                )
            )
        )
        .Classes("card")

    let program = Program.mkProgram init (update props onEvent) view

    this.Content <- Program.runWith props program
