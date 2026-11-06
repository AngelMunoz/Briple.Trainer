module Briple.Trainer.Components.NoteSection

// The note interpretation as an MVUish component: props in (the culture,
// the agent, the memory store and the plan the note is about), events
// out (MemoryConfirmed carries what the trainer confirmed). The note is
// the only input; the agent maps it to the closed surface of the plan,
// the proposals render as confirm/discard cards, and every rejection or
// failure renders next to the field it belongs to. The page seeds the
// model and keeps every transition, so a culture rebuild loses nothing.

open System
open System.Globalization
open System.Threading

open Avalonia.Controls

open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Stores

/// What the section reports to its parent.
[<Struct>]
type Event = MemoryConfirmed of MemoryEntity

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
  /// A confirmation is in flight; a second decision is ignored.
  Saving: bool
  Proposals: (Guid * Memory) list
  Status: Status
  Culture: CultureInfo
}

/// The services the note section needs, built once at the page.
[<Struct>]
type NoteEnv = {
  Agent: IProvideAgent
  Memories: MemoryStore
  Logger: Microsoft.Extensions.Logging.ILogger
  Culture: CultureInfo
  CultureChanged: IObservable<CultureInfo>
}

/// The section inputs.
[<Struct>]
type Props = {
  AthleteId: Guid
  /// The plan the note is observed on, read when the note is asked.
  /// Its vocabulary closes the surface; the plan id stamps plan-scoped
  /// memories.
  Plan: unit -> Plan
  PlanId: Guid
  /// The page's token: leaving the review page cancels the reading.
  Cancel: CancellationToken
  /// The state the page kept from a previous render; the component seeds
  /// its model from it so a culture rebuild never loses a draft.
  Initial: Model
  /// Every transition travels to the page, which keeps the state and
  /// derives the exit phase from it.
  /// Reports every state change the parent must track (note text,
  /// reading, saving). Display-only changes, culture among them, never
  /// report: the parent's re-emission would echo back through its
  /// culture feed and loop the message queue.
  OnState: Model -> unit
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
  | CultureChanged of CultureInfo

/// The note section.
type NoteSection =
  inherit UserControl

  new:
    env: NoteEnv * props: Props * [<InlineIfLambda>] onEvent: (Event -> unit) ->
      NoteSection
