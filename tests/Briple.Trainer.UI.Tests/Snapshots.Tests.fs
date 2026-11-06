module Briple.Trainer.UI.Tests.Snapshots

// The camera checks: the harness session carries the app's theme and
// renders through Skia, so a capture is the frame a user sees. Every
// route's steady state saves one frame under artifacts/snapshots
// (gitignored): captures are evidence for a review round, never
// fixtures.

open System
open System.IO
open System.Reactive.Subjects

open Avalonia.Controls

open Expecto

open Briple.Trainer.Env
open Briple.Trainer.Strings

open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.Theme
open Briple.Trainer.UI.Tests.Fixtures
open Briple.Trainer.UI.Tests.Harness
open Briple.Trainer.Views.Generate
open Briple.Trainer.Views.Login
open Briple.Trainer.Views.Settings

let private sample() : Control =
  let panel = StackPanel()
  panel.VerticalAlignment <- Avalonia.Layout.VerticalAlignment.Center
  panel.HorizontalAlignment <- Avalonia.Layout.HorizontalAlignment.Center
  panel.Spacing <- 12.0

  let swatch = Border()
  swatch.Width <- 96.0
  swatch.Height <- 96.0
  swatch.Background <- accent
  panel.Children.Add swatch

  let label = TextBlock()
  label.Text <- "Briple Trainer"
  label.FontFamily <- font
  label.FontWeight <- Avalonia.Media.FontWeight.Bold
  panel.Children.Add label

  panel :> Control

/// True when the saved frame carries real pixels: an all-but-empty file
/// means the camera saved nothing worth reviewing.
let private assertFrameSaved(path: string) : unit =
  let bytes = File.ReadAllBytes(path)

  Expect.isTrue (bytes.Length > 1000) $"the frame saved real pixels ({path})"

/// The proof of the pipeline itself: one themed window, rendered and
/// saved. Every route's frame rides the same capture.
[<Tests>]
let captureTests =
  testList "snapshots" [
    uiTest "a themed window saves a rendered frame"
    <| fun () -> task {
      let! window = mountWindowSized(sample(), 320.0, 200.0)

      let path = capture("theme-window", window)

      let! shown =
        waitFor(
          2000,
          (fun () -> window),
          (fun w -> hasText(w, "Briple Trainer"))
        )

      Expect.isTrue shown "the window never rendered its label"

      do assertFrameSaved path
    }

    uiTest "the login page saves its frame"
    <| fun () -> task {
      let! window =
        mountWindowSized(
          LoginPage(
            LoginEnv(),
            {
              Lead = ValueNone
              OnDone = fun () -> ()
              Lifetime = testLifetime()
            }
          ),
          1000.0,
          700.0
        )

      let! rendered =
        waitFor(
          2000,
          (fun () -> window),
          (fun w -> hasText(w, get(es, Keys.loginTitle)))
        )

      Expect.isTrue rendered "the login page never rendered its title"

      let path = capture("login-page", window)
      do assertFrameSaved path
    }

    uiTest "the settings page saves its frame"
    <| fun () -> task {
      let! window =
        mountWindowSized(
          SettingsPage(
            {
              Language = Es
              Theme = System
              AgentAvailable = false
            },
            {
              Lead = ValueNone
              OnLanguage = fun _ -> ()
              OnTheme = fun _ -> ()
              OpenLogin = fun () -> ()
              Lifetime = testLifetime()
            }
          ),
          1000.0,
          700.0
        )

      let! rendered =
        waitFor(
          2000,
          (fun () -> window),
          (fun w -> hasText(w, get(es, Keys.navSettings)))
        )

      Expect.isTrue rendered "the settings page never rendered its title"

      let path = capture("settings-page", window)
      do assertFrameSaved path
    }

    uiTest "the athletes list saves its frame"
    <| fun () -> task {
      let _, culture = liveCulture es

      let roster =
        new BehaviorSubject<AthleteEntity list>(
          [
            {
              Id = Guid.NewGuid()
              Profile = {
                Name = "Ana"
                Gender = Female
                Goals = [ Strength ]
              }
              ArchivedAt = ValueNone
            }
            {
              Id = Guid.NewGuid()
              Profile = {
                Name = "Beto"
                Gender = Male
                Goals = [ Hypertrophy; WeightLoss ]
              }
              ArchivedAt = ValueNone
            }
          ]
        )

      let! control =
        probeUi(fun () ->
          AthleteList(
            athleteListEnv roster culture,
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
          (fun () -> window),
          (fun w -> hasText(w, "Ana") && hasText(w, "Beto"))
        )

      Expect.isTrue rendered "the rows did not render"

      let path = capture("athletes-list", window)
      do assertFrameSaved path
    }

    uiTest "the generate wizard saves its frame"
    <| fun () -> task {
      let _, culture = liveCulture es
      let s = fixtureStores()

      let! added = s.Athletes.Add sampleAthlete

      let athlete =
        match added with
        | Ok entity -> entity
        | Error e -> failtest $"athlete add failed: {e}"

      let gate =
        Briple.Trainer.Navigation.PageGate(
          "/athletes/test/generate",
          fun _ _ -> async { return true }
        )

      let! page =
        probeUi(fun () ->
          WizardPage(
            generateEnv s (FakeAgent noReading) culture,
            {
              Lead = ValueSome(fun () -> TextBlock(Text = "back") :> Control)
              Athlete = athlete
              Confirmed = []
              Proposals = []
              Previous = ValueNone
              Gate = gate
              Lifetime = testLifetime()
            },
            ignore
          ))

      let! window = mountWindowSized(page, 1100.0, 800.0)

      let! rendered =
        waitFor(
          3000,
          (fun () -> window),
          (fun w -> hasText(w, get(es, Keys.wizardTitle)))
        )

      Expect.isTrue rendered "the wizard never rendered its title"

      let path = capture("generate-wizard", window)
      do assertFrameSaved path
    }
  ]
