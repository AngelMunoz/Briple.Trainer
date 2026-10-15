// Manual test of the agent with your own key, against the already built
// DLLs. Run from the repo root.
//
//   1) dotnet build Briple.Trainer.slnx
//   2) $env:BRIPLE_KEY = "your-key"
//   3) dotnet fsi spike/probar.fsx -- <mode> [times] [provider] [profile]
//
// Modes:
//   new N [prov] [profile]        generates N new plans and validates each output
//   adjust N [prov] [profile]     generates N adjustments (month 2 over month 1) and validates
//   ab [prov] [profile]           adjustment with and without the confirmed memory, compares
//   consolidate [prov]            test marks -> proposed memories
//   exception [prov]              new plan with the trainer exemption active
//   prompt [new|adjust|consolidate|exception]   prints the prompt without calling
//
// Providers: deepseek (default), opencode.
// Profiles: male (3dias+5dias, default), female (5dias).
// BRIPLE_MODEL overrides the model of any provider.
// Every run uses a fresh provider session and stores its output in
// spike/output-<cell>-<n>.txt for review.

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "Microsoft.Extensions.AI.Abstractions.dll"
#r "Microsoft.Extensions.AI.dll"
#r "Microsoft.Extensions.AI.OpenAI.dll"
#r "OpenAI.dll"
#r "System.ClientModel.dll"

open System
open Microsoft.Extensions.AI
open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prompts
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator

// --- arguments and provider --------------------------------------------------

let arguments = fsi.CommandLineArgs |> Array.skip 1 |> Array.toList

let mode =
  match arguments with
  | m :: _ -> m
  | [] -> "help"

let times =
  match arguments with
  | _ :: n :: _ ->
    match Int32.TryParse n with
    | true, v -> v
    | _ -> 1
  | _ -> 1

// modes without a run count: [provider] goes right after the mode
let modesWithoutTimes = [
  "ab"
  "consolidate"
  "consolidate-raw"
  "exception"
  "prompt"
]

let providerName =
  match arguments with
  | m :: _ :: p :: _ when not(List.contains m modesWithoutTimes) -> p
  | m :: p :: _ when List.contains m modesWithoutTimes ->
    if Int32.TryParse p |> fst then "deepseek" else p
  | _ -> "deepseek"

let profileName =
  let modesWithProfile = [ "ab"; "consolidate"; "consolidate-raw"; "exception" ]

  match arguments with
  // modes with a count: new/adjust N [provider] [profile]
  | m :: _ :: _ :: profile :: _ when m = "new" || m = "adjust" -> profile
  // modes without a count: [provider] [profile]
  | m :: _ :: profile :: _ when List.contains m modesWithProfile -> profile
  | _ -> "male"

let provider name =
  let modelEnv = Environment.GetEnvironmentVariable "BRIPLE_MODEL"

  let defaultModel =
    if String.IsNullOrWhiteSpace modelEnv then
      None
    else
      Some modelEnv

  // OpenCode asks for a session header for routing and caching
  let session = Guid.NewGuid().ToString("N")

  match name with
  | "opencode" -> {
      Name = "opencode"
      Endpoint = Uri "https://opencode.ai/inference/go/v1/responses"
      Model = defaultModel |> Option.defaultValue "muse-spark-1.3-contributor"
      UserAgent = "briple-trainer-spike/1.0"
      Headers = [ ("x-opencode-session", session) ]
    }
  | _ -> {
      // responses: standard effort in reasoning.effort (default high)
      Name = "deepseek"
      Endpoint = Uri "https://api.deepseek.com/responses"
      Model = defaultModel |> Option.defaultValue "deepseek-flash"
      UserAgent = ""
      Headers = []
    }

// --- fixtures and profiles -----------------------------------------------------

let root = IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, ".."))

let fixture name =
  IO.Path.Combine(root, "tests", "Briple.Trainer.Core.Tests", "fixtures", name)
  |> IO.File.ReadAllText

let parseFixture name =
  match fixture name |> parsePlan with
  | Ok parsed -> parsed
  | Error error -> failwith $"fixture {name} does not parse: {error.Message}"

let month1 = parseFixture "training_plan_4w.txt"
let month2 = parseFixture "month2_4w.txt"

let man = athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Male)

let woman = athletesOfPlan month1.Plan |> List.find(fun a -> a.Gender = Female)

