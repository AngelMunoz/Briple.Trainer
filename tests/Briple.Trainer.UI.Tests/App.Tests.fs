module Briple.Trainer.UI.Tests.App

// The UI checks: resx i18n, the athlete list view, the registration form
// as a props-in/events-out component, and the export write.

open System
open System.Globalization
open System.IO

open Avalonia.Controls
open Avalonia.VisualTree

open Expecto

open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.AppBar
open Briple.Trainer.Components.NewAthleteForm
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.UI.Tests.Harness

let private listView props = view props

let private es = Language.toCulture Es

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
            listView {
              Culture = es
              Athletes = []
              OnNew = ignore
              OnOpen = ignore
            })

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
            Variants = [ "5dias" ]
            Goals = [ Strength ]
            Focus = []
          }
        }

        let control =
          probeUi(fun () ->
            listView {
              Culture = es
              Athletes = [ entity ]
              OnNew = ignore
              OnOpen = fun id -> opened.Value <- Some id
            })

        mount control

        let rendered =
          waitFor(
            2000,
            (fun () -> control),
            (fun (c: Control) -> buttonWithText(c, "Ana") |> Option.isSome)
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

        // the name entry is the first TextBox; setting the property
        // raises TextChanged, which the form subscribed through NXUI
        onUi(fun () ->
          match textBoxes form with
          | nameEntry :: _ -> nameEntry.Text <- "Pepe"
          | [] -> failtest "the name entry did not render")

        settle()

        let genderRendered =
          waitFor(
            2000,
            (fun () -> form),
            (fun f -> buttonWithText(f, "Hombre") |> Option.isSome)
          )

        Expect.isTrue genderRendered "the gender chips did not render"

        settle()

        let genderButton =
          match probeUi(fun () -> buttonWithText(form, "Hombre")) with
          | Some button -> button
          | None -> failtest "the gender chip vanished"

        onUi(fun () -> click genderButton)

        settle()

        onUi(fun () ->
          match textBoxes form with
          | _ :: draft :: _ -> draft.Text <- "3dias"
          | _ -> failtest "the variant draft did not render"

          buttons form
          |> List.tryFind(fun button -> string button.Content = "+")
          |> Option.iter click)

        settle()

        let saveRendered =
          waitFor(
            2000,
            (fun () -> form),
            (fun f -> buttonWithText(f, "Guardar") |> Option.isSome)
          )

        Expect.isTrue saveRendered "the save button did not render"

        settle()

        let saveButton =
          match probeUi(fun () -> buttonWithText(form, "Guardar")) with
          | Some button -> button
          | None -> failtest "the save button vanished"

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

          Expect.equal athlete.Variants [ "3dias" ] "variants"
        | None -> failtest "no saved profile"
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

    testList "shell boot" [
      // no settings and no token: the shell opens on the login page
      test "the shell boots over a temp database and opens the login" {
        let db =
          Path.Combine(Path.GetTempPath(), $"briple-boot-{Guid.NewGuid():N}.db")

        match Briple.Trainer.Persistence.migrate db with
        | Error e -> failtest $"migrate failed: {e}"
        | Ok() -> ()

        let env =
          match Briple.Trainer.Env.create db with
          | Ok env -> env
          | Error e -> failtest $"boot failed: {e}"

        Expect.isFalse
          (env :> Briple.Trainer.Env.IProvideAgent).AgentAvailable
          "no settings means no agent"

        let shell =
          probeUi(fun () ->
            let routes = Briple.Trainer.Views.Routes.build env

            let bar = AppBar(es, ignore)

            let root = StackPanel()

            root.Children.Add(bar)
            root.Children.Add(routes)

            let window = Window(Content = root)
            window.Show() |> ignore

            window)

        let rendered =
          waitFor(
            5000,
            (fun () -> shell),
            (fun c -> hasText(c, get(es, Keys.loginTitle)))
          )

        Expect.isTrue rendered "the login page did not render on boot"
      }
    ]
  ]
