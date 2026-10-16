module Briple.Trainer.Core.Parameters

// The parameter layer: the values that travel in the request, each one
// with a traceable origin. Only the format is deterministic; counts, RIR
// and laps travel as parameters and the validator checks them against
// the output.

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories

/// Class of origin of a parametric value; the inventory declares which
/// ones apply to each field.
type OriginClass =
  | FromProfile
  | FromConfirmedMemory
  | FromPreviousPlan
  | FromBrief
  | FromAgent
  | FromDerived

/// Traceable origin of a parameter: the class plus, when it applies, the
/// concrete fact that produced it.
type Origin =
  | AthleteProfile
  | ConfirmedMemory of memory: Memory
  | PreviousPlan
  | TrainerBrief
  | Agent
  | Derived

/// A value of the request with its traced origin.
type Parameter =
  | PlanWeeks of weeks: int * origin: Origin
  | AthleteVariants of variants: string list * origin: Origin
  | VariantCount of variant: string * total: int * origin: Origin
  | WeekRir of week: int * target: string * origin: Origin
  | Adjustment of target: Target * deltas: Delta list * origin: Origin

/// Every plan field and where its value may come from: the traceability
/// inventory the request uses to fix, ask or forbid.
type Field =
  | Title
  | Weeks
  | Generated
  | FileOrigin
  | Variants
  | VariantTitle
  | VariantWeeks
  | Days
  | DayTitle
  | DayNotes
  | Circuit
  | Laps
  | Order
  | Name
  | Reps
  | Rir
  | Rest
  | Notes
  | Extras

/// The valid origin classes of each field; never empty.
val originsFor: field: Field -> OriginClass list

/// All the plan fields, in format order.
val fields: Field list

/// The class of a traced origin.
val classOf: origin: Origin -> OriginClass

/// The adjustments confirmed memories bring into the next request;
/// proposed and discarded ones contribute nothing.
val ofConfirmedMemories: memories: Memory list -> Parameter list
