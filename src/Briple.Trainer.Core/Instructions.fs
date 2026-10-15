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
  Forbid = [ ChangeCounts; OutsideVariants; LeaveFormat ]
}

let forAdjustment(request: AdjustmentRequest) : Instructions = {
  Fix = parametersForAdjustment request
  Ask = agentFields
  Forbid = [ ChangeCounts; OutsideVariants; LeaveFormat ]
}
