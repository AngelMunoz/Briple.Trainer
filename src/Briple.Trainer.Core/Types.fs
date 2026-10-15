module Briple.Trainer.Core.Plan.Types

// Domain model of the training plan format, mirroring the file one-to-one.
// Reps/Rir/Rest/Notes are stored verbatim from the file; "-" means "not
// present" for the UI.

type Exercise = {
  Circuit: string
  Laps: string
  Order: int
  Name: string
  Reps: string
  Rir: string
  Rest: string
  Notes: string
}

type Day = {
  Id: string
  Title: string option
  Notes: string list
  Exercises: Exercise list
}

type Week = { Number: int; Days: Day list }

type Variant = {
  Id: string
  Title: string option
  Weeks: Week list
}

type Gender =
  | Male
  | Female

type Block = {
  Gender: Gender
  Variants: Variant list
}

type Plan = {
  Version: int
  Title: string
  Weeks: int
  Generated: string
  Origin: string
  Blocks: Block list
  Extras: string list
}

module Gender =

  /// File-format tokens of the .briple block header; the format is fixed
  /// and stays Spanish.
  let toFileToken(gender: Gender) =
    match gender with
    | Male -> "hombre"
    | Female -> "mujer"

  let tryFileToken(value: string) =
    match value.Trim().ToLowerInvariant() with
    | "hombre" -> Some Male
    | "mujer" -> Some Female
    | _ -> None

  /// Protocol names of the agent JSON contract.
  let toProtocol(gender: Gender) =
    match gender with
    | Male -> "male"
    | Female -> "female"

  let tryProtocol(value: string) =
    match value.Trim().ToLowerInvariant() with
    | "male" -> Some Male
    | "female" -> Some Female
    | _ -> None

type WarningKind =
  | UnknownDayName
  | UnknownBlockSkipped
  | UnknownMetaKey
  | UnknownLine
  | BadEjField
  | DuplicateEntry
  | EmptyDay
  | UnsupportedVersion
  | MissingMeta

type ParseWarning = {
  Line: int
  Kind: WarningKind
  Message: string
}

type ParseError = {
  Line: int
  Message: string
  Detail: string option
}

type ParsedPlan = {
  Plan: Plan
  Warnings: ParseWarning list
}
