// Dry run for goal-specific prompts. Run from the repo root:
//
//   dotnet build Briple.Trainer.slnx
//   dotnet fsi spike/prompt-goal-dryrun.fsx -- prompts
//   dotnet fsi spike/prompt-goal-dryrun.fsx -- live
//
// "prompts" renders the five goal prompts and writes them under
// spike/prompt-goal-dryrun/artifacts/<stamp>/prompts without calling the
// model. "live" also generates one plan per goal (1 athlete, 2 weeks,
// 3 days) and writes the plan, the checks and the report.
//
// The key comes from BRIPLE_KEY or the app's insecure settings.json.

#I @"..\src\Briple.Trainer.Core\bin\Debug\net10.0"
#r "Briple.Trainer.Core.dll"
#r "Microsoft.Extensions.AI.Abstractions.dll"
#r "Microsoft.Extensions.AI.dll"
#r "Microsoft.Extensions.AI.OpenAI.dll"
#r "OpenAI.dll"
#r "System.ClientModel.dll"
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
open OpenAI
open System.ClientModel

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Data
open Briple.Trainer.Core.Plan.Parser
open Briple.Trainer.Core.Plan.Projection
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Core.Prescription
open Briple.Trainer.Core.Requests
open Briple.Trainer.Core.Validator

// --- the prototype goal profile -------------------------------------------------

type GoalProfile = {
  Goal: PlanGoal
  Name: string
  WeeklyVolume: string
  RepRange: string
  Effort: string
  RirWeeks: int list
  CardioMinutes: int
  MaxRows: int
  Rest: string
  Structure: string
  Rotation: string
}

let profiles = [
  {
    Goal = Hypertrophy
    Name = "hypertrophy"
    WeeklyVolume = "10 to 20 hard sets per muscle group per week"
    RepRange = "6-15 reps on most work"
    Effort = "RIR 0-3 on rep-based rows; never a timed row"
    RirWeeks = [ 3; 2; 1; 0 ]
    CardioMinutes = 0
    MaxRows = 8
    Rest = "2-3 min between straight sets"
    Structure =
      "Straight sets. Group 2-4 exercises per circuit letter, 3-4 rounds. One circuit per movement theme. A strength exercise is never prescribed by time."
    Rotation =
      "Rotate the exercise variant every week (equipment, grip or angle); keep the movement pattern of each day."
  }
  {
    Goal = Strength
    Name = "strength"
    WeeklyVolume =
      "3 to 6 hard sets per main lift per week, plus 2-4 support exercises per session"
    RepRange = "3-6 reps on the main lifts, 6-12 on accessories"
    Effort = "RIR 1-3 on rep-based rows; never a timed row"
    RirWeeks = [ 3; 2; 1; 1 ]
    CardioMinutes = 0
    MaxRows = 6
    Rest = "3-5 min after a main lift, 2 min on accessories"
    Structure =
      "Straight sets. One or two main lifts first, then 2-4 accessories. Keep the main lift across the weeks and rotate the accessories."
    Rotation = "Keep the main lift; rotate accessories every week."
  }
  {
    Goal = WeightLoss
    Name = "weight loss"
    WeeklyVolume = "8 to 14 hard sets per muscle group per week, in circuits"
    RepRange = "8-15 reps"
    Effort = "RIR 1-3 on rep-based rows; zone 2 or rpe on cardio rows"
    RirWeeks = [ 3; 2; 2; 1 ]
    CardioMinutes = 10
    MaxRows = 7
    Rest = "30-60 s between exercises, 60-90 s between rounds"
    Structure =
      "Circuit. Group 4-6 exercises per circuit letter, 2-3 rounds, then one cardio finisher of 10-20 min. Short rests are the point."
    Rotation =
      "Rotate the circuit exercises every two weeks; keep the movement pattern."
  }
  {
    Goal = CardioVascularHealth
    Name = "cardiovascular health"
    WeeklyVolume = "3 to 4 support exercises per session; cardio is the base"
    RepRange = "8-12 reps on support work; cardio by time"
    Effort = "rpe or zone on every timed row; RIR 2-4 on support rows"
    RirWeeks = [ 3; 3; 2; 2 ]
    CardioMinutes = 20
    MaxRows = 6
    Rest = "60-90 s on support work; the cardio block has its own recovery"
    Structure =
      "Aerobic base. One continuous cardio block of at least 20 min, or intervals as laps of a work duration with the rest in the row, then 3-4 support exercises. The cardio block is its own circuit letter with 1 lap, or the intervals circuit with laps equal to the repeats."
    Rotation =
      "Keep the cardio modality for two or three weeks and progress duration first; rotate support exercises weekly."
  }
  {
    Goal = BasicBodyActivation
    Name = "basic body activation"
    WeeklyVolume = "4 to 8 sets per muscle group per week; no failure work"
    RepRange = "10-15 reps; light loads"
    Effort = "RIR 3-5 on rep-based rows; rpe 3-5 on timed holds"
    RirWeeks = [ 4; 4; 3; 3 ]
    CardioMinutes = 10
    MaxRows = 6
    Rest = "60-90 s"
    Structure =
      "Short circuit of 4-6 exercises, 2-3 rounds, then one light cardio block of 10-15 min. Keep the session short; strict technique first."
    Rotation =
      "Minimal rotation: repeat the same movement patterns for two weeks so the athlete learns them."
  }
]

