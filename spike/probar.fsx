// Prueba manual del agente con tu propia clave, sobre las DLL ya
// construidas. Ejecutar desde la raiz del repo.
//
//   1) dotnet build Briple.Trainer.slnx
//   2) $env:BRIPLE_KEY = "tu-clave"
//   3) dotnet fsi spike/probar.fsx -- <modo> [veces] [proveedor] [perfil]
//
// Modos:
//   nuevo N [prov] [perfil]       genera N planes nuevos y valida cada salida
//   ajuste N [prov] [perfil]      genera N ajustes (mes 2 sobre mes 1) y valida
//   ab [prov] [perfil]            ajuste con y sin memoria confirmada, compara
//   consolidar [prov]             marcas de prueba -> memorias propuestas
//   excepcion [prov]              plan nuevo con la excepcion del entrenador
//   prompt [nuevo|ajuste|consolidar|excepcion]   muestra el prompt sin llamar
//
// Proveedores: deepseek (por defecto), opencode.
// Perfiles: hombre (3dias+5dias, por defecto), mujer (5dias).
// BRIPLE_MODELO sobreescribe el modelo de cualquier proveedor.
// Cada corrida usa una sesion nueva del proveedor y guarda su salida en
// spike/salida-<celda>-<n>.txt para revisarla.

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "Microsoft.Extensions.AI.Abstractions.dll"
#r "Microsoft.Extensions.AI.dll"
#r "Microsoft.Extensions.AI.OpenAI.dll"
#r "OpenAI.dll"
#r "System.ClientModel.dll"

open System
open Microsoft.Extensions.AI
open Briple.Trainer.Core.Agente
open Briple.Trainer.Core.Atletas
open Briple.Trainer.Core.Memorias
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Solicitudes
open Briple.Trainer.Core.Validador

// --- argumentos y proveedor --------------------------------------------------

let argumentos = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

let modo =
  match argumentos with
  | m :: _ -> m
  | [] -> "ayuda"

let veces =
  match argumentos with
  | _ :: n :: _ ->
    match Int32.TryParse n with
    | true, v -> v
    | _ -> 1
  | _ -> 1

// modos sin conteo de corridas: [prov] va inmediatamente despues del modo
let modosSinVeces = [
  "ab"
  "consolidar"
  "consolidar-crudo"
  "excepcion"
  "prompt"
]

let nombreProveedor =
  match argumentos with
  | m :: _ :: p :: _ when not(List.contains m modosSinVeces) -> p
  | m :: p :: _ when List.contains m modosSinVeces ->
    if Int32.TryParse p |> fst then "deepseek" else p
  | _ -> "deepseek"

let nombrePerfil =
  let modosConPerfil = [ "ab"; "consolidar"; "consolidar-crudo"; "excepcion" ]

  match argumentos with
  // modos con conteo: nuevo/ajuste N [prov] [perfil]
  | m :: _ :: _ :: perfil :: _ when m = "nuevo" || m = "ajuste" -> perfil
  // modos sin conteo: [prov] [perfil]
  | m :: _ :: perfil :: _ when List.contains m modosConPerfil -> perfil
  | _ -> "hombre"

let proveedor nombre =
  let modeloEnv = Environment.GetEnvironmentVariable "BRIPLE_MODELO"

  let modeloPorOmision =
    if String.IsNullOrWhiteSpace modeloEnv then
      None
    else
      Some modeloEnv

  // OpenCode pide sesion por encabezado para routing y caching
  let sesion = Guid.NewGuid().ToString("N")

  match nombre with
  | "opencode" -> {
      Nombre = "opencode"
      Endpoint = Uri "https://opencode.ai/inference/go/v1/responses"
      Modelo =
        modeloPorOmision |> Option.defaultValue "muse-spark-1.3-contributor"
      AgenteUsuario = "briple-trainer-spike/1.0"
      Encabezados = [ ("x-opencode-session", sesion) ]
    }
  | _ -> {
      // responses: esfuerzo estandar en reasoning.effort (default high)
      Nombre = "deepseek"
      Endpoint = Uri "https://api.deepseek.com/responses"
      Modelo = modeloPorOmision |> Option.defaultValue "deepseek-flash"
      AgenteUsuario = ""
      Encabezados = []
    }

