module Briple.Trainer.Core.Validador

// The validator of agent outputs: the text format (a parse with no
// warnings) and the parameters of the request (gender, variants, weeks,
// counts and RIR). A rejected output is discarded; there is no repair.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Solicitudes

/// One violated rule, with the human-readable detail.
type Violation = { Rule: string; Detail: string }

/// The verdict on an agent output.
type Verdict =
  | Valid of plan: Plan
  | Rejected of Violation list

/// Checks a plan of the domain against the parameters of the request:
/// gender, variants, weeks, counts and RIR.
val checkParameters:
  athlete: Athlete ->
  weeks: int ->
  brief: Brief ->
  counts: (string * int) list ->
  plan: Plan ->
    Violation list

/// Validates the output of a new plan: clean format, one block of the
/// athlete's gender, their variants, the weeks, the counts and the
/// brief's RIR.
val validateNew: request: NewPlanRequest -> output: string -> Verdict

/// Validates the output of an adjustment: the same over the adjustment
/// request, whose previous plan travels as reference.
val validateAdjustment: request: AdjustmentRequest -> output: string -> Verdict
