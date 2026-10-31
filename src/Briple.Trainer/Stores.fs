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

let inline active
  (entities: #seq< ^T >)
  : ^T list when ^T: (member ArchivedAt: DateTime voption) =
  entities |> Seq.filter(fun entity -> entity.ArchivedAt.IsNone) |> List.ofSeq

let inline archived
  (entities: #seq< ^T >)
  : ^T list when ^T: (member ArchivedAt: DateTime voption) =
  entities |> Seq.filter(fun entity -> entity.ArchivedAt.IsSome) |> List.ofSeq

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

  /// The live active athletes; new subscribers get the current snapshot.
  member _.Observe() : IObservable<AthleteEntity list> =
    subject |> Observable.map active

  /// The live archived athletes.
  member _.ObserveArchived() : IObservable<AthleteEntity list> =
    subject |> Observable.map archived

  /// The active snapshot the subject holds right now.
  member _.Snapshot() : AthleteEntity list = active subject.Value

  /// The archived snapshot the subject holds right now.
  member _.SnapshotArchived() : AthleteEntity list = archived subject.Value

  /// A point read; no observable state involved.
  member _.Find(id: Guid) = store.FindAthlete id

  /// Persists the athlete and re-emits the list.
  member _.Add(profile: Athlete) = task {
    let! result = store.AddAthlete profile
    do reload()
    return result
  }

  /// Writes the athlete and its drafts in one transaction.
  member _.Import(profile: Athlete, drafts: PlanDraft list) = task {
    let! result = store.AddAthleteWithPlans(profile, drafts)
    do reload()
    return result
  }

  /// Sets archived_at and reloads the athlete list.
  member _.Archive(id: Guid) = task {
    let! result = store.ArchiveAthlete id
    do reload()
    return result
  }

  /// Clears archived_at and reloads the athlete list.
  member _.Restore(id: Guid) = task {
    let! result = store.RestoreAthlete id
    do reload()
    return result
  }

  /// Deletes the athlete aggregate and reloads the athlete list.
  member _.Delete(id: Guid) = task {
    let! result = store.DeleteAthlete id
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

  let reload(athleteId: Guid) : Task<Result<unit, exn>> = task {
    let! result = store.ListMemories athleteId

    match result with
    | Ok entities ->
      emit(athleteId, entities)
      return Ok()
    | Error e ->
      onError("memory store reload", e)
      return Error e
  }

  /// The live active memories of one athlete; the first subscription
  /// loads the slice, and new subscribers get the current snapshot.
  member _.Observe(athleteId: Guid) : IObservable<MemoryEntity list> =
    ensureLoaded athleteId
    slice athleteId |> Observable.map active

  /// The live archived memories of one athlete.
  member _.ObserveArchived(athleteId: Guid) : IObservable<MemoryEntity list> =
    ensureLoaded athleteId
    slice athleteId |> Observable.map archived

  /// The active slice the subject holds right now.
  member _.Snapshot(athleteId: Guid) : MemoryEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue [] |> active

  /// The archived slice the subject holds right now.
  member _.SnapshotArchived(athleteId: Guid) : MemoryEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue [] |> archived

  /// Persists the memory and re-emits the athlete's slice. A plan-scoped
  /// memory records the plan it was observed on; an athlete-scoped one
  /// belongs to no plan.
  member _.Add(athleteId: Guid, memory: Memory, planId: Guid voption) = task {
    let! result = store.AddMemory(athleteId, memory, planId)
    let! _ = reload athleteId
    return result
  }

  /// Persists the edited entity and re-emits the athlete's slice.
  member _.Update(entity: MemoryEntity) = task {
    let! result = store.UpdateMemory entity
    let! _ = reload entity.AthleteId
    return result
  }

  /// Sets archived_at and re-emits the athlete's slice.
  member _.Archive(athleteId: Guid, id: Guid) = task {
    let! result = store.ArchiveMemory id
    let! _ = reload athleteId
    return result
  }

  /// Clears archived_at and re-emits the athlete's slice.
  member _.Restore(athleteId: Guid, id: Guid) = task {
    let! result = store.RestoreMemory id
    let! _ = reload athleteId
    return result
  }

  /// Deletes one memory and re-emits the athlete's slice.
  member _.Delete(athleteId: Guid, id: Guid) = task {
    let! result = store.DeleteMemory id
    let! _ = reload athleteId
    return result
  }

  /// Reloads one athlete's slice from the storage.
  member _.Refresh(athleteId: Guid) : Task<Result<unit, exn>> = reload athleteId

  /// Drops one athlete's cached slice; the next Observe loads it again.
  member _.Forget(athleteId: Guid) : unit =
    lock gate (fun () -> loaded.Remove athleteId |> ignore)
    emitOnUi(subject, Map.remove athleteId subject.Value)

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

  let reload(athleteId: Guid) : Task<Result<unit, exn>> = task {
    let! result = store.ListPlans athleteId

    match result with
    | Ok entities ->
      emit(athleteId, entities)
      return Ok()
    | Error e ->
      onError("plan store reload", e)
      return Error e
  }

  let reloadFound(found: Result<PlanEntity voption, exn>) : Task<unit> = task {
    match found with
    | Ok(ValueSome entity) ->
      let! _ = reload entity.AthleteId
      ()
    | _ -> ()
  }

  /// The live active plans of one athlete; the first subscription loads
  /// the slice, and new subscribers get the current snapshot.
  member _.Observe(athleteId: Guid) : IObservable<PlanEntity list> =
    ensureLoaded athleteId
    slice athleteId |> Observable.map active

  /// The live archived plans of one athlete.
  member _.ObserveArchived(athleteId: Guid) : IObservable<PlanEntity list> =
    ensureLoaded athleteId
    slice athleteId |> Observable.map archived

  /// The active slice the subject holds right now.
  member _.Snapshot(athleteId: Guid) : PlanEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue [] |> active

  /// The archived slice the subject holds right now.
  member _.SnapshotArchived(athleteId: Guid) : PlanEntity list =
    ensureLoaded athleteId

    subject.Value |> Map.tryFind athleteId |> Option.defaultValue [] |> archived

  /// Persists the plan and re-emits the athlete's slice.
  member _.Add(insert: PlanInsert, plan: Plan) = task {
    let! result = store.AddPlan(insert, plan)
    let! _ = reload insert.AthleteId
    return result
  }

  /// Writes the drafts in one transaction and re-emits the athlete's
  /// slice.
  member _.Import(athleteId: Guid, drafts: PlanDraft list) = task {
    let! result = store.AddPlans(athleteId, drafts)
    let! _ = reload athleteId
    return result
  }

  /// A point read; no observable state involved.
  member _.Find(id: Guid) = store.FindPlan id

  /// Persists the raw text and re-emits the athlete's slice.
  member _.UpdateRaw(id: Guid, raw: string) = task {
    // the athlete is not a parameter; find the plan first
    let! found = store.FindPlan id
    let! result = store.UpdatePlanRaw(id, raw)
    do! reloadFound found
    return result
  }

  /// Sets archived_at and re-emits the athlete's slice.
  member _.Archive(id: Guid) = task {
    let! found = store.FindPlan id
    let! result = store.ArchivePlan id
    do! reloadFound found
    return result
  }

  /// Clears archived_at and re-emits the athlete's slice.
  member _.Restore(id: Guid) = task {
    let! found = store.FindPlan id
    let! result = store.RestorePlan id
    do! reloadFound found
    return result
  }

  /// Deletes the plan and its plan-scoped memories, then re-emits the
  /// athlete's slice.
  member _.Delete(id: Guid) = task {
    let! found = store.FindPlan id
    let! result = store.DeletePlan id
    do! reloadFound found
    return result
  }

  /// Reloads one athlete's slice from the storage.
  member _.Refresh(athleteId: Guid) : Task<Result<unit, exn>> = reload athleteId

  /// Drops one athlete's cached slice; the next Observe loads it again.
  member _.Forget(athleteId: Guid) : unit =
    lock gate (fun () -> loaded.Remove athleteId |> ignore)
    emitOnUi(subject, Map.remove athleteId subject.Value)

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
  (store: IStore, [<InlineIfLambda>] onError: ((string * exn) -> unit))
  : DataStores = {
  Raw = store
  Athletes = AthleteStore(store, onError)
  Memories = MemoryStore(store, onError)
  Plans = PlanStore(store, onError)
}

/// Deletes the athlete at the storage and forgets the cached memory and
/// plan slices.
let deleteAthlete
  (stores: DataStores, athleteId: Guid)
  : Task<Result<unit, exn>> = task {
  let! result = stores.Athletes.Delete athleteId

  match result with
  | Ok() ->
    stores.Memories.Forget athleteId
    stores.Plans.Forget athleteId
    return Ok()
  | Error e -> return Error e
}

/// Deletes the plan and its plan-scoped memories, then refreshes the
/// athlete's memory slice.
let deletePlan(stores: DataStores, planId: Guid) : Task<Result<unit, exn>> =
  task {
    let! found = stores.Plans.Find planId
    let! result = stores.Plans.Delete planId

    match result, found with
    | Ok(), Ok(ValueSome entity) ->
      let! _ = stores.Memories.Refresh entity.AthleteId
      return Ok()
    | Ok(), _ -> return Ok()
    | Error e, _ -> return Error e
  }

/// Writes the athlete and its drafts in one transaction, then refreshes
/// the new athlete's plan slice.
let importAthlete
  (stores: DataStores, profile: Athlete, drafts: PlanDraft list)
  : Task<Result<AthleteEntity, exn>> = task {
  let! result = stores.Athletes.Import(profile, drafts)

  match result with
  | Ok entity ->
    let! _ = stores.Plans.Refresh entity.Id
    return Ok entity
  | Error e -> return Error e
}

/// Writes the drafts in one transaction and refreshes the athlete's plan
/// slice.
let importPlans
  (stores: DataStores, athleteId: Guid, drafts: PlanDraft list)
  : Task<Result<PlanEntity list, exn>> =
  stores.Plans.Import(athleteId, drafts)