// --- fixtures y perfiles -----------------------------------------------------

let raiz = IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, ".."))

let fixture nombre =
  IO.Path.Combine(
    raiz,
    "tests",
    "Briple.Trainer.Core.Tests",
    "fixtures",
    nombre
  )
  |> IO.File.ReadAllText

let parsear nombre =
  match fixture nombre |> parsePlan with
  | Ok parsed -> parsed
  | Error error -> failwith $"fixture {nombre} no parsea: {error.Message}"

let mes1 = parsear "plan_entrenamiento_4sem.txt"
let mes2 = parsear "plan_mes2_4sem.txt"

let hombre = atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Hombre)

let mujer = atletasDelPlan mes1.Plan |> List.find(fun a -> a.Genero = Mujer)

let pedidoHombre = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = [ (1, "3"); (2, "2-3"); (3, "2"); (4, "1") ]
  Variantes = [ "3dias"; "5dias" ]
}

type Perfil = {
  Atleta: Atleta
  Pedido: Pedido
  ConteosMes1: (string * int) list
  ConteosMes2: (string * int) list
}

let perfilDe nombre =
  let atleta, pedido =
    match nombre with
    | "mujer" ->
      mujer,
      {
        pedidoHombre with
            Variantes = [ "5dias" ]
      }
    | _ -> hombre, pedidoHombre

  {
    Atleta = atleta
    Pedido = pedido
    ConteosMes1 = conteosDelPlan atleta mes1.Plan
    ConteosMes2 = conteosDelPlan atleta mes2.Plan
  }

let solicitudNuevoDe (perfil: Perfil) memorias =
  solicitudNuevo perfil.Atleta memorias 4 perfil.Pedido perfil.ConteosMes1

let solicitudAjusteDe (perfil: Perfil) memorias =
  solicitudAjuste
    perfil.Atleta
    memorias
    4
    perfil.Pedido
    mes1.Plan
    perfil.ConteosMes2

let marcaPress = {
  Objetivo = Ejercicio "Press de banca"
  Etiqueta = Leve
  Fecha = DateOnly(2026, 10, 6)
}

let memoria = marcaPress |> consolidar |> confirmar [ Carga 5 ]

// excepcion del entrenador: tecnica de alto riesgo prescrita a proposito
let memoriaExcepcion =
  memoriaDeExcepcion {
    Objetivo = Dia "Miércoles"
    Tecnica = "Sentadilla con salto"
  }

let marcasPrueba = [
  marcaPress
  {
    Objetivo = Dia "Lunes"
    Etiqueta = Pesado
    Fecha = DateOnly(2026, 10, 6)
  }
  {
    Objetivo = Ejercicio "Sentadilla con barra"
    Etiqueta = Dolor
    Fecha = DateOnly(2026, 10, 7)
  }
  {
    Objetivo = Ejercicio "Dominadas"
    Etiqueta = SinEquipo
    Fecha = DateOnly(2026, 10, 8)
  }
  {
    Objetivo = Dia "Viernes"
    Etiqueta = Leve
    Fecha = DateOnly(2026, 10, 9)
  }
]

let objetivoPrompt =
  match argumentos with
  | "prompt" :: objetivo :: _ -> objetivo
  | _ -> "nuevo"

// --- corridas -----------------------------------------------------------------

let clave() =
  let valor = Environment.GetEnvironmentVariable "BRIPLE_KEY"

  if String.IsNullOrWhiteSpace valor then
    failwith "define BRIPLE_KEY en el entorno"

  valor

let proveedorElegido = proveedor nombreProveedor

