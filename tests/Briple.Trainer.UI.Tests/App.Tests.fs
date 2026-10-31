module Briple.Trainer.UI.Tests.App

// The UI checks: resx i18n, the athlete list view, the registration form
// as a props-in/events-out component, and the export write.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Threading.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.Media
open Avalonia.Styling
open Avalonia.VisualTree

open Expecto
open FSharp.Control.Reactive
open Navs.Avalonia

open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.NewAthleteForm
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Dialogs
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Harness

let private es = Language.toCulture Es

let private acceptingDialogs = {
  new IDialogs with
    member _.ConfirmAsync(_) = async { return true }
}

let private quietToasts = {
  new IToasts with
    member _.Show(_, _, _) = ()
}

let private rejectingDialogs = {
  new IDialogs with
    member _.ConfirmAsync(_) = async { return false }
}

let private recordingToasts() =
  let seen = ResizeArray<string * string * bool>()

  {
    new IToasts with
      member _.Show(title, body, isError) = seen.Add(title, body, isError)
  },
  seen

let private okAction: Task<Result<unit, exn>> = Task.FromResult(Ok())

let private athleteListEnvWith
  (athletes: BehaviorSubject<AthleteEntity list>)
  (archived: BehaviorSubject<AthleteEntity list>)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : AthleteListEnv = {
  Observe = fun () -> athletes :> IObservable<_>
  ObserveArchived = fun () -> archived :> IObservable<_>
  Snapshot = fun () -> athletes.Value
  SnapshotArchived = fun () -> archived.Value
  Archive = fun _ -> okAction
  Restore = fun _ -> okAction
  Delete = fun _ -> okAction
  Dialogs = acceptingDialogs
  Toasts = quietToasts
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let private athleteListEnv
  (athletes: BehaviorSubject<AthleteEntity list>)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : AthleteListEnv =
  athleteListEnvWith
    athletes
    (new BehaviorSubject<AthleteEntity list>([]))
    culture

let private moreButton(root: Control) : Button option =
  buttonWithTooltip(root, get(es, Keys.commonMore))

let private menuAction(root: Control, labelKey: string) : MenuItem option =
  moreButton root
  |> Option.bind(fun button -> menuItemWithText(button, get(es, labelKey)))

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let private testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

let private detailStores() : DataStores =
  let db =
    Path.Combine(Path.GetTempPath(), $"briple-detail-{Guid.NewGuid():N}.db")

  match Briple.Trainer.Persistence.migrate(testLogger, db) with
  | Ok() ->
    Briple.Trainer.Stores.createStores(
      Briple.Trainer.Persistence.sqliteStore db,
      ignore
    )
  | Error e -> failtest $"detail store migration failed: {e}"

let private planStore() = (detailStores()).Plans

let private detailEnv
  (plans: PlanStore)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Views.Athletes.AthleteDetailEnv = {
  Plans = plans
  Archive = fun _ -> okAction
  Restore = fun _ -> okAction
  Delete = fun _ -> okAction
  DeletePlan = fun _ -> okAction
  Dialogs = acceptingDialogs
  Toasts = quietToasts
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

[<Struct>]
type private RowMeasure = {
  Left: float
  OpenRight: float
  ActionRight: float
  ActionTop: float
  ActionHeight: float
  RowTop: float
  RowHeight: float
}

// the route table only needs a back-control factory; the tests render a
// plain placeholder in place of the shell's button
let private stubLeadOf(_: Navs.INavigable) = fun () -> TextBlock() :> Control

let private textBoxes(root: Control) : TextBox list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? TextBox as textBox -> Some textBox
    | _ -> None)
  |> List.ofSeq

let private buttons(root: Control) : Button list =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? Button as button -> Some button
    | _ -> None)
  |> List.ofSeq

let private radioWithText(root: Control, text: string) : RadioButton option =
  root.GetVisualDescendants()
  |> Seq.choose(fun visual ->
    match visual with
    | :? RadioButton as radio when hasText(radio, text) -> Some radio
    | _ -> None)
  |> Seq.tryHead

let private controlByName(root: Control, name: string) : Control option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? Control as control when control.Name = name -> Some control
    | _ -> None)

let private textBlockWith(root: Control, text: string) : TextBlock option =
  root.GetVisualDescendants()
  |> Seq.tryPick(fun visual ->
    match visual with
    | :? TextBlock as block when block.Text = text -> Some block
    | _ -> None)

let private textBlockContaining(root: Control, text: string) : bool =
  root.GetVisualDescendants()
  |> Seq.exists(fun visual ->
    match visual with
    | :? TextBlock as block ->
      block.Text
      |> Option.ofObj
      |> Option.exists(fun value -> value.Contains text)
    | _ -> false)

let private visibleButtonWithText(root: Control, text: string) : Button option =
  buttonWithText(root, text)
  |> Option.filter(fun button -> button.IsEffectivelyVisible)

/// Mounts the dialog overlay, asks one confirmation and answers through
/// the buttons; the tuple is the reply cell, the overlay and its window.
let private askDialog() = task {
  let! overlay = probeUi(fun () -> Briple.Trainer.Dialogs.host())

  let! window = mountWindow overlay

  let replied = ref None

  do!
    runUi(fun () ->
      Async.StartWithContinuations(
        Briple.Trainer.Dialogs.confirmAsync(
          {
            Title = "¿Descartar?"
            Body = "Se pierde el plan"
            Accept = "Descartar"
            Cancel = "Cancelar"
            Danger = false
          }
        ),
        (fun value -> replied.Value <- Some value),
        ignore,
        ignore
      ))

  return replied, overlay, window
}

type private LoginEnv() =
  interface Briple.Trainer.Env.IProvideCredentials with
    member _.TokenInsecure = false

    member _.SaveCredentials(_) =
      Ok Briple.Trainer.Env.CredentialSave.Secured

  interface Briple.Trainer.Env.IProvideCulture with
    member _.Culture = es
    member _.CultureChanged = Observable.empty

