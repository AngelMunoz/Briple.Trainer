module Wizard

// Los seis pasos del flujo de generación con confirmación. Cada caso
// carga su entrada; la salida de un paso es la entrada del siguiente.

open Plan.Types
open Atletas
open Memorias
open Solicitudes

/// Qué genera el paso 5: un plan nuevo o un ajuste.
type Generacion =
  | PlanNuevo of solicitud: SolicitudNuevoPlan
  | AjusteDePlan of solicitud: SolicitudAjuste

/// Un paso del wizard.
type Paso =
  /// 1. Entrada: los atletas registrados. Salida: el atleta elegido.
  | ElegirAtleta
  /// 2. Entrada: el atleta y sus memorias propuestas. Salida: las
  /// memorias confirmadas, cada una con los deltas aceptados.
  | RevisarMemorias of atleta: Atleta * propuestas: Memoria list
  /// 3. Entrada: el atleta. Salida: el período en semanas (1 a 4).
  | ElegirPeriodo of atleta: Atleta
  /// 4. Entrada: atleta, memorias confirmadas y semanas. Salida: el
  /// pedido (ajustes, énfasis, RIR, variantes).
  | DefinirPedido of atleta: Atleta * confirmadas: Memoria list * semanas: int
  /// 5. Entrada: la solicitud armada. Salida: el plan candidato y el
  /// resultado del validador.
  | Generar of generacion: Generacion
  /// 6. Entrada: el candidato con sus warnings. Salida: confirmado al
  /// historial, regenerado o descartado.
  | Revisar of candidato: Plan * warnings: ParseWarning list

/// El primer paso del flujo.
val inicial: unit -> Paso
