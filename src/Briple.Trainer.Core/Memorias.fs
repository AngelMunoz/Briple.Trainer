module Briple.Trainer.Core.Memorias

open System

type Etiqueta =
  | Leve
  | Pesado
  | Dolor
  | SinEquipo

type Objetivo =
  | Ejercicio of nombre: string
  | Dia of id: string

type MarcaFeedback = {
  Objetivo: Objetivo
  Etiqueta: Etiqueta
  Fecha: DateOnly
}

type Delta =
  | Rir of cambio: int
  | Repeticiones of cambio: int
  | Series of cambio: int
  | Carga of porcentaje: int
  | VariantePatron of nombre: string

type EstadoMemoria =
  | Propuesta
  | Confirmada
  | Descartada

type Memoria = {
  Resumen: string
  Deltas: Delta list
  Origen: MarcaFeedback
  Estado: EstadoMemoria
}

let ajustesAutomaticos(etiqueta: Etiqueta) : Delta list =
  match etiqueta with
  | Leve -> [ Carga 5 ]
  | Pesado -> [ Carga -5 ]
  | Dolor -> []
  | SinEquipo -> []

let private etiquetaTexto(etiqueta: Etiqueta) =
  match etiqueta with
  | Leve -> "muy leve"
  | Pesado -> "muy pesado"
  | Dolor -> "con dolor"
  | SinEquipo -> "sin equipo"

let private objetivoTexto(objetivo: Objetivo) =
  match objetivo with
  | Ejercicio nombre -> nombre
  | Dia id -> $"el día {id}"

let consolidar(marca: MarcaFeedback) : Memoria = {
  Resumen =
    $"{objetivoTexto marca.Objetivo} se sintió {etiquetaTexto marca.Etiqueta}"
  Deltas = ajustesAutomaticos marca.Etiqueta
  Origen = marca
  Estado = Propuesta
}

let confirmar (deltas: Delta list) (memoria: Memoria) : Memoria = {
  memoria with
      Deltas = deltas
      Estado = Confirmada
}

let descartar(memoria: Memoria) : Memoria = { memoria with Estado = Descartada }
