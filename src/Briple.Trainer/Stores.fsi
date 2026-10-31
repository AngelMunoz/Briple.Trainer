module Briple.Trainer.Stores

// The observable data layer over the SQLite storage: each aggregate lives
// as a BehaviorSubject-backed snapshot. Every write goes through its
// store, which persists with the raw storage, reloads its aggregate and
// notifies the subject on the UI thread. Views subscribe and stay live,
// so route caching stops mattering. Point reads (Find, audit writes)
// stay on the raw IStore.

open System
open System.Threading.Tasks

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence

/// The athletes aggregate: one live list for the whole app.
type AthleteStore =
  /// Takes the storage service and the error sink; the constructor kicks
  /// the initial load, so the first emission lands shortly after.
  new:
    IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) ->
      AthleteStore

  /// The live active athletes; new subscribers get the current snapshot.
  member Observe: unit -> IObservable<AthleteEntity list>

  /// The live archived athletes.
  member ObserveArchived: unit -> IObservable<AthleteEntity list>

  /// The active snapshot the subject holds right now.
  member Snapshot: unit -> AthleteEntity list

  /// The archived snapshot the subject holds right now.
  member SnapshotArchived: unit -> AthleteEntity list

  /// A point read; no observable state involved.
  member Find: Guid -> Task<Result<AthleteEntity voption, exn>>

  /// Persists the athlete and re-emits the list.
  member Add: Athlete -> Task<Result<AthleteEntity, exn>>

  /// Writes the athlete and its drafts in one transaction.
  member Import:
    profile: Athlete * drafts: PlanDraft list ->
      Task<Result<AthleteEntity, exn>>

  /// Sets archived_at and reloads the athlete list.
  member Archive: id: Guid -> Task<Result<unit, exn>>

  /// Clears archived_at and reloads the athlete list.
  member Restore: id: Guid -> Task<Result<unit, exn>>

  /// Deletes the athlete aggregate and reloads the athlete list.
  member Delete: id: Guid -> Task<Result<unit, exn>>

/// The memories aggregate, one slice per athlete. The first Observe of an
/// athlete loads their slice; writes re-emit that slice only.
type MemoryStore =
  new:
    IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) -> MemoryStore

  /// The live active memories of one athlete; the first subscription
  /// loads the slice, and new subscribers get the current snapshot.
  member Observe: athleteId: Guid -> IObservable<MemoryEntity list>

  /// The live archived memories of one athlete.
  member ObserveArchived: athleteId: Guid -> IObservable<MemoryEntity list>

  /// The active slice the subject holds right now.
  member Snapshot: athleteId: Guid -> MemoryEntity list

  /// The archived slice the subject holds right now.
  member SnapshotArchived: athleteId: Guid -> MemoryEntity list

  /// Persists the memory and re-emits the athlete's slice. A plan-scoped
  /// memory records the plan it was observed on; an athlete-scoped one
  /// belongs to no plan.
  member Add:
    athleteId: Guid * memory: Memory * planId: Guid voption ->
      Task<Result<MemoryEntity, exn>>

  /// Persists the edited entity and re-emits the athlete's slice.
  member Update: entity: MemoryEntity -> Task<Result<unit, exn>>

  /// Sets archived_at and re-emits the athlete's slice.
  member Archive: athleteId: Guid * id: Guid -> Task<Result<unit, exn>>

  /// Clears archived_at and re-emits the athlete's slice.
  member Restore: athleteId: Guid * id: Guid -> Task<Result<unit, exn>>

  /// Deletes one memory and re-emits the athlete's slice.
  member Delete: athleteId: Guid * id: Guid -> Task<Result<unit, exn>>

  /// Reloads one athlete's slice from the storage.
  member Refresh: athleteId: Guid -> Task<Result<unit, exn>>

  /// Drops one athlete's cached slice; the next Observe loads it again.
  member Forget: athleteId: Guid -> unit

/// The plans aggregate, one slice per athlete.
type PlanStore =
  new:
    IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) -> PlanStore

  /// The live active plans of one athlete; the first subscription loads
  /// the slice, and new subscribers get the current snapshot.
  member Observe: athleteId: Guid -> IObservable<PlanEntity list>

  /// The live archived plans of one athlete.
  member ObserveArchived: athleteId: Guid -> IObservable<PlanEntity list>

  /// The active slice the subject holds right now.
  member Snapshot: athleteId: Guid -> PlanEntity list

  /// The archived slice the subject holds right now.
  member SnapshotArchived: athleteId: Guid -> PlanEntity list

  /// Persists the plan and re-emits the athlete's slice.
  member Add: insert: PlanInsert * plan: Plan -> Task<Result<PlanEntity, exn>>

  /// Writes the drafts in one transaction and re-emits the athlete's
  /// slice.
  member Import:
    athleteId: Guid * drafts: PlanDraft list ->
      Task<Result<PlanEntity list, exn>>

  /// A point read; no observable state involved.
  member Find: id: Guid -> Task<Result<PlanEntity voption, exn>>

  /// Persists the raw text and re-emits the athlete's slice.
  member UpdateRaw: id: Guid * raw: string -> Task<Result<unit, exn>>

  /// Sets archived_at and re-emits the athlete's slice.
  member Archive: id: Guid -> Task<Result<unit, exn>>

  /// Clears archived_at and re-emits the athlete's slice.
  member Restore: id: Guid -> Task<Result<unit, exn>>

  /// Deletes the plan and its plan-scoped memories, then re-emits the
  /// athlete's slice.
  member Delete: id: Guid -> Task<Result<unit, exn>>

  /// Reloads one athlete's slice from the storage.
  member Refresh: athleteId: Guid -> Task<Result<unit, exn>>

  /// Drops one athlete's cached slice; the next Observe loads it again.
  member Forget: athleteId: Guid -> unit

/// The whole live data layer: the raw storage plus one store per
/// aggregate. Views read and write the stores; only point reads and the
/// audit line touch Raw.
type DataStores = {
  Raw: IStore
  Athletes: AthleteStore
  Memories: MemoryStore
  Plans: PlanStore
}

/// Builds the store layer over the storage; onError receives a context
/// name and the failure.
val inline createStores:
  IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) -> DataStores

/// Deletes the athlete at the storage and forgets the cached memory and
/// plan slices.
val deleteAthlete:
  stores: DataStores * athleteId: Guid -> Task<Result<unit, exn>>

/// Deletes the plan and its plan-scoped memories, then refreshes the
/// athlete's memory slice.
val deletePlan: stores: DataStores * planId: Guid -> Task<Result<unit, exn>>

/// Writes the athlete and its drafts in one transaction, then refreshes
/// the new athlete's plan slice.
val importAthlete:
  stores: DataStores * profile: Athlete * drafts: PlanDraft list ->
    Task<Result<AthleteEntity, exn>>

/// Writes the drafts in one transaction and refreshes the athlete's plan
/// slice.
val importPlans:
  stores: DataStores * athleteId: Guid * drafts: PlanDraft list ->
    Task<Result<PlanEntity list, exn>>
