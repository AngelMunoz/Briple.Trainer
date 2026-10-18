module Briple.Trainer.Core.Plan.Sanitize

// The write boundary of the plan tree: every string goes through the
// same per-field caps and loses the characters that can break a #ej row
// or a #meta line. The parser already rejected a broken file; this is
// the guard for the trees that come from an edit or from the model.
//
// The warnings are visibility, not censorship: a field that looks like
// an instruction is reported to the trainer and stored as data.

open Briple.Trainer.Core.Plan.Types

/// The field a content warning points at; the UI maps each case to a
/// localized sentence.
type ContentField =
  | PlanTitle
  | PlanOrigin
  | ExerciseName of name: string
  | ExerciseNotes of name: string
  | ExtraLine of key: string

/// One field that carries an instruction-like token.
type ContentWarning = { Field: ContentField; Pattern: string }

/// Normalizes every written string: one line, no '|' in a #ej field,
/// no control characters, and the cap of each field. Prose fields keep
/// '|' because they use it as their own separator.
val normalize: plan: Plan -> Plan

/// The fields whose text contains an English or Spanish instruction
/// pattern. One warning per field, with the first matched pattern.
val contentWarnings: plan: Plan -> ContentWarning list
