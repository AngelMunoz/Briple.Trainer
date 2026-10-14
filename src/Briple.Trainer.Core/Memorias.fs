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

/// Excepcion del entrenador: tecnica que las reglas de evidencia no
/// sugieren, prescrita a proposito. El entrenador no justifica; la
/// nota de por que no la sugerimos la escribe el sistema en las notas
/// del ejercicio.
type Excepcion = { Objetivo: Objetivo; Tecnica: string }

/// El origen de una memoria: una marca del atleta o una excepcion del
/// entrenador.
type OrigenMemoria =
  | Marca of MarcaFeedback
  | Excepcion of excepcion: Excepcion

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
  Origen: OrigenMemoria
  Estado: EstadoMemoria
}

let ajustesAutomaticos(etiqueta: Etiqueta) : Delta list =
  match etiqueta with
  | Leve -> [ Carga 5 ]
  | Pesado -> [ Carga -5 ]
  | Dolor -> []
  | SinEquipo -> []

let etiquetaTexto(etiqueta: Etiqueta) =
  match etiqueta with
  | Leve -> "muy leve"
  | Pesado -> "muy pesado"
  | Dolor -> "con dolor"
  | SinEquipo -> "sin equipo"

let objetivoTexto(objetivo: Objetivo) =
  match objetivo with
  | Ejercicio nombre -> nombre
  | Dia id -> $"el día {id}"

let consolidar(marca: MarcaFeedback) : Memoria = {
  Resumen =
    $"{objetivoTexto marca.Objetivo} se sintió {etiquetaTexto marca.Etiqueta}"
  Deltas = ajustesAutomaticos marca.Etiqueta
  Origen = Marca marca
  Estado = Propuesta
}

let confirmar (deltas: Delta list) (memoria: Memoria) : Memoria = {
  memoria with
      Deltas = deltas
      Estado = Confirmada
}

let descartar(memoria: Memoria) : Memoria = { memoria with Estado = Descartada }

/// La memoria de una excepcion del entrenador: nace confirmada, con
/// la tecnica prescrita como variante.
let memoriaDeExcepcion(excepcion: Excepcion) : Memoria = {
  Resumen =
    $"el entrenador prescribió '{excepcion.Tecnica}' sobre {objetivoTexto excepcion.Objetivo}"
  Deltas = [ VariantePatron excepcion.Tecnica ]
  Origen = Excepcion excepcion
  Estado = Confirmada
}
