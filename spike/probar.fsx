// Prueba manual del agente con tu propia clave, sobre las DLL ya
// construidas. Ejecutar desde la raiz del repo.
//
//   1) dotnet build Briple.Trainer.slnx
//   2) $env:BRIPLE_KEY = "tu-clave"
//   3) dotnet fsi spike/probar.fsx -- <modo> [veces] [proveedor]
//
// Modos:
//   nuevo N [prov]       genera N planes nuevos y valida cada salida
//   ajuste N [prov]      genera N ajustes (mes 2 sobre mes 1) y valida
//   ab [prov]            ajuste con y sin memoria confirmada, compara
//   consolidar [prov]    marcas de prueba -> memorias propuestas
//
// Proveedores: glm (por defecto, coding plan de z.ai), deepseek, opencode.
// BRIPLE_MODELO sobreescribe el modelo de cualquier proveedor.
// Cada salida cruda queda en spike/salida-<n>.txt para revisarla.

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "Microsoft.Extensions.AI.Abstractions.dll"
#r "Microsoft.Extensions.AI.dll"
#r "Microsoft.Extensions.AI.OpenAI.dll"
#r "OpenAI.dll"
#r "System.ClientModel.dll"

open System
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

let nombreProveedor =
  match argumentos with
  | m :: n :: p :: _ when
    m <> "ab" && m <> "consolidar" && m <> "excepcion" && m <> "prompt"
    ->
    p
  | m :: p :: _ when
    m = "ab" || m = "consolidar" || m = "excepcion" || m = "prompt"
    ->
    if Int32.TryParse p |> fst then "glm" else p
  | _ -> "glm"

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
  | "deepseek" -> {
      Nombre = "deepseek"
      Endpoint = Uri "https://api.deepseek.com"
      Modelo = modeloPorOmision |> Option.defaultValue "deepseek-flash"
      AgenteUsuario = ""
      Encabezados = []
    }
  | "opencode" -> {
      Nombre = "opencode"
      Endpoint = Uri "https://opencode.ai/inference/go/v1/responses"
      Modelo =
        modeloPorOmision |> Option.defaultValue "muse-spark-1.3-contributor"
      AgenteUsuario = "briple-trainer-spike/1.0"
      Encabezados = [ ("x-opencode-session", sesion) ]
    }
  | _ -> {
      Nombre = "glm"
      Endpoint = Uri "https://api.z.ai/api/coding/paas/v4"
      Modelo = modeloPorOmision |> Option.defaultValue "glm-5.3-flash"
      AgenteUsuario = ""
      Encabezados = []
    }

// --- fixtures y solicitud --------------------------------------------------

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

let pedido = {
  Ajustes = []
  Enfasis = []
  RirPorSemana = [ (1, "3"); (2, "2-3"); (4, "1") ]
  Variantes = [ "3dias"; "5dias" ]
}

let conteosMes2 = conteosDelPlan hombre mes2.Plan

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

// lazy: el modo ayuda no necesita clave
let cliente = lazy (crearCliente (clave()) proveedorElegido)

let guardar nombre (texto: string) =
  IO.Path.Combine(__SOURCE_DIRECTORY__, nombre)
  |> fun ruta -> IO.File.WriteAllText(ruta, texto)

let reportar i (corrida: Corrida) =
  match corrida.Veredicto with
  | Valido _ -> printfn "corrida %d: VALIDO" i
  | Rechazado fallos ->
    printfn "corrida %d: RECHAZADO" i

    fallos
    |> List.iter(fun fallo -> printfn "  [%s] %s" fallo.Regla fallo.Detalle)

  guardar $"salida-{i}.txt" corrida.Salida
  guardar $"salida-{i}.json" corrida.Respuesta

