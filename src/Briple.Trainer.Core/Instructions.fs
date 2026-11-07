module Briple.Trainer.Core.Instructions

open Briple.Trainer.Core.Parameters
open Briple.Trainer.Core.Requests

type Prohibition =
  | ChangeCounts
  | OutsideVariants
  | LeaveFormat

type Instructions = {
  Fix: Parameter list
  Ask: Field list
  Forbid: Prohibition list
  StructureLead: string
}

let agentFields: Field list =
  fields
  |> List.filter(fun field -> originsFor field |> List.contains FromAgent)

/// The fields the agent decides: every agent-origin field, minus the
/// ones the brief took over. A fixed circuit count leaves the agent's
/// hands.
let askOf(brief: Brief) : Field list =
  match brief.CircuitsPerDay with
  | ValueSome _ -> agentFields |> List.except [ Field.Circuit ]
  | ValueNone -> agentFields

let forNew(request: NewPlanRequest) : Instructions = {
  Fix = parametersForNew request
  Ask = askOf request.Brief
  // no ChangeCounts here: the agent owns the exercise volume of a new
  // plan; the frequency and the derived RIR are what stay fixed
  Forbid = [ OutsideVariants; LeaveFormat ]
  StructureLead = "STRUCTURE THIS PLAN MUST HAVE:"
}

let forAdjustment(request: AdjustmentRequest) : Instructions = {
  Fix = parametersForAdjustment request
  Ask = askOf request.Brief
  // the adjustment keeps the previous plan's counts on the variant it
  // adjusts
  Forbid = [ ChangeCounts; OutsideVariants; LeaveFormat ]
  StructureLead =
    "STRUCTURE GUIDANCE (the previous plan's circuit layout and per-day counts win where they differ):"
}
