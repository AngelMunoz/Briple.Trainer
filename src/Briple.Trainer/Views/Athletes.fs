module Briple.Trainer.Views.Athletes

open System
open System.Globalization
open System.Threading.Tasks

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open FSharp.Control.Reactive
open MVUish.Avalonia
open MVUish.Avalonia.Collections

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.Theme

[<Struct>]
type AthleteDetailEnv = {
  Plans: PlanStore
  Archive: Guid -> Task<Result<unit, exn>>
  Restore: Guid -> Task<Result<unit, exn>>
  Delete: Guid -> Task<Result<unit, exn>>
  /// Deletes one plan and its plan-scoped memories.
  DeletePlan: Guid -> Task<Result<unit, exn>>
  Dialogs: IDialogs
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type DetailPageProps = {
  Lead: (unit -> Control) voption
  Athlete: AthleteEntity
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

[<Struct>]
type Event =
  | GenerateRequested
  | ImportRequested
  | ReviewRequested of planId: Guid
  | BackRequested

type Model = {
  Plans: PlanEntity list
  ArchivedPlans: PlanEntity list
  ShowArchived: bool
  Culture: CultureInfo
}

type Msg =
  | PlansChanged of PlanEntity list
  | ArchivedPlansChanged of PlanEntity list
  | CultureChanged of CultureInfo
  | GenerateRequested
  | ImportRequested
  | ReviewRequested of Guid
  | ShowArchivedToggled
  | ArchiveRequested
  | RestoreRequested
  | DeleteRequested
  | DeleteConfirmed of bool
  | ArchivePlanRequested of Guid
  | RestorePlanRequested of Guid
  | DeletePlanRequested of Guid
  | DeletePlanConfirmed of Guid * bool
  | ActionFinished of actionKey: string * result: Result<unit, exn>
  | PlanActionFinished of actionKey: string * result: Result<unit, exn>

[<Struct>]
type Context = {
  Env: AthleteDetailEnv
  Props: DetailPageProps
  OnEvent: Event -> unit
}

let init(ctx: Context) =
  let model = {
    Plans = ctx.Env.Plans.Snapshot(ctx.Props.Athlete.Id)
    ArchivedPlans = ctx.Env.Plans.SnapshotArchived(ctx.Props.Athlete.Id)
    ShowArchived = false
    Culture = ctx.Env.Culture
  }

  model, Cmd.none

let update (ctx: Context) (msg: Msg) (model: Model) =
  match msg with
  | PlansChanged plans -> { model with Plans = plans }, Cmd.none
  | ArchivedPlansChanged plans -> { model with ArchivedPlans = plans }, Cmd.none
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none
  | ShowArchivedToggled ->
    {
      model with
          ShowArchived = not model.ShowArchived
    },
    Cmd.none
  | GenerateRequested ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent Event.GenerateRequested)
  | ImportRequested ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent Event.ImportRequested)
  | ReviewRequested id ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent(Event.ReviewRequested id))
  | ArchiveRequested ->
    model,
    Cmd.OfTask.perform ctx.Env.Archive ctx.Props.Athlete.Id (fun result ->
      ActionFinished(Keys.athleteArchive, result))
  | RestoreRequested ->
    model,
    Cmd.OfTask.perform ctx.Env.Restore ctx.Props.Athlete.Id (fun result ->
      ActionFinished(Keys.athleteRestore, result))
  | DeleteRequested ->
    let culture = model.Culture

    let request: Briple.Trainer.Dialogs.ConfirmRequest = {
      Title = get(culture, Keys.athleteDeleteTitle)
      Body = get(culture, Keys.athleteDeleteBody)
      Accept = get(culture, Keys.athleteDelete)
      Cancel = get(culture, Keys.commonCancel)
      Danger = true
    }

    model,
    Cmd.OfAsync.perform ctx.Env.Dialogs.ConfirmAsync request DeleteConfirmed
  | DeleteConfirmed false -> model, Cmd.none
  | DeleteConfirmed true ->
    model,
    Cmd.OfTask.perform ctx.Env.Delete ctx.Props.Athlete.Id (fun result ->
      ActionFinished(Keys.athleteDelete, result))
  | ArchivePlanRequested id ->
    model,
    Cmd.OfTask.perform ctx.Env.Plans.Archive id (fun result ->
      PlanActionFinished(Keys.planArchive, result))
  | RestorePlanRequested id ->
    model,
    Cmd.OfTask.perform ctx.Env.Plans.Restore id (fun result ->
      PlanActionFinished(Keys.planRestore, result))
  | DeletePlanRequested id ->
    let culture = model.Culture

    let request: Briple.Trainer.Dialogs.ConfirmRequest = {
      Title = get(culture, Keys.planDeleteTitle)
      Body = get(culture, Keys.planDeleteBody)
      Accept = get(culture, Keys.planDelete)
      Cancel = get(culture, Keys.commonCancel)
      Danger = true
    }

    model,
    Cmd.OfAsync.perform ctx.Env.Dialogs.ConfirmAsync request (fun accepted ->
      DeletePlanConfirmed(id, accepted))
  | DeletePlanConfirmed(_, false) -> model, Cmd.none
  | DeletePlanConfirmed(id, true) ->
    model,
    Cmd.OfTask.perform ctx.Env.DeletePlan id (fun result ->
      PlanActionFinished(Keys.planDelete, result))
  | ActionFinished(actionKey, Ok()) ->
    let title = get(model.Culture, actionKey)

    model,
    Cmd.batch [
      Cmd.ofAction(fun () -> ctx.Env.Toasts.Show(title, "", false))
      Cmd.ofAction(fun () -> ctx.OnEvent Event.BackRequested)
    ]
  | ActionFinished(actionKey, Error e) ->
    let culture = model.Culture
    let title = get(culture, actionKey)

    model,
    Cmd.ofAction(fun () ->
      ctx.Env.Toasts.Show(title, friendly(culture, title, e.Message), true))
  | PlanActionFinished(actionKey, Ok()) ->
    let title = get(model.Culture, actionKey)
    model, Cmd.ofAction(fun () -> ctx.Env.Toasts.Show(title, "", false))
  | PlanActionFinished(actionKey, Error e) ->
    let culture = model.Culture
    let title = get(culture, actionKey)

    model,
    Cmd.ofAction(fun () ->
      ctx.Env.Toasts.Show(title, friendly(culture, title, e.Message), true))

