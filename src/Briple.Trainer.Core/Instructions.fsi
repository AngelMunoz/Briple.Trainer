module Briple.Trainer.Core.Instructions

// The parameters → agent instructions mapping: what gets fixed, what
// gets asked and what gets forbidden. The fixed part must appear in the
// output verbatim; the asked part is produced by the agent within the
// origin the inventory declares for it; the forbidden part is untouched.

open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests

/// What the agent cannot do.
type Prohibition =
  | ChangeCounts
  | OutsideVariants
  | LeaveFormat

/// The full mapping of a request.
type Instructions = {
  Fix: Parameter list
  Ask: Field list
  Forbid: Prohibition list
}

/// Instructions of the new-plan flow.
val forNew: request: NewPlanRequest -> Instructions

/// Instructions of the adjustment flow: the counts may trace to the
/// previous plan instead of the brief.
val forAdjustment: request: AdjustmentRequest -> Instructions
