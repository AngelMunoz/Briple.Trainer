module Briple.Trainer.Core.Plan.Escritor

// Render del modelo al formato de texto del archivo: el inverso del
// parser. Las notas de dia no tienen linea en el formato y no se
// emiten; todo lo demas viaja verbatim.

open Briple.Trainer.Core.Plan.Types

/// Escribe el plan completo como texto del formato, un bloque por
/// linea con indentacion fija.
val escribir: plan: Plan -> string
