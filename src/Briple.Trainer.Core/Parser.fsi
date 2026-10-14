module Plan.Parser

open System
open Plan.Types

/// Parses a plan file into the domain model; recoverable problems come back
/// as warnings, structural breakage as a hard error (both carry line numbers).
val parsePlan: input: string -> Result<ParsedPlan, ParseError>

/// Maps a day name ("Lunes", "Monday", …) through the es/en table; unknown
/// names stay in the model but the calendar projection skips them.
val tryDayOfWeek: name: string -> DayOfWeek option
