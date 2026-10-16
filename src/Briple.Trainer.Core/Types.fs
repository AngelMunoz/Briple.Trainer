module Briple.Trainer.Core.Plan.Types

// The IWSAM leaves and edges below declare interfaces with static
// abstract members; this file has no signature file, so it also hosts
// their implementations on the edge types.
#nowarn "3535"

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

type ParsedPlan = {
  Plan: Plan
  Warnings: ParseWarning list
}

// --- the plan tree's named walk ------------------------------------------------
//
// The tree shape (block → variant → week → day → exercise) mirrors the
// file format and stays; these name the walk once so consumers read the
// level they need instead of nesting the same loops.

module Tree =

  /// Every variant of every block, in file order.
  let variantsOf(plan: Plan) : Variant seq = seq {
    for block in plan.Blocks do
      for variant in block.Variants -> variant
  }

  /// Every day of every week, in file order.
  let daysOf(plan: Plan) : Day seq = seq {
    for block in plan.Blocks do
      for variant in block.Variants do
        for week in variant.Weeks do
          for day in week.Days -> day
  }

  /// Every exercise row, in file order.
  let exercisesOf(plan: Plan) : Exercise seq = seq {
    for block in plan.Blocks do
      for variant in block.Variants do
        for week in variant.Weeks do
          for day in week.Days do
            for exercise in day.Exercises -> exercise
  }

// --- IWSAM error leaves and edges -------------------------------------------
//
// This file has no signature file, so it hosts the static-abstract
// interface implementations the edge types carry (a signature file
// cannot hold them). A `raw` or `message` string is legal only when it
// comes from outside the domain: the raw input that failed or an
// external library's message.

/// The structural failures of the .briple format, as the parse leaf
/// declares them.
type IPlanFormatError<'e> =
  static abstract Unterminated: lastLine: int * block: string -> 'e

  static abstract MismatchedEnd:
    line: int * found: string * expected: string -> 'e

  static abstract MissingStart: line: int -> 'e
  static abstract BadVersion: line: int * raw: string -> 'e
  static abstract BadWeekNumber: line: int * raw: string -> 'e

/// The display edge of the parse leaf: what a stored plan's structural
/// failure looks like. The persistence path matches on these cases.
type PlanFailure =
  | Unterminated of lastLine: int * block: string
  | MismatchedEnd of line: int * found: string * expected: string
  | MissingStart of line: int
  | BadVersion of line: int * raw: string
  | BadWeekNumber of line: int * raw: string

  interface IPlanFormatError<PlanFailure> with
    static member Unterminated(lastLine, block) = Unterminated(lastLine, block)

    static member MismatchedEnd(line, found, expected) =
      MismatchedEnd(line, found, expected)

    static member MissingStart line = MissingStart line

    static member BadVersion(line, raw) = BadVersion(line, raw)

    static member BadWeekNumber(line, raw) = BadWeekNumber(line, raw)

/// One invalid field of the model's JSON answer; the decode leaf
/// accumulates every failure in one case (failure-paths: never only the
/// first). The path is the JSON path of the field.
type DataFailure =
  | UnreadableJson of message: string
  | BadGender of raw: string
  | MissingTitle
  | BadWeeks of value: int
  | EmptyVariantId of path: string
  | MissingWeekNumber of path: string
  | MissingOrder of path: string
  | UnknownDay of path: string * raw: string
  | EmptyExerciseName of path: string

/// The JSON decode leaf's declared failures.
type IPlanDataError<'e> =
  static abstract InvalidData: failures: DataFailure list -> 'e

/// The dose leaf's declared failures ("reps" is not a pure range or a
/// pure duration).
type IDoseError<'e> =
  static abstract NotPureRange: raw: string -> 'e
  static abstract UnknownUnit: raw: string -> 'e

/// The advisory edge of the dose leaf: prescription findings that never
/// reject a verdict.
type PrescriptionAdvisory =
  | NotPureRange of raw: string
  | UnknownUnit of raw: string
  | MultipleExercises of name: string
  | RepsWithoutEffort of name: string
  | TimedWithRir of name: string

  interface IDoseError<PrescriptionAdvisory> with
    static member NotPureRange raw = NotPureRange raw
    static member UnknownUnit raw = UnknownUnit raw

/// One violated rule of the agent output, with typed payloads: the UI
/// maps each case to localized prose. This is the agent flow's edge: it
/// implements both error leaves so the parse and the decode compose
/// into a verdict without mapError.
type Violation =
  | GenderMismatch of expected: Gender * found: int
  | WrongWeeks of expected: int * found: int
  | WrongVariants of expected: string list * found: string list
  | BadVariantWeeks of variant: string * found: int list * expected: int
  | WrongFrequency of variant: string * week: int * found: int * expected: int
  | WrongRir of week: int * found: string * expected: string
  | MissingRir of week: int * expected: string
  | CountChanged of variant: string * expected: int * found: int
  | BrokenFormat of failure: PlanFailure
  | FormatWarning of warning: ParseWarning
  | Data of failures: DataFailure list
  | Prescription of advisory: PrescriptionAdvisory

  interface IPlanFormatError<Violation> with
    static member Unterminated(lastLine, block) =
      BrokenFormat(Unterminated(lastLine, block))

    static member MismatchedEnd(line, found, expected) =
      BrokenFormat(MismatchedEnd(line, found, expected))

    static member MissingStart line = BrokenFormat(MissingStart line)

    static member BadVersion(line, raw) = BrokenFormat(BadVersion(line, raw))

    static member BadWeekNumber(line, raw) =
      BrokenFormat(BadWeekNumber(line, raw))

  interface IPlanDataError<Violation> with
    static member InvalidData failures = Data failures
