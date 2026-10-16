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
}

let agentFields: Field list =
  fields
  |> List.filter(fun field -> originsFor field |> List.contains FromAgent)

let forNew(request: NewPlanRequest) : Instructions = {
  Fix = parametersForNew request
  Ask = agentFields
  // no ChangeCounts here: the agent owns the exercise volume of a new
  // plan; the frequency and the derived RIR are what stay fixed
  Forbid = [ OutsideVariants; LeaveFormat ]
}

let forAdjustment(request: AdjustmentRequest) : Instructions = {
  Fix = parametersForAdjustment request
  Ask = agentFields
  // the adjustment keeps the previous plan's counts on the variant it
  // adjusts
  Forbid = [ ChangeCounts; OutsideVariants; LeaveFormat ]
}