let rirFor (profile: GoalProfile) (weeks: int) = [
  for week in 1..weeks ->
    week, string profile.RirWeeks.[min (week - 1) (profile.RirWeeks.Length - 1)]
]

// --- prompt text -----------------------------------------------------------------

let schemaText =
  """RESPONSE SCHEMA: respond only with valid JSON in this shape:
{
  "title": "text",
  "weeks": 2,
  "generated": "2026-10-17",
  "origin": "text",
  "gender": "male" or "female",
  "variants": [
    {
      "id": "3dias",
      "title": "text (optional)",
      "weeks": [
        { "number": 1, "days": [
          { "id": "lunes", "title": "text (optional)", "exercises": [
            { "circuit": "A", "laps": "3", "order": 1, "name": "text", "reps": "6-8", "rir": "2", "rest": "2 min", "notes": "-" }
          ] }
        ] }
      ]
    }
  ],
  "extras": ["free text"]
}
Day ids are Spanish: lunes, martes, miercoles, jueves, viernes, sabado, domingo.
The effort field uses "-" when it does not apply.
Do not write mesociclo effort lines in extras: they are added automatically.
Write all content (titles, exercise names, reps, rest, notes, extras) in Spanish."""

let prescriptionText =
  """PRESCRIPTION RULES:
- reps is either a pure numeric range ("6-8") or a pure duration ("30-45 s", "12 min"). Never append words: rounds come from the circuit laps, per-side work goes in notes.
- The effort field holds the effort target of the row. For rep-based work write a RIR range ("1-2", "3"). For timed work write "rpe 4-6" or a zone ("zona 2"). Never write the word RIR inside the field, and never put a bare RIR number on a timed row.
- A timed row of 5 minutes or more must be a cardio modality: caminata, cinta, bicicleta, elíptica, remo ergómetro, natación, escaladora. A strength exercise is never prescribed as a timed block.
- The exercise name is exactly one exercise; alternatives or progressions go in that exercise's notes. A slash inside a real name (90/90) is fine.
- LOAD DELTAS: load N means N percent of the athlete's habitual load. Reflect it only in that exercise's notes when it adds clarity.
- Inside a circuit letter every row shares the same laps. Order restarts at 1 in each circuit letter."""

let evidenceText =
  """EVIDENCE-BASED TRAINING:
- Every exercise must serve a clear purpose for this athlete, backed by current strength and conditioning evidence: specificity, progressive overload, fatigue management.
- Educated guesses are acceptable where evidence is thin. Uninformed choices are not.
- Do not force high-skill or high-risk techniques the athlete has not earned. Prefer joint-friendly, coachable progressions.
- If you cannot state why an exercise helps this athlete, replace it with one you can justify."""