let manBrief = {
  Adjustments = []
  Focus = []
  RirPerWeek = [ (1, "3"); (2, "2-3"); (3, "2"); (4, "1") ]
  Variants = [ "3dias"; "5dias" ]
}

type Profile = {
  Athlete: Athlete
  Brief: Brief
  Month1Counts: (string * int) list
  Month2Counts: (string * int) list
}

let profileOf name =
  let athlete, brief =
    match name with
    | "female" -> woman, { manBrief with Variants = [ "5dias" ] }
    | _ -> man, manBrief

  {
    Athlete = athlete
    Brief = brief
    Month1Counts = planCounts athlete month1.Plan
    Month2Counts = planCounts athlete month2.Plan
  }

let newRequestOf profile memories =
  newRequest profile.Athlete memories 4 profile.Brief profile.Month1Counts

let adjustmentRequestOf profile memories =
  adjustmentRequest
    profile.Athlete
    memories
    4
    profile.Brief
    month1.Plan
    profile.Month2Counts

let pressMark = {
  Target = Exercise "Press de banca"
  Label = Light
  Date = DateOnly(2026, 10, 6)
}

let memory = pressMark |> consolidate |> confirm [ Load 5 ]

// trainer exemption: a high-risk technique prescribed on purpose
let exemptionMemory =
  memoryOfExemption {
    Target = Day "Miércoles"
    Technique = "Sentadilla con salto"
  }

let testMarks = [
  pressMark
  {
    Target = Day "Lunes"
    Label = Heavy
    Date = DateOnly(2026, 10, 6)
  }
  {
    Target = Exercise "Sentadilla con barra"
    Label = Pain
    Date = DateOnly(2026, 10, 7)
  }
  {
    Target = Exercise "Dominadas"
    Label = NoEquipment
    Date = DateOnly(2026, 10, 8)
  }
  {
    Target = Day "Viernes"
    Label = Light
    Date = DateOnly(2026, 10, 9)
  }
]

let promptTarget =
  match arguments with
  | "prompt" :: target :: _ -> target
  | _ -> "new"

// --- runs -----------------------------------------------------------------

let key() =
  let value = Environment.GetEnvironmentVariable "BRIPLE_KEY"

  if String.IsNullOrWhiteSpace value then
    failwith "define BRIPLE_KEY in the environment"

  value

let chosenProvider = provider providerName

// fresh session per run: the provider cache does not always return the
// same answer across repeated runs
let withFreshSession(provider: Provider) =
  let renew(name, value) =
    if name = "x-opencode-session" then
      (name, Guid.NewGuid().ToString("N"))
    else
      (name, value)

  {
    provider with
        Headers = provider.Headers |> List.map renew
  }

let runClient() =
  createClient (key()) (withFreshSession chosenProvider)

let save name (text: string) =
  IO.Path.Combine(__SOURCE_DIRECTORY__, name)
  |> fun path -> IO.File.WriteAllText(path, text)

let report label i (run: Run) =
  match run.Verdict with
  | Valid _ -> printfn "run %d: VALID" i
  | Rejected violations ->
    printfn "run %d: REJECTED" i

    violations
    |> List.iter(fun violation ->
      printfn "  [%s] %s" violation.Rule violation.Detail)

  save $"output-{label}-{i}.txt" run.Output
  save $"output-{label}-{i}.json" run.Response

let generationMode isAdjustment times (profile: Profile) =
  let label = if isAdjustment then "adjust" else "new"

  save "prompt-system.txt" system

  let run() =
    if isAdjustment then
      let request = adjustmentRequestOf profile [ memory ]

      save $"prompt-{label}-{profileName}.txt" (adjustmentPrompt request)

      Async.AwaitTask(generateAdjustment (runClient()) request)
      |> Async.RunSynchronously
    else
      let request = newRequestOf profile [ memory ]

      save $"prompt-{label}-{profileName}.txt" (newPlanPrompt request)

      Async.AwaitTask(generateNew (runClient()) request)
      |> Async.RunSynchronously

  let valid = ResizeArray()
  let errors = ResizeArray()

  for i in 1..times do
    let result =
      try
        Ok(run())
      with ex ->
        Error ex.Message

    match result with
    | Ok run ->
      report $"{label}-{profileName}" i run

      match run.Verdict with
      | Valid _ -> valid.Add i
      | Rejected _ -> ()
    | Error message ->
      errors.Add i
      printfn "run %d: call error (%s)" i message

  if times > 1 then
    printfn ""

    printfn
      "%d of %d runs valid on the first pass, %d call errors"
      valid.Count
      times
      errors.Count