let subscribe (ctx: Context) (_: Model) : Sub<Msg> =
  Sub.batch [
    Sub.fromObservable
      "plans"
      (ctx.Env.Plans.Observe(ctx.Props.Athlete.Id)
       |> Observable.map PlansChanged)
    Sub.fromObservable
      "archived-plans"
      (ctx.Env.Plans.ObserveArchived(ctx.Props.Athlete.Id)
       |> Observable.map ArchivedPlansChanged)
    Sub.fromObservable
      "culture"
      (ctx.Env.CultureChanged |> Observable.map CultureChanged)
  ]

/// The localized kind of a plan.
let planKindText(culture: CultureInfo, kind: string) : string =
  match kind with
  | "new" -> get(culture, Keys.plansKindNew)
  | "adjust" -> get(culture, Keys.plansKindAdjust)
  | "import" -> get(culture, Keys.plansKindImport)
  | _ -> kind

let frequencyKey(days: int) : string =
  match days with
  | 2 -> Keys.freq2
  | 3 -> Keys.freq3
  | 4 -> Keys.freq4
  | 5 -> Keys.freq5
  | 6 -> Keys.freq6
  | _ -> Keys.freq3

/// The plan variants through the existing frequency keys.
let planVariants(culture: CultureInfo, counts: (string * int) list) : string =
  counts
  |> List.choose(fun (variant, _) ->
    VariantFrequency.tryFileToken variant
    |> Option.map(fun frequency ->
      get(culture, frequencyKey(VariantFrequency.daysPerWeek frequency))))
  |> List.distinct
  |> String.concat ", "

/// The plan metadata line: kind, frequency and date.
let planDetail(culture: CultureInfo, plan: PlanEntity) : string =
  [
    planKindText(culture, plan.Kind)
    planVariants(culture, plan.Counts)
    plan.CreatedAt.ToLocalTime().ToString("g", culture)
  ]
  |> List.filter(fun part -> not(String.IsNullOrWhiteSpace part))
  |> String.concat " · "

/// The plan title, with a neutral archived badge.
let planTitle(culture: IObservable<CultureInfo>, plan: PlanEntity) : Control =
  let text =
    if String.IsNullOrWhiteSpace plan.Title then
      culture |> Observable.map(fun current -> get(current, Keys.plansTitle))
    else
      System.Reactive.Linq.Observable.Return plan.Title

  let title =
    TextBlock()
      .Text(text)
      .Classes("body-strong")
      .TextTrimming(TextTrimming.CharacterEllipsis)

  match plan.ArchivedAt with
  | ValueSome _ ->
    let archived =
      culture |> Observable.map(fun current -> get(current, Keys.planArchived))

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(title, badgeObs archived)
  | ValueNone -> title

