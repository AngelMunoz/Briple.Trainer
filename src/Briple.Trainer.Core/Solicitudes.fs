module Briple.Trainer.Core.Solicitudes

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Parametros

type Pedido = {
  Ajustes: (Objetivo * Delta list) list
  Enfasis: string list
  RirPorSemana: (int * string) list
  Variantes: string list
}

type SolicitudNuevoPlan = {
  Atleta: Atleta
  Memorias: Memoria list
  Semanas: int
  Pedido: Pedido
}

type SolicitudAjuste = {
  Atleta: Atleta
  Memorias: Memoria list
  Semanas: int
  Pedido: Pedido
  PlanAnterior: Plan
  Conteos: (string * int) list
}

let conteosDelPlan (atleta: Atleta) (plan: Plan) : (string * int) list =
  plan.Bloques
  |> List.tryFind(fun bloque -> bloque.Genero = atleta.Genero)
  |> Option.map(fun bloque ->
    bloque.Opciones
    |> List.map(fun opcion ->
      opcion.Id,
      opcion.Semanas
      |> List.sumBy(fun semana ->
        semana.Dias |> List.sumBy(fun dia -> dia.Ejercicios.Length))))
  |> Option.defaultValue []

let solicitudNuevo atleta memorias semanas pedido = {
  Atleta = atleta
  Memorias = memorias
  Semanas = semanas
  Pedido = pedido
}

let solicitudAjuste atleta memorias semanas pedido planAnterior conteos = {
  Atleta = atleta
  Memorias = memorias
  Semanas = semanas
  Pedido = pedido
  PlanAnterior = planAnterior
  Conteos = conteos
}

let delPedido (semanas: int) (pedido: Pedido) : Parametro list = [
  SemanasPlan(semanas, AjustePedido)
  VariantesAtleta(pedido.Variantes, AjustePedido)
  yield!
    pedido.RirPorSemana
    |> List.map(fun (semana, objetivo) ->
      RirSemana(semana, objetivo, AjustePedido))
  yield!
    pedido.Ajustes
    |> List.map(fun (objetivo, deltas) ->
      Ajuste(objetivo, deltas, AjustePedido))
]

let parametrosDeNuevo(solicitud: SolicitudNuevoPlan) : Parametro list =
  delPerfil solicitud.Atleta
  @ deMemoriasConfirmadas solicitud.Memorias
  @ delPedido solicitud.Semanas solicitud.Pedido

let conteoParametro
  (atleta: Atleta)
  (planAnterior: Plan)
  (opcion, total)
  : Parametro =
  let anterior =
    conteosDelPlan atleta planAnterior
    |> List.tryFind(fun (id, _) -> id = opcion)

  match anterior with
  | Some(_, totalAnterior) when totalAnterior = total ->
    ConteoOpcion(opcion, total, PlanAnterior)
  | _ -> ConteoOpcion(opcion, total, AjustePedido)

let parametrosDeAjuste(solicitud: SolicitudAjuste) : Parametro list =
  let baseParametros =
    delPerfil solicitud.Atleta
    @ deMemoriasConfirmadas solicitud.Memorias
    @ delPedido solicitud.Semanas solicitud.Pedido

  baseParametros
  @ (solicitud.Conteos
     |> List.map(conteoParametro solicitud.Atleta solicitud.PlanAnterior))
