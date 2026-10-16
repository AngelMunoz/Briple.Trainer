module Briple.Trainer.Core.Prescription

#nowarn "3535"

open System
open System.Text.RegularExpressions
open Briple.Trainer.Core.Plan.Types

/// How an exercise's dose is counted.
type Dose =
  | Reps of lo: int * hi: int
  | Timed of loSeconds: int * hiSeconds: int

/// Effort target: RIR for rep-based work; perceived effort or zone for
/// timed work.
type Effort =
  | Rir of lo: int * hi: int
  | Rpe of lo: int * hi: int
  | Zone of text: string

let inline group (matchResult: Match) (index: int) =
  let value = matchResult.Groups.[index].Value

  if value = "" then None else Some value

/// Parses a dose: a pure rep range ("6-8", "10") or a pure duration
/// ("30-45 s", "20 min"). Qualifiers fail by design ("8-12 reps por
/// ronda", "30-45 s por lado"): rounds come from the laps and sides go
/// in notes.
let tryParseDose<'e when IDoseError<'e>>(text: string) : Result<Dose, 'e> =
  let clean = text.Trim().ToLowerInvariant()

  let rangeMatch =
    Regex.Match(
      clean,
      @"^(\d+)(?:\s*-\s*(\d+))?\s*(s|seg|segundos|min|minutos)?$"
    )

  if not rangeMatch.Success then
    Error('e.NotPureRange(text.Trim()))
  else
    let lo = int rangeMatch.Groups.[1].Value

    let hi =
      match group rangeMatch 2 with
      | Some hi -> int hi
      | None -> lo

    match group rangeMatch 3 with
    | None -> Ok(Reps(lo, hi))
    | Some "s"
    | Some "seg"
    | Some "segundos" -> Ok(Timed(lo, hi))
    | Some "min"
    | Some "minutos" -> Ok(Timed(lo * 60, hi * 60))
    | Some unit -> Error('e.UnknownUnit unit)

/// Parses an effort target: "-" is ValueNone; "1-2" and "2" are Rir;
/// "rpe 6-7" is Rpe; any other text is a zone description ("zona 2").
let tryParseEffort(text: string) : Effort voption =
  let clean = text.Trim().ToLowerInvariant()

  if clean = "" || clean = "-" then
    ValueNone
  else
    let rpeMatch = Regex.Match(clean, @"^rpe\s*(\d+)(?:\s*-\s*(\d+))?$")

    if rpeMatch.Success then
      let lo = int rpeMatch.Groups.[1].Value

      let hi =
        match group rpeMatch 2 with
        | Some hi -> int hi
        | None -> lo

      ValueSome(Rpe(lo, hi))
    else
      let rangeMatch = Regex.Match(clean, @"^(\d+)(?:\s*-\s*(\d+))?$")

      if rangeMatch.Success then
        let lo = int rangeMatch.Groups.[1].Value

        let hi =
          match group rangeMatch 2 with
          | Some hi -> int hi
          | None -> lo

        ValueSome(Rir(lo, hi))
      else
        ValueSome(Zone(text.Trim()))
