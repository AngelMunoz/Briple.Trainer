module Briple.Trainer.Core.Parameters

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories

type OriginClass =
  | FromProfile
  | FromConfirmedMemory
  | FromPreviousPlan
  | FromBrief
  | FromAgent

type Origin =
  | AthleteProfile
  | ConfirmedMemory of memory: Memory
  | PreviousPlan
  | TrainerBrief
  | Agent

type Parameter =
  | PlanWeeks of weeks: int * origin: Origin
  | AthleteVariants of variants: string list * origin: Origin
  | VariantCount of variant: string * total: int * origin: Origin
  | WeekRir of week: int * target: string * origin: Origin
  | Adjustment of target: Target * deltas: Delta list * origin: Origin

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

let originsFor(field: Field) : OriginClass list =
  match field with
  | Title -> [ FromAgent; FromBrief ]
  | Weeks -> [ FromBrief ]
  | Generated -> [ FromAgent ]
  | FileOrigin -> [ FromAgent ]
  | Variants -> [ FromProfile; FromBrief ]
  | VariantTitle -> [ FromAgent ]
  | VariantWeeks -> [ FromPreviousPlan; FromBrief ]
  | Days -> [ FromProfile; FromPreviousPlan; FromBrief ]
  | DayTitle -> [ FromAgent ]
  | DayNotes -> [ FromAgent; FromBrief ]
  | Circuit -> [ FromPreviousPlan; FromAgent ]
  | Laps -> [ FromPreviousPlan; FromAgent; FromBrief ]
  | Order -> [ FromPreviousPlan; FromAgent ]
  | Name -> [ FromPreviousPlan; FromAgent; FromConfirmedMemory; FromBrief ]
  | Reps -> [ FromPreviousPlan; FromAgent; FromConfirmedMemory; FromBrief ]
  | Rir -> [ FromPreviousPlan; FromAgent; FromConfirmedMemory; FromBrief ]
  | Rest -> [ FromPreviousPlan; FromAgent; FromBrief ]
  | Notes -> [ FromPreviousPlan; FromAgent; FromConfirmedMemory; FromBrief ]
  | Extras -> [ FromPreviousPlan; FromAgent; FromBrief ]

let fields: Field list = [
  Title
  Weeks
  Generated
  FileOrigin
  Variants
  VariantTitle
  VariantWeeks
  Days
  DayTitle
  DayNotes
  Circuit
  Laps
  Order
  Name
  Reps
  Rir
  Rest
  Notes
  Extras
]

let classOf(origin: Origin) : OriginClass =
  match origin with
  | AthleteProfile -> FromProfile
  | ConfirmedMemory _ -> FromConfirmedMemory
  | PreviousPlan -> FromPreviousPlan
  | TrainerBrief -> FromBrief
  | Agent -> FromAgent

let ofProfile(athlete: Athlete) : Parameter list = [
  AthleteVariants(athlete.Variants, AthleteProfile)
]

let ofConfirmedMemories(memories: Memory list) : Parameter list =
  memories
  |> List.choose(fun memory ->
    match memory.Status with
    | Confirmed ->
      let target =
        match memory.Origin with
        | Mark mark -> mark.Target
        | Exemption exemption -> exemption.Target

      Some(Adjustment(target, memory.Deltas, ConfirmedMemory memory))
    | Proposed
    | Discarded -> None)
