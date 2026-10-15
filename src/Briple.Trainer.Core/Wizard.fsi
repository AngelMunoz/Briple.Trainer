module Briple.Trainer.Core.Wizard

// The six steps of the generation flow with confirmation. Each case
// carries its input; the output of one step is the input of the next.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests

/// What step 5 produces: a new plan or an adjustment.
type Generation =
  | NewPlan of request: NewPlanRequest
  | PlanAdjustment of request: AdjustmentRequest

/// One step of the wizard.
type Step =
  /// 1. Input: the registered athletes. Output: the chosen athlete.
  | ChooseAthlete
  /// 2. Input: the athlete and their proposed memories. Output: the
  /// confirmed memories, each with the accepted deltas.
  | ReviewMemories of athlete: Athlete * proposals: Memory list
  /// 3. Input: the athlete. Output: the period in weeks (1 to 4).
  | ChoosePeriod of athlete: Athlete
  /// 4. Input: athlete, confirmed memories and weeks. Output: the
  /// brief (adjustments, focus, RIR, variants).
  | DefineBrief of athlete: Athlete * confirmed: Memory list * weeks: int
  /// 5. Input: the assembled request. Output: the candidate plan and the
  /// validator result.
  | Generate of generation: Generation
  /// 6. Input: the candidate with its warnings. Output: confirmed into
  /// the history, regenerated or discarded.
  | Review of candidate: Plan * warnings: ParseWarning list

/// The first step of the flow.
val initial: unit -> Step