let abMode(profile: Profile) =
  printfn "running the same adjustment with and without the confirmed memory..."
  printfn ""

  let run request =
    try
      Some(
        Async.AwaitTask(generateAdjustment (runClient()) request)
        |> Async.RunSynchronously
      )
    with ex ->
      printfn "call error (%s)" ex.Message
      None

  let withMemory = run(adjustmentRequestOf profile [ memory ])
  let withoutMemory = run(adjustmentRequestOf profile [])

  withMemory |> Option.iter(report $"ab-with-{profileName}" 1)
  withoutMemory |> Option.iter(report $"ab-without-{profileName}" 2)

  printfn ""

  match withMemory, withoutMemory with
  | Some withRun, Some withoutRun ->
    printfn
      "the output changes with the memory: %b"
      (withRun.Output <> withoutRun.Output)
  | _ -> printfn "the comparison does not close: there was a call error"

// generates a new plan with the trainer exemption active
let exceptionMode() =
  let profile = profileOf "male"

  let request = newRequestOf profile [ exemptionMemory ]

  save "prompt-system.txt" system
  save "prompt-exception.txt" (newPlanPrompt request)

  let run =
    Async.AwaitTask(generateNew (runClient()) request) |> Async.RunSynchronously

  report "exception" 1 run

let consolidateMode() =
  let marks = testMarks

  let memories =
    Async.AwaitTask(consolidateMarks (runClient()) marks)
    |> Async.RunSynchronously

  printfn
    "%d of %d marks consolidated into well-formed memories"
    memories.Length
    marks.Length

  printfn ""

  memories
  |> List.iter(fun memory ->
    printfn "- %s" memory.Summary
    printfn "  deltas: %A" memory.Deltas)

// calls consolidation and stores the raw answer, to inspect which lines
// the protocol discards; it replicates the Agent's ask
let consolidateRawMode() =
  let askRaw (client: IChatClient) (system: string) (user: string) = task {
    let messages = [|
      ChatMessage(ChatRole.System, system)
      ChatMessage(ChatRole.User, user)
    |]

    let! response = client.GetResponseAsync(messages)

    return response.Text
  }

  let response =
    askRaw (runClient()) consolidationSystem (consolidationPrompt testMarks)
    |> Async.AwaitTask
    |> Async.RunSynchronously

  save "output-consolidate-raw.txt" response
  printfn "%s" response

// prints the prompt without calling the API
let promptMode target =
  let profile = profileOf "male"

  let name, user =
    match target with
    | "adjust" ->
      "prompt-adjust.txt",
      adjustmentPrompt(adjustmentRequestOf profile [ memory ])
    | "consolidate" -> "prompt-consolidate.txt", consolidationPrompt testMarks
    | "exception" ->
      "prompt-exception.txt",
      newPlanPrompt(newRequestOf profile [ exemptionMemory ])
    | _ -> "prompt-new.txt", newPlanPrompt(newRequestOf profile [ memory ])

  let systemText =
    if target = "consolidate" then
      consolidationSystem
    else
      system

  save "prompt-system.txt" systemText
  save name user

  printfn "=== SYSTEM ==="
  printfn "%s" systemText
  printfn ""
  printfn "=== USER (%s) ===" name
  printfn "%s" user

let help() =
  printfn
    "modes: new N [prov] [profile] | adjust N [prov] [profile] | ab [prov] [profile] | consolidate [prov] | consolidate-raw [prov] | exception [prov] | prompt [new|adjust|consolidate|exception]"

  printfn "providers: deepseek (default), opencode"

  printfn "profiles: male (default), female"

  printfn "define BRIPLE_KEY; BRIPLE_MODEL overrides the model"

printfn
  "provider: %s (%s, model %s)"
  chosenProvider.Name
  (chosenProvider.Endpoint.ToString())
  chosenProvider.Model

printfn "profile: %s" profileName

printfn ""

let chosenProfile = profileOf profileName

match mode with
| "new" -> generationMode false times chosenProfile
| "adjust" -> generationMode true times chosenProfile
| "ab" -> abMode chosenProfile
| "consolidate" -> consolidateMode()
| "consolidate-raw" -> consolidateRawMode()
| "exception" -> exceptionMode()
| "prompt" -> promptMode promptTarget
| _ -> help()
