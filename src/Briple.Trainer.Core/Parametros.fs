module Briple.Trainer.Core.Parametros

open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias

type ClaseOrigen =
  | DelPerfil
  | DeMemoriaConfirmada
  | DelPlanAnterior
  | DelAjustePedido
  | DelAgente

type Origen =
  | PerfilDelAtleta
  | MemoriaConfirmada of memoria: Memoria
  | PlanAnterior
  | AjustePedido
  | Agente

type Parametro =
  | SemanasPlan of semanas: int * origen: Origen
  | VariantesAtleta of variantes: string list * origen: Origen
  | ConteoOpcion of opcion: string * total: int * origen: Origen
  | RirSemana of semana: int * objetivo: string * origen: Origen
  | Ajuste of objetivo: Objetivo * deltas: Delta list * origen: Origen

type Campo =
  | Titulo
  | Semanas
  | Generado
  | OrigenArchivo
  | Opciones
  | TituloOpcion
  | SemanasDeOpcion
  | Dias
  | TituloDia
  | NotasDia
  | Circuito
  | Vueltas
  | Orden
  | Nombre
  | Reps
  | Rir
  | Descanso
  | Notas
  | Extras

let origenesDe(campo: Campo) : ClaseOrigen list =
  match campo with
  | Titulo -> [ DelAgente; DelAjustePedido ]
  | Semanas -> [ DelAjustePedido ]
  | Generado -> [ DelAgente ]
  | OrigenArchivo -> [ DelAgente ]
  | Opciones -> [ DelPerfil; DelAjustePedido ]
  | TituloOpcion -> [ DelAgente ]
  | SemanasDeOpcion -> [ DelPlanAnterior; DelAjustePedido ]
  | Dias -> [ DelPerfil; DelPlanAnterior; DelAjustePedido ]
  | TituloDia -> [ DelAgente ]
  | NotasDia -> [ DelAgente; DelAjustePedido ]
  | Circuito -> [ DelPlanAnterior; DelAgente ]
  | Vueltas -> [ DelPlanAnterior; DelAgente; DelAjustePedido ]
  | Orden -> [ DelPlanAnterior; DelAgente ]
  | Nombre -> [
      DelPlanAnterior
      DelAgente
      DeMemoriaConfirmada
      DelAjustePedido
    ]
  | Reps -> [ DelPlanAnterior; DelAgente; DeMemoriaConfirmada; DelAjustePedido ]
  | Rir -> [ DelPlanAnterior; DelAgente; DeMemoriaConfirmada; DelAjustePedido ]
  | Descanso -> [ DelPlanAnterior; DelAgente; DelAjustePedido ]
  | Notas -> [
      DelPlanAnterior
      DelAgente
      DeMemoriaConfirmada
      DelAjustePedido
    ]
  | Extras -> [ DelPlanAnterior; DelAgente; DelAjustePedido ]

let campos: Campo list = [
  Titulo
  Semanas
  Generado
  OrigenArchivo
  Opciones
  TituloOpcion
  SemanasDeOpcion
  Dias
  TituloDia
  NotasDia
  Circuito
  Vueltas
  Orden
  Nombre
  Reps
  Rir
  Descanso
  Notas
  Extras
]

let claseDe(origen: Origen) : ClaseOrigen =
  match origen with
  | PerfilDelAtleta -> DelPerfil
  | MemoriaConfirmada _ -> DeMemoriaConfirmada
  | PlanAnterior -> DelPlanAnterior
  | AjustePedido -> DelAjustePedido
  | Agente -> DelAgente

let delPerfil(atleta: Atleta) : Parametro list = [
  VariantesAtleta(atleta.Variantes, PerfilDelAtleta)
]

let deMemoriasConfirmadas(memorias: Memoria list) : Parametro list =
  memorias
  |> List.choose(fun memoria ->
    match memoria.Estado with
    | Confirmada ->
      Some(
        Ajuste(
          memoria.Origen.Objetivo,
          memoria.Deltas,
          MemoriaConfirmada memoria
        )
      )
    | Propuesta
    | Descartada -> None)