// sesion nueva por corrida: el cache del proveedor no devuelve siempre
// la misma respuesta entre corridas repetidas
let conSesionNueva(proveedor: Proveedor) =
  let renovar(nombre, valor) =
    if nombre = "x-opencode-session" then
      (nombre, Guid.NewGuid().ToString("N"))
    else
      (nombre, valor)

  {
    proveedor with
        Encabezados = proveedor.Encabezados |> List.map renovar
  }

let clienteCorrida() =
  crearCliente (clave()) (conSesionNueva proveedorElegido)

let guardar nombre (texto: string) =
  IO.Path.Combine(__SOURCE_DIRECTORY__, nombre)
  |> fun ruta -> IO.File.WriteAllText(ruta, texto)

let reportar etiqueta i (corrida: Corrida) =
  match corrida.Veredicto with
  | Valido _ -> printfn "corrida %d: VALIDO" i
  | Rechazado fallos ->
    printfn "corrida %d: RECHAZADO" i

    fallos
    |> List.iter(fun fallo -> printfn "  [%s] %s" fallo.Regla fallo.Detalle)

  guardar $"salida-{etiqueta}-{i}.txt" corrida.Salida
  guardar $"salida-{etiqueta}-{i}.json" corrida.Respuesta

let moduloGeneracion esAjuste veces (perfil: Perfil) =
  let etiqueta = if esAjuste then "ajuste" else "nuevo"

  guardar "prompt-sistema.txt" sistema

  let correr() =
    if esAjuste then
      let solicitud = solicitudAjusteDe perfil [ memoria ]

      guardar $"prompt-{etiqueta}-{nombrePerfil}.txt" (promptAjuste solicitud)

      Async.AwaitTask(generarAjuste (clienteCorrida()) solicitud)
      |> Async.RunSynchronously
    else
      let solicitud = solicitudNuevoDe perfil [ memoria ]

      guardar $"prompt-{etiqueta}-{nombrePerfil}.txt" (promptNuevo solicitud)

      Async.AwaitTask(generarNuevo (clienteCorrida()) solicitud)
      |> Async.RunSynchronously

  let validas = ResizeArray()
  let errores = ResizeArray()

  for i in 1..veces do
    let resultado =
      try
        Ok(correr())
      with ex ->
        Error ex.Message

    match resultado with
    | Ok corrida ->
      reportar $"{etiqueta}-{nombrePerfil}" i corrida

      match corrida.Veredicto with
      | Valido _ -> validas.Add i
      | Rechazado _ -> ()
    | Error mensaje ->
      errores.Add i
      printfn "corrida %d: ERROR de llamada (%s)" i mensaje

  if veces > 1 then
    printfn ""

    printfn
      "%d de %d corridas validas a la primera, %d errores de llamada"
      validas.Count
      veces
      errores.Count

let moduloAB(perfil: Perfil) =
  printfn "corriendo el mismo ajuste con y sin la memoria confirmada..."
  printfn ""

  let correr solicitud =
    try
      Some(
        Async.AwaitTask(generarAjuste (clienteCorrida()) solicitud)
        |> Async.RunSynchronously
      )
    with ex ->
      printfn "ERROR de llamada (%s)" ex.Message
      None

  let conMemoria = correr(solicitudAjusteDe perfil [ memoria ])
  let sinMemoria = correr(solicitudAjusteDe perfil [])

  conMemoria |> Option.iter(reportar $"ab-con-{nombrePerfil}" 1)
  sinMemoria |> Option.iter(reportar $"ab-sin-{nombrePerfil}" 2)

  printfn ""

  match conMemoria, sinMemoria with
  | Some con, Some sin ->
    printfn "la salida cambia con la memoria: %b" (con.Salida <> sin.Salida)
  | _ -> printfn "la comparacion no cierra: hubo error de llamada"

