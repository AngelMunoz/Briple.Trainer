module Briple.Trainer.Core.Prompts

// El render de la solicitud al prompt del agente: el formato estricto
// del archivo, lo fijado (parametros trazados), lo que el agente
// decide (campos de su origen) y lo prohibido. El ajuste agrega el
// plan anterior como referencia.

open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Solicitudes

/// Mensaje de sistema de los flujos de generacion: rol, esquema de
/// respuesta JSON y reglas fijas.
val sistema: string

/// Mensaje de sistema de la consolidacion de marcas.
val sistemaConsolidar: string

/// Mensaje de usuario del flujo de plan nuevo.
val promptNuevo: solicitud: SolicitudNuevoPlan -> string

/// Mensaje de usuario del flujo de ajuste: el comun mas el plan
/// anterior en el mismo esquema.
val promptAjuste: solicitud: SolicitudAjuste -> string

/// Mensaje de usuario de la consolidacion: las marcas registradas.
val promptConsolidar: marcas: MarcaFeedback list -> string
