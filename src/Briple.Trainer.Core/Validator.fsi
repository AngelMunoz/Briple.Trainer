module Briple.Trainer.Core.Validator

// The validator of agent outputs: the text format (a parse with no
// warnings) and the parameters of the request (gender, variants, weeks,
// counts and RIR). A rejected output is discarded; there is no repair.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Requests

/// The verdict on an agent output. Prescription advisories travel with
/// a valid verdict and never reject it.
type Verdict =
  | Valid of plan: Plan * advisories: Violation list
  | Rejected of Violation list

/// Checks a plan of the domain against the parameters of the request:
/// gender, the brief's frequency (one variant, its days per week), the
/// weeks and the derived RIR.
val checkParameters:
  athlete: Athlete -> weeks: int -> brief: Brief -> plan: Plan -> Violation list

/// Reviews the prescription of every exercise row: one exercise per
/// name, a pure dose, and an effort target consistent with the dose.
/// Advisory only: the violations never reject the verdict, because real
/// source plans carry these patterns and the adjustment flow inherits
/// them.
val checkPrescription: plan: Plan -> Violation list

/// Validates the output of a new plan: clean format, one block of the
/// athlete's gender, one variant of the brief's frequency with its days
/// per week, the weeks and the derived RIR.
val validateNew: request: NewPlanRequest -> output: string -> Verdict

/// Validates the output of an adjustment: the same, plus the counts of
/// the previous plan on the variant the adjustment keeps.
val validateAdjustment: request: AdjustmentRequest -> output: string -> Verdict
