module Briple.Trainer.Core.Agente

// el adaptador IChatClient de la API Responses es experimental
#nowarn "57"

open System
open IcedTasks
open System.ClientModel
open System.ClientModel.Primitives
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.AI
open OpenAI
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Plan.Datos
open Briple.Trainer.Core.Plan.Escritor
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Validador

/// Agrega los encabezados del proveedor a cada request.
type EncabezadosHandler(encabezados: (string * string) list) =
  inherit DelegatingHandler(new SocketsHttpHandler())

  override this.SendAsync
    (request: HttpRequestMessage, cancelacion: CancellationToken)
    : Task<HttpResponseMessage> =
    for nombre, valor in encabezados do
      request.Headers.Add(nombre, valor) |> ignore

    base.SendAsync(request, cancelacion)

/// Un proveedor BYOK OpenAI-compatible.
type Proveedor = {
  Nombre: string
  Endpoint: Uri
  Modelo: string
  AgenteUsuario: string
  Encabezados: (string * string) list
}

/// Una corrida del flujo de generacion: la respuesta cruda del modelo,
/// el plan renderizado al formato y el veredicto.
type Corrida = {
  Respuesta: string
  Salida: string
  Veredicto: Veredicto
}

/// El cliente de chat del proveedor con su clave, user agent y
/// encabezados aplicados. Un endpoint que termina en /responses usa
/// el cliente de la API Responses con esa base.
let crearCliente (clave: string) (proveedor: Proveedor) : IChatClient =
  let uri = proveedor.Endpoint.AbsoluteUri.TrimEnd('/')

  let usaResponses = uri.EndsWith("/responses")

  let baseUri =
    if usaResponses then
      Uri(uri.Substring(0, uri.Length - "/responses".Length))
    else
      proveedor.Endpoint

  let opciones =
    OpenAIClientOptions(
      Endpoint = baseUri,
      Transport =
        new HttpClientPipelineTransport(
          new HttpClient(new EncabezadosHandler(proveedor.Encabezados))
        )
    )

  if proveedor.AgenteUsuario <> "" then
    opciones.UserAgentApplicationId <- proveedor.AgenteUsuario

  let cliente = OpenAIClient(ApiKeyCredential(clave), opciones)

  if usaResponses then
    cliente.GetResponsesClient().AsIChatClient(proveedor.Modelo)
  else
    cliente.GetChatClient(proveedor.Modelo).AsIChatClient()

let pedir
  (cliente: IChatClient)
  (sistema: string)
  (usuario: string)
  : Task<string> =
  let mensajes = [|
    ChatMessage(ChatRole.System, sistema)
    ChatMessage(ChatRole.User, usuario)
  |]

  // la transmision viaja fria con su token de cancelacion y devuelve el
  // texto acumulado cuando cierra
  let transmitir = cancellableTask {
    let acumulado = Text.StringBuilder()

    for actualizacion in cliente.GetStreamingResponseAsync mensajes do
      acumulado.Append actualizacion.Text |> ignore

    return acumulado.ToString()
  }

  transmitir CancellationToken.None

// las lineas de mesociclo son parametro de la solicitud: se agregan
// deterministicamente, el modelo no las escribe
let conMesociclo (rirPorSemana: (int * string) list) (plan: Plan) =
  let lineas =
    rirPorSemana
    |> List.map(fun (semana, valor) ->
      $"mesociclo | Semana {semana}: objetivo | RIR {valor}")

  {
    plan with
        Extras = plan.Extras @ lineas
  }

let correr
  (rirPorSemana: (int * string) list)
  (revisar: Plan -> Fallo list)
  (respuesta: string)
  : Corrida =
  match deJson respuesta with
  | Error fallosDatos -> {
      Respuesta = respuesta
      Salida = respuesta
      Veredicto =
        Rechazado(
          fallosDatos
          |> List.map(fun detalle -> { Regla = "datos"; Detalle = detalle })
        )
    }
  | Ok planBruto ->
    let plan = conMesociclo rirPorSemana planBruto
    let fallos = revisar plan

    let veredicto =
      match fallos with
      | [] -> Valido plan
      | _ -> Rechazado fallos

    {
      Respuesta = respuesta
      Salida = escribir plan
      Veredicto = veredicto
    }

