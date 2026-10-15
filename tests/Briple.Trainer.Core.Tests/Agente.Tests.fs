module Briple.Trainer.Core.Tests.Agente.Tests

// El protocolo de consolidacion contra un cliente falso: la respuesta
// del modelo pasa por consolidarMarcas tal cual y las memorias bien
// formadas sobreviven. El modelo decora los numeros (carga +5%) y el
// protocolo ya declara porcentual la carga: el adorno se tolera.

open System
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.AI
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Agent

/// Responde siempre el mismo texto, sin llamar a la red.
type ClienteFalso(respuesta: string) =
  interface IChatClient with
    member _.GetResponseAsync
      (
        _mensajes: Collections.Generic.IEnumerable<ChatMessage>,
        _opciones: ChatOptions,
        _cancelacion: Threading.CancellationToken
      ) : Task<ChatResponse> =
      Task.FromResult(ChatResponse(ChatMessage(ChatRole.Assistant, respuesta)))

    member _.GetStreamingResponseAsync
      (
        _mensajes: Collections.Generic.IEnumerable<ChatMessage>,
        _opciones: ChatOptions,
        _cancelacion: Threading.CancellationToken
      ) : Collections.Generic.IAsyncEnumerable<ChatResponseUpdate> =
      // una sola actualizacion con todo el texto: el consumidor no nota
      // la diferencia contra una transmision por trozos
      {
        new Collections.Generic.IAsyncEnumerable<ChatResponseUpdate> with
          member _.GetAsyncEnumerator
            (_ct: Threading.CancellationToken)
            : Collections.Generic.IAsyncEnumerator<ChatResponseUpdate> =
            let mutable servido = false

            let mutable actual = Unchecked.defaultof<ChatResponseUpdate>

            {
              new Collections.Generic.IAsyncEnumerator<ChatResponseUpdate> with
                member _.Current = actual

                member _.MoveNextAsync() : Threading.Tasks.ValueTask<bool> =
                  if servido then
                    Threading.Tasks.ValueTask<bool>(false)
                  else
                    servido <- true
                    actual <- ChatResponseUpdate(ChatRole.Assistant, respuesta)

                    Threading.Tasks.ValueTask<bool>(true)

                member _.DisposeAsync() : Threading.Tasks.ValueTask =
                  Threading.Tasks.ValueTask.CompletedTask
            }
      }

    member _.GetService(_tipo: Type, _clave: obj) : obj =
      Unchecked.defaultof<obj>

  interface IDisposable with
    member _.Dispose() = ()

let private marcaPress = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let private consolidarCon respuesta marcas =
  use cliente = new ClienteFalso(respuesta)

  consolidateMarks cliente marcas |> Async.AwaitTask |> Async.RunSynchronously

let tests =
  testList "Agente" [
    testCase "un delta con signo y porcentaje se tolera" (fun () ->
      let memorias =
        consolidarCon "Press de banca | light | light. | load +5%" [
          marcaPress
        ]

      Expect.equal memorias.Length 1 "la marca sobrevive"

      Expect.equal memorias.[0].Deltas [ Load 5 ] "el adorno se descarta")

    testCase "un delta con signo negativo y porcentaje se tolera" (fun () ->
      let marca = { marcaPress with Label = Heavy }

      let memorias =
        consolidarCon "Press de banca | heavy | heavy. | load -10%" [ marca ]

      Expect.equal memorias.[0].Deltas [ Load -10 ] "queda -10")

    testCase "una linea fuera del protocolo se descarta" (fun () ->
      let memorias = consolidarCon "nota suelta sin pipes" [ marcaPress ]

      Expect.isEmpty memorias "no hay memoria de una linea mal formada")
  ]
