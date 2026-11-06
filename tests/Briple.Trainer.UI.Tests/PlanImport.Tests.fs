module Briple.Trainer.UI.Tests.PlanImport.Tests

// The import page's model over the shared multi-plan fixture, and one
// draft landing in the store as an "import" plan row. The picker itself
// needs a host window, so the page's pure model carries these paths.

open System
open System.Threading.Tasks

open Avalonia.Controls
open Expecto

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Strings
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Fixtures
open Briple.Trainer.UI.Tests.Harness

let private male: Athlete = sampleAthlete

let private female: Athlete = { sampleAthlete with Gender = Female }

let private document() =
  match Briple.Trainer.Views.Import.documentOf importRaw with
  | Briple.Trainer.Views.Import.Readable parsed -> parsed
  | other -> failtest $"the import fixture did not parse: {other}"

let private testListName = "import page model"

/// The CPU the process burns over one idle window. A review page that
/// loops its message queue pegs one core; a quiet one stays near zero.
let private cpuOver(ms: int) : Task<float> = task {
  let before = Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
  do! Task.Delay ms
  let after = Diagnostics.Process.GetCurrentProcess().TotalProcessorTime
  return (after - before).TotalMilliseconds
}

/// One review page over one raw plan text, mounted and left idle.
let private reviewOver(raw: string) : Task<float * Control> = task {
  let stores = fixtureStores()

  let! added = stores.Athletes.Add male

  let athlete =
    match added with
    | Ok entity -> entity
    | Error e -> failtest $"athlete add failed: {e}"

  let entity = {
    Id = Guid.NewGuid()
    AthleteId = athlete.Id
    Title = "Repro"
    Kind = "import"
    Weeks = 4
    Counts = []
    Raw = raw
    CreatedAt = DateTime.UtcNow
    ArchivedAt = ValueNone
  }

  let _, culture = liveCulture es

  let gate =
    Briple.Trainer.Navigation.PageGate(
      "/review/repro",
      fun _ _ -> async { return true }
    )

  let! page =
    probeUi(fun () ->
      Briple.Trainer.Views.Review.ReviewPage(
        reviewEnv stores (FakeAgent noReading) culture,
        {
          Lead = ValueNone
          Entity = entity
          Gate = gate
          Lifetime = testLifetime()
        }
      ))

  do! mount page
  let! cpu = cpuOver 2000
  return cpu, page :> Control
}

let tests =
  testList testListName [
    test "a readable file parses with no warnings" {
      let parsed = document()

      Expect.equal parsed.Warnings [] "no warnings in the fixture"

      Expect.equal parsed.Plan.Title "Importable" "the title"
    }

    test "a broken file is unreadable" {
      match Briple.Trainer.Views.Import.documentOf "#start plan v1" with
      | Briple.Trainer.Views.Import.Unreadable _ -> ()
      | other -> failtest $"expected Unreadable, got {other}"
    }

    test "a file with a warning stays readable" {
      // no #meta semanas: the parser warns at #end plan instead of failing
      let withWarning =
        String.concat "\n" [
          "#start plan v1"
          "#meta titulo Con avisos"
          "#meta generado 2026-11-06"
          "#meta origen warning fixture"
          "#start genero hombre"
          "#start opcion 3dias"
          "#start semana 1"
          "#start dia Lunes"
          "#ej A|3|1|Press|8-10|2|90 s|-"
          "#end dia"
          "#end semana"
          "#end opcion"
          "#end genero"
          "#end plan"
        ]

      match Briple.Trainer.Views.Import.documentOf withWarning with
      | Briple.Trainer.Views.Import.Readable parsed ->
        Expect.isNonEmpty parsed.Warnings "the warning is visible"
      | other -> failtest $"expected Readable, got {other}"
    }

    test "variantsFor lists the athlete's gender block" {
      let plan = (document()).Plan

      Expect.equal
        (Briple.Trainer.Views.Import.variantsFor(male, plan) |> List.map _.Id)
        [ "3dias"; "5dias" ]
        "the man variants"

      Expect.equal
        (Briple.Trainer.Views.Import.variantsFor(female, plan) |> List.map _.Id)
        [ "5dias" ]
        "the woman variants"
    }

    test "draftOf builds the import draft of one variant" {
      let plan = (document()).Plan

      match Briple.Trainer.Views.Import.draftOf(male, "5dias", plan) with
      | Some draft ->
        Expect.equal draft.Kind "import" "the kind"

        Expect.equal draft.Counts [ ("5dias", 3) ] "the counts"

        Expect.equal
          (draft.Plan.Blocks
           |> List.collect(fun b ->
             b.Variants |> List.map(fun v -> b.Gender, v.Id)))
          [ (Male, "5dias") ]
          "one block, one variant"

        Expect.equal draft.Plan.Title "Importable" "the metadata stays"
      | None -> failtest "the draft was None"
    }

    test "draftOf is None for a missing variant" {
      let plan = (document()).Plan

      Expect.equal
        (Briple.Trainer.Views.Import.draftOf(male, "2dias", plan))
        None
        "no such variant"
    }

    uiTest "one draft lands as an imported plan row"
    <| fun () -> task {
      let stores = fixtureStores()

      let! added = stores.Athletes.Add male

      let athlete =
        match added with
        | Ok entity -> entity
        | Error e -> failtest $"athlete add failed: {e}"

      let draft =
        match
          Briple.Trainer.Views.Import.draftOf(male, "5dias", (document()).Plan)
        with
        | Some draft -> draft
        | None -> failtest "the draft was None"

      let! result = stores.Plans.Import(athlete.Id, [ draft ])

      Expect.isOk result "the import wrote"

      let! landed =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans ->
            plans
            |> List.exists(fun entity ->
              entity.Kind = "import" && entity.Title = "Importable")
        )

      Expect.isTrue landed "the imported row is in the slice"
    }

    uiTest "the page renders its empty state and the pick command"
    <| fun () -> task {
      let _, culture = liveCulture es
      let plans = (fixtureStores()).Plans

      let env: Briple.Trainer.Views.Import.ImportEnv = {
        Plans = plans
        Host = ValueNone
        Logger = testLogger
        Toasts = quietToasts
        Culture = culture.Current()
        CultureChanged = culture.Changed
      }

      let athlete = {
        Id = Guid.NewGuid()
        Profile = male
        ArchivedAt = ValueNone
      }

      let! page =
        probeUi(fun () ->
          Briple.Trainer.Views.Import.ImportPage(
            env,
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
          fun page ->
            buttonWithText(page, get(es, Keys.importPick)) |> Option.isSome
        )

      Expect.isTrue rendered "the pick command did not render"

      Expect.isTrue
        (hasText(page, get(es, Keys.importEmptyHint)))
        "the empty hint did not render"
    }

    uiTest "a sample-plan review stays idle after mounting"
    <| fun () -> task {
      let! cpu, _ = reviewOver sampleRaw

      Expect.isLessThan
        cpu
        1500.0
        $"the sample review burned {cpu}ms of CPU over an idle window"
    }

    uiTest "an imported-plan review stays idle after mounting"
    <| fun () -> task {
      let draft =
        match
          Briple.Trainer.Views.Import.draftOf(male, "3dias", (document()).Plan)
        with
        | Some draft -> draft
        | None -> failtest "the draft was None"

      let raw = Briple.Trainer.Core.Plan.Writer.write draft.Plan

      let! cpu, _ = reviewOver raw

      Expect.isLessThan
        cpu
        1500.0
        $"the imported review burned {cpu}ms of CPU over an idle window"
    }
  ]
