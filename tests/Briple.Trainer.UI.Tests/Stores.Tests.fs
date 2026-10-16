module Briple.Trainer.UI.Tests.Stores

// The observable stores through their own surface over a throwaway
// database: write-through (persist + re-emit), replay to late
// subscribers and per-athlete slices.

open System
open System.IO
open System.Threading.Tasks

open Expecto

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.UI.Tests.Harness

let private wait(task: Task<Result<'a, exn>>) : Result<'a, exn> =
  task.GetAwaiter().GetResult()

let private tempDb() : string =
  Path.Combine(Path.GetTempPath(), $"briple-stores-{Guid.NewGuid():N}.db")

let private sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Hypertrophy ]
}

let tests =
  testList "stores" [
    test "the athlete store emits the saved athlete and replays it" {
      let db = tempDb()
      migrate db |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let seen = ResizeArray<AthleteEntity list>()

      let tap = stores.Athletes.Observe() |> Observable.subscribe seen.Add

      wait(stores.Athletes.Add sampleAthlete) |> ignore

      let emitted =
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

      settle()

      Expect.isNonEmpty
        (List.ofSeq replayed)
        "a late subscriber received the snapshot"

      late.Dispose()
      tap.Dispose()
    }

    test "the memory store loads the slice per athlete and re-emits writes" {
      let db = tempDb()
      migrate db |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athleteId =
        match wait(stores.Athletes.Add sampleAthlete) with
        | Ok entity -> entity.Id
        | Error e -> failtest $"add failed: {e}"

      let seen = ResizeArray<MemoryEntity list>()

      let tap =
        stores.Memories.Observe athleteId |> Observable.subscribe seen.Add

      // the first subscription loads the (empty) slice
      let loaded =
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

      wait(
        stores.Memories.Add(
          athleteId,
          consolidate mark,
          ValueSome(Guid.NewGuid())
        )
      )
      |> ignore

      let emitted =
        waitFor(
          2000,
          (fun () -> List.ofSeq seen),
          fun slices ->
            slices |> List.exists(fun entities -> entities.Length = 1)
        )

      Expect.isTrue emitted "the memory add re-emitted the slice"

      tap.Dispose()
    }

    test "the plan store emits the saved plan slice" {
      let db = tempDb()
      migrate db |> ignore

      let stores = createStores(sqliteStore db, ignore)

      let athleteId =
        match wait(stores.Athletes.Add sampleAthlete) with
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

      wait(
        stores.Plans.Add(
          {
            AthleteId = athleteId
            Kind = "new"
            Counts = []
          },
          plan
        )
      )
      |> ignore

      let emitted =
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
  ]
