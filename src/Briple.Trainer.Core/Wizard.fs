module Briple.Trainer.Core.Wizard

open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Solicitudes

type Generacion =
  | PlanNuevo of solicitud: SolicitudNuevoPlan
  | AjusteDePlan of solicitud: SolicitudAjuste

type Paso =
  | ElegirAtleta
  | RevisarMemorias of atleta: Atleta * propuestas: Memoria list
  | ElegirPeriodo of atleta: Atleta
  | DefinirPedido of atleta: Atleta * confirmadas: Memoria list * semanas: int
  | Generar of generacion: Generacion
  | Revisar of candidato: Plan * warnings: ParseWarning list

let inicial() : Paso = ElegirAtleta
