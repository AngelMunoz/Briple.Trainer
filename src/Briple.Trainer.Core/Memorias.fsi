module Briple.Trainer.Core.Memorias

// La capa de feedback y memorias: las marcas rápidas que se registran
// sobre el plan activo y las memorias que de ellas se consolidan. Una
// memoria nace propuesta; llega a la solicitud siguiente solo cuando
// el entrenador la confirma con los ajustes que aceptó o eligió.

open System

/// Marcas rápidas disponibles sobre un ejercicio o un día.
type Etiqueta =
  | Leve
  | Pesado
  | Dolor
  | SinEquipo

/// El blanco de una marca dentro del plan activo.
type Objetivo =
  | Ejercicio of nombre: string
  | Dia of id: string

/// Una marca rápida registrada sobre el plan activo.
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

/// Ajuste paramétrico predefinido: los cinco con los que el entrenador
/// responde. Los numéricos son cambios relativos; la carga va en
/// porcentaje.
type Delta =
  | Rir of cambio: int
  | Repeticiones of cambio: int
  | Series of cambio: int
  | Carga of porcentaje: int
  | VariantePatron of nombre: string

/// Ciclo de vida de una memoria.
type EstadoMemoria =
  | Propuesta
  | Confirmada
  | Descartada

/// Hecho relevante que persiste entre planes. `Deltas` vacío significa
/// que la etiqueta no tiene ajuste automático y el entrenador elige
/// uno de los predefinidos a mano.
type Memoria = {
  Resumen: string
  Deltas: Delta list
  Origen: OrigenMemoria
  Estado: EstadoMemoria
}

/// El ajuste automático de cada etiqueta: leve sube la carga, pesado
/// la baja; dolor y sin equipo no tienen ajuste automático.
val ajustesAutomaticos: etiqueta: Etiqueta -> Delta list

/// Consolida una marca en la memoria propuesta con su resumen y su
/// ajuste automático.
val consolidar: marca: MarcaFeedback -> Memoria

/// Confirma la memoria con los ajustes que el entrenador aceptó o
/// eligió de los predefinidos.
val confirmar: deltas: Delta list -> memoria: Memoria -> Memoria

/// Descarta la memoria: no llega a la solicitud siguiente.
val descartar: memoria: Memoria -> Memoria

/// La memoria de una excepcion del entrenador: nace confirmada, con
/// la tecnica prescrita como variante.
val memoriaDeExcepcion: excepcion: Excepcion -> Memoria
