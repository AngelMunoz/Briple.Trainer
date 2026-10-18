// One live generation with the production Core prompts. Run from the repo root:
//
//   dotnet fsi spike/production-prompt-smoke.fsx
//
// The key comes from BRIPLE_KEY or the app's insecure settings.json.

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "Microsoft.Extensions.AI.Abstractions.dll"
#r "Microsoft.Extensions.AI.dll"
#r "Microsoft.Extensions.AI.OpenAI.dll"
#r "OpenAI.dll"
#r "System.ClientModel.dll"
#r "Microsoft.Extensions.Logging.Abstractions.dll"
#r "JDeck.dll"
#r "XParsec.dll"
#r "FsToolkit.ErrorHandling.dll"
#r "IcedTasks.dll"

#nowarn "57"

open System
open System.IO
open System.Text
open System.Text.Json
open Microsoft.Extensions.AI
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Briple.Trainer.Core
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Goals
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator

type Env(client: IChatClient) =
  interface IProvideChat with
    member _.Chat = client

  interface IProvideLog with
    member _.Log = NullLogger.Instance :> ILogger

let settingsKey() =
  let fromEnv = Environment.GetEnvironmentVariable "BRIPLE_KEY"

  if not(String.IsNullOrWhiteSpace fromEnv) then
    Some fromEnv
  else
    let path =
      Path.Combine(
        Environment.GetFolderPath Environment.SpecialFolder.ApplicationData,
        "Briple.Trainer",
        "settings.json"
      )

    if not(File.Exists path) then
      None
    else
      use document = JsonDocument.Parse(File.ReadAllText path)

      match document.RootElement.TryGetProperty "key" with
      | true, key when key.ValueKind = JsonValueKind.String ->
        let value = key.GetString()

        if String.IsNullOrWhiteSpace value then None else Some value
      | _ -> None

let athlete = {
  Name = "Ramiro"
  Gender = Male
  Goals = [ CardioVascularHealth; BasicBodyActivation ]
}

let brief = {
  Goal = CardioVascularHealth
  Frequency = TresDias
  Emphasis = []
  Adjustments = []
}

let request = {
  Athlete = athlete
  Memories = []
  Weeks = 2
  Brief = brief
}

let stamp = DateTime.Now.ToString "yyyyMMdd-HHmmss"

let dir =
  Path.Combine(__SOURCE_DIRECTORY__, "production-smoke", "artifacts", stamp)

Directory.CreateDirectory dir |> ignore

File.WriteAllText(Path.Combine(dir, "cardio.system.txt"), system brief.Goal)
File.WriteAllText(Path.Combine(dir, "cardio.user.txt"), newPlanPrompt request)

match settingsKey() with
| None -> failwith "no key: define BRIPLE_KEY or save the token in the app"
| Some key ->
  let provider: Provider = {
    Name = "deepseek"
    Endpoint = Uri "https://api.deepseek.com/responses"
    Model = "deepseek-flash"
    UserAgent = ""
    Headers = []
  }

  let client = createClient key provider

  let run =
    generateNew (Env client, request) Threading.CancellationToken.None
    |> fun work -> work.GetAwaiter().GetResult()

  File.WriteAllText(
    Path.Combine(dir, "cardio.briple"),
    run.Output,
    Encoding.UTF8
  )

  printfn "verdict:"
  printfn "%A" run.Verdict

  match run.Verdict with
  | Valid(plan, advisories) ->
    let days = Tree.daysOf plan |> List.ofSeq
    let rows = days |> List.sumBy(fun day -> day.Exercises.Length)
    let perDay = float rows / float days.Length

    printfn ""
    printfn "rows: %d, days: %d, rows/day: %.1f" rows days.Length perDay
    printfn "advisories: %d" advisories.Length

    for advisory in advisories do
      printfn "  %A" advisory
  | Rejected violations -> printfn "rejected: %d" violations.Length

  printfn ""
  printfn "artifacts: %s" dir
