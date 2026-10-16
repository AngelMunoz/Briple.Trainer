module Briple.Trainer.Stores

// The observable data layer. Each store owns one aggregate as a
// BehaviorSubject snapshot; writes persist through the raw IStore, then
// reload the aggregate and notify the subject on the UI thread (Avalonia
// bindings consume emissions there). BehaviorSubject replays the last
// snapshot to late subscribers, which is exactly what a cached route
// needs when it comes back.

open System
open System.Collections.Generic
open System.Threading.Tasks

open System.Reactive.Subjects

open Avalonia.Threading
open IcedTasks
open IcedTasks.Polyfill.Task.Tasks

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence

/// Emits on the UI thread; Post works from any thread, including the
/// one the dispatcher runs on.
let emitOnUi(subject: BehaviorSubject<'a>, value: 'a) : unit =
  Dispatcher.UIThread.Post(fun () -> subject.OnNext value)

type AthleteStore
  (store: IStore, [<InlineIfLambda>] onError: (string * exn) -> unit) =

  let subject = new BehaviorSubject<AthleteEntity list>([])

  let reload() : unit =
    asyncEx {
      match! store.ListAthletes() with
      | Ok athletes -> emitOnUi(subject, athletes)
      | Error e -> onError("athlete store reload", e)
    }
    |> Async.StartImmediate

  do reload()

  /// The live athletes list; new subscribers get the current snapshot.
  member _.Observe() : IObservable<AthleteEntity list> = subject :> _

  /// The snapshot the subject holds right now.
  member _.Snapshot() : AthleteEntity list = subject.Value

  /// A point read; no observable state involved.
  member _.Find(id: Guid) = store.FindAthlete id

  /// Persists the athlete and re-emits the list.
  member _.Add(profile: Athlete) = task {
    let! result = store.AddAthlete profile
    do reload()
    return result
  }

/// The per-athlete slice pattern shared by memories and plans: one
/// subject over the whole map, projected per athlete, loaded on first
/// Observe.
type MemoryStore
  (store: IStore, [<InlineIfLambda>] onError: (string * exn) -> unit) =

  let subject = new BehaviorSubject<Map<Guid, MemoryEntity list>>(Map.empty)

  let loaded = HashSet<Guid>()
  let gate = obj()

  let emit(athleteId: Guid, entities: MemoryEntity list) : unit =
    emitOnUi(subject, Map.add athleteId entities subject.Value)

  let ensureLoaded(athleteId: Guid) : unit =
    let load = lock gate (fun () -> loaded.Add athleteId)

    if load then
      asyncEx {
        match! store.ListMemories athleteId with
        | Ok entities -> emit(athleteId, entities)
        | Error e -> onError("memory store load", e)
      }
      |> Async.StartImmediate

  let slice(athleteId: Guid) : IObservable<MemoryEntity list> =
    subject
    |> Observable.map(fun slices ->
      slices |> Map.tryFind athleteId |> Option.defaultValue [])

  /// The live memories of one athlete; the first subscription loads the
  /// slice, and new subscribers get the current snapshot.
  member _.Observe(athleteId: Guid) : IObservable<MemoryEntity list> =
    ensureLoaded athleteId
    slice athleteId

  /// The slice the subject holds right now.
  member _.Snapshot(athleteId: Guid) : MemoryEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue []

  /// Persists the memory and re-emits the athlete's slice. A plan-scoped
  /// memory records the plan it was observed on; an athlete-scoped one
  /// belongs to no plan.
  member _.Add(athleteId: Guid, memory: Memory, planId: Guid voption) = task {
    let! result = store.AddMemory(athleteId, memory, planId)

    asyncEx {
      match! store.ListMemories athleteId with
      | Ok entities -> emit(athleteId, entities)
      | Error e -> onError("memory store reload", e)
    }
    |> Async.StartImmediate

    return result
  }

  /// Persists the edited entity and re-emits the athlete's slice.
  member _.Update(entity: MemoryEntity) = task {
    let! result = store.UpdateMemory entity

    asyncEx {
      match! store.ListMemories entity.AthleteId with
      | Ok entities -> emit(entity.AthleteId, entities)
      | Error e -> onError("memory store reload", e)
    }
    |> Async.StartImmediate

    return result
  }

type PlanStore
  (store: IStore, [<InlineIfLambda>] onError: (string * exn) -> unit) =

  let subject = new BehaviorSubject<Map<Guid, PlanEntity list>>(Map.empty)

  let loaded = HashSet<Guid>()
  let gate = obj()

  let emit(athleteId: Guid, entities: PlanEntity list) : unit =
    emitOnUi(subject, Map.add athleteId entities subject.Value)

  let ensureLoaded(athleteId: Guid) : unit =
    let load = lock gate (fun () -> loaded.Add athleteId)

    if load then
      asyncEx {
        match! store.ListPlans athleteId with
        | Ok entities -> emit(athleteId, entities)
        | Error e -> onError("plan store load", e)
      }
      |> Async.StartImmediate

  let slice(athleteId: Guid) : IObservable<PlanEntity list> =
    subject
    |> Observable.map(fun slices ->
      slices |> Map.tryFind athleteId |> Option.defaultValue [])

  /// The live plans of one athlete; the first subscription loads the
  /// slice, and new subscribers get the current snapshot.
  member _.Observe(athleteId: Guid) : IObservable<PlanEntity list> =
    ensureLoaded athleteId
    slice athleteId

  /// The slice the subject holds right now.
  member _.Snapshot(athleteId: Guid) : PlanEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue []

  /// Persists the plan and re-emits the athlete's slice.
  member _.Add(insert: PlanInsert, plan: Plan) = task {
    let! result = store.AddPlan(insert, plan)

    asyncEx {
      match! store.ListPlans insert.AthleteId with
      | Ok entities -> emit(insert.AthleteId, entities)
      | Error e -> onError("plan store reload", e)
    }
    |> Async.StartImmediate

    return result
  }

  /// A point read; no observable state involved.
  member _.Find(id: Guid) = store.FindPlan id

  /// Persists the raw text and re-emits the athlete's slice.
  member _.UpdateRaw(id: Guid, raw: string) = task {
    // the athlete is not a parameter; find the plan first
    let! found = store.FindPlan id

    let! result = store.UpdatePlanRaw(id, raw)

    match found with
    | Ok(ValueSome entity) ->
      asyncEx {
        match! store.ListPlans entity.AthleteId with
        | Ok entities -> emit(entity.AthleteId, entities)
        | Error e -> onError("plan store reload", e)
      }
      |> Async.StartImmediate
    | _ -> ()

    return result
  }

/// The whole live data layer: the raw storage plus one store per
/// aggregate. Views read and write the stores; only point reads and the
/// audit line touch Raw.
type DataStores = {
  Raw: IStore
  Athletes: AthleteStore
  Memories: MemoryStore
  Plans: PlanStore
}

/// Builds the store layer over the storage.
let inline createStores
  (store: IStore, [<InlineIfLambda>] onError: (string * exn) -> unit)
  : DataStores = {
  Raw = store
  Athletes = AthleteStore(store, onError)
  Memories = MemoryStore(store, onError)
  Plans = PlanStore(store, onError)
}
