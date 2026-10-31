module Briple.Trainer.UI.Tests.Persistence

// The SQLite store against a throwaway database: every capability the UI
// consumes, asserted through the store's own surface.

open System
open System.IO
open System.Threading.Tasks

open Expecto
open Microsoft.Data.Sqlite

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let private testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

let private tempDb() : string =
  Path.Combine(Path.GetTempPath(), $"briple-ui-{Guid.NewGuid():N}.db")

let private execSql(db: string, sql: string) : unit =
  use conn = new SqliteConnection($"Data Source={db}")
  conn.Open()
  use command = conn.CreateCommand()
  command.CommandText <- sql
  command.ExecuteNonQuery() |> ignore

let private countRows(db: string, table: string) : int64 =
  use conn = new SqliteConnection($"Data Source={db}")
  conn.Open()
  use command = conn.CreateCommand()
  command.CommandText <- $"SELECT COUNT(*) FROM {table}"
  command.ExecuteScalar() :?> int64

let private sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Hypertrophy ]
}

let private samplePlan: Plan = {
  Version = 1
  Title = "Mes 1"
  Weeks = 4
  Generated = "2026-10-15"
  Origin = "trainer"
  Blocks = [
    {
      Gender = Male
      Variants = [
        {
          Id = "3dias"
          Title = Some "Tres dias"
          Weeks = [
            {
              Number = 1
              Days = [
                {
                  Id = "Lunes"
                  Title = Some "Empuje"
                  Notes = []
                  Exercises = [
                    {
                      Circuit = "A"
                      Laps = "3"
                      Order = 1
                      Name = "Press de banca"
                      Reps = "8-10"
                      Rir = "2"
                      Rest = "120s"
                      Notes = "-"
                    }
                  ]
                }
              ]
            }
          ]
        }
      ]
    }
  ]
  Extras = [ "Descanso 120s" ]
}

let private memoryFor(scope: MemoryScope) : Memory = {
  consolidate {
    Target = Exercise "Press de banca"
    Label = Light
    Date = DateOnly(2026, 10, 6)
  } with
      Scope = scope
}

let private draft(kind: string, createdAt: DateTime, plan: Plan) : PlanDraft = {
  Kind = kind
  Counts = []
  CreatedAt = ValueSome createdAt
  Plan = plan
}

