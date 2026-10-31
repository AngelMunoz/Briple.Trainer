module Briple.Trainer.UI.Tests.Stores

// The observable stores through their own surface over a throwaway
// database: write-through (persist + re-emit), replay to late
// subscribers and per-athlete slices.

open System
open System.IO

open Expecto

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Harness

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let private testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

let private tempDb() : string =
  Path.Combine(Path.GetTempPath(), $"briple-stores-{Guid.NewGuid():N}.db")

let private sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Hypertrophy ]
}

let private samplePlan: Plan = {
  Version = 1
  Title = "Mes 1"
  Weeks = 1
  Generated = "2026-10-15"
  Origin = "trainer"
  Blocks = []
  Extras = []
}

let private draft(kind: string, createdAt: DateTime, plan: Plan) : PlanDraft = {
  Kind = kind
  Counts = []
  CreatedAt = ValueSome createdAt
  Plan = plan
}

let private memoryFor(scope: MemoryScope) : Memory = {
  consolidate {
    Target = Exercise "Press de banca"
    Label = Light
    Date = DateOnly(2026, 10, 6)
  } with
      Scope = scope
}

let private ok(result: Result<'a, exn>) : 'a =
  match result with
  | Ok value -> value
  | Error e -> failtest $"operation failed: {e}"

let private unitOk(result: Result<unit, exn>) : unit = ok result

let tests =
  testList "stores" [
    uiTest "the athlete store emits the saved athlete and replays it"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let seen = ResizeArray<AthleteEntity list>()

      let tap = stores.Athletes.Observe() |> Observable.subscribe seen.Add

      let! _ = stores.Athletes.Add sampleAthlete

      let! emitted =
        waitFor(
          2000,
          (fun () -> List.ofSeq seen),
          fun lists ->
            lists
            |> List.exists(fun list ->
              list |> List.exists(fun entity -> entity.Profile.Name = "Pepe"))
        )

      Expect.isTrue emitted "the add reached the subscribers"

      // a late subscriber gets the current snapshot through replay
      let replayed = ResizeArray<AthleteEntity list>()

      let late = stores.Athletes.Observe() |> Observable.subscribe replayed.Add

      let! replayedOk =
        waitFor(
          2000,
          (fun () -> List.ofSeq replayed),
          fun lists -> not(List.isEmpty lists)
        )

      Expect.isTrue replayedOk "a late subscriber received the snapshot"

      late.Dispose()
      tap.Dispose()
    }

    uiTest "the memory store loads the slice per athlete and re-emits writes"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete

      let athleteId =
        match athleteResult with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let seen = ResizeArray<MemoryEntity list>()

      let tap =
        stores.Memories.Observe athleteId |> Observable.subscribe seen.Add

      // the first subscription loads the (empty) slice
      let! loaded =
        waitFor(
          2000,
          (fun () -> List.ofSeq seen),
          fun slices -> slices |> List.exists List.isEmpty
        )

      Expect.isTrue loaded "the initial slice load arrived"

      let mark = {
        Target = Exercise "Press de banca"
        Label = Light
        Date = DateOnly(2026, 10, 6)
      }

      let! _ =
        stores.Memories.Add(
          athleteId,
          consolidate mark,
          ValueSome(Guid.NewGuid())
        )

      let! emitted =
        waitFor(
          2000,
          (fun () -> List.ofSeq seen),
          fun slices ->
            slices |> List.exists(fun entities -> entities.Length = 1)
        )

      Expect.isTrue emitted "the memory add re-emitted the slice"

      tap.Dispose()
    }

    uiTest "the plan store emits the saved plan slice"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete

      let athleteId =
        match athleteResult with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let plan: Plan = {
        Version = 1
        Title = "Mes 1"
        Weeks = 1
        Generated = "2026-10-15"
        Origin = "trainer"
        Blocks = []
        Extras = []
      }

      let seen = ResizeArray<PlanEntity list>()

      let tap = stores.Plans.Observe athleteId |> Observable.subscribe seen.Add

      let! _ =
        stores.Plans.Add(
          {
            AthleteId = athleteId
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          plan
        )

      let! emitted =
        waitFor(
          2000,
          (fun () -> List.ofSeq seen),
          fun slices ->
            slices
            |> List.exists(fun entities ->
              entities |> List.exists(fun entity -> entity.Title = "Mes 1"))
        )

      Expect.isTrue emitted "the plan add re-emitted the slice"

      tap.Dispose()
    }

    uiTest "the athlete store splits active and archived rows"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let activeSeen = ResizeArray<AthleteEntity list>()
      let archivedSeen = ResizeArray<AthleteEntity list>()

      let activeTap =
        stores.Athletes.Observe() |> Observable.subscribe activeSeen.Add

      let archivedTap =
        stores.Athletes.ObserveArchived()
        |> Observable.subscribe archivedSeen.Add

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      let! firstActive =
        waitFor(
          2000,
          (fun () -> stores.Athletes.Snapshot()),
          fun athletes ->
            athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
        )

      Expect.isTrue firstActive "the new athlete is active"

      let! archiveResult = stores.Athletes.Archive athlete.Id
      unitOk archiveResult

      let! archived =
        waitFor(
          2000,
          (fun () -> stores.Athletes.SnapshotArchived()),
          fun athletes ->
            athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
        )

      Expect.isTrue archived "the archived athlete moved to the archived slice"

      let! activeEmpty =
        waitFor(2000, (fun () -> stores.Athletes.Snapshot()), List.isEmpty)

      Expect.isTrue activeEmpty "the archived athlete left the active slice"

      let! archivedEmission =
        waitFor(
          2000,
          (fun () -> List.ofSeq archivedSeen),
          fun lists ->
            lists
            |> List.exists(fun athletes ->
              athletes |> List.exists(fun entity -> entity.Id = athlete.Id))
        )

      Expect.isTrue archivedEmission "the archived slice emitted"

      let! restoreResult = stores.Athletes.Restore athlete.Id
      unitOk restoreResult

      let! restored =
        waitFor(
          2000,
          (fun () -> stores.Athletes.Snapshot()),
          fun athletes ->
            athletes |> List.exists(fun entity -> entity.Id = athlete.Id)
        )

      Expect.isTrue restored "restore moved the athlete back"

      let! archivedEmpty =
        waitFor(
          2000,
          (fun () -> stores.Athletes.SnapshotArchived()),
          List.isEmpty
        )

      Expect.isTrue archivedEmpty "the archived slice is empty after restore"

      activeTap.Dispose()
      archivedTap.Dispose()
    }

    uiTest "memory and plan actions move rows between the slices"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      let! planResult =
        stores.Plans.Add(
          {
            AthleteId = athlete.Id
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          samplePlan
        )

      let plan = ok planResult

      let! planMemoryResult =
        stores.Memories.Add(athlete.Id, memoryFor Plan, ValueSome plan.Id)

      let planMemory = ok planMemoryResult

      let! memoryArchiveResult =
        stores.Memories.Archive(athlete.Id, planMemory.Id)

      unitOk memoryArchiveResult

      let! memoryArchived =
        waitFor(
          2000,
          (fun () -> stores.Memories.SnapshotArchived athlete.Id),
          fun entities ->
            entities |> List.exists(fun entity -> entity.Id = planMemory.Id)
        )

      Expect.isTrue
        memoryArchived
        "the archived memory moved to the archived slice"

      let! memoryActive =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athlete.Id),
          fun entities ->
            entities |> List.forall(fun entity -> entity.Id <> planMemory.Id)
        )

      Expect.isTrue memoryActive "the archived memory left the active slice"

      let! memoryRestoreResult =
        stores.Memories.Restore(athlete.Id, planMemory.Id)

      unitOk memoryRestoreResult

      let! memoryRestored =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athlete.Id),
          fun entities ->
            entities |> List.exists(fun entity -> entity.Id = planMemory.Id)
        )

      Expect.isTrue memoryRestored "restore moved the memory back"

      let! planArchiveResult = stores.Plans.Archive plan.Id
      unitOk planArchiveResult

      let! planArchived =
        waitFor(
          2000,
          (fun () -> stores.Plans.SnapshotArchived athlete.Id),
          fun plans -> plans |> List.exists(fun entity -> entity.Id = plan.Id)
        )

      Expect.isTrue planArchived "the archived plan moved to the archived slice"

      let! planActiveEmpty =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue planActiveEmpty "the archived plan left the active slice"

      let! planRestoreResult = stores.Plans.Restore plan.Id
      unitOk planRestoreResult

      let! planRestored =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans -> plans |> List.exists(fun entity -> entity.Id = plan.Id)
        )

      Expect.isTrue planRestored "restore moved the plan back"
    }

    uiTest "deletePlan keeps the athlete-scoped memories"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      let! planResult =
        stores.Plans.Add(
          {
            AthleteId = athlete.Id
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          samplePlan
        )

      let plan = ok planResult

      let! planMemoryResult =
        stores.Memories.Add(athlete.Id, memoryFor Plan, ValueSome plan.Id)

      let planMemory = ok planMemoryResult

      let! athleteMemoryResult =
        stores.Memories.Add(athlete.Id, memoryFor Athlete, ValueNone)

      let athleteMemory = ok athleteMemoryResult

      let! deletePlanResult = deletePlan(stores, plan.Id)
      unitOk deletePlanResult

      let! plansGone =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue plansGone "the deleted plan left the active slice"

      let! memories =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athlete.Id),
          fun entities ->
            entities |> List.exists(fun entity -> entity.Id = athleteMemory.Id)
        )

      Expect.isTrue memories "the athlete-scoped memory stayed"

      let current = stores.Memories.Snapshot athlete.Id

      Expect.isFalse
        (current |> List.exists(fun entity -> entity.Id = planMemory.Id))
        "the plan-scoped memory is gone"
    }

    uiTest "deleteAthlete forgets the memory and plan slices"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      let! planResult =
        stores.Plans.Add(
          {
            AthleteId = athlete.Id
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          samplePlan
        )

      let plan = ok planResult

      let! _ =
        stores.Memories.Add(athlete.Id, memoryFor Plan, ValueSome plan.Id)

      let! deleteAthleteResult = deleteAthlete(stores, athlete.Id)
      unitOk deleteAthleteResult

      let! athletesEmpty =
        waitFor(2000, (fun () -> stores.Athletes.Snapshot()), List.isEmpty)

      Expect.isTrue athletesEmpty "the athlete left the active slice"

      let! plansEmpty =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue plansEmpty "the plan slice is empty"

      let! memoriesEmpty =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue memoriesEmpty "the memory slice is empty"
    }

    uiTest "importAthlete and importPlans fill the active slices"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)
      let early = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
      let late = DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)

      let! athleteResult =
        importAthlete(
          stores,
          sampleAthlete,
          [
            draft("import", late, { samplePlan with Title = "C" })
            draft("import", early, { samplePlan with Title = "A" })
            draft("import", early, { samplePlan with Title = "B" })
          ]
        )

      let athlete = ok athleteResult

      let! imported =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans -> plans.Length = 3
        )

      Expect.isTrue imported "the import filled the active plan slice"

      Expect.equal
        (stores.Plans.Snapshot athlete.Id
         |> List.map(fun entity -> entity.Title))
        [ "A"; "B"; "C" ]
        "the slice keeps created_at then rowid order"

      let! _ =
        importPlans(
          stores,
          athlete.Id,
          [ draft("import", early, { samplePlan with Title = "D" }) ]
        )

      let! appended =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans -> plans.Length = 4
        )

      Expect.isTrue appended "the appended import filled the slice"
    }

    uiTest "archiving an athlete keeps its plans and memories"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      let! planResult =
        stores.Plans.Add(
          {
            AthleteId = athlete.Id
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          samplePlan
        )

      let plan = ok planResult

      let! memoryResult =
        stores.Memories.Add(athlete.Id, memoryFor Plan, ValueSome plan.Id)

      let memory = ok memoryResult

      let! archiveResult = stores.Athletes.Archive athlete.Id
      unitOk archiveResult

      let! plansKept =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans -> plans |> List.exists(fun entity -> entity.Id = plan.Id)
        )

      Expect.isTrue plansKept "archiving the athlete removed a plan"

      let! memoriesKept =
        waitFor(
          2000,
          (fun () -> stores.Memories.Snapshot athlete.Id),
          fun memories ->
            memories |> List.exists(fun entity -> entity.Id = memory.Id)
        )

      Expect.isTrue memoriesKept "archiving the athlete removed a memory"
    }

    uiTest "Refresh reloads a slice and Forget drops it"
    <| fun () -> task {
      let db = tempDb()
      migrate(testLogger, db) |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let! athleteResult = stores.Athletes.Add sampleAthlete
      let athlete = ok athleteResult

      // load the empty slice, then write behind the store's back
      let! empty =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue empty "the plan slice starts empty"

      let! planResult =
        stores.Raw.AddPlan(
          {
            AthleteId = athlete.Id
            Kind = "new"
            Counts = []
            CreatedAt = ValueNone
          },
          samplePlan
        )

      let plan = ok planResult

      let! refreshResult = stores.Plans.Refresh athlete.Id
      unitOk refreshResult

      let! refreshed =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          fun plans -> plans |> List.exists(fun entity -> entity.Id = plan.Id)
        )

      Expect.isTrue refreshed "Refresh reloaded the slice"

      let! deleteResult = stores.Raw.DeletePlan plan.Id
      unitOk deleteResult

      stores.Plans.Forget athlete.Id

      let! forgotten =
        waitFor(
          2000,
          (fun () -> stores.Plans.Snapshot athlete.Id),
          List.isEmpty
        )

      Expect.isTrue forgotten "Forget dropped the cached slice"
    }
  ]
