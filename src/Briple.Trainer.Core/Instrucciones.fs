module Briple.Trainer.Core.Instrucciones

open Briple.Trainer.Core.Parametros
open Briple.Trainer.Core.Solicitudes

type Prohibicion =
  | CambiarConteos
  | OpcionesFueraDeVariantes
  | SalirseDelFormato

type Instrucciones = {
  Fijar: Parametro list
  Pedir: Campo list
  Prohibir: Prohibicion list
}

let pedidosAlAgente: Campo list =
  campos
  |> List.filter(fun campo -> origenesDe campo |> List.contains DelAgente)

let delNuevo(solicitud: SolicitudNuevoPlan) : Instrucciones = {
  Fijar = parametrosDeNuevo solicitud
  Pedir = pedidosAlAgente
  Prohibir = [ OpcionesFueraDeVariantes; SalirseDelFormato ]
}

let delAjuste(solicitud: SolicitudAjuste) : Instrucciones = {
  Fijar = parametrosDeAjuste solicitud
  Pedir = pedidosAlAgente
  Prohibir = [ CambiarConteos; OpcionesFueraDeVariantes; SalirseDelFormato ]
}
