module Briple.Trainer.UI.Tests.App

// The UI checks: resx i18n, the athlete list view, the registration form
// as a props-in/events-out component, and the export write.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects

open Avalonia.Controls
open Avalonia.VisualTree

open Expecto
open Navs.Avalonia

open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.NewAthleteForm
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Dialogs
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.UI.Tests.Harness

let private es = Language.toCulture Es

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let private testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

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

/// Mounts the dialog overlay, asks one confirmation and answers through
/// the buttons; the pair is the reply cell and the overlay.
let private askDialog() =
  let overlay = probeUi(fun () -> Briple.Trainer.Dialogs.host)

  mount overlay

  let replied = ref None

  onUi(fun () ->
    Async.StartWithContinuations(
      Briple.Trainer.Dialogs.confirmAsync(
        {
          Title = "¿Descartar?"
          Body = "Se pierde el plan"
          Accept = "Descartar"
          Cancel = "Cancelar"
        }
      ),
      (fun value -> replied.Value <- Some value),
      ignore,
      ignore
    ))

  replied, overlay

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

      test "an unknown culture falls back to the neutral" {
        let fr = CultureInfo "fr-FR"

        Expect.equal (get(fr, Keys.athletesTitle)) "Atletas" "fr falls back"
      }
    ]

    testList "athlete list" [
      test "the empty page renders the empty-state text" {
        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = es
                Athletes = new BehaviorSubject<AthleteEntity list>([])
              },
              ignore
            ))

        mount control

        let rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> hasText(c, "Sin atletas todavía. Registra el primero."))
          )

        Expect.isTrue rendered "the empty state did not render"
      }

      test "a registered athlete renders as a row and opens" {
        let opened = ref None

        let entity = {
          Id = Guid.NewGuid()
          Profile = {
            Name = "Ana"
            Gender = Female
            Goals = [ Strength ]
          }
        }

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = es
                Athletes = new BehaviorSubject<AthleteEntity list>([ entity ])
              },
              fun event ->
                match event with
                | OpenRequested id -> opened.Value <- Some id
                | NewRequested -> ()
            ))

        mount control

        let rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> buttonWithText(c, "Ana") |> Option.isSome)
          )

        Expect.isTrue rendered "the athlete row did not render"

        settle()

        let row =
          match probeUi(fun () -> buttonWithText(control, "Ana")) with
          | Some row -> row
          | None -> failtest "the row vanished"

        onUi(fun () -> click row)

        Expect.equal
          opened.Value
          (Some entity.Id)
          "the open event carries the id"
      }
    ]

    testList "new athlete form" [
      test "a valid save reports the profile through the event" {
        let events = ResizeArray<Event>()

        let form =
          probeUi(fun () -> NewAthleteForm({ Culture = es }, events.Add))

        mount form

        // the name entry is the only TextBox; setting the property
        // raises TextChanged, which the form subscribed through NXUI
        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Pepe"
          | [] -> failtest "the name entry did not render")

        settle()

        // the gender is a radio row; the goals ship with one checked
        let genderRendered =
          waitFor(
            2000,
            (fun () -> form),
            (fun f -> buttonWithText(f, "Hombre") |> Option.isSome)
          )

        Expect.isTrue genderRendered "the gender radios did not render"

        settle()

        let genderRadio =
          match probeUi(fun () -> buttonWithText(form, "Hombre")) with
          | Some button -> button
          | None -> failtest "the gender radio vanished"

        onUi(fun () -> click genderRadio)

        settle()

        let saveButton =
          match probeUi(fun () -> buttonWithText(form, "Guardar")) with
          | Some button -> button
          | None -> failtest "the save button did not render"

        onUi(fun () -> click saveButton)

        let reported =
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
    ]

    testList "navigation history" [
      test "push ignores a repeat of the current top" {
        let stack =
          [] |> History.push "/a" |> History.push "/b" |> History.push "/b"

        Expect.equal stack [ "/b"; "/a" ] "dedup"
      }

      test "back answers the previous entry and pop drops the current" {
        let stack = [] |> History.push "/" |> History.push "/athletes/new"

        Expect.equal (History.back stack) (Some "/") "back target"

        Expect.equal (History.pop stack) [ "/" ] "pop"
      }

      test "a single entry cannot go back" {
        let stack = [] |> History.push "/"

        Expect.isFalse (History.canBack stack) "one entry"

        Expect.equal (History.back stack) None "no target"
      }

      test "the stack caps at one hundred entries" {
        let stack =
          [ 1..300 ]
          |> List.map string
          |> List.fold (fun acc url -> History.push url acc) []

        Expect.equal (List.length stack) 100 "capped"
      }

      test "urls map to pane sections" {
        Expect.equal (History.sectionOf "/") (ValueSome "/") "home"

        Expect.equal
          (History.sectionOf "/athletes/6f9619ff-8b86-d011-b42d-00c04fc964ff")
          (ValueSome "/")
          "detail"

        Expect.equal
          (History.sectionOf "/settings")
          (ValueSome "/settings")
          "settings"

        Expect.equal
          (History.sectionOf "/login")
          ValueNone
          "login has no section"
      }

      test "the live stack records and answers back" {
        let stack = BackStack()

        stack.Sync "/"
        stack.Sync "/athletes/new"

        Expect.equal stack.Current (Some "/athletes/new") "current"

        Expect.equal (stack.Back()) (Some "/") "back target"

        Expect.equal stack.Current (Some "/") "popped"

        Expect.equal (stack.Back()) None "nothing below"
      }
    ]

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
      test "the dialog answers accept and cancel through its buttons" {
        let replied, overlay = askDialog()

        let shown =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> hasText(o, "¿Descartar?"))
          )

        Expect.isTrue shown "the dialog did not render"

        let accept =
          match probeUi(fun () -> buttonWithText(overlay, "Descartar")) with
          | Some button -> button
          | None -> failtest "the accept button did not render"

        onUi(fun () -> click accept)

        settle()

        Expect.equal replied.Value (Some true) "accepted"

        let cleared =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> not(hasText(o, "¿Descartar?")))
          )

        Expect.isTrue cleared "the overlay did not clear"

        onUi(fun () ->
          Async.StartWithContinuations(
            Briple.Trainer.Dialogs.confirmAsync(
              {
                Title = "¿Descartar?"
                Body = "Se pierde el plan"
                Accept = "Descartar"
                Cancel = "Cancelar"
              }
            ),
            (fun value -> replied.Value <- Some value),
            ignore,
            ignore
          ))

        let shownAgain =
          waitFor(
            2000,
            (fun () -> overlay),
            (fun o -> hasText(o, "¿Descartar?"))
          )

        Expect.isTrue shownAgain "the dialog did not render again"

        let cancel =
          match probeUi(fun () -> buttonWithText(overlay, "Cancelar")) with
          | Some button -> button
          | None -> failtest "the cancel button did not render"

        onUi(fun () -> click cancel)

        settle()

        Expect.equal replied.Value (Some false) "cancelled"
      }
    ]

    testList "agent guard" [
      test "the agent routes redirect to login without a token" {
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
        let env =
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

        let router = probeUi(fun () -> Briple.Trainer.Views.Routes.build env)

        let current() =
          router.RouteSnapshot |> ValueOption.map _.path |> ValueOption.toOption

        onUi(fun () ->
          Briple.Trainer.Views.Routes.go(
            router,
            $"/athletes/{Guid.NewGuid()}/generate"
          ))

        let landed = waitFor(5000, current, (fun path -> path = Some "/login"))

        Expect.isTrue landed "the guard did not redirect to login"
      }
    ]

    testList "shell boot" [
      // The token gates the data routes: the boot lands on the login
      // without one, on the athletes list with one. Both envs are
      // constructed, so the real user settings never interfere.
      test "the first page follows the token" {
        let db =
          Path.Combine(Path.GetTempPath(), $"briple-boot-{Guid.NewGuid():N}.db")

        match Briple.Trainer.Persistence.migrate(testLogger, db) with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        let envOf(key: string) =
          probeUi(fun () ->
            Briple.Trainer.Env.AppEnv(
              Briple.Trainer.Stores.createStores(sqliteStore db, fun _ -> ()),
              Briple.Trainer.Secrets.create(),
              ValueSome {
                Briple.Trainer.Env.defaultSettings with
                    Key = key
              }
            ))

        let windowOf(env: Briple.Trainer.Env.AppEnv) =
          probeUi(fun () ->
            let router = Briple.Trainer.Views.Routes.build env
            let outlet = RouterOutlet()
            outlet.Router <- router

            let window = Window(Content = outlet)
            window.Show() |> ignore

            window)

        let closed = windowOf(envOf "")

        let onLogin =
          waitFor(
            5000,
            (fun () -> closed),
            (fun w -> hasText(w, get(es, Keys.loginTitle)))
          )

        Expect.isTrue onLogin "no token: the boot did not land on the login"

        let openWindow = windowOf(envOf "token")

        let onList =
          waitFor(
            5000,
            (fun () -> openWindow),
            (fun w -> hasText(w, get(es, Keys.athletesTitle)))
          )

        Expect.isTrue
          onList
          "with token: the boot did not land on the athletes list"
      }
    ]
  ]
