module Briple.Trainer.Core.Tests.Agent.Tests

// The consolidation protocol against a fake client: the model's answer
// passes through consolidateMarks as is and the well-formed memories
// survive. The model decorates numbers (load +5%) and the protocol
// already declares load as percentage: the adornment is tolerated.

open System
open System.Threading.Tasks
open Expecto
open Microsoft.Extensions.AI
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Agent

/// Always answers the same text, without touching the network.
type FakeClient(answer: string) =
  interface IChatClient with
    member _.GetResponseAsync
      (
        _messages: Collections.Generic.IEnumerable<ChatMessage>,
        _options: ChatOptions,
        _cancellation: Threading.CancellationToken
      ) : Task<ChatResponse> =
      Task.FromResult(ChatResponse(ChatMessage(ChatRole.Assistant, answer)))

    member _.GetStreamingResponseAsync
      (
        _messages: Collections.Generic.IEnumerable<ChatMessage>,
        _options: ChatOptions,
        _cancellation: Threading.CancellationToken
      ) : Collections.Generic.IAsyncEnumerable<ChatResponseUpdate> =
      // a single update with the whole text: the consumer cannot tell it
      // apart from a chunked stream
      {
        new Collections.Generic.IAsyncEnumerable<ChatResponseUpdate> with
          member _.GetAsyncEnumerator
            (_ct: Threading.CancellationToken)
            : Collections.Generic.IAsyncEnumerator<ChatResponseUpdate> =
            let mutable served = false

            let mutable current = Unchecked.defaultof<ChatResponseUpdate>

            {
              new Collections.Generic.IAsyncEnumerator<ChatResponseUpdate> with
                member _.Current = current

                member _.MoveNextAsync() : Threading.Tasks.ValueTask<bool> =
                  if served then
                    Threading.Tasks.ValueTask<bool>(false)
                  else
                    served <- true
                    current <- ChatResponseUpdate(ChatRole.Assistant, answer)

                    Threading.Tasks.ValueTask<bool>(true)

                member _.DisposeAsync() : Threading.Tasks.ValueTask =
                  Threading.Tasks.ValueTask.CompletedTask
            }
      }

    member _.GetService(_kind: Type, _key: obj) : obj = Unchecked.defaultof<obj>

  interface IDisposable with
    member _.Dispose() = ()

let private pressMark = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let private consolidateWith answer marks =
  use client = new FakeClient(answer)

  consolidateMarks client marks |> Async.AwaitTask |> Async.RunSynchronously

let tests =
  testList "Agent" [
    testCase "a delta with sign and percentage is tolerated" (fun () ->
      let memories =
        consolidateWith "Press de banca | light | light. | load +5%" [
          pressMark
        ]

      Expect.equal memories.Length 1 "the mark survives"

      Expect.equal memories.[0].Deltas [ Load 5 ] "the adornment is dropped")

    testCase "a delta with negative sign and percentage is tolerated" (fun () ->
      let mark = { pressMark with Label = Heavy }

      let memories =
        consolidateWith "Press de banca | heavy | heavy. | load -10%" [ mark ]

      Expect.equal memories.[0].Deltas [ Load -10 ] "stays -10")

    testCase "a line outside the protocol is discarded" (fun () ->
      let memories = consolidateWith "loose note without pipes" [ pressMark ]

      Expect.isEmpty memories "no memory from a malformed line")
  ]
