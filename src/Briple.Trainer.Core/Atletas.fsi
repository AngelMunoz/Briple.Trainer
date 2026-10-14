module Briple.Trainer.Core.Atletas

// La capa de atletas: cada entrenado es un perfil propio con las
// variantes que puede entrenar. Un plan lleva un bloque por atleta y
// las opciones del bloque son sus variantes; el formato no tiene
// nombres, el género discrimina al atleta dentro del archivo.

open Briple.Trainer.Core.Plan.Types

/// Un entrenado. `Variantes` son las opciones que puede entrenar
/// ("3dias", "5dias") para su propia flexibilidad; no son atletas
/// distintos.
type Atleta = {
  Nombre: string
  Genero: Genero
  Variantes: string list
  Metas: string list
  Enfasis: string list
}

/// Los atletas que cubre un plan: uno por bloque, con las opciones del
/// bloque como variantes. El nombre queda como el género en minúsculas
/// hasta que el entrenador lo complete.
val atletasDelPlan: plan: Plan -> Atleta list
