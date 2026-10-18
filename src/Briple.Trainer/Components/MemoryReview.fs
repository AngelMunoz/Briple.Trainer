module Briple.Trainer.Components.MemoryReview

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive

open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.PromptText
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type Event =
  | Confirmed of id: Guid * memory: Memory
  | Discarded of discardedId: Guid

type DeltaKind =
  | RirKind
  | RepsKind
  | SetsKind
  | LoadKind
  | VariantKind
  | DurationKind
  | ZoneKind
  | ModalityKind

[<Struct>]
type Props = {
  Culture: PageCulture
  Id: Guid
  Memory: Memory
}

type Model = {
  Selected: Delta list
  Added: Delta list
  Scope: MemoryScope
  Kind: DeltaKind
  Value: string
}

type Msg =
  | Toggle of Delta
  | SetScope of MemoryScope
  | SetKind of DeltaKind
  | SetValue of string
  | AddDelta
  | RemoveAdded of Delta
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
  | Sets change -> $"{get(culture, Keys.deltaSets)} {withSign change}"
  | Load percentage ->
    $"{get(culture, Keys.deltaLoad)} " + withSign percentage + "%"
  | PatternVariant name -> name
  | Duration minutes -> $"{withSign minutes} {get(culture, Keys.deltaDuration)}"
  | Zone change -> $"{get(culture, Keys.deltaZone)} {withSign change}"
  | Modality name -> $"{get(culture, Keys.deltaModality)} {name}"

let kindOf(delta: Delta) : DeltaKind =
  match delta with
  | Rir _ -> RirKind
  | Reps _ -> RepsKind
  | Sets _ -> SetsKind
  | Load _ -> LoadKind
  | PatternVariant _ -> VariantKind
  | Duration _ -> DurationKind
  | Zone _ -> ZoneKind
  | Modality _ -> ModalityKind

let kindOptions = [
  RirKind, Keys.planRir
  RepsKind, Keys.planReps
  SetsKind, Keys.deltaSets
  LoadKind, Keys.deltaLoad
  VariantKind, Keys.deltaVariant
  DurationKind, Keys.deltaDuration
  ZoneKind, Keys.deltaZone
  ModalityKind, Keys.deltaModality
]

/// The cardio modalities the plan already names. The catalog comes from
/// Core, so the card and the delta decoder accept the same values.
let modalityCatalog = modalities

let init(props: Props) =
  {
    Selected = props.Memory.Deltas
    Added = []
    Scope = props.Memory.Scope
    Kind = RirKind
    Value = ""
  },
  Cmd.none

let tryDelta(kind: DeltaKind, value: string) : Delta option =
  let text = value.Trim()

  if text = "" then
    None
  else
    match kind with
    | VariantKind ->
      let name = token 60 text

      if name = "" then None else Some(PatternVariant name)
    | ModalityKind ->
      let name = token 60 text

      if name = "" then None else Some(Modality name)
    | _ ->
      match Int32.TryParse text with
      | true, number ->
        match kind with
        | RirKind -> Some(Rir number)
        | RepsKind -> Some(Reps number)
        | SetsKind -> Some(Sets number)
        | LoadKind -> Some(Load number)
        | DurationKind -> Some(Duration number)
        | ZoneKind -> Some(Zone number)
        | VariantKind
        | ModalityKind -> None
      | _ -> None

let addDelta(kind: DeltaKind, delta: Delta, deltas: Delta list) =
  (deltas |> List.filter(fun existing -> kindOf existing <> kind)) @ [ delta ]

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

  | SetKind kind -> { model with Kind = kind; Value = "" }, Cmd.none

  | SetValue value -> { model with Value = value }, Cmd.none

  | AddDelta ->
    match tryDelta(model.Kind, model.Value) with
    | Some delta ->
      {
        model with
            Added = addDelta(model.Kind, delta, model.Added)
            Value = ""
      },
      Cmd.none
    | None -> model, Cmd.none

  | RemoveAdded delta ->
    {
      model with
          Added = List.except [ delta ] model.Added
    },
    Cmd.none

  | Accept ->
    let memory = {
      props.Memory with
          Deltas = model.Selected @ model.Added
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
      let initial, _ = init props

      let optionRow(delta: Delta) : Control =
        let selected =
          model |> Observable.map(fun m -> List.contains delta m.Selected)

        let text =
          culture.Changed
          |> Observable.map(fun current -> deltaText current delta)

        checkRowObs(text, selected, fun () -> dispatch(Toggle delta))

      // the deltas the trainer adds in this card
      let addedRows =
        ContentControl()
          .Content(
            (Observable.combineLatest model props.Culture.Changed
             |> Observable.map(fun (current, culture) ->
               let children: Control array = [|
                 for delta in current.Added ->
                   DockPanel()
                     .LastChildFill(false)
                     .Children(
                       TextBlock()
                         .Text(deltaText culture delta)
                         .Classes("body")
                         .DockLeft(),
                       iconButtonKey(
                         Keys.memoriesDiscard,
                         Icons.delete,
                         fun () -> dispatch(RemoveAdded delta)
                       )
                         .DockRight()
                     )
               |]

               StackPanel().Spacing(4.0).Children(children)))
              .ToBinding()
          )

      let kindPicker =
        let combo = ComboBox(MinWidth = 130.0)

        for _, key in kindOptions do
          combo.Items.Add(ComboBoxItem(Content = Localization.textBlock key))
          |> ignore

        combo.SelectedIndex <-
          kindOptions |> List.findIndex(fun (kind, _) -> kind = initial.Kind)

        combo.SelectionChanged.Add(fun _ ->
          let index = combo.SelectedIndex

          if index >= 0 then
            kindOptions
            |> List.tryItem index
            |> Option.iter(fun (kind, _) -> dispatch(SetKind kind)))

        combo

      let valueEntry =
        TextBox()
          .Width(140.0)
          .MaxLength(60)
          .Text(model |> Observable.map _.Value)
          .PlaceholderText(Localization.text Keys.memoryAddValue)
          .OnTextChangedHandler(fun args _ ->
            args.Text
            |> Option.ofObj
            |> Option.defaultValue ""
            |> SetValue
            |> dispatch)

      let modalityPicker() : Control =
        let combo = ComboBox(MinWidth = 150.0)

        for value in modalityCatalog do
          combo.Items.Add(ComboBoxItem(Content = value)) |> ignore

        combo.SelectedIndex <-
          modalityCatalog
          |> List.tryFindIndex(fun value -> value = initial.Value)
          |> Option.defaultValue -1

        combo.SelectionChanged.Add(fun _ ->
          let index = combo.SelectedIndex

          if index >= 0 then
            modalityCatalog
            |> List.tryItem index
            |> Option.iter(SetValue >> dispatch))

        combo

      let emptyControl: Control = StackPanel()

      let modalityHost =
        ContentControl()
          .Content(
            (model
             |> Observable.map _.Kind
             |> Observable.distinctUntilChanged
             |> Observable.map(fun kind ->
               if kind = ModalityKind then
                 modalityPicker()
               else
                 emptyControl))
              .ToBinding()
          )

      let add =
        let button =
          iconButtonKey(
            Keys.memoryAddAdjustment,
            Icons.plus,
            fun () -> dispatch AddDelta
          )

        button.IsEnabled(
          model
          |> Observable.map(fun current ->
            not(String.IsNullOrWhiteSpace current.Value))
        )
        |> ignore

        button

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
              addedRows,
              StackPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6.0)
                .Children(kindPicker, valueEntry, modalityHost, add),
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
