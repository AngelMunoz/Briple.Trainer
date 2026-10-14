module Briple.Trainer.Core.Instrucciones

// El mapeo parámetros → instrucciones del agente: qué se fija, qué se
// pide y qué se prohíbe. Lo fijado debe aparecer en la salida tal
// cual; lo pedido lo produce el agente dentro de su origen declarado
// en el inventario; lo prohibido no se toca.

open Briple.Trainer.Core.Parametros
open Briple.Trainer.Core.Solicitudes

/// Lo que el agente no puede hacer.
type Prohibicion =
  | CambiarConteos
  | OpcionesFueraDeVariantes
  | SalirseDelFormato

/// El mapeo completo de una solicitud.
type Instrucciones = {
  Fijar: Parametro list
  Pedir: Campo list
  Prohibir: Prohibicion list
}

/// Instrucciones del flujo de plan nuevo.
val delNuevo: solicitud: SolicitudNuevoPlan -> Instrucciones

/// Instrucciones del flujo de ajuste: agrega la prohibición de cambiar
/// los conteos.
val delAjuste: solicitud: SolicitudAjuste -> Instrucciones
