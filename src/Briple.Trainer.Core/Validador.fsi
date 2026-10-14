module Briple.Trainer.Core.Validador

// El validador de salidas del agente: el formato del texto (parseo sin
// warnings) y los parámetros de la solicitud (genero, opciones,
// semanas, conteos y RIR). Una salida rechazada se descarta; no hay
// reparo automatico.

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Solicitudes

/// Una regla violada, con el detalle legible.
type Fallo = { Regla: string; Detalle: string }

/// El veredicto sobre una salida del agente.
type Veredicto =
  | Valido of plan: Plan
  | Rechazado of Fallo list

/// Revisa un plan del dominio contra los parametros de la solicitud:
/// genero, opciones, semanas, conteos y RIR.
val revisarParametros:
  atleta: Atleta ->
  semanas: int ->
  pedido: Pedido ->
  conteos: (string * int) list ->
  plan: Plan ->
    Fallo list

/// Valida la salida de un plan nuevo: formato limpio, un bloque del
/// genero del atleta, sus variantes, las semanas y el RIR del pedido.
val validarNuevo: solicitud: SolicitudNuevoPlan -> salida: string -> Veredicto

/// Valida la salida de un ajuste: lo anterior mas los conteos objetivo
/// por opcion.
val validarAjuste: solicitud: SolicitudAjuste -> salida: string -> Veredicto
