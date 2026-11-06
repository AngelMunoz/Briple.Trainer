module Briple.Trainer.Views.Import

open System
open System.Globalization
open System.IO
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform.Storage
open NXUI.Extensions

open FSharp.Control.Reactive
open IcedTasks
open Microsoft.Extensions.Logging
open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Slice
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type ImportEnv = {
  Plans: PlanStore
  Host: Window voption
  Logger: ILogger
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type ImportPageProps = {
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  Lifetime: Navs.IDisposableBag
}

type Event = Imported of planId: Guid

type Document =
  | NoFile
  | Unreadable of PlanFailure
  | Readable of ParsedPlan

// --- the page's model, pure over the format ----------------------------------
//
// The three helpers are the import flow without the UI: the signature
// file exports them so the page's behavior is testable without a file
// picker.

let documentOf(text: string) : Document =
  match Parse.parse text with
  | Ok parsed -> Readable parsed
  | Error failure -> Unreadable failure

let variantsFor(athlete: Athlete, plan: Plan) : Variant list =
  plan.Blocks
  |> List.tryFind(fun block -> block.Gender = athlete.Gender)
  |> Option.map _.Variants
  |> Option.defaultValue []

let draftOf
  (athlete: Athlete, variantId: string, plan: Plan)
  : PlanDraft option =
  variant(athlete.Gender, variantId, plan)
  |> Option.map(fun sliced -> {
    Kind = "import"
    Counts = planCounts athlete sliced
    CreatedAt = ValueNone
    Plan = sliced
  })

type Model = {
  Document: Document
  /// The chosen variant id, when the trainer picked one.
  Selected: string voption
  Importing: bool
  Culture: CultureInfo
}

type Msg =
  | PickRequested
  | FilePicked of Result<string option, exn>
  | VariantPicked of string
  | ImportRequested
  | ImportDone of Result<PlanEntity list, exn>
  | CultureChanged of CultureInfo

[<Struct>]
type Context = {
  Env: ImportEnv
  Props: ImportPageProps
  OnEvent: Event -> unit
}

let init(ctx: Context) =
  {
    Document = NoFile
    Selected = ValueNone
    Importing = false
    Culture = ctx.Env.Culture
  },
  Cmd.none

/// The open picker and the read. The picker starts on the UI thread; the
/// file read is small enough to finish there.
let pickAndRead(ctx: Context) : Async<Result<string option, exn>> = asyncEx {
  match ctx.Env.Host with
  | ValueNone -> return Error(exn "import without a host window")
  | ValueSome host ->
    try
      let culture = ctx.Env.Culture

      let fileType = FilePickerFileType(get(culture, Keys.importFileType))
      fileType.Patterns <- [| "*.briple"; "*.txt" |]

      let options =
        FilePickerOpenOptions(
          Title = get(culture, Keys.importPick),
          AllowMultiple = false
        )

      options.FileTypeFilter <- [| fileType |]

      let! picked = host.StorageProvider.OpenFilePickerAsync(options)

      match List.ofSeq picked with
      | [] -> return Ok None
      | file :: _ ->
        use! stream = file.OpenReadAsync()
        use reader = new StreamReader(stream)
        let! text = reader.ReadToEndAsync()
        return Ok(Some text)
    with e ->
      return Error e
}

let update (ctx: Context) (msg: Msg) (model: Model) =
  match msg with
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none

  | PickRequested when model.Importing -> model, Cmd.none
  | PickRequested -> model, Cmd.OfAsync.perform pickAndRead ctx FilePicked

  // the picker closed without a file: the document stays as it was
  | FilePicked(Ok None) -> model, Cmd.none

  | FilePicked(Ok(Some text)) ->
    let document = documentOf text

    let selected =
      match document with
      | Readable parsed ->
        variantsFor(ctx.Props.Athlete.Profile, parsed.Plan)
        |> List.tryHead
        |> Option.map _.Id
        |> ValueOption.ofOption
      | _ -> ValueNone

    {
      model with
          Document = document
          Selected = selected
    },
    Cmd.none

  | FilePicked(Error e) ->
    let culture = model.Culture
    let title = get(culture, Keys.importReadFailed)

    model,
    Cmd.batch [
      Cmd.ofAction(fun () ->
        ctx.Env.Logger.LogError(e, "plan file read failed"))
      Cmd.ofAction(fun () ->
        ctx.Env.Toasts.Show(title, friendly(culture, title, e.Message), true))
    ]

  | VariantPicked id -> { model with Selected = ValueSome id }, Cmd.none

  | ImportRequested when model.Importing -> model, Cmd.none

  | ImportRequested ->
    match model.Document, model.Selected with
    | Readable parsed, ValueSome variantId ->
      match draftOf(ctx.Props.Athlete.Profile, variantId, parsed.Plan) with
      | Some draft ->
        { model with Importing = true },
        Cmd.OfTask.perform
          ctx.Env.Plans.Import
          (ctx.Props.Athlete.Id, [ draft ])
          ImportDone
      | None -> model, Cmd.none
    | _ -> model, Cmd.none

  | ImportDone(Ok(entity :: _)) ->
    let culture = model.Culture

    { model with Importing = false },
    Cmd.batch [
      Cmd.ofAction(fun () ->
        ctx.Env.Toasts.Show(get(culture, Keys.importImported), "", false))
      Cmd.ofAction(fun () -> ctx.OnEvent(Imported entity.Id))
    ]

  // one draft went in; an empty answer cannot happen, and staying on
  // the page with the import off is the safe reaction to it
  | ImportDone(Ok []) -> { model with Importing = false }, Cmd.none

  | ImportDone(Error e) ->
    let culture = model.Culture
    let title = get(culture, Keys.importConfirm)

    { model with Importing = false },
    Cmd.batch [
      Cmd.ofAction(fun () -> ctx.Env.Logger.LogError(e, "plan import failed"))
      Cmd.ofAction(fun () ->
        ctx.Env.Toasts.Show(title, friendly(culture, title, e.Message), true))
    ]

let subscribe (ctx: Context) (_: Model) : Sub<Msg> =
  Sub.batch [
    Sub.fromObservable
      "culture"
      (ctx.Env.CultureChanged |> Observable.map CultureChanged)
  ]

// --- the view ----------------------------------------------------------------

/// The variant heading of a chooser row: the variant title, or the id.
let variantTitle(variant: Variant) =
  variant.Title |> Option.defaultValue variant.Id

/// The frequency label of a variant id through the frequency keys; an
/// unknown token shows itself.
let frequencyText(culture: CultureInfo, variantId: string) =
  match VariantFrequency.tryFileToken variantId with
  | Some frequency ->
    let days = VariantFrequency.daysPerWeek frequency

    let key =
      match days with
      | 2 -> Keys.freq2
      | 3 -> Keys.freq3
      | 4 -> Keys.freq4
      | 5 -> Keys.freq5
      | _ -> Keys.freq6

    get(culture, key)
  | None -> variantId

/// The row detail: frequency, weeks and exercise count of one variant.
let variantDetail(culture: CultureInfo, variant: Variant) =
  let weeks = variant.Weeks.Length

  let exercises =
    variant.Weeks
    |> List.sumBy(fun week ->
      week.Days |> List.sumBy(fun day -> day.Exercises.Length))

  [
    frequencyText(culture, variant.Id)
    getf(culture, Keys.importWeeksCount, [ weeks :> obj ])
    getf(culture, Keys.importExercises, [ exercises :> obj ])
  ]
  |> String.concat " · "

/// The two-line body of one chooser row.
let variantBody(culture: CultureInfo, variant: Variant) : Control =
  StackPanel()
    .Spacing(2.0)
    .Children(
      TextBlock()
        .Text(variantTitle variant)
        .Classes("body-strong")
        .TextWrapping(TextWrapping.Wrap),
      TextBlock()
        .Text(variantDetail(culture, variant))
        .Classes("caption", "secondary")
        .TextWrapping(TextWrapping.Wrap)
    )

let emptyView
  (props: ImportPageProps)
  (current: Model)
  (dispatch: Dispatch<Msg>)
  : Control =
  let pick =
    iconCommandKey(
      Keys.importPick,
      Icons.upload,
      (fun () -> dispatch PickRequested)
    )

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.plansImport
    Commands = [ pick ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          glyph(Icons.upload, 32.0, muted),
          TextBlock()
            .Text(get(current.Culture, Keys.importEmptyHint))
            .Classes("caption", "secondary")
            .TextWrapping(TextWrapping.Wrap)
        )
  }

