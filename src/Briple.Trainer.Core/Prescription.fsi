module Briple.Trainer.Core.Prescription

// The typed layer over the verbatim reps/rir fields: the dose and the
// effort target parsed at the boundary. The file format and the JSON
// contract stay verbatim; this layer only reads them.

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

/// Parses a dose: a pure rep range ("6-8", "10") or a pure duration
/// ("30-45 s", "20 min"). Qualifiers fail by design: rounds come from
/// the laps and sides go in notes.
val tryParseDose: text: string -> Result<Dose, string>

/// Parses an effort target: "-" is ValueNone; "1-2" and "2" are Rir;
/// "rpe 6-7" is Rpe; any other text is a zone description ("zona 2").
val tryParseEffort: text: string -> Result<Effort voption, string>
