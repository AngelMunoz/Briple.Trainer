module Briple.Trainer.Components.AthleteList

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
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type AthleteListEnv = {
  Observe: unit -> IObservable<AthleteEntity list>
  ObserveArchived: unit -> IObservable<AthleteEntity list>
  Snapshot: unit -> AthleteEntity list
  SnapshotArchived: unit -> AthleteEntity list
  Archive: Guid -> Task<Result<unit, exn>>
  Restore: Guid -> Task<Result<unit, exn>>
  Delete: Guid -> Task<Result<unit, exn>>
  Dialogs: IDialogs
  Toasts: IToasts
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

[<Struct>]
type AthleteListProps = {
  Lead: (unit -> Control) voption
  /// The route lifetime: the page program stops when the route leaves.
  Lifetime: Navs.IDisposableBag
}

[<Struct>]
type Event =
  | NewRequested
  | OpenRequested of Guid

type Model = {
  Athletes: AthleteEntity list
  Archived: AthleteEntity list
  ShowArchived: bool
  Culture: CultureInfo
}

type Msg =
  | AthletesChanged of AthleteEntity list
  | ArchivedChanged of AthleteEntity list
  | CultureChanged of CultureInfo
  | ShowArchivedToggled
  | NewRequested
  | OpenRequested of Guid
  | ArchiveRequested of Guid
  | RestoreRequested of Guid
  | DeleteRequested of Guid
  | DeleteConfirmed of Guid * bool
  | ActionFinished of actionKey: string * result: Result<unit, exn>

[<Struct>]
type Context = {
  Env: AthleteListEnv
  Props: AthleteListProps
  OnEvent: Event -> unit
}

[<Struct>]
type AthleteRow = {
  Culture: IObservable<CultureInfo>
  OnOpen: Guid -> unit
  OnArchive: Guid -> unit
  OnRestore: Guid -> unit
  OnDelete: Guid -> unit
  Athlete: AthleteEntity
}

let init(ctx: Context) =
  let model = {
    Athletes = ctx.Env.Snapshot()
    Archived = ctx.Env.SnapshotArchived()
    ShowArchived = false
    Culture = ctx.Env.Culture
  }

  model, Cmd.none

let update (ctx: Context) (msg: Msg) (model: Model) =
  match msg with
  | AthletesChanged athletes -> { model with Athletes = athletes }, Cmd.none
  | ArchivedChanged archived -> { model with Archived = archived }, Cmd.none
  | CultureChanged culture -> { model with Culture = culture }, Cmd.none
  | ShowArchivedToggled ->
    {
      model with
          ShowArchived = not model.ShowArchived
    },
    Cmd.none
  | NewRequested ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent Event.NewRequested)
  | OpenRequested id ->
    model, Cmd.ofAction(fun () -> ctx.OnEvent(Event.OpenRequested id))
  | ArchiveRequested id ->
    model,
    Cmd.OfTask.perform ctx.Env.Archive id (fun result ->
      ActionFinished(Keys.athleteArchive, result))
  | RestoreRequested id ->
    model,
    Cmd.OfTask.perform ctx.Env.Restore id (fun result ->
      ActionFinished(Keys.athleteRestore, result))
  | DeleteRequested id ->
    let culture = model.Culture

    let request: Briple.Trainer.Dialogs.ConfirmRequest = {
      Title = get(culture, Keys.athleteDeleteTitle)
      Body = get(culture, Keys.athleteDeleteBody)
      Accept = get(culture, Keys.athleteDelete)
      Cancel = get(culture, Keys.commonCancel)
      Danger = true
    }

    model,
    Cmd.OfAsync.perform ctx.Env.Dialogs.ConfirmAsync request (fun accepted ->
      DeleteConfirmed(id, accepted))
  | DeleteConfirmed(_, false) -> model, Cmd.none
  | DeleteConfirmed(id, true) ->
    model,
    Cmd.OfTask.perform ctx.Env.Delete id (fun result ->
      ActionFinished(Keys.athleteDelete, result))
  | ActionFinished(actionKey, Ok()) ->
    let title = get(model.Culture, actionKey)
    model, Cmd.ofAction(fun () -> ctx.Env.Toasts.Show(title, "", false))
  | ActionFinished(actionKey, Error e) ->
    let culture = model.Culture
    let title = get(culture, actionKey)

    model,
    Cmd.ofAction(fun () ->
      ctx.Env.Toasts.Show(title, friendly(culture, title, e.Message), true))

let subscribe (ctx: Context) (_: Model) : Sub<Msg> =
  Sub.batch [
    Sub.fromObservable
      "athletes"
      (ctx.Env.Observe() |> Observable.map AthletesChanged)
    Sub.fromObservable
      "archived-athletes"
      (ctx.Env.ObserveArchived() |> Observable.map ArchivedChanged)
    Sub.fromObservable
      "culture"
      (ctx.Env.CultureChanged |> Observable.map CultureChanged)
  ]

let goalName(culture: CultureInfo, goal: PlanGoal) : string =
  match goal with
  | Hypertrophy -> get(culture, Keys.goalHypertrophy)
  | WeightLoss -> get(culture, Keys.goalWeightLoss)
  | CardioVascularHealth -> get(culture, Keys.goalCardio)
  | BasicBodyActivation -> get(culture, Keys.goalActivation)
  | Strength -> get(culture, Keys.goalStrength)

let genderName(culture: CultureInfo, gender: Gender) : string =
  match gender with
  | Male -> get(culture, Keys.genderMale)
  | Female -> get(culture, Keys.genderFemale)

