module Briple.Trainer.Core.Agente

// La orquestacion del agente sobre Microsoft.Extensions.AI con
// proveedores BYOK OpenAI-compatible. El modelo responde datos en
// JSON; el plan del dominio se convierte con Plan.Datos, se revisa
// contra los parametros de la solicitud y el formato lo escribe
// Escritor de forma determinista. Las salidas rechazadas se
// descartan; no hay reparo automatico.

open System
open System.Threading.Tasks
open Microsoft.Extensions.AI
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Validador

/// Un proveedor BYOK OpenAI-compatible.
type Proveedor = {
  Nombre: string
  Endpoint: Uri
  Modelo: string
  /// User-Agent propio; algunos proveedores lo piden en lugar del
  /// generico del SDK.
  AgenteUsuario: string
  /// Encabezados extra del proveedor (p. ej. x-opencode-session).
  Encabezados: (string * string) list
}

/// El cliente de chat del proveedor con su clave, user agent y
/// encabezados aplicados. Un endpoint que termina en /responses usa
/// el cliente de la API Responses con esa base.
val crearCliente: clave: string -> proveedor: Proveedor -> IChatClient

/// Una corrida del flujo de generacion: la respuesta cruda del modelo,
/// el plan renderizado al formato y el veredicto.
type Corrida = {
  Respuesta: string
  Salida: string
  Veredicto: Veredicto
}

val generarNuevo:
  cliente: IChatClient -> solicitud: SolicitudNuevoPlan -> Task<Corrida>

val generarAjuste:
  cliente: IChatClient -> solicitud: SolicitudAjuste -> Task<Corrida>

/// El agente consolida las marcas en memorias propuestas. Las lineas
/// que no siguen el protocolo se descartan.
val consolidarMarcas:
  cliente: IChatClient -> marcas: MarcaFeedback list -> Task<Memoria list>