// genera un plan nuevo con la excepcion del entrenador activa
let moduloExcepcion() =
  let perfil = perfilDe "hombre"

  let solicitud = solicitudNuevoDe perfil [ memoriaExcepcion ]

  guardar "prompt-sistema.txt" sistema
  guardar "prompt-excepcion.txt" (promptNuevo solicitud)

  let corrida =
    Async.AwaitTask(generarNuevo (clienteCorrida()) solicitud)
    |> Async.RunSynchronously

  reportar "excepcion" 1 corrida

let moduloConsolidar() =
  let marcas = marcasPrueba

  let memorias =
    Async.AwaitTask(consolidarMarcas (clienteCorrida()) marcas)
    |> Async.RunSynchronously

  printfn
    "%d de %d marcas consolidadas en memorias bien formadas"
    memorias.Length
    marcas.Length

  printfn ""

  memorias
  |> List.iter(fun memoria ->
    printfn "- %s" memoria.Resumen
    printfn "  deltas: %A" memoria.Deltas)

// llama a la consolidacion y guarda la respuesta cruda, para inspeccionar
// que lineas descarta el protocolo; replica el pedir del Agente
let moduloConsolidarCrudo() =
  let pedirCrudo (cliente: IChatClient) (sistema: string) (usuario: string) =
    task {
      let mensajes = [|
        ChatMessage(ChatRole.System, sistema)
        ChatMessage(ChatRole.User, usuario)
      |]

      let! respuesta = cliente.GetResponseAsync(mensajes)

      return respuesta.Text
    }

  let respuesta =
    pedirCrudo
      (clienteCorrida())
      sistemaConsolidar
      (promptConsolidar marcasPrueba)
    |> Async.AwaitTask
    |> Async.RunSynchronously

  guardar "salida-consolidar-crudo.txt" respuesta
  printfn "%s" respuesta

// muestra el prompt sin llamar a la API
let moduloPrompt objetivo =
  let perfil = perfilDe "hombre"

  let nombre, usuario =
    match objetivo with
    | "ajuste" ->
      "prompt-ajuste.txt", promptAjuste(solicitudAjusteDe perfil [ memoria ])
    | "consolidar" -> "prompt-consolidar.txt", promptConsolidar marcasPrueba
    | "excepcion" ->
      "prompt-excepcion.txt",
      promptNuevo(solicitudNuevoDe perfil [ memoriaExcepcion ])
    | _ -> "prompt-nuevo.txt", promptNuevo(solicitudNuevoDe perfil [ memoria ])

  let sistemaTexto =
    if objetivo = "consolidar" then
      sistemaConsolidar
    else
      sistema

  guardar "prompt-sistema.txt" sistemaTexto
  guardar nombre usuario

  printfn "=== SYSTEM ==="
  printfn "%s" sistemaTexto
  printfn ""
  printfn "=== USER (%s) ===" nombre
  printfn "%s" usuario

let ayuda() =
  printfn
    "modos: nuevo N [prov] [perfil] | ajuste N [prov] [perfil] | ab [prov] [perfil] | consolidar [prov] | consolidar-crudo [prov] | excepcion [prov] | prompt [nuevo|ajuste|consolidar|excepcion]"

  printfn "proveedores: deepseek (default), opencode"

  printfn "perfiles: hombre (default), mujer"

  printfn "define BRIPLE_KEY; BRIPLE_MODELO sobreescribe el modelo"

printfn
  "proveedor: %s (%s, modelo %s)"
  proveedorElegido.Nombre
  (proveedorElegido.Endpoint.ToString())
  proveedorElegido.Modelo

printfn "perfil: %s" nombrePerfil

printfn ""

let perfilElegido = perfilDe nombrePerfil

match modo with
| "nuevo" -> moduloGeneracion false veces perfilElegido
| "ajuste" -> moduloGeneracion true veces perfilElegido
| "ab" -> moduloAB perfilElegido
| "consolidar" -> moduloConsolidar()
| "consolidar-crudo" -> moduloConsolidarCrudo()
| "excepcion" -> moduloExcepcion()
| "prompt" -> moduloPrompt objetivoPrompt
| _ -> ayuda()