/// Genera un plan nuevo: pide los datos, los convierte, los revisa y
/// escribe el formato.
let generarNuevo
  (cliente: IChatClient)
  (solicitud: SolicitudNuevoPlan)
  : Task<Corrida> = task {
  let! respuesta = pedir cliente sistema (promptNuevo solicitud)

  return
    correr
      solicitud.Pedido.RirPorSemana
      (revisarParametros
        solicitud.Atleta
        solicitud.Semanas
        solicitud.Pedido
        solicitud.Conteos)
      respuesta
}

/// Genera un ajuste: lo mismo con el plan anterior como referencia.
let generarAjuste
  (cliente: IChatClient)
  (solicitud: SolicitudAjuste)
  : Task<Corrida> = task {
  let! respuesta = pedir cliente sistema (promptAjuste solicitud)

  return
    correr
      solicitud.Pedido.RirPorSemana
      (revisarParametros
        solicitud.Atleta
        solicitud.Semanas
        solicitud.Pedido
        solicitud.Conteos)
      respuesta
}

// --- Consolidacion de marcas -------------------------------------------------

// el modelo decora los numeros con signo y porcentaje (carga +5%): el
// protocolo ya declara que carga es porcentual, el adorno se descarta
let numeroLimpio(texto: string) =
  texto.Trim().TrimStart('+').TrimEnd('%')

let deltaDe(texto: string) : Delta option =
  let partes = texto.Trim().Split(' ', 2)

  match partes.[0].ToLowerInvariant(), partes |> Array.tryItem 1 with
  | "variante", Some nombre -> Some(VariantePatron(nombre.Trim()))
  | clase, Some numeroText ->
    match numeroText |> numeroLimpio |> Int32.TryParse with
    | true, numero ->
      match clase with
      | "rir" -> Some(Delta.Rir numero)
      | "reps" -> Some(Repeticiones numero)
      | "series" -> Some(Series numero)
      | "carga" -> Some(Carga numero)
      | _ -> None
    | _ -> None
  | _ -> None

let etiquetaDe(texto: string) =
  match texto.Trim().ToLowerInvariant() with
  | "leve" -> Some Leve
  | "pesado" -> Some Pesado
  | "dolor" -> Some Dolor
  | "sin-equipo"
  | "sin equipo" -> Some SinEquipo
  | _ -> None

let objetivoEs (texto: string) (objetivo: Objetivo) =
  let normalizado = texto.Trim().ToLowerInvariant()

  match objetivo with
  | Ejercicio nombre ->
    normalizado = nombre.ToLowerInvariant()
    || normalizado.EndsWith(nombre.ToLowerInvariant())
  | Dia id -> normalizado.Contains(id.ToLowerInvariant())

let memoriaDe (marcas: MarcaFeedback list) (linea: string) =
  match linea.Split('|') |> Array.map(fun parte -> parte.Trim()) with
  | [| objetivoText; etiquetaText; resumen; deltasText |] ->
    marcas
    |> List.tryFind(fun marca ->
      objetivoEs objetivoText marca.Objetivo
      && etiquetaDe etiquetaText = Some marca.Etiqueta)
    |> Option.bind(fun marca ->
      let deltas =
        deltasText.Split(';') |> Array.choose deltaDe |> Array.toList

      if List.isEmpty deltas then
        None
      else
        Some {
          Resumen = resumen
          Deltas = deltas
          Origen = Marca marca
          Estado = Propuesta
        })
  | _ -> None

/// El agente consolida las marcas en memorias propuestas. Las lineas
/// que no siguen el protocolo se descartan.
let consolidarMarcas
  (cliente: IChatClient)
  (marcas: MarcaFeedback list)
  : Task<Memoria list> = task {
  let! respuesta = pedir cliente sistemaConsolidar (promptConsolidar marcas)

  return
    respuesta.Replace("\r\n", "\n").Split('\n')
    |> Array.choose(fun linea -> memoriaDe marcas linea)
    |> Array.toList
}
