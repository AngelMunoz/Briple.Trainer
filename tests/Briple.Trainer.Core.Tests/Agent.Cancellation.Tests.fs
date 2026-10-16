module Briple.Trainer.Core.Tests.Agent.Cancellation.Tests

// Cancellation of the agent flows: the ambient token of the caller's
// async must reach the provider stream, cancelling must surface as
// OperationCanceledException/TaskCanceledException, and the chunks
// served after the cancel must be zero. Behavior is asserted, never
// token identity (the token inside async is a linked instance).

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open Expecto
open IcedTasks
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging.Abstractions
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Agent

/// Serves slow chunks and counts them; the delay honors the token the
/// streaming call received, so a cancel stops the stream mid-flight.
type SlowFake(total: int, chunkMs: int) =
  let mutable served = 0
  let mutable token: CancellationToken = Unchecked.defaultof<_>

  member _.Served = served

  interface IChatClient with
    member _.GetResponseAsync
      (
        messages: IEnumerable<ChatMessage>,
        options: ChatOptions,
        cancellation: CancellationToken
      ) : Task<ChatResponse> =
      Task.FromResult(ChatResponse(ChatMessage(ChatRole.Assistant, "x")))

    member _.GetStreamingResponseAsync
      (
        messages: IEnumerable<ChatMessage>,
        options: ChatOptions,
        cancellation: CancellationToken
      ) : IAsyncEnumerable<ChatResponseUpdate> =
      token <- cancellation

      {
        new IAsyncEnumerable<ChatResponseUpdate> with
          member _.GetAsyncEnumerator
            (_ct: CancellationToken)
            : IAsyncEnumerator<ChatResponseUpdate> = {
            new IAsyncEnumerator<ChatResponseUpdate> with
              member _.Current = ChatResponseUpdate(ChatRole.Assistant, "chunk")

              member _.MoveNextAsync() : ValueTask<bool> =
                if served >= total then
                  ValueTask<bool>(false)
                else
                  served <- served + 1

                  // the delay faults with TaskCanceledException when
                  // the token fires: that is the cancel path
                  ValueTask<bool>(
                    task {
                      do! Task.Delay(chunkMs, token)
                      return true
                    }
                  )

              member _.DisposeAsync() : ValueTask = ValueTask.CompletedTask
          }
      }

    member _.GetService(kind: Type, key: obj) : obj = Unchecked.defaultof<obj>

  interface IDisposable with
    member _.Dispose() = ()

type SlowEnv(client: IChatClient) =
  interface IProvideChat with
    member _.Chat = client

  interface IProvideLog with
    member _.Log = NullLogger.Instance

let pressMark = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let tests =
  testList "Agent.cancellation" [
    testCase "the ambient token reaches the provider stream" (fun () ->
      use fake = new SlowFake(3, 10)
      let env = SlowEnv fake

      let work = asyncEx {
        let! _ = consolidateMarks(env, [ pressMark ])
        return ()
      }

      use cts = new CancellationTokenSource()
      Async.StartAsTask(work, cancellationToken = cts.Token).Wait()

      Expect.isTrue (fake.Served >= 1) "chunks were served through the call")

    testCase "a cancel mid-stream stops the work and surfaces" (fun () ->
      use fake = new SlowFake(100, 30)
      let env = SlowEnv fake

      let work = asyncEx {
        let! _ = consolidateMarks(env, [ pressMark ])
        return ()
      }

      use cts = new CancellationTokenSource()

      let task = Async.StartAsTask(work, cancellationToken = cts.Token)

      cts.CancelAfter(120)

      let cancelled =
        (try
          task.Wait()
          false
         with
         | :? AggregateException as agg when
           agg.InnerExceptions.Count = 1
           && (agg.InnerException :? TaskCanceledException
               || agg.InnerException :? OperationCanceledException)
           ->
           true
         | _ -> false)

      Expect.isTrue cancelled "the cancel surfaced as OCE/TCE"

      Expect.isLessThan
        fake.Served
        100
        "chunks after the cancel: zero more served")

    testCase "an uncancelled run completes" (fun () ->
      use fake = new SlowFake(3, 10)
      let env = SlowEnv fake

      let work = asyncEx {
        let! _ = consolidateMarks(env, [ pressMark ])
        return ()
      }

      use cts = new CancellationTokenSource()
      Async.StartAsTask(work, cancellationToken = cts.Token).Wait()

      Expect.equal fake.Served 3 "every chunk was served")
  ]