let tests =
  testList "persistence" [
    test "the plan selectors skip archived plans and memories" {
      let athleteId = Guid.NewGuid()
      let activePlanId = Guid.NewGuid()
      let archivedPlanId = Guid.NewGuid()

      let activePlan: PlanEntity = {
        Id = activePlanId
        AthleteId = athleteId
        Title = "Active"
        Kind = "new"
        Weeks = 1
        Counts = []
        Raw = ""
        CreatedAt = DateTime(2026, 10, 1)
        ArchivedAt = ValueNone
      }

      let archivedPlan = {
        activePlan with
            Id = archivedPlanId
            Title = "Archived"
            ArchivedAt = ValueSome DateTime.UtcNow
      }

      Expect.equal
        (lastActivePlan [ activePlan; archivedPlan ])
        (ValueSome activePlan)
        "the newest active plan wins"

      Expect.equal
        (lastActivePlan [ archivedPlan ])
        ValueNone
        "an archived-only list has no active plan"

      let memoryEntity
        (id: Guid)
        (planId: Guid voption)
        (status: MemoryStatus)
        (archivedAt: DateTime voption)
        : MemoryEntity = {
        Id = id
        AthleteId = athleteId
        Memory = {
          memoryFor Athlete with
              Status = status
        }
        PlanId = planId
        CreatedAt = DateTime.UtcNow
        ArchivedAt = archivedAt
      }

      let visible =
        memoryEntity
          (Guid.NewGuid())
          (ValueSome activePlanId)
          Proposed
          ValueNone

      let archivedMemory =
        memoryEntity
          (Guid.NewGuid())
          (ValueSome activePlanId)
          Proposed
          (ValueSome DateTime.UtcNow)

      let onArchivedPlan =
        memoryEntity
          (Guid.NewGuid())
          (ValueSome archivedPlanId)
          Proposed
          ValueNone

      let athleteScoped =
        memoryEntity (Guid.NewGuid()) ValueNone Proposed ValueNone

      let proposals =
        activeProposals(
          [ activePlan; archivedPlan ],
          [ visible; archivedMemory; onArchivedPlan; athleteScoped ]
        )

      Expect.equal
        (proposals |> List.map _.Id)
        [ visible.Id; athleteScoped.Id ]
        "only active proposals on active plans travel"
    }

    test "migrate creates the schema and the store round-trips an athlete" {
      let db = tempDb()

      match migrate(testLogger, db) with
      | Ok() -> ()
      | Error e -> failtest $"migrate failed: {e}"

      let store = sqliteStore db

      match wait(store.AddAthlete sampleAthlete) with
      | Error e -> failtest $"add failed: {e}"
      | Ok entity ->
        Expect.equal entity.Profile.Name "Pepe" "name"

        match wait(store.FindAthlete entity.Id) with
        | Error e -> failtest $"find failed: {e}"
        | Ok ValueNone -> failtest "the athlete vanished"
        | Ok(ValueSome loaded) ->
          Expect.equal loaded.Profile.Goals [ Hypertrophy ] "goals"
    }

    test "memories round-trip through confirm" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athleteId =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let mark = {
        Target = Exercise "Press de banca"
        Label = Light
        Date = DateOnly(2026, 10, 6)
      }

      let planId = Guid.NewGuid()

      match
        wait(store.AddMemory(athleteId, consolidate mark, ValueSome planId))
      with
      | Error e -> failtest $"add memory failed: {e}"
      | Ok memoryEntity ->
        Expect.equal memoryEntity.Memory.Status Proposed "born proposed"

        Expect.equal memoryEntity.Memory.Scope Plan "a mark is situational"

        Expect.equal memoryEntity.PlanId (ValueSome planId) "the plan rides"


        Expect.notEqual memoryEntity.Memory.Deltas [] "light carries a delta"

        let confirmed = {
          memoryEntity with
              Memory = confirm memoryEntity.Memory.Deltas memoryEntity.Memory
        }

        match wait(store.UpdateMemory confirmed) with
        | Error e -> failtest $"update failed: {e}"
        | Ok() ->
          match wait(store.ListMemories athleteId) with
          | Error e -> failtest $"list failed: {e}"
          | Ok [] -> failtest "the memory vanished"
          | Ok(entity :: _) ->
            Expect.equal entity.Memory.Status Confirmed "confirmed"
    }

    test "plans store the raw text verbatim and update it" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athleteId =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      match
        wait(
          store.AddPlan(
            {
              AthleteId = athleteId
              Kind = "new"
              Counts = [ "3dias", 1 ]
              CreatedAt = ValueNone
            },
            samplePlan
          )
        )
      with
      | Error e -> failtest $"add plan failed: {e}"
      | Ok entity ->
        Expect.stringContains
          entity.Raw
          "Press de banca"
          "raw carries the exercise"

        Expect.equal entity.Counts [ "3dias", 1 ] "counts"

        match wait(store.FindPlan entity.Id) with
        | Error e -> failtest $"find failed: {e}"
        | Ok ValueNone -> failtest "the plan vanished"
        | Ok(ValueSome loaded) ->
          Expect.equal loaded.Raw entity.Raw "raw is verbatim"

          match wait(store.UpdatePlanRaw(entity.Id, "nuevo raw")) with
          | Error e -> failtest $"update failed: {e}"
          | Ok() ->
            match wait(store.FindPlan entity.Id) with
            | Error e -> failtest $"find failed: {e}"
            | Ok ValueNone -> failtest "the plan vanished"
            | Ok(ValueSome reloaded) ->
              Expect.equal reloaded.Raw "nuevo raw" "raw updated"
    }

    test "requests record the audit line" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athleteId =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      match
        wait(store.AddRequest(athleteId, "new", "goal=hypertrophy;weeks=4"))
      with
      | Error e -> failtest $"add request failed: {e}"
      | Ok() -> ()
    }

    test "archive and restore flag every aggregate" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athlete =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add athlete failed: {e}"

      let plan =
        match
          wait(
            store.AddPlan(
              {
                AthleteId = athlete.Id
                Kind = "new"
                Counts = []
                CreatedAt = ValueNone
              },
              samplePlan
            )
          )
        with
        | Ok entity -> entity
        | Error e -> failtest $"add plan failed: {e}"

      let planMemory =
        match
          wait(store.AddMemory(athlete.Id, memoryFor Plan, ValueSome plan.Id))
        with
        | Ok entity -> entity
        | Error e -> failtest $"add memory failed: {e}"

      let athleteMemory =
        match
          wait(store.AddMemory(athlete.Id, memoryFor Athlete, ValueNone))
        with
        | Ok entity -> entity
        | Error e -> failtest $"add memory failed: {e}"

      let step(result: Result<unit, exn>) =
        match result with
        | Ok() -> ()
        | Error e -> failtest $"archive step failed: {e}"

      step(wait(store.ArchiveAthlete athlete.Id))
      step(wait(store.ArchivePlan plan.Id))
      step(wait(store.ArchiveMemory planMemory.Id))
      step(wait(store.ArchiveMemory athleteMemory.Id))

      match wait(store.FindAthlete athlete.Id) with
      | Ok(ValueSome saved) ->
        Expect.isTrue
          (ValueOption.isSome saved.ArchivedAt)
          "the athlete carries archived_at"
      | _ -> failtest "the archived athlete vanished"

      match wait(store.FindPlan plan.Id) with
      | Ok(ValueSome saved) ->
        Expect.isTrue
          (ValueOption.isSome saved.ArchivedAt)
          "the plan carries archived_at"
      | _ -> failtest "the archived plan vanished"

      let memories =
        match wait(store.ListMemories athlete.Id) with
        | Ok entities -> entities
        | Error e -> failtest $"list memories failed: {e}"

      Expect.isTrue
        (memories
         |> List.forall(fun memory -> ValueOption.isSome memory.ArchivedAt))
        "every memory carries archived_at"

      step(wait(store.RestoreAthlete athlete.Id))
      step(wait(store.RestorePlan plan.Id))
      step(wait(store.RestoreMemory planMemory.Id))
      step(wait(store.RestoreMemory athleteMemory.Id))

      let athletes =
        match wait(store.ListAthletes()) with
        | Ok entities -> entities
        | Error e -> failtest $"list athletes failed: {e}"

      let plans =
        match wait(store.ListPlans athlete.Id) with
        | Ok entities -> entities
        | Error e -> failtest $"list plans failed: {e}"

      let memories =
        match wait(store.ListMemories athlete.Id) with
        | Ok entities -> entities
        | Error e -> failtest $"list memories failed: {e}"

      Expect.isTrue
        (athletes
         |> List.forall(fun entity -> ValueOption.isNone entity.ArchivedAt))
        "restore clears every athlete flag"

      Expect.isTrue
        (plans
         |> List.forall(fun entity -> ValueOption.isNone entity.ArchivedAt))
        "restore clears every plan flag"

      Expect.isTrue
        (memories
         |> List.forall(fun entity -> ValueOption.isNone entity.ArchivedAt))
        "restore clears every memory flag"
    }

    test "deleting a plan keeps athlete-scoped memories" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athlete =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add athlete failed: {e}"

      let plan =
        match
          wait(
            store.AddPlan(
              {
                AthleteId = athlete.Id
                Kind = "new"
                Counts = []
                CreatedAt = ValueNone
              },
              samplePlan
            )
          )
        with
        | Ok entity -> entity
        | Error e -> failtest $"add plan failed: {e}"

      let planMemory =
        match
          wait(store.AddMemory(athlete.Id, memoryFor Plan, ValueSome plan.Id))
        with
        | Ok entity -> entity
        | Error e -> failtest $"add memory failed: {e}"

      let athleteMemory =
        match
          wait(store.AddMemory(athlete.Id, memoryFor Athlete, ValueNone))
        with
        | Ok entity -> entity
        | Error e -> failtest $"add memory failed: {e}"

      match wait(store.DeletePlan plan.Id) with
      | Ok() -> ()
      | Error e -> failtest $"delete plan failed: {e}"

      match wait(store.ListPlans athlete.Id) with
      | Ok [] -> ()
      | Ok _ -> failtest "the plan survived the delete"
      | Error e -> failtest $"list plans failed: {e}"

      match wait(store.ListMemories athlete.Id) with
      | Ok memories ->
        Expect.equal
          (memories |> List.map(fun entity -> entity.Id))
          [ athleteMemory.Id ]
          "the plan-scoped memory left, the athlete-scoped one stayed"

        Expect.isFalse
          (memories |> List.exists(fun entity -> entity.Id = planMemory.Id))
          "the plan-scoped memory is gone"
      | Error e -> failtest $"list memories failed: {e}"

      match wait(store.FindPlan plan.Id) with
      | Ok ValueNone -> ()
      | Ok(ValueSome _) -> failtest "the plan is still findable"
      | Error e -> failtest $"find plan failed: {e}"
    }

    test "deleting an athlete removes the whole aggregate" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let athlete =
        match wait(store.AddAthlete sampleAthlete) with
        | Ok entity -> entity
        | Error e -> failtest $"add athlete failed: {e}"

      let plan =
        match
          wait(
            store.AddPlan(
              {
                AthleteId = athlete.Id
                Kind = "new"
                Counts = []
                CreatedAt = ValueNone
              },
              samplePlan
            )
          )
        with
        | Ok entity -> entity
        | Error e -> failtest $"add plan failed: {e}"

      match
        wait(store.AddMemory(athlete.Id, memoryFor Plan, ValueSome plan.Id))
      with
      | Ok _ -> ()
      | Error e -> failtest $"add memory failed: {e}"

      match wait(store.AddRequest(athlete.Id, "new", "payload")) with
      | Ok() -> ()
      | Error e -> failtest $"add request failed: {e}"

      match wait(store.DeleteAthlete athlete.Id) with
      | Ok() -> ()
      | Error e -> failtest $"delete athlete failed: {e}"

      match wait(store.ListAthletes()) with
      | Ok [] -> ()
      | Ok _ -> failtest "the athlete survived the delete"
      | Error e -> failtest $"list athletes failed: {e}"

      match wait(store.ListPlans athlete.Id) with
      | Ok [] -> ()
      | Ok _ -> failtest "the plans survived the athlete delete"
      | Error e -> failtest $"list plans failed: {e}"

      match wait(store.ListMemories athlete.Id) with
      | Ok [] -> ()
      | Ok _ -> failtest "the memories survived the athlete delete"
      | Error e -> failtest $"list memories failed: {e}"

      Expect.equal
        (countRows(db, "requests"))
        0L
        "the requests left with the athlete"
    }

    test "imports keep the created order and append atomically" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db

      let planA = { samplePlan with Title = "A" }
      let planB = { samplePlan with Title = "B" }
      let planC = { samplePlan with Title = "C" }
      let planD = { samplePlan with Title = "D" }

      let early = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
      let late = DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)

      let athlete =
        match
          wait(
            store.AddAthleteWithPlans(
              sampleAthlete,
              [
                draft("import", late, planC)
                draft("import", early, planA)
                draft("import", early, planB)
              ]
            )
          )
        with
        | Ok entity -> entity
        | Error e -> failtest $"import athlete failed: {e}"

      match wait(store.ListPlans athlete.Id) with
      | Ok plans ->
        Expect.equal
          (plans |> List.map(fun entity -> entity.Title))
          [ "A"; "B"; "C" ]
          "created_at then rowid orders the import"
      | Error e -> failtest $"list plans failed: {e}"

      match
        wait(store.AddPlans(athlete.Id, [ draft("import", early, planD) ]))
      with
      | Ok [ entity ] ->
        Expect.equal entity.Title "D" "the appended plan came back"
      | Ok _ -> failtest "the append returned the wrong plans"
      | Error e -> failtest $"append plans failed: {e}"

      match wait(store.ListPlans athlete.Id) with
      | Ok plans ->
        Expect.equal
          (plans |> List.map(fun entity -> entity.Title))
          [ "A"; "B"; "D"; "C" ]
          "the appended row keeps the created order"
      | Error e -> failtest $"list plans failed: {e}"
    }

    test "a failed import leaves no athlete and no plans" {
      let db = tempDb()

      migrate(testLogger, db) |> ignore

      let store = sqliteStore db
      let valid = draft("import", DateTime.UtcNow, samplePlan)

      execSql(
        db,
        "CREATE TRIGGER fail_plans BEFORE INSERT ON plans BEGIN SELECT RAISE(ABORT, 'boom'); END;"
      )

      match
        wait(store.AddAthleteWithPlans(sampleAthlete, [ valid; valid ]))
      with
      | Ok _ -> failtest "the import should have failed"
      | Error _ -> ()

      match wait(store.ListAthletes()) with
      | Ok [] -> ()
      | Ok _ -> failtest "the failed import left an athlete"
      | Error e -> failtest $"list athletes failed: {e}"

      Expect.equal (countRows(db, "plans")) 0L "the failed import left no plans"
    }

    test "the migration applies to a copy of the real database" {
      let real = defaultDbPath()

      if File.Exists real then
        let copy = tempDb()
        File.Copy(real, copy)
        let before = File.GetLastWriteTimeUtc real

        match migrate(testLogger, copy) with
        | Error e -> failtest $"migration on the copy failed: {e}"
        | Ok() ->
          let store = sqliteStore copy

          match wait(store.ListAthletes()) with
          | Ok _ -> ()
          | Error e -> failtest $"the migrated copy is unreadable: {e}"

        SqliteConnection.ClearAllPools()
        File.Delete copy

        Expect.equal
          (File.GetLastWriteTimeUtc real)
          before
          "the real database stays untouched"
    }
  ]
