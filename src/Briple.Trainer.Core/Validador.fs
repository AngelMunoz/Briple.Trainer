module Briple.Trainer.Core.Validador

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Solicitudes

type Fallo = { Regla: string; Detalle: string }

type Veredicto =
  | Valido of plan: Plan
  | Rechazado of Fallo list

let fallo regla detalle = { Regla = regla; Detalle = detalle }

/// Revisa un plan ya convertido del dominio contra los parametros de
/// la solicitud: genero, opciones, semanas, conteos y RIR.
let revisarParametros
  (atleta: Atleta)
  (semanas: int)
  (pedido: Pedido)
  (conteos: (string * int) list)
  (plan: Plan)
  : Fallo list =
  let fallos = ResizeArray<Fallo>()

  match plan.Bloques with
  | [ bloque ] when bloque.Genero = atleta.Genero -> ()
  | _ ->
    fallos.Add(
      fallo
        "genero"
        $"se esperaba solo el bloque {Genero.toString atleta.Genero}, hay {plan.Bloques.Length} bloques"
    )

  if plan.Semanas <> semanas then
    fallos.Add(
      fallo "semanas" $"se esperaban {semanas}, el plan declara {plan.Semanas}"
    )

  let opciones =
    plan.Bloques
    |> List.filter(fun bloque -> bloque.Genero = atleta.Genero)
    |> List.collect(fun bloque -> bloque.Opciones)

  let esperadas = List.sort pedido.Variantes
  let reales = opciones |> List.map(fun opcion -> opcion.Id) |> List.sort

  if esperadas <> reales then
    fallos.Add(
      fallo "opciones" $"se esperaban {esperadas}, el plan trae {reales}"
    )

  for opcion in opciones do
    let numeros = opcion.Semanas |> List.map(fun semana -> semana.Numero)

    if numeros <> [ 1..semanas ] then
      fallos.Add(
        fallo
          "semanas-opcion"
          $"la opcion {opcion.Id} trae las semanas {numeros}, se esperaba 1..{semanas}"
      )

  let conteosReales = conteosDelPlan atleta plan |> Map.ofList

  for opcion, total in conteos do
    match Map.tryFind opcion conteosReales with
    | Some actual when actual = total -> ()
    | Some actual ->
      fallos.Add(
        fallo
          "conteo"
          $"la opcion {opcion} debe tener {total} #ej, tiene {actual}"
      )
    | None ->
      fallos.Add(fallo "conteo" $"la opcion {opcion} no existe en la salida")

  for semana, objetivo in pedido.RirPorSemana do
    match tryMesocicloRir plan semana with
    | Some actual when actual = objetivo -> ()
    | Some actual ->
      fallos.Add(
        fallo
          "rir"
          $"la semana {semana} debe declarar RIR {objetivo}, declara {actual}"
      )
    | None ->
      fallos.Add(fallo "rir" $"la semana {semana} no declara RIR {objetivo}")

  List.ofSeq fallos

let validarComun atleta semanas pedido conteos (salida: string) =
  match parsePlan salida with
  | Error error ->
    Rechazado [ fallo "formato" $"linea {error.Line}: {error.Message}" ]
  | Ok parsed ->
    let fallosFormato =
      parsed.Warnings
      |> List.map(fun warning ->
        fallo "formato" $"linea {warning.Line}: {warning.Message}")

    let fallosParametros =
      revisarParametros atleta semanas pedido conteos parsed.Plan

    match fallosFormato @ fallosParametros with
    | [] -> Valido parsed.Plan
    | fallos -> Rechazado fallos

/// Valida la salida de un plan nuevo: formato limpio, un bloque del
/// genero del atleta, sus variantes, las semanas y el RIR del pedido.
let validarNuevo (solicitud: SolicitudNuevoPlan) (salida: string) =
  validarComun solicitud.Atleta solicitud.Semanas solicitud.Pedido [] salida

/// Valida la salida de un ajuste: lo anterior mas los conteos objetivo
/// por opcion.
let validarAjuste (solicitud: SolicitudAjuste) (salida: string) =
  validarComun
    solicitud.Atleta
    solicitud.Semanas
    solicitud.Pedido
    solicitud.Conteos
    salida