let systemFor(profile: GoalProfile) =
  let profileText =
    String.concat "\n" [
      "GOAL PROFILE: " + profile.Name.ToUpperInvariant()
      "- Weekly volume: " + profile.WeeklyVolume
      "- Repetitions: " + profile.RepRange
      "- Effort: " + profile.Effort
      "- Cardio per session: "
      + (if profile.CardioMinutes = 0 then
           "none required"
         else
           string profile.CardioMinutes + " minutes or more")
      "- Exercises per day: at most " + string profile.MaxRows
      "- Rest: " + profile.Rest
      "- Structure: " + profile.Structure
      "- Progression: " + profile.Rotation
    ]

  String.concat "\n\n" [
    "You are a strength and conditioning coach who writes training plans."
    schemaText
    prescriptionText
    evidenceText
    profileText
    "Respond with the JSON only: no markdown, no extra text."
  ]

let userFor (profile: GoalProfile) (athlete: Athlete) (weeks: int) =
  let goals =
    match athlete.Goals with
    | [] -> ""
    | goals ->
      " Profile goals: "
      + (goals |> List.map(fun g -> sprintf "%A" g) |> String.concat ", ")
      + "."

  String.concat "\n" [
    "ATHLETE"
    "Athlete: "
    + athlete.Name
    + " ("
    + (Gender.toProtocol athlete.Gender)
    + ")."
    + goals
    ""
    "PLAN GOAL: " + profile.Name + "."
    "MANDATORY IN THE RESPONSE:"
    "- the plan covers " + string weeks + " weeks"
    "- the plan has one option per variant: 3dias"
    ""
    "STRUCTURE THIS PLAN MUST HAVE:"
    profile.Structure
    ""
    "The app adds the mesociclo effort line for each week; do not write it and do not copy a weekly RIR target into the rows."
    ""
    "WHAT YOU DECIDE:"
    "the plan title, the generation date, the origin line, each option title, each day title, each exercise circuit, each circuit rounds, the exercise order, each exercise name, the rep ranges, the effort, the rest periods, each exercise notes, the extras: guidance lines (no mesociclo)."
    ""
    "FORBIDDEN:"
    "- do not add options or genders other than the requested ones"
    "- do not invent fields outside the schema"
  ]

// --- the plan checks ---------------------------------------------------------------

type Finding = {
  Rule: string
  Detail: string
  Hard: bool
}

let cardioWords = [
  "caminata"
  "cinta"
  "bicicleta"
  "bici "
  "elíptica"
  "eliptica"
  "remo erg"
  "ergómetro"
  "ergometro"
  "natación"
  "natacion"
  "trote"
  "marcha"
  "intervalos"
  "escaladora"
]

let isCardio(name: string) =
  let lowered = name.ToLowerInvariant()
  cardioWords |> List.exists(fun word -> lowered.Contains word)

let tryTimed(exercise: Exercise) : int option =
  match (tryParseDose exercise.Reps: Result<Dose, PrescriptionAdvisory>) with
  | Ok(Timed(_, hi)) -> Some hi
  | _ -> None

let effortKind(exercise: Exercise) =
  match tryParseEffort exercise.Rir with
  | ValueSome(Effort.Rir _) -> "rir"
  | ValueSome(Effort.Rpe _) -> "rpe"
  | ValueSome(Effort.Zone _) -> "zone"
  | ValueNone -> "none"