let moduloGeneracion esAjuste veces =
  guardar "prompt-sistema.txt" sistema

  let correr() =
    if esAjuste then
      let solicitud =
        solicitudAjuste hombre [ memoria ] 4 pedido mes1.Plan conteosMes2

      guardar "prompt-ajuste.txt" (promptAjuste solicitud)

      Async.AwaitTask(generarAjuste cliente.Value solicitud)
      |> Async.RunSynchronously
    else
      let solicitud = solicitudNuevo hombre [ memoria ] 4 pedido

      guardar "prompt-nuevo.txt" (promptNuevo solicitud)

      Async.AwaitTask(generarNuevo cliente.Value solicitud)
      |> Async.RunSynchronously

  let validas = ResizeArray()

  for i in 1..veces do
    let corrida = correr()

    reportar i corrida

    match corrida.Veredicto with
    | Valido _ -> validas.Add i
    | Rechazado _ -> ()

  if veces > 1 then
    printfn ""
    printfn "%d de %d corridas validas a la primera" validas.Count veces

let moduloAB() =
  printfn "corriendo el mismo ajuste con y sin la memoria confirmada..."
  printfn ""

  let conMemoria =
    Async.AwaitTask(
      generarAjuste
        cliente.Value
        (solicitudAjuste hombre [ memoria ] 4 pedido mes1.Plan conteosMes2)
    )
    |> Async.RunSynchronously

  let sinMemoria =
    Async.AwaitTask(
      generarAjuste
        cliente.Value
        (solicitudAjuste hombre [] 4 pedido mes1.Plan conteosMes2)
    )
    |> Async.RunSynchronously

  reportar 1 conMemoria
  reportar 2 sinMemoria

  printfn ""

  printfn
    "la salida cambia con la memoria: %b"
    (conMemoria.Salida <> sinMemoria.Salida)

// genera un plan nuevo con la excepcion del entrenador activa
let moduloExcepcion() =
  let solicitud = solicitudNuevo hombre [ memoriaExcepcion ] 4 pedido

  guardar "prompt-sistema.txt" sistema
  guardar "prompt-nuevo.txt" (promptNuevo solicitud)

  let corrida =
    Async.AwaitTask(generarNuevo cliente.Value solicitud)
    |> Async.RunSynchronously

  reportar 1 corrida

let moduloConsolidar() =
  let marcas = marcasPrueba

  let memorias =
    Async.AwaitTask(consolidarMarcas cliente.Value marcas)
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

// muestra el prompt sin llamar a la API
let moduloPrompt objetivo =
  let nombre, usuario =
    match objetivo with
    | "ajuste" ->
      "prompt-ajuste.txt",
      promptAjuste(
        solicitudAjuste hombre [ memoria ] 4 pedido mes1.Plan conteosMes2
      )
    | "consolidar" -> "prompt-consolidar.txt", promptConsolidar marcasPrueba
    | "excepcion" ->
      "prompt-nuevo.txt",
      promptNuevo(solicitudNuevo hombre [ memoriaExcepcion ] 4 pedido)
    | _ ->
      "prompt-nuevo.txt",
      promptNuevo(solicitudNuevo hombre [ memoria ] 4 pedido)

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
    "modos: nuevo N [prov] | ajuste N [prov] | ab [prov] | consolidar [prov] | excepcion [prov] | prompt [nuevo|ajuste|consolidar|excepcion]"

  printfn "proveedores: glm (default, coding plan z.ai), deepseek, opencode"

  printfn "define BRIPLE_KEY; BRIPLE_MODELO sobreescribe el modelo"

printfn
  "proveedor: %s (%s, modelo %s)"
  proveedorElegido.Nombre
  (proveedorElegido.Endpoint.ToString())
  proveedorElegido.Modelo

printfn ""

match modo with
| "nuevo" -> moduloGeneracion false veces
| "ajuste" -> moduloGeneracion true veces
| "ab" -> moduloAB()
| "consolidar" -> moduloConsolidar()
| "excepcion" -> moduloExcepcion()
| "prompt" -> moduloPrompt objetivoPrompt
| _ -> ayuda()