let summary(culture: CultureInfo, athlete: AthleteEntity) : string =
  [
    genderName(culture, athlete.Profile.Gender)
    athlete.Profile.Goals
    |> List.map(fun goal -> goalName(culture, goal))
    |> String.concat ", "
  ]
  |> String.concat " · "

/// The name and, for an archived row, the archived badge.
let athleteTitle
  (culture: IObservable<CultureInfo>, athlete: AthleteEntity)
  : Control =
  let name =
    TextBlock()
      .Text(athlete.Profile.Name)
      .Classes("body-strong")
      .TextTrimming(TextTrimming.CharacterEllipsis)

  match athlete.ArchivedAt with
  | ValueSome _ ->
    let archived =
      culture
      |> Observable.map(fun current -> get(current, Keys.athleteArchived))

    StackPanel()
      .Orientation(Orientation.Horizontal)
      .Spacing(8.0)
      .VerticalAlignment(VerticalAlignment.Center)
      .Children(name, badgeObs archived)
  | ValueNone -> name

/// One row: the name and the profile summary, with the actions for its
/// state. The summary follows the model's culture; the name is data.
let athleteRow(row: AthleteRow) : Control =
  let athlete = row.Athlete

  let detail =
    row.Culture |> Observable.map(fun current -> summary(current, athlete))

  let label key =
    row.Culture |> Observable.map(fun current -> get(current, key))

  let actions =
    match athlete.ArchivedAt with
    | ValueSome _ -> [
        {
          Glyph = Icons.unarchive
          Label = label Keys.athleteRestore
          Danger = false
          OnClick = fun () -> row.OnRestore athlete.Id
        }
        {
          Glyph = Icons.delete
          Label = label Keys.athleteDelete
          Danger = true
          OnClick = fun () -> row.OnDelete athlete.Id
        }
      ]
    | ValueNone -> [
        {
          Glyph = Icons.archive
          Label = label Keys.athleteArchive
          Danger = false
          OnClick = fun () -> row.OnArchive athlete.Id
        }
        {
          Glyph = Icons.delete
          Label = label Keys.athleteDelete
          Danger = true
          OnClick = fun () -> row.OnDelete athlete.Id
        }
      ]

  actionRow {
    Title = athleteTitle(row.Culture, athlete)
    Detail = detail
    MoreLabel = label Keys.commonMore
    OnOpen = fun () -> row.OnOpen athlete.Id
    Actions = actions
  }

/// The page: static text comes from the resource dictionary; the rows
/// follow the model's athlete lists and culture.
let view
  (props: AthleteListProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let culture = model |> Observable.map _.Culture
  let athletes = model |> Observable.map _.Athletes
  let archived = model |> Observable.map _.Archived
  let showArchived = model |> Observable.map _.ShowArchived
  let onOpen id = dispatch(OpenRequested id)
  let onArchive id = dispatch(ArchiveRequested id)
  let onRestore id = dispatch(RestoreRequested id)
  let onDelete id = dispatch(DeleteRequested id)

  let rows(items: IObservable<AthleteEntity list>) =
    let template =
      FuncDataTemplate<AthleteEntity>(
        (fun athlete _ ->
          athleteRow {
            Culture = culture
            OnOpen = onOpen
            OnArchive = onArchive
            OnRestore = onRestore
            OnDelete = onDelete
            Athlete = athlete
          }),
        false
      )

    ItemsControl().ItemTemplate(template)
    |> stretchItems
    |> fun list -> list.BindItemsSource(items, fun athlete -> athlete.Id)

  let activeRows = rows athletes

  let archivedRows = rows archived |> fun list -> list.IsVisible(showArchived)

  let showArchivedToggle =
    toggleCommand {
      Key = Keys.athletesShowArchived
      Glyph = Icons.filter
      IsOn = showArchived
      OnToggle = fun () -> dispatch ShowArchivedToggled
    }

  let emptyState =
    StackPanel()
      .Spacing(8.0)
      .Children(
        glyph(Icons.accountSearchOutline, 32.0, muted),
        labelKey Keys.athletesEmpty
      )
      .IsVisible(
        model
        |> Observable.map(fun current ->
          List.isEmpty current.Athletes
          && (not current.ShowArchived || List.isEmpty current.Archived))
      )

  let newCommand =
    iconPrimaryButtonKey(
      Keys.athletesNew,
      Icons.plus,
      (fun () -> dispatch NewRequested)
    )

  let commandRow =
    let grid =
      Grid()
        .HorizontalAlignment(HorizontalAlignment.Stretch)
        .Margin(0.0, 0.0, 0.0, 8.0)

    grid.ColumnDefinitions <- ColumnDefinitions("*,Auto")

    grid.Children(
      showArchivedToggle.HorizontalAlignment(HorizontalAlignment.Left).Column(0),
      newCommand.HorizontalAlignment(HorizontalAlignment.Right).Column(1)
    )
    |> ignore

    grid

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.athletesTitle
    Commands = []
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(8.0)
        .Children(commandRow, emptyState, activeRows, archivedRows)
  }

type AthleteList
  (
    env: AthleteListEnv,
    props: AthleteListProps,
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
        (fun (_: AthleteListProps) -> init ctx)
        (update ctx)
        (view props)
      |> Program.withSubscription(subscribe ctx)

    let root, lifetime = Program.runWithLifecycle props program

    this.Content <- root
    props.Lifetime.AddDisposable lifetime