let checkDay (profile: GoalProfile) (day: Day) : Finding list = [
  if day.Exercises.Length > profile.MaxRows then
    {
      Rule = "rows-per-day"
      Detail =
        sprintf
          "%s has %d rows; the profile allows %d"
          day.Id
          day.Exercises.Length
          profile.MaxRows
      Hard = true
    }

  for exercise in day.Exercises do
    match tryTimed exercise with
    | Some seconds when seconds >= 300 && not(isCardio exercise.Name) -> {
        Rule = "timed-strength"
        Detail =
          sprintf
            "%s: %s is prescribed for %d min and is not a cardio modality"
            day.Id
            exercise.Name
            (seconds / 60)
        Hard = true
      }
    | _ -> ()

    match tryTimed exercise with
    | Some _ when
        effortKind exercise = "rir"
        || exercise.Rir.ToLowerInvariant().Contains "rir"
        ->
        {
          Rule = "timed-rir"
          Detail =
            sprintf
              "%s: %s is timed and its effort is '%s'"
              day.Id
              exercise.Name
              exercise.Rir
          Hard = true
        }
    | _ -> ()

  let cardioMinutes =
    day.Exercises
    |> List.filter(fun exercise -> isCardio exercise.Name)
    |> List.sumBy(fun exercise ->
      match tryTimed exercise with
      | Some seconds -> seconds / 60
      | None -> 0)

  if profile.CardioMinutes > 0 && cardioMinutes < profile.CardioMinutes then
    {
      Rule = "cardio-minutes"
      Detail =
        sprintf
          "%s has %d cardio minutes; the profile asks for %d"
          day.Id
          cardioMinutes
          profile.CardioMinutes
      Hard = false
    }

  for (circuit, rows) in
    (day.Exercises |> List.groupBy(fun exercise -> exercise.Circuit)) do
    let laps = rows |> List.map(fun exercise -> exercise.Laps) |> List.distinct

    if laps.Length > 1 then
      {
        Rule = "circuit-laps"
        Detail =
          sprintf
            "%s circuit %s mixes laps %s"
            day.Id
            circuit
            (String.concat ", " laps)
        Hard = true
      }

    let single = rows.Length = 1
    let cardioOnly = rows |> List.forall(fun exercise -> isCardio exercise.Name)

    if single && not cardioOnly then
      {
        Rule = "circuit-size"
        Detail =
          sprintf
            "%s circuit %s has one exercise and it is not a cardio block"
            day.Id
            circuit
        Hard = false
      }

    let orders = rows |> List.map(fun exercise -> exercise.Order)

    if orders <> [ 1 .. rows.Length ] then
      {
        Rule = "circuit-order"
        Detail = sprintf "%s circuit %s has orders %A" day.Id circuit orders
        Hard = false
      }
]

let checkGoal (profile: GoalProfile) (plan: Plan) : Finding list = [
  for day in Tree.daysOf plan do
    yield! checkDay profile day

  for week, expected in rirFor profile plan.Weeks do
    match tryMesocycleRir plan week with
    | Some actual when actual = expected -> ()
    | Some actual -> {
        Rule = "mesociclo"
        Detail =
          sprintf
            "week %d has RIR %s; the profile asks for %s"
            week
            actual
            expected
        Hard = true
      }
    | None -> {
        Rule = "mesociclo"
        Detail =
          sprintf
            "week %d has no mesociclo line; the profile asks for RIR %s"
            week
            expected
        Hard = true
      }
]

let appendMesocycle (profile: GoalProfile) (plan: Plan) =
  let lines = [
    for week, value in rirFor profile plan.Weeks ->
      sprintf "mesociclo | Semana %d: objetivo | RIR %s" week value
  ]

  {
    plan with
        Extras = plan.Extras @ lines
  }

let textOfViolation(violation: Violation) = sprintf "%A" violation

// --- I/O ---------------------------------------------------------------------------

let mode =
  match fsi.CommandLineArgs |> Array.skip 1 |> Array.toList with
  | m :: _ -> m
  | [] -> "prompts"

let stamp = DateTime.Now.ToString "yyyyMMdd-HHmmss"

let runDir =
  Path.Combine(__SOURCE_DIRECTORY__, "prompt-goal-dryrun", "artifacts", stamp)

let promptDir = Path.Combine(runDir, "prompts")
let planDir = Path.Combine(runDir, "plans")
Directory.CreateDirectory promptDir |> ignore
Directory.CreateDirectory planDir |> ignore

let write (path: string) (text: string) =
  File.WriteAllText(path, text, Encoding.UTF8)