let tests =
  testList "app" [
    testList "resx i18n" [
      test "the neutral resource is spanish" {
        Expect.equal (get(es, Keys.athletesTitle)) "Atletas" "neutral es"
      }

      test "the en satellite overrides" {
        let en = Language.toCulture En

        Expect.equal (get(en, Keys.athletesTitle)) "Athletes" "en satellite"
      }

      test "the athlete action strings exist in both languages" {
        let en = Language.toCulture En

        Expect.equal (get(es, Keys.athleteArchive)) "Archivar" "es archive"
        Expect.equal (get(en, Keys.athleteArchive)) "Archive" "en archive"

        Expect.equal
          (get(es, Keys.athletesShowArchived))
          "Mostrar archivados"
          "es filter"

        Expect.equal
          (get(en, Keys.athletesShowArchived))
          "Show archived"
          "en filter"
      }

      test "an unknown culture falls back to the neutral" {
        let fr = CultureInfo "fr-FR"

        Expect.equal (get(fr, Keys.athletesTitle)) "Atletas" "fr falls back"
      }
    ]

    testList "localization resources" [
      uiTest "a resource dictionary updates static text without a rebuild"
      <| fun () -> task {
        // a local host: the test never mutates the app dictionary that
        // the other parallel tests read
        let! host =
          probeUi(fun () ->
            let host = Border()
            host.Resources["athletes.title"] <- "Atletas"
            host)

        let! text =
          probeUi(fun () ->
            Briple.Trainer.Localization.textBlockFor(host, Keys.athletesTitle))

        do! mount text

        let! spanish =
          waitFor(2000, (fun () -> text), fun block -> block.Text = "Atletas")

        Expect.isTrue spanish "the initial resource dictionary"

        do! runUi(fun () -> host.Resources["athletes.title"] <- "Athletes")

        let! english =
          waitFor(2000, (fun () -> text), fun block -> block.Text = "Athletes")

        Expect.isTrue english "the swapped dictionary updated the bound text"
      }
    ]

    testList "theme" [
      uiTest "theme text follows the applied variant"
      <| fun () -> task {
        let! block =
          probeUi(fun () ->
            let block = TextBlock(Text = "Tema")
            block.Classes.Add("body")
            block)

        do! mount block

        let color() =
          probeUi(fun () ->
            match block.Foreground with
            | :? SolidColorBrush as brush -> brush.Color
            | _ -> failwith "the text has no solid foreground")

        try
          do!
            runUi(fun () ->
              Briple.Trainer.Theme.applyVariant ThemeVariant.Light)

          let! light = color()
          Expect.equal light (Color.Parse "#1A1A1A") "light text"

          do!
            runUi(fun () -> Briple.Trainer.Theme.applyVariant ThemeVariant.Dark)

          let! dark = color()
          Expect.equal dark (Color.Parse "#FFFFFF") "dark text"
        with error ->
          do!
            runUi(fun () ->
              Briple.Trainer.Theme.applyVariant ThemeVariant.Light)

          return raise error

        do!
          runUi(fun () -> Briple.Trainer.Theme.applyVariant ThemeVariant.Light)
      }

      uiTest "the danger button uses the error fill"
      <| fun () -> task {
        let! button =
          probeUi(fun () ->
            let button = Button(Content = "Eliminar")
            button.Classes.Add("danger")
            button)

        do! mount button

        let! color =
          probeUi(fun () ->
            match button.Background with
            | :? SolidColorBrush as brush -> brush.Color
            | _ -> failwith "the danger button has no solid background")

        Expect.equal color (Color.Parse "#C0392B") "the danger fill"
      }
    ]

    testList "athlete list" [
      uiTest "the empty page renders the empty-state text"
      <| fun () -> task {
        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv
                (new BehaviorSubject<AthleteEntity list>([]))
                culture,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> hasText(c, "Sin atletas todavía. Registra el primero."))
          )

        Expect.isTrue rendered "the empty state did not render"
      }

      uiTest "a registered athlete renders as a row and opens"
      <| fun () -> task {
        let opened = ref None

        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv
                (new BehaviorSubject<AthleteEntity list>([ entity ]))
                culture,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              fun event ->
                match event with
                | OpenRequested id -> opened.Value <- Some id
                | NewRequested -> ()
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> buttonWithText(c, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        let! row = probeUi(fun () -> buttonWithText(control, "Ana"))

        let row =
          row |> Option.defaultWith(fun () -> failtest "the row vanished")

        do! runUi(fun () -> click row)

        let! openedOk = waitFor(2000, (fun () -> opened.Value), Option.isSome)

        Expect.isTrue openedOk "the open event was dispatched"

        Expect.equal
          opened.Value
          (Some entity.Id)
          "the open event carries the id"
      }

      uiTest "archived rows stay hidden until the toggle shows them"
      <| fun () -> task {
        let active = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let archived = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Beto"
            Gender = Male
            Goals = [ Hypertrophy ]
          }
          ArchivedAt = ValueSome DateTime.UtcNow
        }

        let current = new BehaviorSubject<AthleteEntity list>([ active ])
        let older = new BehaviorSubject<AthleteEntity list>([ archived ])
        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnvWith current older culture,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root -> buttonWithText(root, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the active row did not render"

        let! hidden =
          probeUi(fun () ->
            match buttonWithText(control, "Beto") with
            | Some row -> not row.IsEffectivelyVisible
            | None -> true)

        Expect.isTrue hidden "the archived row is hidden by default"

        let! toggle =
          probeUi(fun () ->
            buttonWithText(control, get(es, Keys.athletesShowArchived)))

        let toggle =
          toggle
          |> Option.defaultWith(fun () ->
            failtest "the show-archived toggle did not render")

        do! runUi(fun () -> click toggle)

        let! shown =
          waitFor(
            2000,
            (fun () -> buttonWithText(control, "Beto")),
            fun row ->
              row |> Option.exists(fun button -> button.IsEffectivelyVisible)
          )

        Expect.isTrue shown "the toggle did not show the archived row"

        let! badge =
          waitFor(
            2000,
            (fun () -> control),
            fun root -> hasText(root, get(es, Keys.athleteArchived))
          )

        Expect.isTrue badge "the archived row has no badge"
      }

      uiTest "archiving and restoring move the row between the lists"
      <| fun () -> task {
        let db =
          Path.Combine(Path.GetTempPath(), $"briple-list-{Guid.NewGuid():N}.db")

        match Briple.Trainer.Persistence.migrate(testLogger, db) with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        let stores = Briple.Trainer.Stores.createStores(sqliteStore db, ignore)

        let! athleteResult =
          stores.Athletes.Add {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }

        let athlete =
          athleteResult
          |> Result.defaultWith(fun e -> failtest $"athlete add failed: {e}")

        let _, culture = liveCulture es

        let env: AthleteListEnv = {
          Observe = stores.Athletes.Observe
          ObserveArchived = stores.Athletes.ObserveArchived
          Snapshot = stores.Athletes.Snapshot
          SnapshotArchived = stores.Athletes.SnapshotArchived
          Archive = stores.Athletes.Archive
          Restore = stores.Athletes.Restore
          Delete = stores.Athletes.Delete
          Dialogs = acceptingDialogs
          Toasts = quietToasts
          Culture = culture.Current()
          CultureChanged = culture.Changed
        }

        let! control =
          probeUi(fun () ->
            AthleteList(
              env,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root ->
              buttonWithText(root, "Ana")
              |> Option.exists(fun button -> button.IsEffectivelyVisible))
          )

        Expect.isTrue rendered "the active row did not render"

        let! archive =
          probeUi(fun () -> menuAction(control, Keys.athleteArchive))

        let archive =
          archive
          |> Option.defaultWith(fun () ->
            failtest "the archive menu item did not render")

        do! runUi(fun () -> clickMenuItem archive)

        let! moved =
          waitFor(
            2000,
            (fun () -> stores.Athletes.SnapshotArchived()),
            fun athletes ->
              athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
          )

        Expect.isTrue moved "the athlete did not move to the archived list"

        let! toggle =
          probeUi(fun () ->
            buttonWithText(control, get(es, Keys.athletesShowArchived)))

        let toggle =
          toggle
          |> Option.defaultWith(fun () ->
            failtest "the show-archived toggle did not render")

        do! runUi(fun () -> click toggle)

        let! archivedRow =
          waitFor(
            2000,
            (fun () -> buttonWithText(control, "Ana")),
            fun row ->
              row |> Option.exists(fun button -> button.IsEffectivelyVisible)
              && (menuAction(control, Keys.athleteRestore) |> Option.isSome)
          )

        Expect.isTrue archivedRow "the archived row did not appear"

        let! restore =
          probeUi(fun () -> menuAction(control, Keys.athleteRestore))

        let restore =
          restore
          |> Option.defaultWith(fun () ->
            failtest "the restore menu item did not render")

        do! runUi(fun () -> clickMenuItem restore)

        let! restored =
          waitFor(
            2000,
            (fun () -> stores.Athletes.Snapshot()),
            fun athletes ->
              athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
          )

        Expect.isTrue restored "the athlete did not return to the active list"
      }

      uiTest "an action click does not open the row"
      <| fun () -> task {
        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let current = new BehaviorSubject<AthleteEntity list>([ entity ])
        let archived = new BehaviorSubject<AthleteEntity list>([])
        let calls = ResizeArray<Guid>()
        let opened = ref None
        let _, culture = liveCulture es

        let env = {
          athleteListEnvWith current archived culture with
              Archive =
                fun id ->
                  calls.Add id
                  okAction
        }

        let! control =
          probeUi(fun () ->
            AthleteList(
              env,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              fun event ->
                match event with
                | OpenRequested id -> opened.Value <- Some id
                | NewRequested -> ()
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root -> buttonWithText(root, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        let! action =
          probeUi(fun () -> menuAction(control, Keys.athleteArchive))

        let action =
          action
          |> Option.defaultWith(fun () ->
            failtest "the archive menu item did not render")

        do! runUi(fun () -> clickMenuItem action)

        let! archivedCall =
          waitFor(2000, (fun () -> calls.Count), fun count -> count > 0)

        Expect.isTrue archivedCall "the archive action did not reach the store"
        Expect.equal calls[0] entity.Id "the archive action carries the id"
        Expect.isTrue opened.Value.IsNone "the action opened the row"
      }

      uiTest "delete asks before it removes the athlete"
      <| fun () -> task {
        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let current = new BehaviorSubject<AthleteEntity list>([ entity ])
        let archived = new BehaviorSubject<AthleteEntity list>([])
        let deleted = ResizeArray<Guid>()
        let _, culture = liveCulture es

        let env = {
          athleteListEnvWith current archived culture with
              Delete =
                fun id ->
                  deleted.Add id
                  okAction
        }

        let! control =
          probeUi(fun () ->
            AthleteList(
              env,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root -> buttonWithText(root, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        let! action = probeUi(fun () -> menuAction(control, Keys.athleteDelete))

        let action =
          action
          |> Option.defaultWith(fun () ->
            failtest "the delete menu item did not render")

        do! runUi(fun () -> clickMenuItem action)

        let! deletedCall =
          waitFor(2000, (fun () -> deleted.Count), fun count -> count > 0)

        Expect.isTrue deletedCall "the confirmed delete did not reach the store"
        Expect.equal deleted[0] entity.Id "the delete action carries the id"
      }

      uiTest "cancelling delete keeps the athlete"
      <| fun () -> task {
        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let current = new BehaviorSubject<AthleteEntity list>([ entity ])
        let archived = new BehaviorSubject<AthleteEntity list>([])
        let deleted = ResizeArray<Guid>()
        let _, culture = liveCulture es

        let env = {
          athleteListEnvWith current archived culture with
              Dialogs = rejectingDialogs
              Delete =
                fun id ->
                  deleted.Add id
                  okAction
        }

        let! control =
          probeUi(fun () ->
            AthleteList(
              env,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root -> buttonWithText(root, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        let! action = probeUi(fun () -> menuAction(control, Keys.athleteDelete))

        let action =
          action
          |> Option.defaultWith(fun () ->
            failtest "the delete menu item did not render")

        do! runUi(fun () -> clickMenuItem action)
        do! Task.Delay 100

        Expect.isEmpty deleted "cancel must not delete the athlete"

        let! stillThere =
          probeUi(fun () -> buttonWithText(control, "Ana") |> Option.isSome)

        Expect.isTrue stillThere "cancel removed the athlete row"
      }

      uiTest "a failed action shows an error toast and keeps the row"
      <| fun () -> task {
        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let current = new BehaviorSubject<AthleteEntity list>([ entity ])
        let archived = new BehaviorSubject<AthleteEntity list>([])
        let toasts, seen = recordingToasts()
        let _, culture = liveCulture es

        let env = {
          athleteListEnvWith current archived culture with
              Archive = fun _ -> Task.FromResult(Error(System.Exception "boom"))
              Toasts = toasts
        }

        let! control =
          probeUi(fun () ->
            AthleteList(
              env,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun root -> buttonWithText(root, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        let! action =
          probeUi(fun () -> menuAction(control, Keys.athleteArchive))

        let action =
          action
          |> Option.defaultWith(fun () ->
            failtest "the archive menu item did not render")

        do! runUi(fun () -> clickMenuItem action)

        let! toast =
          waitFor(2000, (fun () -> seen.Count), fun count -> count > 0)

        Expect.isTrue toast "the failed action did not show a toast"

        let title, _, isError = seen[0]

        Expect.equal
          title
          (get(es, Keys.athleteArchive))
          "the toast names the action"

        Expect.isTrue isError "the toast is an error"

        let! stillThere =
          probeUi(fun () -> buttonWithText(control, "Ana") |> Option.isSome)

        Expect.isTrue stillThere "the failed action removed the row"
      }

      uiTest "rows of different lengths share one action edge"
      <| fun () -> task {
        let entity name goals = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = goals
          }
          ArchivedAt = ValueNone
        }

        let ana = entity "Ana" [ Strength ]

        let beto =
          entity "Beto" [
            Hypertrophy
            WeightLoss
            CardioVascularHealth
            BasicBodyActivation
            Strength
          ]

        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv
                (new BehaviorSubject<AthleteEntity list>([ ana; beto ]))
                culture,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        let! window = mountWindowSized(control, 1000.0, 700.0)

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            fun root ->
              buttonWithText(root, "Ana") |> Option.isSome
              && buttonWithText(root, "Beto") |> Option.isSome
          )

        Expect.isTrue rendered "the rows did not render"

        let measure name =
          probeUi(fun () ->
            let openRow =
              buttonWithText(control, name)
              |> Option.defaultWith(fun () ->
                failtest $"the {name} row vanished")

            let row =
              match openRow.Parent with
              | :? Control as parent -> parent
              | _ -> failtest $"the {name} row grid vanished"

            let more =
              buttonWithTooltip(row, get(es, Keys.commonMore))
              |> Option.defaultWith(fun () ->
                failtest $"the {name} overflow button vanished")

            let left = openRow.TranslatePoint(Point(0.0, 0.0), window)
            let actionRight = more.TranslatePoint(Point(0.0, 0.0), window)

            {
              Left = left.Value.X
              OpenRight = left.Value.X + openRow.Bounds.Width
              ActionRight = actionRight.Value.X + more.Bounds.Width
              ActionTop = actionRight.Value.Y
              ActionHeight = more.Bounds.Height
              RowTop = left.Value.Y
              RowHeight = openRow.Bounds.Height
            })

        let! ana = measure "Ana"
        let! beto = measure "Beto"

        Expect.equal ana.Left beto.Left "both rows share the left edge"

        Expect.isLessThan ana.Left 100.0 "the rows start at the page margin"

        Expect.equal
          ana.OpenRight
          beto.OpenRight
          "both open buttons share the right edge"

        Expect.equal
          ana.ActionRight
          beto.ActionRight
          "a longer summary must not push the actions"

        Expect.isGreaterThan
          ana.ActionRight
          ana.OpenRight
          "the actions sit after the open button"

        Expect.isGreaterThan
          ana.ActionRight
          900.0
          "the actions sit at the row's trailing edge"

        let anaActionCenter = ana.ActionTop + ana.ActionHeight / 2.0
        let anaRowCenter = ana.RowTop + ana.RowHeight / 2.0

        Expect.isLessThan
          (abs(anaActionCenter - anaRowCenter))
          1.5
          "the delete action centers on the row's full height"

        let betoActionCenter = beto.ActionTop + beto.ActionHeight / 2.0
        let betoRowCenter = beto.RowTop + beto.RowHeight / 2.0

        Expect.isLessThan
          (abs(betoActionCenter - betoRowCenter))
          1.5
          "the longer row centers its actions too"
      }

      uiTest "the page fills a wide window to its edge"
      <| fun () -> task {
        let entity name = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let ana = entity "Ana"
        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv
                (new BehaviorSubject<AthleteEntity list>([ ana ]))
                culture,
              {
                Lead = ValueNone
                Lifetime = testLifetime()
              },
              ignore
            ))

        let! window = mountWindowSized(control, 1600.0, 800.0)

        let! rendered =
          waitFor(
            2000,
            (fun () -> control),
            fun root -> buttonWithText(root, "Ana") |> Option.isSome
          )

        Expect.isTrue rendered "the row did not render"

        let! right =
          probeUi(fun () ->
            let action =
              buttonWithTooltip(control, get(es, Keys.commonMore))
              |> Option.defaultWith(fun () ->
                failtest "the row overflow button vanished")

            let left = action.TranslatePoint(Point(0.0, 0.0), window)

            left.Value.X + action.Bounds.Width)

        Expect.isGreaterThan
          right
          1500.0
          "the page has no width cap: the row reaches the window edge"
      }

      uiTest "the page lead sits before the title"
      <| fun () -> task {
        let _, culture = liveCulture es

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv
                (new BehaviorSubject<AthleteEntity list>([]))
                culture,
              {
                Lead = ValueSome(fun () -> TextBlock(Text = "back") :> Control)
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount control

        let! header =
          waitFor(
            2000,
            (fun () -> control),
            fun current ->
              let texts =
                current.GetVisualDescendants()
                |> Seq.choose(fun visual ->
                  match visual with
                  | :? TextBlock as text -> text.Text |> Option.ofObj
                  | _ -> None)
                |> Seq.toList

              List.contains "back" texts && List.contains "Atletas" texts
          )

        Expect.isTrue header "the lead and the title rendered"

        let! lead, title =
          probeUi(fun () ->
            let blocks =
              control.GetVisualDescendants()
              |> Seq.choose(fun visual ->
                match visual with
                | :? TextBlock as text -> Some text
                | _ -> None)
              |> Seq.toList

            blocks |> List.tryFind(fun text -> text.Text = "back"),
            blocks |> List.tryFind(fun text -> text.Text = "Atletas"))

        match lead, title with
        | Some lead, Some title ->
          Expect.isLessThan
            lead.Bounds.X
            title.Bounds.X
            "the lead must be the first element of the header row"
        | _ -> failtest "the lead or the title did not render"
      }

      uiTest "a store emission after detach does not reach the disposed list"
      <| fun () -> task {
        let entity name = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let source = new BehaviorSubject<AthleteEntity list>([ entity "Ana" ])

        let _, culture = liveCulture es
        let lifetime = new TestLifetime()

        let! control =
          probeUi(fun () ->
            AthleteList(
              athleteListEnv source culture,
              {
                Lead = ValueNone
                Lifetime = lifetime :> Navs.IDisposableBag
              },
              ignore
            ))

        // the test owns the window so the detach is exact
        let! host = mountWindow control

        let! ana =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> buttonWithText(c, "Ana") |> Option.isSome)
          )

        Expect.isTrue ana "the first row rendered"

        // leaving the page detaches the control and disposes the route
        // lifetime, the way the router does on every navigation away; the
        // list route is NoCache, so the next visit builds a fresh page
        do!
          runUi(fun () ->
            host.Content <- null
            (lifetime :> IDisposable).Dispose())

        let! stopped = waitFor(2000, (fun () -> source.HasObservers), not)

        Expect.isTrue stopped "the detached page stopped listening to the store"

        // the store emits while nobody is watching
        do! runUi(fun () -> source.OnNext([ entity "Ana"; entity "Beto" ]))

        // the route builds a new page on the next visit; this instance
        // must not restart its disposed program on reattach
        do! mount control

        let! beto =
          probeUi(fun () -> buttonWithText(control, "Beto") |> Option.isSome)

        Expect.isFalse beto "the disposed page did not resume on reattach"
      }
    ]

    testList "athlete detail" [
      uiTest "the detail page shows the actions for the athlete state"
      <| fun () -> task {
        let plans = planStore()
        let _, culture = liveCulture es

        let active = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let archived = {
          active with
              Id = Guid.NewGuid()
              ArchivedAt = ValueSome DateTime.UtcNow
        }

        let! activePage =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              detailEnv plans culture,
              {
                Lead = ValueNone
                Athlete = active
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount activePage

        let! activeRendered =
          waitFor(
            2000,
            (fun () -> activePage),
            fun page ->
              buttonWithText(page, get(es, Keys.plansGenerate)) |> Option.isSome
          )

        Expect.isTrue activeRendered "the active detail page did not render"

        Expect.isSome
          (buttonWithText(activePage, get(es, Keys.athleteArchive)))
          "the active page has no archive action"

        Expect.isNone
          (buttonWithText(activePage, get(es, Keys.athleteRestore)))
          "the active page shows restore"

        let! archivedPage =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              detailEnv plans culture,
              {
                Lead = ValueNone
                Athlete = archived
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount archivedPage

        let! archivedRendered =
          waitFor(
            2000,
            (fun () -> archivedPage),
            fun page -> hasText(page, get(es, Keys.athleteArchived))
          )

        Expect.isTrue archivedRendered "the archived badge did not render"

        Expect.isNone
          (buttonWithText(archivedPage, get(es, Keys.plansGenerate)))
          "the archived page shows generate"

        Expect.isSome
          (buttonWithText(archivedPage, get(es, Keys.athleteRestore)))
          "the archived page has no restore action"

        Expect.isSome
          (buttonWithText(archivedPage, get(es, Keys.athleteDelete)))
          "the archived page has no delete action"
      }

      uiTest "the detail delete asks and then navigates back"
      <| fun () -> task {
        let plans = planStore()
        let _, culture = liveCulture es
        let deleted = ResizeArray<Guid>()
        let events = ResizeArray<Briple.Trainer.Views.Athletes.Event>()

        let athlete = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueNone
        }

        let env = {
          detailEnv plans culture with
              Delete =
                fun id ->
                  deleted.Add id
                  okAction
        }

        let! page =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              env,
              {
                Lead = ValueNone
                Athlete = athlete
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount page

        let! rendered =
          waitFor(
            2000,
            (fun () -> page),
            (fun current ->
              buttonWithText(current, get(es, Keys.athleteDelete))
              |> Option.isSome)
          )

        Expect.isTrue rendered "the delete action did not render"

        let! action =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.athleteDelete)))

        let action =
          action
          |> Option.defaultWith(fun () -> failtest "the delete action vanished")

        do! runUi(fun () -> click action)

        let! deletedCall =
          waitFor(2000, (fun () -> deleted.Count), fun count -> count > 0)

        Expect.isTrue deletedCall "the confirmed delete did not reach the store"

        let! navigated =
          waitFor(
            2000,
            (fun () -> events),
            fun seen ->
              seen
              |> Seq.exists(fun event ->
                event = Briple.Trainer.Views.Athletes.Event.BackRequested)
          )

        Expect.isTrue navigated "the delete did not request navigation"
      }

      uiTest "a failed detail action shows an error toast and stays"
      <| fun () -> task {
        let plans = planStore()
        let _, culture = liveCulture es
        let toasts, seen = recordingToasts()
        let events = ResizeArray<Briple.Trainer.Views.Athletes.Event>()

        let athlete = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
          ArchivedAt = ValueSome DateTime.UtcNow
        }

        let env = {
          detailEnv plans culture with
              Restore = fun _ -> Task.FromResult(Error(System.Exception "boom"))
              Toasts = toasts
        }

        let! page =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              env,
              {
                Lead = ValueNone
                Athlete = athlete
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount page

        let! rendered =
          waitFor(
            2000,
            (fun () -> page),
            (fun current ->
              buttonWithText(current, get(es, Keys.athleteRestore))
              |> Option.isSome)
          )

        Expect.isTrue rendered "the restore action did not render"

        let! action =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.athleteRestore)))

        let action =
          action
          |> Option.defaultWith(fun () ->
            failtest "the restore action vanished")

        do! runUi(fun () -> click action)

        let! toast =
          waitFor(2000, (fun () -> seen.Count), fun count -> count > 0)

        Expect.isTrue toast "the failed action did not show a toast"

        let title, _, isError = seen[0]

        Expect.equal
          title
          (get(es, Keys.athleteRestore))
          "the toast names the action"

        Expect.isTrue isError "the toast is an error"
        Expect.isEmpty events "a failed action requested navigation"
      }
      uiTest "plan rows localize, archive, filter and restore"
      <| fun () -> task {
        let stores = detailStores()
        let _, culture = liveCulture es

        let! athleteResult =
          stores.Athletes.Add {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }

        let athlete =
          match athleteResult with
          | Ok athlete -> athlete
          | Error e -> failtest $"athlete add failed: {e}"

        let plan: Plan = {
          Version = 1
          Title = "Mes 1"
          Weeks = 1
          Generated = "2026-10-15"
          Origin = "trainer"
          Blocks = []
          Extras = []
        }

        let insert: PlanInsert = {
          AthleteId = athlete.Id
          Kind = "new"
          Counts = [ "3dias", 1 ]
          CreatedAt = ValueSome DateTime.UtcNow
        }

        let! planResult = stores.Plans.Add(insert, plan)

        let planId =
          match planResult with
          | Ok entity -> entity.Id
          | Error e -> failtest $"plan add failed: {e}"

        let! page =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              detailEnv stores.Plans culture,
              {
                Lead = ValueNone
                Athlete = athlete
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount page

        let! rendered =
          waitFor(
            2000,
            (fun () -> page),
            fun root -> buttonWithText(root, "Mes 1") |> Option.isSome
          )

        Expect.isTrue rendered "the plan row did not render"

        Expect.isTrue
          (textBlockContaining(page, "Nuevo · 3 días por semana"))
          "the plan row is not localized"

        let! more =
          probeUi(fun () -> buttonWithTooltip(page, get(es, Keys.commonMore)))

        let more =
          more
          |> Option.defaultWith(fun () ->
            failtest "the plan overflow button vanished")

        let! archive =
          probeUi(fun () -> menuItemWithText(more, get(es, Keys.planArchive)))

        let archive =
          archive
          |> Option.defaultWith(fun () ->
            failtest "the archive plan action vanished")

        do! runUi(fun () -> clickMenuItem archive)

        let! archived =
          waitFor(
            2000,
            (fun () -> stores.Plans.SnapshotArchived(athlete.Id)),
            fun plans -> plans |> List.exists(fun plan -> plan.Id = planId)
          )

        Expect.isTrue
          archived
          "archive did not move the plan to the archived slice"

        let! toggle =
          waitFor(
            2000,
            (fun () ->
              visibleButtonWithText(page, get(es, Keys.plansShowArchived))),
            Option.isSome
          )

        Expect.isTrue toggle "the archived filter did not appear"

        let toggle =
          visibleButtonWithText(page, get(es, Keys.plansShowArchived))
          |> Option.defaultWith(fun () ->
            failtest "the archived filter vanished")

        do! runUi(fun () -> click toggle)

        let! shown =
          waitFor(
            2000,
            (fun () -> visibleButtonWithText(page, "Mes 1")),
            Option.isSome
          )

        Expect.isTrue shown "the archived plan did not show"

        Expect.isTrue
          (textBlockContaining(page, get(es, Keys.planArchived)))
          "the archived plan badge did not render"

        let! moreArchived =
          probeUi(fun () -> buttonWithTooltip(page, get(es, Keys.commonMore)))

        let moreArchived =
          moreArchived
          |> Option.defaultWith(fun () ->
            failtest "the archived plan overflow button vanished")

        let! restore =
          probeUi(fun () ->
            menuItemWithText(moreArchived, get(es, Keys.planRestore)))

        let restore =
          restore
          |> Option.defaultWith(fun () ->
            failtest "the restore plan action vanished")

        do! runUi(fun () -> clickMenuItem restore)

        let! restored =
          waitFor(
            2000,
            (fun () -> stores.Plans.Snapshot(athlete.Id)),
            fun plans -> plans |> List.exists(fun plan -> plan.Id = planId)
          )

        Expect.isTrue
          restored
          "restore did not move the plan to the active slice"

        let! toggleGone =
          waitFor(
            2000,
            (fun () ->
              visibleButtonWithText(page, get(es, Keys.plansShowArchived))),
            Option.isNone
          )

        Expect.isTrue
          toggleGone
          "the archived filter stayed with no archived plans"
      }

      uiTest "plan delete asks and calls the aggregate delete"
      <| fun () -> task {
        let stores = detailStores()
        let _, culture = liveCulture es
        let toasts, seen = recordingToasts()
        let deleted = ResizeArray<Guid>()
        let events = ResizeArray<Briple.Trainer.Views.Athletes.Event>()

        let! athleteResult =
          stores.Athletes.Add {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }

        let athlete =
          match athleteResult with
          | Ok athlete -> athlete
          | Error e -> failtest $"athlete add failed: {e}"

        let plan: Plan = {
          Version = 1
          Title = "Mes 1"
          Weeks = 1
          Generated = "2026-10-15"
          Origin = "trainer"
          Blocks = []
          Extras = []
        }

        let insert: PlanInsert = {
          AthleteId = athlete.Id
          Kind = "new"
          Counts = []
          CreatedAt = ValueSome DateTime.UtcNow
        }

        let! planResult = stores.Plans.Add(insert, plan)

        match planResult with
        | Ok _ -> ()
        | Error e -> failtest $"plan add failed: {e}"

        let env = {
          detailEnv stores.Plans culture with
              DeletePlan =
                fun id ->
                  deleted.Add id
                  okAction
              Toasts = toasts
        }

        let! page =
          probeUi(fun () ->
            Briple.Trainer.Views.Athletes.AthleteDetailPage(
              env,
              {
                Lead = ValueNone
                Athlete = athlete
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount page

        let! rendered =
          waitFor(
            2000,
            (fun () -> page),
            fun root -> buttonWithText(root, "Mes 1") |> Option.isSome
          )

        Expect.isTrue rendered "the plan row did not render"

        let! more =
          probeUi(fun () -> buttonWithTooltip(page, get(es, Keys.commonMore)))

        let more =
          more
          |> Option.defaultWith(fun () ->
            failtest "the plan overflow button vanished")

        let! delete =
          probeUi(fun () -> menuItemWithText(more, get(es, Keys.planDelete)))

        let delete =
          delete
          |> Option.defaultWith(fun () ->
            failtest "the delete plan action vanished")

        do! runUi(fun () -> clickMenuItem delete)

        let! deletedCall =
          waitFor(2000, (fun () -> deleted.Count), fun count -> count > 0)

        Expect.isTrue
          deletedCall
          "the confirmed delete did not reach the aggregate"

        Expect.isEmpty events "deleting a plan navigated away"

        let! toast =
          waitFor(2000, (fun () -> seen.Count), fun count -> count > 0)

        Expect.isTrue toast "the delete did not show a toast"

        let title, _, _ = seen[0]

        Expect.equal
          title
          (get(es, Keys.planDelete))
          "the toast names the action"
      }
    ]

    uiTest "an archived athlete route restores and returns to the list"
    <| fun () -> task {
      let db =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-archived-{Guid.NewGuid():N}.db"
        )

      match Briple.Trainer.Persistence.migrate(testLogger, db) with
      | Error e -> failtest $"migrate failed: {e}"
      | Ok() -> ()

      let! env =
        probeUi(fun () ->
          Briple.Trainer.Env.AppEnv(
            Briple.Trainer.Stores.createStores(sqliteStore db, ignore),
            Briple.Trainer.Secrets.create(),
            ValueSome {
              Briple.Trainer.Env.defaultSettings with
                  Key = "token"
            }
          ))

      let! athleteResult =
        env.Stores.Athletes.Add {
          Name = "Ana"
          Gender = Female
          Goals = [ Strength ]
        }

      let athlete =
        athleteResult
        |> Result.defaultWith(fun e -> failtest $"athlete add failed: {e}")

      let! archived = env.Stores.Athletes.Archive athlete.Id

      match archived with
      | Ok() -> ()
      | Error e -> failtest $"athlete archive failed: {e}"

      let! shell =
        probeUi(fun () -> Briple.Trainer.Views.Routes.build(env, stubLeadOf))

      let router = shell.Router

      let path() =
        router.Snapshot.Route |> ValueOption.map _.Path |> ValueOption.toOption

      let! window =
        probeUi(fun () ->
          let outlet = RouterOutlet()
          outlet.Router <- Some router

          let window = Window(Content = outlet)
          window.Show() |> ignore
          window)

      trackWindowForTest window

      do!
        runUi(fun () ->
          Briple.Trainer.Views.Routes.go(router, $"/athletes/{athlete.Id}"))

      let! atDetail =
        waitFor(
          5000,
          path,
          fun current -> current = Some $"/athletes/{athlete.Id}"
        )

      Expect.isTrue atDetail "the archived detail route did not open"

      let! rendered =
        waitFor(
          5000,
          (fun () -> window),
          fun current -> hasText(current, get(es, Keys.athleteArchived))
        )

      Expect.isTrue rendered "the archived detail page did not render"

      Expect.isNone
        (buttonWithText(window, get(es, Keys.plansGenerate)))
        "the archived route shows generate"

      do!
        runUi(fun () ->
          Briple.Trainer.Views.Routes.go(
            router,
            $"/athletes/{athlete.Id}/generate"
          ))

      let! blocked =
        waitFor(
          5000,
          (fun () -> window),
          fun current -> hasText(current, "not found")
        )

      Expect.isTrue blocked "the archived athlete reached the generate route"

      do!
        runUi(fun () ->
          Briple.Trainer.Views.Routes.go(router, $"/athletes/{athlete.Id}"))

      let! backAtDetail =
        waitFor(
          5000,
          path,
          fun current -> current = Some $"/athletes/{athlete.Id}"
        )

      Expect.isTrue backAtDetail "the detail route did not return"

      let! restoreReady =
        waitFor(
          5000,
          (fun () -> window),
          fun current ->
            buttonWithText(current, get(es, Keys.athleteRestore))
            |> Option.isSome
        )

      Expect.isTrue restoreReady "the restored detail page did not render"

      let! restore =
        probeUi(fun () -> buttonWithText(window, get(es, Keys.athleteRestore)))

      let restore =
        restore
        |> Option.defaultWith(fun () ->
          failtest "the restore action did not render")

      do! runUi(fun () -> click restore)

      let! returned = waitFor(5000, path, fun current -> current = Some "/")

      Expect.isTrue returned "restore did not return to the athletes list"

      let! active =
        waitFor(
          5000,
          (fun () -> env.Stores.Athletes.Snapshot()),
          fun athletes ->
            athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
        )

      Expect.isTrue
        active
        "the restored athlete did not return to the active list"
    }

    testList "new athlete form" [
      uiTest "a name is enough to save and the default gender rides"
      <| fun () -> task {
        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let! form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Lead = ValueNone
                Gate =
                  Briple.Trainer.Navigation.PageGate(
                    "/athletes/new",
                    fun _ _ -> async { return true }
                  )
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount form

        // the name entry is the only TextBox; the gender already reads
        // Hombre from the model
        do!
          runUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text <- "Pepe"
            | [] -> failtest "the name entry did not render")

        let! saveButton = probeUi(fun () -> buttonWithText(form, "Guardar"))

        let saveButton =
          saveButton
          |> Option.defaultWith(fun () ->
            failtest "the save button did not render")

        let! canSave = waitFor(2000, (fun () -> saveButton.IsEnabled), id)
        Expect.isTrue canSave "the form accepted the typed name"

        do! runUi(fun () -> click saveButton)

        let! reported =
          waitFor(
            2000,
            (fun () -> List.ofSeq events),
            (fun current ->
              current
              |> List.exists(function
                | Saved _ -> true
                | Cancelled -> false))
          )

        Expect.isTrue reported "the save event reached the parent"

        let saved =
          List.ofSeq events
          |> List.tryPick(function
            | Saved athlete -> Some athlete
            | Cancelled -> None)

        match saved with
        | Some athlete ->
          Expect.equal athlete.Name "Pepe" "name"

          Expect.equal athlete.Gender Male "gender"

          Expect.equal athlete.Goals [ Hypertrophy ] "the default goal rides"
        | None -> failtest "no saved profile"
      }

      uiTest "the picked gender reaches the saved profile"
      <| fun () -> task {
        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let! form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Lead = ValueNone
                Gate =
                  Briple.Trainer.Navigation.PageGate(
                    "/athletes/new",
                    fun _ _ -> async { return true }
                  )
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount form

        let! female =
          probeUi(fun () -> radioWithText(form, get(es, Keys.genderFemale)))

        let female =
          female
          |> Option.defaultWith(fun () ->
            failtest "the female radio did not render")

        do! runUi(fun () -> click female)

        do!
          runUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text <- "Ana"
            | [] -> failtest "the name entry did not render")

        let! saveButton = probeUi(fun () -> buttonWithText(form, "Guardar"))

        let saveButton =
          saveButton
          |> Option.defaultWith(fun () ->
            failtest "the save button did not render")

        let! canSave = waitFor(2000, (fun () -> saveButton.IsEnabled), id)
        Expect.isTrue canSave "the form accepted the typed name"

        do! runUi(fun () -> click saveButton)

        let! saved =
          waitFor(
            2000,
            (fun () ->
              events
              |> Seq.tryPick(function
                | Saved athlete -> Some athlete
                | Cancelled -> None)),
            Option.isSome
          )

        Expect.isTrue saved "the save event reached the parent"

        match
          events
          |> Seq.tryPick(function
            | Saved athlete -> Some athlete
            | Cancelled -> None)
        with
        | Some athlete ->
          Expect.equal athlete.Gender Female "the picked gender rides"
        | None -> failtest "no saved profile"
      }

      uiTest "a double click on save reports one athlete"
      <| fun () -> task {
        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let! form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Lead = ValueNone
                Gate =
                  Briple.Trainer.Navigation.PageGate(
                    "/athletes/new",
                    fun _ _ -> async { return true }
                  )
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount form

        do!
          runUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text <- "Pepe"
            | [] -> failtest "the name entry did not render")

        let! saveButton = probeUi(fun () -> buttonWithText(form, "Guardar"))

        let saveButton =
          saveButton
          |> Option.defaultWith(fun () ->
            failtest "the save button did not render")

        let! canSave = waitFor(2000, (fun () -> saveButton.IsEnabled), id)
        Expect.isTrue canSave "the form accepted the typed name"

        // a double click: two clicks land before the form has any reason
        // to consider itself done
        do! runUi(fun () -> click saveButton)
        do! runUi(fun () -> click saveButton)

        let! reported =
          waitFor(
            2000,
            (fun () -> List.ofSeq events),
            fun current ->
              current
              |> List.exists(function
                | Saved _ -> true
                | Cancelled -> false)
          )

        Expect.isTrue reported "the save event reached the parent"

        let saves =
          events
          |> Seq.filter(function
            | Saved _ -> true
            | Cancelled -> false)
          |> Seq.length

        Expect.equal saves 1 "one save per form, no matter the clicks"
      }

      uiTest "the phase is derived: typing marks unsaved, the save clears it"
      <| fun () -> task {
        let gate =
          Briple.Trainer.Navigation.PageGate(
            "/athletes/new",
            fun _ _ -> async { return true }
          )

        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let! form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Lead = ValueNone
                Gate = gate
                Lifetime = testLifetime()
              },
              events.Add
            ))

        do! mount form

        Expect.equal
          gate.PhaseValue
          Briple.Trainer.Navigation.PagePhase.Ready
          "a fresh form is ready"

        do!
          runUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text <- "Pepe"
            | [] -> failtest "the name entry did not render")

        let! dirty =
          waitFor(
            2000,
            (fun () -> gate.PhaseValue),
            fun value ->
              value = Briple.Trainer.Navigation.PagePhase.UnsavedChanges
          )

        Expect.isTrue dirty "typing marks the form unsaved"

        let! saveButton = probeUi(fun () -> buttonWithText(form, "Guardar"))

        let saveButton =
          saveButton
          |> Option.defaultWith(fun () ->
            failtest "the save button did not render")

        do! runUi(fun () -> click saveButton)

        let! ready =
          waitFor(
            2000,
            (fun () -> gate.PhaseValue),
            fun value -> value = Briple.Trainer.Navigation.PagePhase.Ready
          )

        Expect.isTrue ready "an in-flight save is not lost work"
      }

      uiTest "a culture switch keeps the typed name"
      <| fun () -> task {
        let source, culture = liveCulture es
        let en = Language.toCulture En

        let gate =
          Briple.Trainer.Navigation.PageGate(
            "/athletes/new",
            fun _ _ -> async { return true }
          )

        let! form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Lead = ValueNone
                Gate = gate
                Lifetime = testLifetime()
              },
              ignore
            ))

        do! mount form

        do!
          runUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text <- "Emilio"
            | [] -> failtest "the name entry did not render")

        do! runUi(fun () -> source.OnNext en)

        let! nameStillThere =
          probeUi(fun () ->
            match textBoxes form with
            | nameEntry :: _ -> nameEntry.Text
            | [] -> "")

        Expect.equal
          nameStillThere
          "Emilio"
          "the typed name survived the rebuild"
      }
    ]

    testList "pane sections" [
      test "urls map to pane sections" {
        Expect.equal (Sections.sectionOf "/") (ValueSome "/") "home"

        Expect.equal
          (Sections.sectionOf "/athletes/6f9619ff-8b86-d011-b42d-00c04fc964ff")
          (ValueSome "/")
          "detail"

        Expect.equal
          (Sections.sectionOf "/settings")
          (ValueSome "/settings")
          "settings"

        Expect.equal
          (Sections.sectionOf "/login")
          ValueNone
          "login has no section"
      }
    ]

    uiTest "a back navigation through the router returns to the previous page"
    <| fun () -> task {
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-back-{Guid.NewGuid():N}.db")

      match Briple.Trainer.Persistence.migrate(testLogger, db) with
      | Error e -> failtest $"migrate failed: {e}"
      | Ok() -> ()

      let! env =
        probeUi(fun () ->
          Briple.Trainer.Env.AppEnv(
            Briple.Trainer.Stores.createStores(sqliteStore db, ignore),
            Briple.Trainer.Secrets.create(),
            ValueSome {
              Briple.Trainer.Env.defaultSettings with
                  Key = "token"
            }
          ))

      let! shell =
        probeUi(fun () -> Briple.Trainer.Views.Routes.build(env, stubLeadOf))

      let router = shell.Router

      let path() =
        router.Snapshot.Route |> ValueOption.map _.Path |> ValueOption.toOption

      let! booted = waitFor(5000, path, fun current -> current = Some "/")
      Expect.isTrue booted "the boot landed on the athletes list"
      Expect.isFalse router.History.CanGoBack "the list is the first entry"

      do!
        runUi(fun () -> Briple.Trainer.Views.Routes.go(router, "/athletes/new"))

      let! atForm =
        waitFor(5000, path, fun current -> current = Some "/athletes/new")

      Expect.isTrue atForm "the form opened"
      Expect.isTrue router.History.CanGoBack "the form has a previous entry"

      Expect.equal
        (router.History.Current |> Option.map _.Url)
        (Some "/athletes/new")
        "the form is the current history entry"

      // no input: the form is Ready and the back proceeds
      do! runUi(fun () -> Briple.Trainer.Views.Routes.back router)

      let! returned = waitFor(5000, path, fun current -> current = Some "/")

      Expect.isTrue returned "the back navigation landed on the list"
      Expect.isFalse router.History.CanGoBack "the list is the first entry"

      Expect.equal
        (router.History.Current |> Option.map _.Url)
        (Some "/")
        "the list is the current history entry"
    }

    test "the export writes the raw text verbatim" {
      let path =
        Path.Combine(
          Path.GetTempPath(),
          $"briple-export-{Guid.NewGuid():N}.briple"
        )

      match exportBriple(path, "#start mes 1") with
      | Error e -> failtest $"export failed: {e}"
      | Ok written ->
        Expect.equal written path "the path comes back"

        Expect.stringContains (File.ReadAllText path) "#start mes 1" "verbatim"
    }

    testList "confirmation dialog" [
      // one mounted overlay: the host is a single control, so both
      // answers run over the same instance
      uiTest "the dialog answers accept and cancel through its buttons"
      <| fun () -> task {
        let! replied, overlay, window = askDialog()

        let! shown =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> hasText(o, "¿Descartar?"))
          )

        Expect.isTrue shown "the dialog did not render"

        let! accept = probeUi(fun () -> buttonWithText(overlay, "Descartar"))

        let accept =
          accept
          |> Option.defaultWith(fun () ->
            failtest "the accept button did not render")

        do! runUi(fun () -> click accept)

        let! accepted =
          waitFor(
            2000,
            (fun () -> replied.Value),
            fun value -> value = Some true
          )

        Expect.isTrue accepted "accepted"

        let! cleared =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> not(hasText(o, "¿Descartar?")))
          )

        Expect.isTrue cleared "the overlay did not clear"

        do!
          runUi(fun () ->
            Async.StartWithContinuations(
              Briple.Trainer.Dialogs.confirmAsync(
                {
                  Title = "¿Descartar?"
                  Body = "Se pierde el plan"
                  Accept = "Descartar"
                  Cancel = "Cancelar"
                  Danger = false
                }
              ),
              (fun value -> replied.Value <- Some value),
              ignore,
              ignore
            ))

        let! shownAgain =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> hasText(o, "¿Descartar?"))
          )

        Expect.isTrue shownAgain "the dialog did not render again"

        let! cancel = probeUi(fun () -> buttonWithText(overlay, "Cancelar"))

        let cancel =
          cancel
          |> Option.defaultWith(fun () ->
            failtest "the cancel button did not render")

        do! runUi(fun () -> click cancel)

        let! cancelled =
          waitFor(
            2000,
            (fun () -> replied.Value),
            fun value -> value = Some false
          )

        Expect.isTrue cancelled "cancelled"

        // release the singleton host so the shell can mount it next
        do!
          runUi(fun () ->
            window.Content <- null
            window.Close())
      }

      uiTest "a danger confirmation marks its accept button"
      <| fun () -> task {
        let! overlay = probeUi(fun () -> Briple.Trainer.Dialogs.host())
        let! window = mountWindow overlay
        let replied = ref None

        do!
          runUi(fun () ->
            Async.StartWithContinuations(
              Briple.Trainer.Dialogs.confirmAsync(
                {
                  Title = "¿Eliminar atleta?"
                  Body = "Se pierden sus planes y memorias"
                  Accept = "Eliminar"
                  Cancel = "Cancelar"
                  Danger = true
                }
              ),
              (fun value -> replied.Value <- Some value),
              ignore,
              ignore
            ))

        let! shown =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun current -> hasText(current, "¿Eliminar atleta?"))
          )

        Expect.isTrue shown "the danger dialog did not render"

        let! accept = probeUi(fun () -> buttonWithText(overlay, "Eliminar"))

        let accept =
          accept
          |> Option.defaultWith(fun () ->
            failtest "the danger accept button did not render")

        Expect.isTrue
          (accept.Classes.Contains "danger")
          "the accept button does not carry the danger class"

        do! runUi(fun () -> click accept)

        let! accepted =
          waitFor(
            2000,
            (fun () -> replied.Value),
            fun value -> value = Some true
          )

        Expect.isTrue accepted "the danger dialog did not accept"

        do!
          runUi(fun () ->
            window.Content <- null
            window.Close())
      }
    ]

    testList "agent guard" [
      uiTest "the agent routes redirect to login without a token"
      <| fun () -> task {
        let db =
          Path.Combine(
            Path.GetTempPath(),
            $"briple-guard-{Guid.NewGuid():N}.db"
          )

        match Briple.Trainer.Persistence.migrate(testLogger, db) with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        // a constructed env with an empty key: no token, no matter what
        // the real user's vault holds
        let! env =
          probeUi(fun () ->
            Briple.Trainer.Env.AppEnv(
              Briple.Trainer.Stores.createStores(sqliteStore db, fun _ -> ()),
              Briple.Trainer.Secrets.create(),
              ValueSome {
                Briple.Trainer.Env.defaultSettings with
                    Key = ""
              }
            ))

        Expect.isFalse
          ((env :> Briple.Trainer.Env.IProvideAgent).AgentAvailable)
          "the env carries no token"

        let! shell =
          probeUi(fun () -> Briple.Trainer.Views.Routes.build(env, stubLeadOf))

        let router = shell.Router

        let current() =
          router.Snapshot.Route
          |> ValueOption.map _.Path
          |> ValueOption.toOption

        do!
          runUi(fun () ->
            Briple.Trainer.Views.Routes.go(
              router,
              $"/athletes/{Guid.NewGuid()}/generate"
            ))

        let! landed = waitFor(5000, current, (fun path -> path = Some "/login"))

        Expect.isTrue landed "the guard did not redirect to login"
      }
    ]

    testList "shell boot" [
      // The token gates the data routes: the boot lands on the login
      // without one, on the athletes list with one. Both envs are
      // constructed, so the real user settings never interfere.
      uiTest "the first page follows the token"
      <| fun () -> task {
        let db =
          Path.Combine(Path.GetTempPath(), $"briple-boot-{Guid.NewGuid():N}.db")

        match Briple.Trainer.Persistence.migrate(testLogger, db) with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        let windowOf(key: string) = task {
          let! env =
            probeUi(fun () ->
              Briple.Trainer.Env.AppEnv(
                Briple.Trainer.Stores.createStores(sqliteStore db, fun _ -> ()),
                Briple.Trainer.Secrets.create(),
                ValueSome {
                  Briple.Trainer.Env.defaultSettings with
                      Key = key
                }
              ))

          let! window =
            probeUi(fun () ->
              let router =
                (Briple.Trainer.Views.Routes.build(env, stubLeadOf)).Router

              let outlet = RouterOutlet()
              outlet.Router <- Some router

              let window = Window(Content = outlet)
              window.Show() |> ignore

              window)

          trackWindowForTest window
          return window
        }

        let! closed = windowOf ""

        let! onLogin =
          waitFor(
            5000,
            (fun () -> closed),
            (fun w -> hasText(w, get(es, Keys.loginTitle)))
          )

        Expect.isTrue onLogin "no token: the boot did not land on the login"

        let! openWindow = windowOf "token"

        let! onList =
          waitFor(
            5000,
            (fun () -> openWindow),
            (fun w -> hasText(w, get(es, Keys.athletesTitle)))
          )

        Expect.isTrue
          onList
          "with token: the boot did not land on the athletes list"
      }

      uiTest "the title bar band clears the page and hides the root back"
      <| fun () -> task {
        let db =
          Path.Combine(Path.GetTempPath(), $"briple-band-{Guid.NewGuid():N}.db")

        match Briple.Trainer.Persistence.migrate(testLogger, db) with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        let! env =
          probeUi(fun () ->
            Briple.Trainer.Env.AppEnv(
              Briple.Trainer.Stores.createStores(sqliteStore db, fun _ -> ()),
              Briple.Trainer.Secrets.create(),
              ValueSome {
                Briple.Trainer.Env.defaultSettings with
                    Key = "token"
              }
            ))

        let! window = probeUi(fun () -> Briple.Trainer.Shell.create env)
        trackWindowForTest window

        do! runUi(fun () -> window.Show() |> ignore)

        let! rendered =
          waitFor(
            5000,
            (fun () -> window),
            fun w -> hasText(w, get(es, Keys.athletesTitle))
          )

        Expect.isTrue rendered "the athletes page did not render"

        let! band = probeUi(fun () -> controlByName(window, "TitleBarBand"))

        let band =
          band
          |> Option.defaultWith(fun () ->
            failtest "the title bar band is missing")

        let! title =
          probeUi(fun () -> textBlockWith(window, get(es, Keys.athletesTitle)))

        let title =
          title
          |> Option.defaultWith(fun () -> failtest "the page title is missing")

        let! command =
          probeUi(fun () -> buttonWithText(window, get(es, Keys.athletesNew)))

        let command =
          command
          |> Option.defaultWith(fun () -> failtest "the new command is missing")

        let! bandHeight, bandBottom, titleTop, commandTop =
          probeUi(fun () ->
            let bandHeight = band.Bounds.Height

            let bandBottom =
              band.TranslatePoint(Point(0.0, bandHeight), window).Value.Y

            let titleTop =
              title.TranslatePoint(Point(0.0, 0.0), window).Value.Y

            let commandTop =
              command.TranslatePoint(Point(0.0, 0.0), window).Value.Y

            bandHeight, bandBottom, titleTop, commandTop)

        Expect.equal bandHeight 32.0 "the band is 32px"

        Expect.isGreaterThan
          titleTop
          bandBottom
          "the page title starts below the band"

        Expect.isGreaterThan
          commandTop
          bandBottom
          "the page command starts below the band"

        let! rootBack =
          probeUi(fun () ->
            buttons(window)
            |> List.tryFind(fun button -> button.Classes.Contains("back"))
            |> Option.map(fun button -> button.IsVisible))

        match rootBack with
        | Some visible ->
          Expect.isFalse visible "the root back control is hidden"
        | None -> ()

        let! minimize =
          probeUi(fun () -> controlByName(window, "MinimizeButton"))

        let! maximize =
          probeUi(fun () -> controlByName(window, "MaximizeButton"))

        let! close = probeUi(fun () -> controlByName(window, "CloseButton"))

        Expect.isSome minimize "the minimize button is missing"
        Expect.isSome maximize "the maximize button is missing"
        Expect.isSome close "the close button is missing"

        let roleOf(control: Control) =
          probeUi(fun () ->
            Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(
              control
            ))

        let! dragRole =
          probeUi(fun () ->
            band.GetVisualDescendants()
            |> Seq.tryFind(fun visual ->
              let role =
                Avalonia.Controls.Chrome.WindowDecorationProperties
                  .GetElementRole(visual)

              role = Avalonia.Input.WindowDecorationsElementRole.TitleBar))

        Expect.isSome dragRole "the band has a title bar drag surface"

        let! minimizeRole = roleOf minimize.Value
        let! maximizeRole = roleOf maximize.Value
        let! closeRole = roleOf close.Value

        Expect.equal
          minimizeRole
          Avalonia.Input.WindowDecorationsElementRole.MinimizeButton
          "the minimize button carries its system role"

        Expect.equal
          maximizeRole
          Avalonia.Input.WindowDecorationsElementRole.MaximizeButton
          "the maximize button carries its system role"

        Expect.equal
          closeRole
          Avalonia.Input.WindowDecorationsElementRole.CloseButton
          "the close button carries its system role"

        let maximizeButton = maximize.Value :?> Button

        do! runUi(fun () -> click maximizeButton)

        let! maximized =
          waitFor(
            2000,
            (fun () -> window.WindowState),
            fun state -> state = WindowState.Maximized
          )

        Expect.isTrue maximized "the caption button maximizes the window"

        do! runUi(fun () -> click maximizeButton)

        let! restored =
          waitFor(
            2000,
            (fun () -> window.WindowState),
            fun state -> state = WindowState.Normal
          )

        Expect.isTrue restored "the caption button restores the window"

        let! newButton =
          probeUi(fun () -> buttonWithText(window, get(es, Keys.athletesNew)))

        let newButton =
          newButton
          |> Option.defaultWith(fun () -> failtest "the new command vanished")

        do! runUi(fun () -> click newButton)

        let! form =
          waitFor(
            5000,
            (fun () -> window),
            fun w -> hasText(w, get(es, Keys.athleteName))
          )

        Expect.isTrue form "the form did not open"

        let! formBack =
          probeUi(fun () ->
            buttons(window)
            |> List.tryFind(fun button -> button.Classes.Contains("back"))
            |> Option.map(fun button -> button.IsVisible))

        match formBack with
        | Some visible ->
          Expect.isTrue visible "the form back control is visible"
        | None -> failtest "the form back control is missing"

        do! runUi(fun () -> window.Close())
      }
    ]

    testList "login guards" [
      uiTest "typing keeps the outcome band; a save rebuilds it"
      <| fun () -> task {
        let env = LoginEnv()

        let! page =
          probeUi(fun () ->
            Briple.Trainer.Views.Login.LoginPage<LoginEnv>(
              env,
              {
                Lead = ValueNone
                OnDone = ignore
                Lifetime = testLifetime()
              }
            ))

        do! mount page

        let! token = probeUi(fun () -> textBoxes page |> List.tryHead)

        let token =
          token
          |> Option.defaultWith(fun () ->
            failtest "the token entry did not render")

        let! host = probeUi(fun () -> controlByName(page, "login-outcome"))

        let host =
          host
          |> Option.map(fun control -> control :?> ContentControl)
          |> Option.defaultWith(fun () ->
            failtest "the outcome host did not render")

        let before = host.Content

        do! runUi(fun () -> token.Text <- "abc")

        let! save =
          probeUi(fun () -> buttonWithText(page, get(es, Keys.loginSave)))

        let save =
          save
          |> Option.defaultWith(fun () ->
            failtest "the save button did not render")

        let! canSave = waitFor(2000, (fun () -> save.IsEnabled), id)
        Expect.isTrue canSave "the token reached the model"

        let! unchanged =
          waitFor(
            2000,
            (fun () -> obj.ReferenceEquals(host.Content, before)),
            id
          )

        Expect.isTrue unchanged "typing does not rebuild the outcome band"

        do! runUi(fun () -> click save)

        let! changed =
          waitFor(
            2000,
            (fun () -> obj.ReferenceEquals(host.Content, before)),
            not
          )

        Expect.isTrue changed "a save state change rebuilds the outcome band"
      }
    ]
  ]
