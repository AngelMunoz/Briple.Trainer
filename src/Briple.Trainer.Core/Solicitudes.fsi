module Briple.Trainer.Core.Solicitudes

// El contrato de solicitud: lo que viaja al agente en cada flujo. Las
// solicitudes son autocontenidas — atleta, memorias confirmadas,
// período, pedido y conteos objetivo — sin memoria de conversación;
// el ajuste agrega el plan anterior completo.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Parametros

/// El pedido del entrenador: ajustes sueltos, énfasis, RIR por semana
/// y las variantes a generar (subconjunto de las del atleta).
type Pedido = {
  Ajustes: (Objetivo * Delta list) list
  Enfasis: string list
  RirPorSemana: (int * string) list
  Variantes: string list
}

/// Plan nuevo desde cero; los conteos objetivo por opción viajan
/// explícitos y el validador los comprueba en la salida.
type SolicitudNuevoPlan = {
  Atleta: Atleta
  Memorias: Memoria list
  Semanas: int
  Pedido: Pedido
  Conteos: (string * int) list
}

/// Ajuste sobre el plan anterior; los conteos objetivo viajan
/// explícitos y el validador los comprueba en la salida.
type SolicitudAjuste = {
  Atleta: Atleta
  Memorias: Memoria list
  Semanas: int
  Pedido: Pedido
  PlanAnterior: Plan
  Conteos: (string * int) list
}

/// Los #ej de cada opción del atleta en un plan.
val conteosDelPlan: atleta: Atleta -> plan: Plan -> (string * int) list

val solicitudNuevo:
  atleta: Atleta ->
  memorias: Memoria list ->
  semanas: int ->
  pedido: Pedido ->
  conteos: (string * int) list ->
    SolicitudNuevoPlan

val solicitudAjuste:
  atleta: Atleta ->
  memorias: Memoria list ->
  semanas: int ->
  pedido: Pedido ->
  planAnterior: Plan ->
  conteos: (string * int) list ->
    SolicitudAjuste

/// Los parámetros trazados del plan nuevo: perfil, memorias, pedido y
/// conteos; sin plan anterior, los conteos siempre trazan al pedido.
val parametrosDeNuevo: solicitud: SolicitudNuevoPlan -> Parametro list

/// Los parámetros trazados del ajuste: los del plan nuevo más los
/// conteos; un conteo igual al del plan anterior traza a PlanAnterior,
/// uno distinto a AjustePedido.
val parametrosDeAjuste: solicitud: SolicitudAjuste -> Parametro list