let athlete = {
  Name = "Ramiro"
  Gender = Male
  Goals = [ CardioVascularHealth; BasicBodyActivation ]
}

let weeks = 2

let briefOf profile = {
  Goal = profile.Goal
  Frequency = TresDias
  Emphasis = []
  Adjustments = []
}

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

let fence = String.replicate 3 (string(char 96))

let stripFences(text: string) =
  let trimmed = text.Trim()

  if trimmed.StartsWith fence then
    let firstNewline = trimmed.IndexOf '\n'
    let lastFence = trimmed.LastIndexOf fence

    if firstNewline > 0 && lastFence > firstNewline then
      trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim()
    else
      trimmed
  else
    trimmed

let callModel (client: IChatClient) (system: string) (user: string) = task {
  use cts = new Threading.CancellationTokenSource(TimeSpan.FromMinutes 5.0)

  let messages = [|
    ChatMessage(ChatRole.System, system)
    ChatMessage(ChatRole.User, user)
  |]

  let! response =
    client.GetResponseAsync(messages, cancellationToken = cts.Token)

  return response.Text
}

let client: IChatClient =
  if mode = "live" then
    match settingsKey() with
    | None -> failwith "no key: define BRIPLE_KEY or save the token in the app"
    | Some key ->
      let options =
        OpenAIClientOptions(
          Endpoint = Uri "https://api.deepseek.com",
          NetworkTimeout = TimeSpan.FromMinutes 4.0
        )

      let openAi = OpenAIClient(ApiKeyCredential key, options)
      openAi.GetResponsesClient().AsIChatClient "deepseek-flash"
  else
    Unchecked.defaultof<IChatClient>

// --- run ---------------------------------------------------------------------------

let report = StringBuilder()

let append(text: string) =
  report.AppendLine text |> ignore
  printfn "%s" text

append(sprintf "# Prompt dry run — %s" stamp)
append ""
append(sprintf "- Mode: %s" mode)

append(
  sprintf
    "- Athlete: %s (%s), %d weeks, 3dias"
    athlete.Name
    (Gender.toProtocol athlete.Gender)
    weeks
)

append ""

append "## Prompt sizes"
append ""
append "| Goal | System chars | User chars |"
append "|---|---:|---:|"

let promptOf profile =
  let system = systemFor profile
  let user = userFor profile athlete weeks

  write
    (Path.Combine(promptDir, profile.Name.Replace(" ", "-") + ".system.txt"))
    system

  write
    (Path.Combine(promptDir, profile.Name.Replace(" ", "-") + ".user.txt"))
    user

  system, user

let prompts = profiles |> List.map(fun profile -> profile, promptOf profile)

for (profile, (system, user)) in prompts do
  append(sprintf "| %s | %d | %d |" profile.Name system.Length user.Length)

append ""

if mode = "live" then
  append "## Plans"
  append ""

  append
    "| Goal | Rows | Days | Rows/day | Sets/day | Cardio min/day | Hard | Soft | Decoded |"

  append "|---|---:|---:|---:|---:|---:|---:|---:|---|"

