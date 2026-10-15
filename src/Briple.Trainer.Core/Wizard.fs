module Briple.Trainer.Core.Wizard

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Solicitudes

type Generation =
  | NewPlan of request: NewPlanRequest
  | PlanAdjustment of request: AdjustmentRequest

type Step =
  | ChooseAthlete
  | ReviewMemories of athlete: Athlete * proposals: Memory list
  | ChoosePeriod of athlete: Athlete
  | DefineBrief of athlete: Athlete * confirmed: Memory list * weeks: int
  | Generate of generation: Generation
  | Review of candidate: Plan * warnings: ParseWarning list

let initial() : Step = ChooseAthlete
