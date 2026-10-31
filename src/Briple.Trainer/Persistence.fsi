module Briple.Trainer.Persistence

// The SQLite layer: Migrondi owns the schema (the migration files ship
// next to the executable), Donald runs the queries, JDeck codes the
// column payloads. The confirmed plan's Raw verbatim is the source of
// truth. Every store operation returns Error with the exception as the
// cause instead of throwing, because a local database failure is an
// expected failure the UI reports.

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types

type AthleteEntity = {
  Id: Guid
  Profile: Athlete
  ArchivedAt: DateTime voption
}

type MemoryEntity = {
  Id: Guid
  AthleteId: Guid
  Memory: Memory
  /// The plan a plan-scoped memory was observed on; ValueNone for
  /// athlete-scoped memories.
  PlanId: Guid voption
  CreatedAt: DateTime
  /// Separate from MemoryStatus.Discarded: an archived memory is kept
  /// but hidden from the active slices.
  ArchivedAt: DateTime voption
}

type PlanEntity = {
  Id: Guid
  AthleteId: Guid
  Title: string
  /// "new", "adjust" or "import"
  Kind: string
  Weeks: int
  Counts: (string * int) list
  Raw: string
  CreatedAt: DateTime
  ArchivedAt: DateTime voption
}

/// The owner and metadata of a plan insert.
[<Struct>]
type PlanInsert = {
  AthleteId: Guid
  Kind: string
  Counts: (string * int) list
  CreatedAt: DateTime voption
}

/// One plan in an import: its metadata and the tree to write.
[<Struct>]
type PlanDraft = {
  Kind: string
  Counts: (string * int) list
  CreatedAt: DateTime voption
  Plan: Plan
}

/// The newest plan that is not archived.
val lastActivePlan: plans: PlanEntity list -> PlanEntity voption

/// The proposals that may travel to a new request: active proposals on
/// active plans. An archived plan or memory is kept, but hidden.
val activeProposals:
  plans: PlanEntity list * memories: MemoryEntity list -> MemoryEntity list

/// The store the UI reads and writes; every operation fails with Error.
/// The list methods return active and archived rows; the observable
/// layer splits the slices.
type IStore =
  abstract ListAthletes: unit -> Task<Result<AthleteEntity list, exn>>
  abstract AddAthlete: profile: Athlete -> Task<Result<AthleteEntity, exn>>
  abstract FindAthlete: id: Guid -> Task<Result<AthleteEntity voption, exn>>
  abstract ArchiveAthlete: id: Guid -> Task<Result<unit, exn>>
  abstract RestoreAthlete: id: Guid -> Task<Result<unit, exn>>
  abstract DeleteAthlete: id: Guid -> Task<Result<unit, exn>>

  abstract AddAthleteWithPlans:
    profile: Athlete * drafts: PlanDraft list ->
      Task<Result<AthleteEntity, exn>>

  abstract ListMemories: athleteId: Guid -> Task<Result<MemoryEntity list, exn>>

  abstract AddMemory:
    athleteId: Guid * memory: Memory * planId: Guid voption ->
      Task<Result<MemoryEntity, exn>>

  abstract UpdateMemory: entity: MemoryEntity -> Task<Result<unit, exn>>
  abstract ArchiveMemory: id: Guid -> Task<Result<unit, exn>>
  abstract RestoreMemory: id: Guid -> Task<Result<unit, exn>>
  abstract DeleteMemory: id: Guid -> Task<Result<unit, exn>>
  abstract ListPlans: athleteId: Guid -> Task<Result<PlanEntity list, exn>>

  abstract AddPlan:
    insert: PlanInsert * plan: Plan -> Task<Result<PlanEntity, exn>>

  abstract AddPlans:
    athleteId: Guid * drafts: PlanDraft list ->
      Task<Result<PlanEntity list, exn>>

  abstract FindPlan: id: Guid -> Task<Result<PlanEntity voption, exn>>
  abstract UpdatePlanRaw: id: Guid * raw: string -> Task<Result<unit, exn>>
  abstract ArchivePlan: id: Guid -> Task<Result<unit, exn>>
  abstract RestorePlan: id: Guid -> Task<Result<unit, exn>>
  abstract DeletePlan: id: Guid -> Task<Result<unit, exn>>

  abstract AddRequest:
    athleteId: Guid * kind: string * payload: string -> Task<Result<unit, exn>>

/// The database file under the user's application data.
val defaultDbPath: unit -> string

/// Applies the pending migrations shipped next to the executable; the
/// Migrondi diagnostics ride the given logger.
val migrate: logger: ILogger * dbPath: string -> Result<unit, exn>

/// The store implementation over the given database file.
val sqliteStore: dbPath: string -> IStore

/// Writes the .briple export: the raw plan text, verbatim, to the path.
val exportBriple: path: string * raw: string -> Result<string, exn>
