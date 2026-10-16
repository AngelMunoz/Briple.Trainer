module Briple.Trainer.UI.Tests.Persistence

// The SQLite store against a throwaway database: every capability the UI
// consumes, asserted through the store's own surface.

open System
open System.IO
open System.Threading.Tasks

open Expecto

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

let private tempDb() : string =
  Path.Combine(Path.GetTempPath(), $"briple-ui-{Guid.NewGuid():N}.db")

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

let tests =
  testList "persistence" [
    test "migrate creates the schema and the store round-trips an athlete" {
      let db = tempDb()

      match migrate db with
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

      migrate db |> ignore

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

      migrate db |> ignore

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

      migrate db |> ignore

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
  ]