[<Struct>]
type PlanRow = {
  Culture: IObservable<CultureInfo>
  Plan: PlanEntity
  OnOpen: Guid -> unit
  OnArchive: Guid -> unit
  OnRestore: Guid -> unit
  OnDelete: Guid -> unit
}

let planRow(row: PlanRow) : Control =
  let label key =
    row.Culture |> Observable.map(fun current -> get(current, key))

  let actions =
    match row.Plan.ArchivedAt with
    | ValueSome _ -> [
        {
          Glyph = Icons.unarchive
          Label = label Keys.planRestore
          Danger = false
          OnClick = fun () -> row.OnRestore row.Plan.Id
        }
        {
          Glyph = Icons.delete
          Label = label Keys.planDelete
          Danger = true
          OnClick = fun () -> row.OnDelete row.Plan.Id
        }
      ]
    | ValueNone -> [
        {
          Glyph = Icons.archive
          Label = label Keys.planArchive
          Danger = false
          OnClick = fun () -> row.OnArchive row.Plan.Id
        }
        {
          Glyph = Icons.delete
          Label = label Keys.planDelete
          Danger = true
          OnClick = fun () -> row.OnDelete row.Plan.Id
        }
      ]

  actionRow {
    Title = planTitle(row.Culture, row.Plan)
    Detail =
      row.Culture
      |> Observable.map(fun current -> planDetail(current, row.Plan))
    MoreLabel = label Keys.commonMore
    OnOpen = fun () -> row.OnOpen row.Plan.Id
    Actions = actions
  }

[<Struct>]
type PlanSectionContext = {
  Model: IObservable<Model>
  OnToggleShowArchived: unit -> unit
  OnOpen: Guid -> unit
  OnArchive: Guid -> unit
  OnRestore: Guid -> unit
  OnDelete: Guid -> unit
}

let planSection(context: PlanSectionContext) : Control =
  let culture = context.Model |> Observable.map _.Culture
  let active = context.Model |> Observable.map _.Plans
  let archived = context.Model |> Observable.map _.ArchivedPlans
  let showArchived = context.Model |> Observable.map _.ShowArchived

  let empty =
    context.Model
    |> Observable.map(fun current ->
      List.isEmpty current.Plans
      && (not current.ShowArchived || List.isEmpty current.ArchivedPlans))

  let rows(items: IObservable<PlanEntity list>) =
    let template =
      FuncDataTemplate<PlanEntity>(
        (fun plan _ ->
          planRow {
            Culture = culture
            Plan = plan
            OnOpen = context.OnOpen
            OnArchive = context.OnArchive
            OnRestore = context.OnRestore
            OnDelete = context.OnDelete
          }),
        false
      )

    ItemsControl().ItemTemplate(template)
    |> stretchItems
    |> fun list -> list.BindItemsSource(items, fun plan -> plan.Id)

  let activeRows = rows active

  let archivedRows = rows archived |> fun list -> list.IsVisible(showArchived)

  let showArchivedToggle =
    toggleCommand {
      Key = Keys.plansShowArchived
      Glyph = Icons.filter
      IsOn = showArchived
      OnToggle = context.OnToggleShowArchived
    }

  showArchivedToggle.IsVisible(
    archived |> Observable.map(fun plans -> not(List.isEmpty plans))
  )
  |> ignore

  let header =
    let grid =
      Grid()
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Margin(0.0, 0.0, 0.0, 4.0)

    grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")

    grid.Children(
      TextBlock()
        .Text(
          culture
          |> Observable.map(fun current -> get(current, Keys.plansTitle))
        )
        .Classes("subtitle")
        .VerticalAlignment(VerticalAlignment.Center)
        .Column(0),
      showArchivedToggle.VerticalAlignment(VerticalAlignment.Center).Column(1)
    )
    |> ignore

    grid

  let archivedHeading =
    TextBlock()
      .Text(
        culture
        |> Observable.map(fun current -> get(current, Keys.plansArchived))
      )
      .Classes("caption-strong", "secondary")
      .Margin(0.0, 8.0, 0.0, 0.0)
      .IsVisible(
        Observable.combineLatest showArchived archived
        |> Observable.map(fun (show, plans) -> show && not(List.isEmpty plans))
      )

  let emptyTitle =
    Observable.combineLatest culture archived
    |> Observable.map(fun (current, plans) ->
      get(
        current,
        if List.isEmpty plans then
          Keys.plansEmpty
        else
          Keys.plansEmptyActive
      ))

  let emptyHint =
    Observable.combineLatest culture archived
    |> Observable.map(fun (current, plans) ->
      get(
        current,
        if List.isEmpty plans then
          Keys.plansEmptyHint
        else
          Keys.plansEmptyActiveHint
      ))

  let emptyState =
    StackPanel()
      .Spacing(8.0)
      .Children(
        glyph(Icons.documentText, 32.0, muted),
        TextBlock().Text(emptyTitle).Classes("body-strong"),
        TextBlock()
          .Text(emptyHint)
          .Classes("caption", "secondary")
          .TextWrapping(TextWrapping.Wrap)
      )
      .IsVisible(empty)

  Border()
    .Classes("card")
    .Child(
      StackPanel()
        .Spacing(8.0)
        .Children(header, activeRows, archivedHeading, archivedRows, emptyState)
    )

