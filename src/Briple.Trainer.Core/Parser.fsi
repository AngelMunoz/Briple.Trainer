module Briple.Trainer.Core.Plan.Parser

#nowarn "3535"

// Parser for the line-based training plan format (#start/#end blocks,
// #meta, #ej rows, #x extras). Line bodies go through XParsec; block
// nesting is assembled by recursive descent so every diagnostic carries
// a line number.
//
// Recoverable problems (unknown blocks, unrecognized day names,
// malformed #ej rows, unknown meta keys, duplicates, empty days, wrong
// versions) come back as warnings in ParsedPlan. Structural breakage
// (#end mismatches, unterminated blocks, a missing #start plan header)
// is the leaf's error: an IWSAM interface the caller's edge type
// implements, so failures compose without mapError and the edge matches
// exhaustively.

open System
open Briple.Trainer.Core.Plan.Types

/// Maps a day name ("Lunes", "Monday", …) through the es/en table; unknown
/// names stay in the model but the calendar projection skips them.
val tryDayOfWeek: name: string -> DayOfWeek option

/// Parses a plan file into the domain model; recoverable problems come
/// back as warnings, structural breakage as the leaf's error (both
/// carry line numbers).
val parsePlan<'e when IPlanFormatError<'e>> :
  input: string -> Result<ParsedPlan, 'e>

/// The fixed display edge of the parse leaf.
module Parse =

  /// Parses with `PlanFailure`; the annotation is what fixes `'e`.
  val parse: input: string -> Result<ParsedPlan, PlanFailure>

  /// One diagnostic line per failure. The file format is the domain, so
  /// the description is technical and single-language, like the parse
  /// warnings themselves.
  val describe: failure: PlanFailure -> string
