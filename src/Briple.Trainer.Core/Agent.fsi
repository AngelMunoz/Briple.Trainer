module Briple.Trainer.Core.Agent

// The agent orchestration over Microsoft.Extensions.AI with BYOK
// OpenAI-compatible providers. Responses stream in chunks inside a
// cancellableTask: the token travels ambient from the caller's
// asyncEx/cancellableTask down to the provider stream, and surfaces as
// OperationCanceledException when cancelled. The model answers data as
// JSON; the domain plan is converted with Plan.Data, checked against the
// request parameters and the format is written by Plan.Writer
// deterministically. Rejected outputs are discarded; there is no repair.

open System
open IcedTasks
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator

/// A BYOK OpenAI-compatible provider.
type Provider = {
  Name: string
  Endpoint: Uri
  Model: string
  /// Own User-Agent; some providers ask for it instead of the SDK's
  /// generic one.
  UserAgent: string
  /// Extra provider headers (e.g. x-opencode-session).
  Headers: (string * string) list
}

/// The provider's chat client with its key, user agent and headers
/// applied. An endpoint ending in /responses uses the Responses API
/// client with that base.
val createClient: key: string -> provider: Provider -> IChatClient

/// One run of the generation flow: the raw model response, the plan
/// rendered to the format and the verdict.
type Run = {
  Response: string
  Output: string
  Verdict: Verdict
}

/// Capability: the provider's chat client.
type IProvideChat =
  abstract Chat: IChatClient

/// Capability: the boundary log. The app injects its own provider;
/// Core never creates a logger.
type IProvideLog =
  abstract Log: ILogger

val generateNew:
  env: #IProvideChat & #IProvideLog * request: NewPlanRequest ->
    CancellableTask<Run>

val generateAdjustment:
  env: #IProvideChat & #IProvideLog * request: AdjustmentRequest ->
    CancellableTask<Run>

/// The agent consolidates the marks into proposed memories. Lines that
/// do not follow the protocol are discarded; numeric deltas tolerate
/// sign and percentage (load +5% reads as load 5).
val consolidateMarks:
  env: #IProvideChat & #IProvideLog * marks: FeedbackMark list ->
    CancellableTask<Memory list>

// --- note interpretation -----------------------------------------------------

/// Why a part of a trainer's note did not become a memory. Structured
/// data only: the UI maps each case to localized prose and the app logs
/// them to track uncovered surfaces.
type Rejection =
  | UnknownLabel of token: string
  | UnknownTarget of text: string
  | MissingField of field: string
  | OutsideProtocol of detail: string

/// The reading of one trainer's note: the memories that mapped to the
/// closed surface, and the parts that did not (with the reason).
type NoteInterpretation = {
  Memories: Memory list
  Rejections: Rejection list
}

/// What a note reading needs: the closed vocabulary, the note verbatim,
/// and the day the fact was observed. The date travels here; Core never
/// reads the clock.
type NoteAsk = {
  Targets: Target list
  Note: string
  Date: DateOnly
}

/// The closed target vocabulary of a plan: its exercise names and day
/// ids. A note can only produce targets from this list.
val vocabularyOfPlan: plan: Plan -> Target list

/// The pure reading of an interpretation response: tolerant JSON
/// decoding, labels from the closed enum, targets matched against the
/// vocabulary, deltas through the existing parser, at most ten
/// memories. Everything outside the surface lands as a Rejection.
val interpretationOf:
  targets: Target list * date: DateOnly * response: string -> NoteInterpretation

/// The agent reads the trainer's free-text note and answers with the
/// memories it maps to the closed surface. The note travels alone in the
/// user message; the system message carries the surface.
val interpretNote:
  env: #IProvideChat & #IProvideLog * reading: NoteAsk ->
    CancellableTask<NoteInterpretation>
