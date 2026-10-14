module Briple.Trainer.Core.Atletas

open Briple.Trainer.Core.Plan.Types

type Atleta = {
  Nombre: string
  Genero: Genero
  Variantes: string list
  Metas: string list
  Enfasis: string list
}

let atletasDelPlan(plan: Plan) : Atleta list =
  plan.Bloques
  |> List.map(fun bloque -> {
    Nombre = Genero.toString bloque.Genero
    Genero = bloque.Genero
    Variantes = bloque.Opciones |> List.map(fun opcion -> opcion.Id)
    Metas = []
    Enfasis = []
  })