for (profile, (system, user)) in prompts do
  let fileBase = profile.Name.Replace(" ", "-")

  if mode = "live" then
    let response =
      try
        (callModel client system user).GetAwaiter().GetResult()
      with ex ->
        printfn "call failed for %s: %s" profile.Name ex.Message
        ""

    write (Path.Combine(planDir, fileBase + ".response.txt")) response

    if String.IsNullOrWhiteSpace response then
      append(
        sprintf "| %s | - | - | - | - | - | - | - | call failed |" profile.Name
      )
    else
      let json = stripFences response

      match (fromJson json: Result<Plan, Violation>) with
      | Error violation ->
        append(
          sprintf
            "| %s | - | - | - | - | - | - | - | %s |"
            profile.Name
            (textOfViolation violation)
        )

        write
          (Path.Combine(planDir, fileBase + ".error.txt"))
          (textOfViolation violation)
      | Ok decoded ->
        let plan = appendMesocycle profile decoded
        let written = Briple.Trainer.Core.Plan.Writer.write plan
        write (Path.Combine(planDir, fileBase + ".briple")) written

        let days = Tree.daysOf plan |> List.ofSeq
        let rows = days |> List.sumBy(fun day -> day.Exercises.Length)

        let sets =
          days
          |> List.sumBy(fun day ->
            day.Exercises
            |> List.sumBy(fun e ->
              match Int32.TryParse e.Laps with
              | true, n -> n
              | _ -> 0))

        let cardioMinutes =
          days
          |> List.sumBy(fun day ->
            day.Exercises
            |> List.filter(fun e -> isCardio e.Name)
            |> List.sumBy(fun e ->
              match tryTimed e with
              | Some s -> s / 60
              | None -> 0))

        let daysCount = days.Length
        let perDay = if daysCount = 0 then 0.0 else float rows / float daysCount

        let setsPerDay =
          if daysCount = 0 then 0.0 else float sets / float daysCount

        let cardioPerDay =
          if daysCount = 0 then
            0.0
          else
            float cardioMinutes / float daysCount

        let findings = checkGoal profile plan
        let hard = findings |> List.filter(fun f -> f.Hard)
        let soft = findings |> List.filter(fun f -> not f.Hard)

        let existing = checkParameters athlete weeks (briefOf profile) plan

        let rirOnly, others =
          existing
          |> List.partition(function
            | WrongRir _
            | MissingRir _ -> true
            | _ -> false)

        let advisories = checkPrescription plan

        let checks = StringBuilder()
        checks.AppendLine(sprintf "goal: %s" profile.Name) |> ignore

        checks.AppendLine(
          sprintf
            "rows: %d, days: %d, rows/day: %.1f, sets/day: %.1f"
            rows
            daysCount
            perDay
            setsPerDay
        )
        |> ignore

        checks.AppendLine(sprintf "cardio min/day: %.1f" cardioPerDay) |> ignore

        checks.AppendLine(sprintf "existing non-RIR: %d" others.Length)
        |> ignore

        for v in others do
          checks.AppendLine("  " + textOfViolation v) |> ignore

        checks.AppendLine(
          sprintf
            "existing RIR (the new profile changes these): %d"
            rirOnly.Length
        )
        |> ignore

        for v in rirOnly do
          checks.AppendLine("  " + textOfViolation v) |> ignore

        checks.AppendLine(
          sprintf "prescription advisories: %d" advisories.Length
        )
        |> ignore

        for v in advisories do
          checks.AppendLine("  " + textOfViolation v) |> ignore

        checks.AppendLine(sprintf "new hard: %d" hard.Length) |> ignore

        for f in hard do
          checks.AppendLine(sprintf "  [%s] %s" f.Rule f.Detail) |> ignore

        checks.AppendLine(sprintf "new soft: %d" soft.Length) |> ignore

        for f in soft do
          checks.AppendLine(sprintf "  [%s] %s" f.Rule f.Detail) |> ignore

        write
          (Path.Combine(planDir, fileBase + ".checks.txt"))
          (checks.ToString())

        append(
          sprintf
            "| %s | %d | %d | %.1f | %.1f | %.1f | %d | %d | yes |"
            profile.Name
            rows
            daysCount
            perDay
            setsPerDay
            cardioPerDay
            hard.Length
            soft.Length
        )


append ""
append "## Findings by goal"
append ""

for (profile, _) in prompts do
  let path =
    Path.Combine(planDir, profile.Name.Replace(" ", "-") + ".checks.txt")

  if File.Exists path then
    append("### " + profile.Name)
    append ""
    append "~~~"
    append(File.ReadAllText path |> fun s -> s.TrimEnd())
    append "~~~"
    append ""

append "## Artifacts"
append ""
append("- prompts: " + promptDir)
append("- plans: " + planDir)
append ""

write (Path.Combine(runDir, "report.md")) (report.ToString())
printfn ""
printfn "artifacts: %s" runDir
