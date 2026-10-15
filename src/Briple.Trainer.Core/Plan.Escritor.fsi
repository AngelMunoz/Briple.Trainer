module Briple.Trainer.Core.Plan.Escritor

// Render of the model to the file text format: the inverse of the
// parser. Day notes have no line in the format and are not emitted;
// everything else travels verbatim.

open Briple.Trainer.Core.Plan.Types

/// Writes the full plan as format text, one block per line with fixed
/// indentation.
val write: plan: Plan -> string