let brokenView
  (props: ImportPageProps)
  (current: Model)
  (dispatch: Dispatch<Msg>)
  (failure: PlanFailure)
  : Control =
  let pick =
    iconCommandKey(
      Keys.importPick,
      Icons.upload,
      (fun () -> dispatch PickRequested)
    )

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.plansImport
    Commands = [ pick ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          glyph(Icons.alertCircle, 32.0, muted),
          TextBlock()
            .Text(get(current.Culture, Keys.importParseFailed))
            .Classes("body-strong")
            .TextWrapping(TextWrapping.Wrap),
          TextBlock()
            .Text(Parse.describe failure)
            .Classes("caption", "secondary")
            .TextWrapping(TextWrapping.Wrap)
        )
  }

let loadedView
  (props: ImportPageProps)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  (parsed: ParsedPlan)
  : Control =
  let culture = current.Culture

  let warnings =
    match parsed.Warnings with
    | [] -> None
    | warnings ->
      let lines: Control array = [|
        yield
          TextBlock()
            .Text(get(culture, Keys.importWarnings))
            .Classes("caption-strong", "secondary")
        for warning in warnings ->
          TextBlock()
            .Text($"L{warning.Line}: {warning.Message}")
            .Classes("caption", "secondary")
            .TextWrapping(TextWrapping.Wrap)
      |]

      Some(StackPanel().Spacing(4.0).Children lines: Control)

  let planEnv: PlanReview.PlanReviewEnv = {
    Culture = culture
    CultureChanged =
      model |> Observable.map _.Culture |> Observable.distinctUntilChanged
  }

  let preview =
    PlanReview.PlanReview(
      planEnv,
      {
        Parsed = parsed
        Week = ValueNone
        Editing = false
      },
      ignore
    )

  let variants = variantsFor(props.Athlete.Profile, parsed.Plan)

  let chooser: Control =
    match variants with
    | [] ->
      let gender =
        match props.Athlete.Profile.Gender with
        | Male -> get(culture, Keys.genderMale).ToLower culture
        | Female -> get(culture, Keys.genderFemale).ToLower culture

      StackPanel()
        .Spacing(4.0)
        .Children(
          glyph(Icons.alertCircle, 20.0, muted),
          TextBlock()
            .Text(getf(culture, Keys.importNone, [ gender :> obj ]))
            .Classes("caption", "secondary")
            .TextWrapping(TextWrapping.Wrap)
        )
    | variants ->
      let selected = model |> Observable.map _.Selected

      let rows: Control array = [|
        for variant in variants ->
          let isChecked =
            selected
            |> Observable.map(fun current ->
              let here = current = ValueSome variant.Id
              Nullable here)

          RadioButton(
            Content = variantBody(culture, variant),
            GroupName = "import-variants"
          )
            .IsChecked(isChecked)
            .OnClickHandler(fun _ _ -> dispatch(VariantPicked variant.Id))
      |]

      StackPanel().Spacing(8.0).Children rows

  let pick =
    iconCommandKey(
      Keys.importPick,
      Icons.upload,
      (fun () -> dispatch PickRequested)
    )

  let confirm =
    iconPrimaryButtonKey(
      Keys.importConfirm,
      Icons.upload,
      (fun () -> dispatch ImportRequested)
    )

  let canImport =
    model
    |> Observable.map(fun current ->
      not current.Importing
      && current.Selected.IsSome
      && (match current.Document with
          | Readable _ -> true
          | _ -> false))

  confirm.IsEnabled(canImport) |> ignore

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.plansImport
    Commands = [ pick ]
    Footer = {
      Secondary = []
      Primary = [ confirm ]
    }
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(
          [|
            match warnings with
            | Some panel -> panel
            | None -> ()

            sectionTitleKey Keys.importContents
            preview :> Control

            sectionTitleKey Keys.importChoose
            chooser
          |]
        )
  }

let documentView
  (props: ImportPageProps)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  match current.Document with
  | NoFile -> emptyView props current dispatch
  | Unreadable failure -> brokenView props current dispatch failure
  | Readable parsed -> loadedView props current model dispatch parsed

/// The document switches when the state or the culture changes; the
/// choice inside a document stays in the rows' own bindings.
let documentKey(current: Model) =
  let state =
    match current.Document with
    | NoFile -> 0
    | Unreadable _ -> 1
    | Readable _ -> 2

  ((state, current.Culture.Name), current)

let view
  (props: ImportPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  ContentControl()
    .Content(
      (model
       |> Observable.map documentKey
       |> Observable.distinctUntilChangedKey fst
       |> Observable.map(fun (_, current) ->
         documentView props current model dispatch))
        .ToBinding()
    )

type ImportPage
  (
    env: ImportEnv,
    props: ImportPageProps,
    [<InlineIfLambda>] onEvent: Event -> unit
  ) as this =
  inherit UserControl()

  do
    let ctx = {
      Env = env
      Props = props
      OnEvent = onEvent
    }

    let program =
      Program.mkProgram
        (fun (_: ImportPageProps) -> init ctx)
        (update ctx)
        (view props)
      |> Program.withSubscription(subscribe ctx)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