let profileLine(culture: IObservable<CultureInfo>, athlete: Athlete) : Control =
  let goalText (current: CultureInfo) goal =
    match goal with
    | Hypertrophy -> get(current, Keys.goalHypertrophy)
    | WeightLoss -> get(current, Keys.goalWeightLoss)
    | CardioVascularHealth -> get(current, Keys.goalCardio)
    | BasicBodyActivation -> get(current, Keys.goalActivation)
    | Strength -> get(current, Keys.goalStrength)

  let genderName(current: CultureInfo) =
    match athlete.Gender with
    | Male -> get(current, Keys.genderMale)
    | Female -> get(current, Keys.genderFemale)

  let detail =
    culture
    |> Observable.map(fun current ->
      let goals =
        athlete.Goals |> List.map(goalText current) |> String.concat ", "

      $"{genderName current} · {goals}")

  TextBlock().Text(detail).Classes("caption", "secondary")

/// The page: static text comes from the resource dictionary; the profile
/// and plan metadata follow the model's culture.
let view
  (props: DetailPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let culture = model |> Observable.map _.Culture
  let archived = props.Athlete.ArchivedAt.IsSome

  let nameLine: Control =
    if archived then
      StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8.0)
        .VerticalAlignment(VerticalAlignment.Center)
        .Children(
          pageTitle props.Athlete.Profile.Name,
          badgeObs(
            culture
            |> Observable.map(fun current -> get(current, Keys.athleteArchived))
          )
        )
      :> Control
    else
      pageTitle props.Athlete.Profile.Name

  let title =
    StackPanel()
      .Spacing(2.0)
      .Children(nameLine, profileLine(culture, props.Athlete.Profile))

  let generateCommand =
    iconPrimaryButtonKey(
      Keys.plansGenerate,
      Icons.flash,
      (fun () -> dispatch GenerateRequested)
    )

  let importCommand =
    iconCommandKey(
      Keys.plansImport,
      Icons.upload,
      (fun () -> dispatch ImportRequested)
    )

  let archiveCommand =
    iconCommandKey(
      Keys.athleteArchive,
      Icons.archive,
      (fun () -> dispatch ArchiveRequested)
    )

  let restoreCommand =
    iconCommandKey(
      Keys.athleteRestore,
      Icons.unarchive,
      (fun () -> dispatch RestoreRequested)
    )

  let deleteCommand =
    iconCommandKey(
      Keys.athleteDelete,
      Icons.delete,
      (fun () -> dispatch DeleteRequested)
    )

  let commands: Control list =
    if archived then
      [ restoreCommand; deleteCommand ]
    else
      [ generateCommand; importCommand; archiveCommand; deleteCommand ]

  fluentPage {
    Lead = props.Lead
    Title = title
    Commands = commands
    Footer = noFooter
    Body =
      planSection {
        Model = model
        OnToggleShowArchived = fun () -> dispatch ShowArchivedToggled
        OnOpen = fun id -> dispatch(ReviewRequested id)
        OnArchive = fun id -> dispatch(ArchivePlanRequested id)
        OnRestore = fun id -> dispatch(RestorePlanRequested id)
        OnDelete = fun id -> dispatch(DeletePlanRequested id)
      }
  }

type AthleteDetailPage
  (
    env: AthleteDetailEnv,
    props: DetailPageProps,
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
        (fun (_: DetailPageProps) -> init ctx)
        (update ctx)
        (view props)
      |> Program.withSubscription(subscribe ctx)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
