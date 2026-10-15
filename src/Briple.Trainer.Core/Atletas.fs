module Briple.Trainer.Core.Atletas

open Briple.Trainer.Core.Plan.Types

type Atleta = {
  Nombre: string
  Genero: Gender
  Variantes: string list
  Metas: string list
  Enfasis: string list
}

let atletasDelPlan(plan: Plan) : Atleta list =
  plan.Blocks
  |> List.map(fun bloque -> {
    Nombre = Gender.toFileToken bloque.Gender
    Genero = bloque.Gender
    Variantes = bloque.Variants |> List.map(fun opcion -> opcion.Id)
    Metas = []
    Enfasis = []
  })
