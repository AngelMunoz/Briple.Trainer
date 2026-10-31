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
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.UI.Tests.Harness

let private es = Language.toCulture Es

let private athleteListEnv
  (athletes: BehaviorSubject<AthleteEntity list>)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : AthleteListEnv = {
  Observe = fun () -> athletes :> IObservable<_>
  Snapshot = fun () -> athletes.Value
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

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

      uiTest "rows of different lengths share one chevron edge"
      <| fun () -> task {
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

        let! (anaLeft,
              anaRight,
              anaChevron,
              anaTop,
              anaHeight,
              anaIconTop,
              anaIconHeight) =
          measure "Ana"

        let! (betoLeft,
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

      uiTest "the page fills a wide window to its edge"
      <| fun () -> task {
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
