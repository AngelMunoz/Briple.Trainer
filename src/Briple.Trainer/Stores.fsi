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

  /// The live athletes list; new subscribers get the current snapshot.
  member Observe: unit -> IObservable<AthleteEntity list>

  /// The snapshot the subject holds right now.
  member Snapshot: unit -> AthleteEntity list

  /// A point read; no observable state involved.
  member Find: Guid -> Task<Result<AthleteEntity voption, exn>>

  /// Persists the athlete and re-emits the list.
  member Add: Athlete -> Task<Result<AthleteEntity, exn>>

/// The memories aggregate, one slice per athlete. The first Observe of an
/// athlete loads their slice; writes re-emit that slice only.
type MemoryStore =
  new:
    IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) -> MemoryStore

  /// The live memories of one athlete; the first subscription loads the
  /// slice, and new subscribers get the current snapshot.
  member Observe: athleteId: Guid -> IObservable<MemoryEntity list>

  /// The slice the subject holds right now.
  member Snapshot: athleteId: Guid -> MemoryEntity list

  /// Persists the memory and re-emits the athlete's slice. A plan-scoped
  /// memory records the plan it was observed on; an athlete-scoped one
  /// belongs to no plan.
  member Add:
    athleteId: Guid * memory: Memory * planId: Guid voption ->
      Task<Result<MemoryEntity, exn>>

  /// Persists the edited entity and re-emits the athlete's slice.
  member Update: entity: MemoryEntity -> Task<Result<unit, exn>>

/// The plans aggregate, one slice per athlete.
type PlanStore =
  new:
    IStore * [<InlineIfLambda>] onError: ((string * exn) -> unit) -> PlanStore

  /// The live plans of one athlete; the first subscription loads the
  /// slice, and new subscribers get the current snapshot.
  member Observe: athleteId: Guid -> IObservable<PlanEntity list>

  /// The slice the subject holds right now.
  member Snapshot: athleteId: Guid -> PlanEntity list

  /// Persists the plan and re-emits the athlete's slice.
  member Add: insert: PlanInsert * plan: Plan -> Task<Result<PlanEntity, exn>>

  /// A point read; no observable state involved.
  member Find: id: Guid -> Task<Result<PlanEntity voption, exn>>

  /// Persists the raw text and re-emits the athlete's slice.
  member UpdateRaw: id: Guid * raw: string -> Task<Result<unit, exn>>

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
