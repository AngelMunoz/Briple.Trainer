module Parametros

// La capa de parámetros: los valores que viajan en la solicitud, cada
// uno con su origen trazable. Solo el formato es determinista; conteos,
// RIR y vueltas viajan como parámetros y el validador los comprueba
// contra la salida.

open Atletas
open Memorias

/// Clase de origen de un valor paramétrico; el inventario declara
/// cuáles aplican a cada campo.
type ClaseOrigen =
  | DelPerfil
  | DeMemoriaConfirmada
  | DelPlanAnterior
  | DelAjustePedido
  | DelAgente

/// Origen trazable de un parámetro: la clase y, cuando aplica, el
/// hecho concreto que lo produjo.
type Origen =
  | PerfilDelAtleta
  | MemoriaConfirmada of memoria: Memoria
  | PlanAnterior
  | AjustePedido
  | Agente

/// Un valor de la solicitud con su origen trazado.
type Parametro =
  | SemanasPlan of semanas: int * origen: Origen
  | VariantesAtleta of variantes: string list * origen: Origen
  | ConteoOpcion of opcion: string * total: int * origen: Origen
  | RirSemana of semana: int * objetivo: string * origen: Origen
  | Ajuste of objetivo: Objetivo * deltas: Delta list * origen: Origen

/// Cada campo del plan y de dónde puede salir su valor: el inventario
/// de trazabilidad que la solicitud usa para fijar, pedir o prohibir.
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

/// Las clases de origen válidas de cada campo; nunca vacío.
val origenesDe: campo: Campo -> ClaseOrigen list

/// Todos los campos del plan, en el orden del formato.
val campos: Campo list

/// La clase de un origen trazado.
val claseDe: origen: Origen -> ClaseOrigen

/// Lo que el perfil del atleta fija: sus variantes.
val delPerfil: atleta: Atleta -> Parametro list

/// Los ajustes que las memorias confirmadas aportan a la solicitud
/// siguiente; propuestas y descartadas no aportan nada.
val deMemoriasConfirmadas: memorias: Memoria list -> Parametro list
