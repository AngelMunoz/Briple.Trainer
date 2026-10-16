module Briple.Trainer.Components.NoteSection

// The note interpretation as an MVUish component: props in (the culture,
// the agent, the memory store and the plan the note is about), events
// out (MemoryConfirmed carries what the trainer confirmed). The note is
// the only input; the agent maps it to the closed surface of the plan,
// the proposals render as confirm/discard cards, and every rejection or
// failure renders next to the field it belongs to.

open System
open System.Globalization
open System.Threading

open Avalonia.Controls

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// What the section reports to its parent.
[<Struct>]
type Event = MemoryConfirmed of MemoryEntity

/// The section inputs.
[<Struct>]
type Props = {
  Culture: CultureInfo
  Agent: IProvideAgent
  Memories: MemoryStore
  AthleteId: Guid
  /// The plan the note is observed on: its vocabulary closes the
  /// surface and its id stamps plan-scoped memories.
  Plan: Plan
  PlanId: Guid
  /// The page's token: leaving the review page cancels the reading.
  Cancel: CancellationToken
}

/// What the last reading produced, rendered next to the field.
type Status =
  | Idle
  | Reading
  | Rejected
  | Full
  | Failed of reason: string

type Model = {
  Note: string
  Busy: bool
  Proposals: (Guid * Memory) list
  Status: Status
}

/// One pass over the agent: the cap verdict or the reading.
type Reading =
  | SurfaceFull
  | Read of NoteInterpretation
  | Unreadable of exn

type Msg =
  | SetNote of string
  | InterpretRequested
  | Interpreted of Reading
  | Decided of MemoryReview.Event
  | Saved of localId: Guid * Result<MemoryEntity, exn>

/// The note section.
type NoteSection =
  inherit UserControl

  new: props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) -> NoteSection
