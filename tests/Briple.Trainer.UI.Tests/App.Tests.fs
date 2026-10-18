module Briple.Trainer.UI.Tests.App

// The UI checks: resx i18n, the athlete list view, the registration form
// as a props-in/events-out component, and the export write.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.VisualTree

open Expecto
open FSharp.Control.Reactive
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

/// Mounts the dialog overlay, asks one confirmation and answers through
/// the buttons; the tuple is the reply cell, the overlay and its window.
let private askDialog() =
  let overlay = probeUi(fun () -> Briple.Trainer.Dialogs.host)

  let window = mountWindow overlay

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

  replied, overlay, window

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

    testList "localization resources" [
      test "a resource dictionary updates static text without a rebuild" {
        // a local host: the test never mutates the app dictionary that
        // the other parallel tests read
        let host =
          probeUi(fun () ->
            let host = Border()
            host.Resources["athletes.title"] <- "Atletas"
            host)

        let text =
          probeUi(fun () ->
            Briple.Trainer.Localization.textBlockFor(host, Keys.athletesTitle))

        mount text

        let spanish =
          waitFor(2000, (fun () -> text), fun block -> block.Text = "Atletas")

        Expect.isTrue spanish "the initial resource dictionary"

        onUi(fun () -> host.Resources["athletes.title"] <- "Athletes")

        let english =
          waitFor(2000, (fun () -> text), fun block -> block.Text = "Athletes")

        Expect.isTrue english "the swapped dictionary updated the bound text"
      }
    ]

    testList "athlete list" [
      test "the empty page renders the empty-state text" {
        let _, culture = liveCulture es

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueNone
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

        let _, culture = liveCulture es

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueNone
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

      test "rows of different lengths share one chevron edge" {
        let entity name goals = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = goals
          }
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

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueNone
                Athletes =
                  new BehaviorSubject<AthleteEntity list>([ ana; beto ])
              },
              ignore
            ))

        let window =
          probeUi(fun () ->
            let window =
              Window(Content = control, Width = 1000.0, Height = 700.0)

            window.Show() |> ignore
            window)

        let rendered =
          waitFor(
            2000,
            (fun () -> control),
            fun root ->
              buttonWithText(root, "Ana") |> Option.isSome
              && buttonWithText(root, "Beto") |> Option.isSome
          )

        Expect.isTrue rendered "the rows did not render"

        settle()

        let measure name =
          probeUi(fun () ->
            let row =
              buttonWithText(control, name)
              |> Option.defaultWith(fun () ->
                failtest $"the {name} row vanished")

            let icon =
              row.GetVisualDescendants()
              |> Seq.choose(fun visual ->
                match visual with
                | :? PathIcon as icon -> Some icon
                | _ -> None)
              |> Seq.tryHead
              |> Option.defaultWith(fun () -> failtest "the chevron vanished")

            let left = row.TranslatePoint(Point(0.0, 0.0), window)
            let iconLeft = icon.TranslatePoint(Point(0.0, 0.0), window)

            left.Value.X,
            left.Value.X + row.Bounds.Width,
            iconLeft.Value.X,
            left.Value.Y,
            row.Bounds.Height,
            iconLeft.Value.Y,
            icon.Bounds.Height)

        let (anaLeft,
             anaRight,
             anaChevron,
             anaTop,
             anaHeight,
             anaIconTop,
             anaIconHeight) =
          measure "Ana"

        let (betoLeft,
             betoRight,
             betoChevron,
             betoTop,
             betoHeight,
             betoIconTop,
             betoIconHeight) =
          measure "Beto"

        Expect.equal anaLeft betoLeft "both rows share the left edge"

        Expect.isLessThan anaLeft 100.0 "the rows start at the page margin"

        Expect.equal anaRight betoRight "both rows share the right edge"

        Expect.equal
          anaChevron
          betoChevron
          "a longer summary must not push the chevron"

        Expect.isGreaterThan
          anaChevron
          (anaRight - 40.0)
          "the chevron sits at the row's trailing edge"

        let anaIconCenter = anaIconTop + anaIconHeight / 2.0
        let anaRowCenter = anaTop + anaHeight / 2.0

        Expect.isLessThan
          (abs(anaIconCenter - anaRowCenter))
          1.5
          "the chevron centers on the row's full height"

        let betoIconCenter = betoIconTop + betoIconHeight / 2.0
        let betoRowCenter = betoTop + betoHeight / 2.0

        Expect.isLessThan
          (abs(betoIconCenter - betoRowCenter))
          1.5
          "the longer row centers its chevron too"
      }

      test "the page fills a wide window to its edge" {
        let entity name = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = [ Strength ]
          }
        }

        let ana = entity "Ana"
        let _, culture = liveCulture es

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueNone
                Athletes = new BehaviorSubject<AthleteEntity list>([ ana ])
              },
              ignore
            ))

        let window =
          probeUi(fun () ->
            let window =
              Window(Content = control, Width = 1600.0, Height = 800.0)

            window.Show() |> ignore
            window)

        let rendered =
          waitFor(
            2000,
            (fun () -> control),
            fun root -> buttonWithText(root, "Ana") |> Option.isSome
          )

        Expect.isTrue rendered "the row did not render"

        settle()

        let right =
          probeUi(fun () ->
            let row =
              buttonWithText(control, "Ana")
              |> Option.defaultWith(fun () -> failtest "the row vanished")

            let left = row.TranslatePoint(Point(0.0, 0.0), window)

            left.Value.X + row.Bounds.Width)

        Expect.isGreaterThan
          right
          1500.0
          "the page has no width cap: the row reaches the window edge"
      }

      test "the page lead sits before the title" {
        let _, culture = liveCulture es

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueSome(fun () -> TextBlock(Text = "back") :> Control)
                Athletes = new BehaviorSubject<AthleteEntity list>([])
              },
              ignore
            ))

        mount control
        settle()

        let header =
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

        let lead, title =
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

      test "a store emission after detach still reaches the list" {
        let entity name = {
          Id = Guid.NewGuid()
          Profile = {
            Name = name
            Gender = Female
            Goals = [ Strength ]
          }
        }

        let source = new BehaviorSubject<AthleteEntity list>([ entity "Ana" ])

        let _, culture = liveCulture es

        let control =
          probeUi(fun () ->
            AthleteList(
              {
                Culture = culture
                Lead = ValueNone
                Athletes = source
              },
              ignore
            ))

        // the test owns the window so the detach is exact
        let host =
          probeUi(fun () ->
            let window = Window(Content = control)
            window.Show() |> ignore
            window)

        let ana =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> buttonWithText(c, "Ana") |> Option.isSome)
          )

        Expect.isTrue ana "the first row rendered"

        // leaving the page detaches the control: a cached route does
        // exactly this on every navigation away
        onUi(fun () -> host.Content <- null)

        settle()

        // the store emits while nobody is watching
        onUi(fun () -> source.OnNext([ entity "Ana"; entity "Beto" ]))

        settle()

        // coming back re-attaches the same control instance
        mount control

        let beto =
          waitFor(
            2000,
            (fun () -> control),
            (fun c -> buttonWithText(c, "Beto") |> Option.isSome)
          )

        Expect.isTrue beto "the late emission reached the reattached list"
      }
    ]

    testList "new athlete form" [
      test "a name is enough to save and the default gender rides" {
        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Culture = culture
                Lead = ValueNone
                Gate =
                  Briple.Trainer.Navigation.PageGate(
                    "/athletes/new",
                    fun _ _ -> async { return true }
                  )
              },
              events.Add
            ))

        mount form

        // the name entry is the only TextBox; the gender already reads
        // Hombre from the model
        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Pepe"
          | [] -> failtest "the name entry did not render")

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

      test "a double click on save reports one athlete" {
        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Culture = culture
                Lead = ValueNone
                Gate =
                  Briple.Trainer.Navigation.PageGate(
                    "/athletes/new",
                    fun _ _ -> async { return true }
                  )
              },
              events.Add
            ))

        mount form

        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Pepe"
          | [] -> failtest "the name entry did not render")

        settle()

        let saveButton =
          match probeUi(fun () -> buttonWithText(form, "Guardar")) with
          | Some button -> button
          | None -> failtest "the save button did not render"

        // a double click: two clicks land before the form has any reason
        // to consider itself done
        onUi(fun () -> click saveButton)
        onUi(fun () -> click saveButton)

        let reported =
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

      test "the phase is derived: typing marks unsaved, the save clears it" {
        let gate =
          Briple.Trainer.Navigation.PageGate(
            "/athletes/new",
            fun _ _ -> async { return true }
          )

        let events = ResizeArray<Event>()
        let _, culture = liveCulture es

        let form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Culture = culture
                Lead = ValueNone
                Gate = gate
              },
              events.Add
            ))

        mount form

        Expect.equal
          gate.PhaseValue
          Briple.Trainer.Navigation.PagePhase.Ready
          "a fresh form is ready"

        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Pepe"
          | [] -> failtest "the name entry did not render")

        let dirty =
          waitFor(
            2000,
            (fun () -> gate.PhaseValue),
            fun value ->
              value = Briple.Trainer.Navigation.PagePhase.UnsavedChanges
          )

        Expect.isTrue dirty "typing marks the form unsaved"

        let saveButton =
          match probeUi(fun () -> buttonWithText(form, "Guardar")) with
          | Some button -> button
          | None -> failtest "the save button did not render"

        onUi(fun () -> click saveButton)

        let ready =
          waitFor(
            2000,
            (fun () -> gate.PhaseValue),
            fun value -> value = Briple.Trainer.Navigation.PagePhase.Ready
          )

        Expect.isTrue ready "an in-flight save is not lost work"
      }

      test "a culture switch keeps the typed name" {
        let source, culture = liveCulture es
        let en = Language.toCulture En

        let gate =
          Briple.Trainer.Navigation.PageGate(
            "/athletes/new",
            fun _ _ -> async { return true }
          )

        let form =
          probeUi(fun () ->
            NewAthleteForm(
              {
                Culture = culture
                Lead = ValueNone
                Gate = gate
              },
              ignore
            ))

        mount form

        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Emilio"
          | [] -> failtest "the name entry did not render")

        settle()

        onUi(fun () -> source.OnNext en)
        settle()

        let nameStillThere =
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

    test "a back navigation through the router returns to the previous page" {
      let db =
        Path.Combine(Path.GetTempPath(), $"briple-back-{Guid.NewGuid():N}.db")

      match Briple.Trainer.Persistence.migrate(testLogger, db) with
      | Error e -> failtest $"migrate failed: {e}"
      | Ok() -> ()

      let env =
        probeUi(fun () ->
          Briple.Trainer.Env.AppEnv(
            Briple.Trainer.Stores.createStores(sqliteStore db, ignore),
            Briple.Trainer.Secrets.create(),
            ValueSome {
              Briple.Trainer.Env.defaultSettings with
                  Key = "token"
            }
          ))

      let shell =
        probeUi(fun () -> Briple.Trainer.Views.Routes.build(env, stubLeadOf))

      let router = shell.Router

      let path() =
        router.Snapshot.Route |> ValueOption.map _.Path |> ValueOption.toOption

      let booted = waitFor(5000, path, fun current -> current = Some "/")
      Expect.isTrue booted "the boot landed on the athletes list"
      Expect.isFalse router.History.CanGoBack "the list is the first entry"

      onUi(fun () -> Briple.Trainer.Views.Routes.go(router, "/athletes/new"))

      let atForm =
        waitFor(5000, path, fun current -> current = Some "/athletes/new")

      Expect.isTrue atForm "the form opened"
      Expect.isTrue router.History.CanGoBack "the form has a previous entry"

      Expect.equal
        (router.History.Current |> Option.map _.Url)
        (Some "/athletes/new")
        "the form is the current history entry"

      // no input: the form is Ready and the back proceeds
      onUi(fun () -> Briple.Trainer.Views.Routes.back router)

      let returned = waitFor(5000, path, fun current -> current = Some "/")

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
      test "the dialog answers accept and cancel through its buttons" {
        let replied, overlay, window = askDialog()

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

        // release the singleton host so the shell can mount it next
        onUi(fun () ->
          window.Content <- null
          window.Close())

        settle()
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

        let shell =
          probeUi(fun () -> Briple.Trainer.Views.Routes.build(env, stubLeadOf))

        let router = shell.Router

        let current() =
          router.Snapshot.Route
          |> ValueOption.map _.Path
          |> ValueOption.toOption

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
            let router =
              (Briple.Trainer.Views.Routes.build(env, stubLeadOf)).Router

            let outlet = RouterOutlet()
            outlet.Router <- Some router

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
