module Briple.Trainer.Persistence

open System
open System.Data.Common
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Donald
open JDeck
open Migrondi.Core

open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Plan.Types

type AthleteEntity = { Id: Guid; Profile: Athlete }

type MemoryEntity = {
  Id: Guid
  AthleteId: Guid
  Memory: Memory
  CreatedAt: DateTime
}

type PlanEntity = {
  Id: Guid
  AthleteId: Guid
  Title: string
  Kind: string
  Weeks: int
  Counts: (string * int) list
  Raw: string
  CreatedAt: DateTime
}

[<Struct>]
type PlanInsert = {
  AthleteId: Guid
  Kind: string
  Counts: (string * int) list
}

type IStore =
  abstract ListAthletes: unit -> Task<Result<AthleteEntity list, exn>>
  abstract AddAthlete: profile: Athlete -> Task<Result<AthleteEntity, exn>>
  abstract FindAthlete: id: Guid -> Task<Result<AthleteEntity voption, exn>>
  abstract ListMemories: athleteId: Guid -> Task<Result<MemoryEntity list, exn>>

  abstract AddMemory:
    athleteId: Guid * memory: Memory -> Task<Result<MemoryEntity, exn>>

  abstract UpdateMemory: entity: MemoryEntity -> Task<Result<unit, exn>>
  abstract ListPlans: athleteId: Guid -> Task<Result<PlanEntity list, exn>>

  abstract AddPlan:
    insert: PlanInsert * plan: Plan -> Task<Result<PlanEntity, exn>>

  abstract FindPlan: id: Guid -> Task<Result<PlanEntity voption, exn>>
  abstract UpdatePlanRaw: id: Guid * raw: string -> Task<Result<unit, exn>>

  abstract AddRequest:
    athleteId: Guid * kind: string * payload: string -> Task<Result<unit, exn>>

// --- codecs: JDeck for the JSON columns, plain text for the counts ---

module Codec =

  /// Maps every element, keeping the list only when all succeed.
  let traverse (f: 'a -> 'b option) (xs: 'a list) : 'b list option =
    let inline folder acc x =
      acc |> Option.bind(fun ys -> f x |> Option.map(fun y -> y :: ys))

    List.fold folder (Some []) xs |> Option.map List.rev

  let stringList(xs: string list) : string =
    Json.sequence(xs, Encode.string).ToJsonString()

  let tryStringList(json: string) : Result<string list, exn> =
    Decoding.fromString(json, Decode.list(fun _ el -> Required.string el))
    |> Result.mapError DecodingException

  let (|GoalToken|_|)(token: string) =
    match token with
    | "hypertrophy" -> Some Hypertrophy
    | "weightloss" -> Some WeightLoss
    | "cardio" -> Some CardioVascularHealth
    | "activation" -> Some BasicBodyActivation
    | "strength" -> Some Strength
    | _ -> None

  let goals(xs: PlanGoal list) : string =
    let goalToken =
      function
      | Hypertrophy -> "hypertrophy"
      | WeightLoss -> "weightloss"
      | CardioVascularHealth -> "cardio"
      | BasicBodyActivation -> "activation"
      | Strength -> "strength"

    xs |> List.map goalToken |> stringList

  let tryGoals(json: string) : Result<PlanGoal list, exn> =
    tryStringList json
    |> Result.bind(fun tokens ->
      tokens
      |> traverse(fun token ->
        match token with
        | GoalToken goal -> Some goal
        | _ -> None)
      |> Option.map Ok
      |> Option.defaultWith(fun () -> Error(exn "unknown goal token")))

  let statusToken status =
    match status with
    | Proposed -> "proposed"
    | Confirmed -> "confirmed"
    | Discarded -> "discarded"

  let (|StatusToken|_|)(token: string) =
    match token with
    | "proposed" -> Some Proposed
    | "confirmed" -> Some Confirmed
    | "discarded" -> Some Discarded
    | _ -> None

  let labelToken label =
    match label with
    | Light -> "light"
    | Heavy -> "heavy"
    | Pain -> "pain"
    | NoEquipment -> "noequipment"

  let (|LabelToken|_|)(token: string) =
    match token with
    | "light" -> Some Light
    | "heavy" -> Some Heavy
    | "pain" -> Some Pain
    | "noequipment" -> Some NoEquipment
    | _ -> None

  let deltaToken =
    function
    | Rir change -> $"rir:{change}"
    | Reps change -> $"reps:{change}"
    | Sets change -> $"sets:{change}"
    | Load percentage -> $"load:{percentage}"
    | PatternVariant name -> $"variant:{name}"

  let (|DeltaToken|_|)(token: string) =
    let parts = token.Split(':', 2)

    let number(value: string) =
      match Int32.TryParse value with
      | true, parsed -> Some parsed
      | _ -> None

    match parts with
    | [| kind; value |] ->
      match kind with
      | "rir" -> number value |> Option.map Rir
      | "reps" -> number value |> Option.map Reps
      | "sets" -> number value |> Option.map Sets
      | "load" -> number value |> Option.map Load
      | "variant" -> Some(PatternVariant value)
      | _ -> None
    | _ -> None

  let deltas(xs: Delta list) : string = xs |> List.map deltaToken |> stringList

  let tryDeltas(json: string) : Result<Delta list, exn> =
    tryStringList json
    |> Result.bind(fun tokens ->
      tokens
      |> traverse (function
        | DeltaToken delta -> Some delta
        | _ -> None)
      |> Option.map Ok
      |> Option.defaultWith(fun () -> Error(exn "unknown delta token")))

  /// The origin as flat JSON; target kind is "exercise" or "day".
  let origin(origin: MemoryOrigin) : string =
    let target =
      function
      | Exercise name -> "exercise", Encode.string name
      | Day id -> "day", Encode.string id

    let markEntity(mark: FeedbackMark) =
      let kind, value = target mark.Target

      Json.object [
        "kind", Encode.string "mark"
        "targetKind", Encode.string kind
        "target", value
        "label", Encode.string(labelToken mark.Label)
        "date", Encode.string(mark.Date.ToString("yyyy-MM-dd"))
      ]

    let exemptionEntity(exemption: Exemption) =
      let kind, value = target exemption.Target

      Json.object [
        "kind", Encode.string "exemption"
        "targetKind", Encode.string kind
        "target", value
        "technique", Encode.string exemption.Technique
      ]

    match origin with
    | Mark mark -> markEntity mark
    | Exemption exemption -> exemptionEntity exemption
    |> fun entity -> entity.ToJsonString()

  let tryOrigin(json: string) : Result<MemoryOrigin, exn> =
    let decoder(element: JsonElement) : Result<MemoryOrigin, DecodeError> =
      let inline text name =
        element |> Required.Property.get(name, Required.string)

      decode {
        let! kind = text "kind"
        let! targetKind = text "targetKind"
        let! target = text "target"

        let target =
          match targetKind with
          | "day" -> Day target
          | _ -> Exercise target

        match kind with
        | "mark" ->
          let! label = text "label"
          let! date = text "date"

          let label =
            match label with
            | LabelToken label -> Ok label
            | _ -> Error(DecodeError.ofError(element.Clone(), "unknown label"))

          let! label = label

          let! date =
            match DateOnly.TryParse date with
            | true, parsed -> Ok parsed
            | _ -> Error(DecodeError.ofError(element.Clone(), "bad date"))

          return
            Mark {
              Target = target
              Label = label
              Date = date
            }
        | _ ->
          let! technique = text "technique"

          return
            Exemption {
              Target = target
              Technique = technique
            }
      }

    Decoding.fromString(json, decoder) |> Result.mapError DecodingException

  let counts(xs: (string * int) list) : string =
    xs
    |> List.map(fun (variant, count) -> $"{variant}={count}")
    |> String.concat "\n"

  let tryCounts(raw: string) : Result<(string * int) list, exn> =
    let (|Count|_|)(line: string) =
      match line.Split('=', 2) with
      | [| variant; count |] ->
        match Int32.TryParse count with
        | true, parsed -> Some(variant, parsed)
        | _ -> None
      | _ -> None

    raw.Split('\n')
    |> Array.toList
    |> traverse (function
      | Count pair -> Some pair
      | _ -> None)
    |> Option.map Ok
    |> Option.defaultWith(fun () -> Error(exn "malformed counts"))

// --- database location and migrations ---

let defaultDbPath() =
  let dir =
    Path.Combine(
      Environment.GetFolderPath Environment.SpecialFolder.ApplicationData,
      "Briple.Trainer"
    )

  Directory.CreateDirectory dir |> ignore
  Path.Combine(dir, "briple.db")

let migrate(dbPath: string) : Result<unit, exn> =
  try
    let root = AppContext.BaseDirectory

    let config = {
      MigrondiConfig.Default with
          migrations = Path.Combine(root, "migrations")
          connection = $"Data Source={dbPath};"
    }

    let migrondi = Migrondi.MigrondiFactory(config, root)
    migrondi.Initialize()

    let pending =
      migrondi.MigrationsList()
      |> Seq.exists(function
        | Pending _ -> true
        | _ -> false)

    if pending then
      migrondi.RunUp() |> ignore

    Ok()
  with e ->
    Error e

let createConnection(dbPath: string) : unit -> SqliteConnection =
  fun () -> new SqliteConnection $"Data Source={dbPath}"

let exportBriple(path: string, raw: string) : Result<string, exn> =
  try
    File.WriteAllText(path, raw)
    Ok path
  with e ->
    Error e

// --- store ---

let inline timestamp() = DateTime.UtcNow.ToString "O"

type SqliteStore(dbPath: string) =

  let guarded(op: unit -> Task<Result<'a, exn>>) : Task<Result<'a, exn>> = task {
    try
      return! op()
    with e ->
      return Error e
  }

  let withConn
    (op: SqliteConnection -> Task<Result<'a, exn>>)
    : Task<Result<'a, exn>> =
    guarded(fun () -> task {
      use conn = createConnection dbPath ()
      do! conn.OpenAsync()
      return! op conn
    })

  let mapAthlete(rd: DbDataReader) : AthleteEntity = {
    Id = rd.ReadString("id") |> Guid.Parse
    Profile = {
      Name = rd.ReadString("name")
      Gender =
        rd.ReadString("gender")
        |> Gender.tryFileToken
        |> Option.defaultValue Male
      Variants =
        rd.ReadString("variants")
        |> Codec.tryStringList
        |> Result.defaultValue []
      Goals = rd.ReadString("goals") |> Codec.tryGoals |> Result.defaultValue []
      Focus =
        rd.ReadString("focus") |> Codec.tryStringList |> Result.defaultValue []
    }
  }

  let mapMemory(rd: DbDataReader) : MemoryEntity = {
    Id = rd.ReadString("id") |> Guid.Parse
    AthleteId = rd.ReadString("athlete_id") |> Guid.Parse
    Memory = {
      Summary = rd.ReadString("summary")
      Status =
        match rd.ReadString("status") with
        | Codec.StatusToken status -> status
        | _ -> Proposed
      Origin =
        rd.ReadString("origin")
        |> Codec.tryOrigin
        |> function
          | Ok origin -> origin
          | Error e -> failwith $"undecodable origin: {e.Message}"
      Deltas =
        rd.ReadString("deltas") |> Codec.tryDeltas |> Result.defaultValue []
    }
    CreatedAt = rd.ReadString("created_at") |> DateTime.Parse
  }

  let mapPlan(rd: DbDataReader) : PlanEntity = {
    Id = rd.ReadString("id") |> Guid.Parse
    AthleteId = rd.ReadString("athlete_id") |> Guid.Parse
    Title = rd.ReadString("title")
    Kind = rd.ReadString("kind")
    Weeks = rd.ReadInt32("weeks")
    Counts =
      rd.ReadString("counts") |> Codec.tryCounts |> Result.defaultValue []
    Raw = rd.ReadString("raw")
    CreatedAt = rd.ReadString("created_at") |> DateTime.Parse
  }

  interface IStore with

    member _.ListAthletes() =
      withConn(fun conn -> task {
        let! athletes =
          conn
          |> Db.newCommand "SELECT * FROM athletes ORDER BY created_at"
          |> Db.Async.query mapAthlete

        return Ok athletes
      })

    member _.AddAthlete(profile: Athlete) =
      withConn(fun conn -> task {
        let entity = {
          Id = Guid.NewGuid()
          Profile = profile
        }

        do!
          conn
          |> Db.newCommand
            "INSERT INTO athletes (id, name, gender, variants, goals, focus, created_at) VALUES ($id, $name, $gender, $variants, $goals, $focus, $created_at)"
          |> Db.setParams [
            "$id", sqlString(string entity.Id)
            "$name", sqlString profile.Name
            "$gender", sqlString(Gender.toFileToken profile.Gender)
            "$variants", sqlString(Codec.stringList profile.Variants)
            "$goals", sqlString(Codec.goals profile.Goals)
            "$focus", sqlString(Codec.stringList profile.Focus)
            "$created_at", sqlString(timestamp())
          ]
          |> Db.Async.exec

        return Ok entity
      })

    member _.FindAthlete(id: Guid) =
      withConn(fun conn -> task {
        let! athlete =
          conn
          |> Db.newCommand "SELECT * FROM athletes WHERE id = $id"
          |> Db.setParams [ "$id", sqlString(string id) ]
          |> Db.Async.querySingle mapAthlete

        return Ok(ValueOption.ofOption athlete)
      })

    member _.ListMemories(athleteId: Guid) =
      withConn(fun conn -> task {
        let! memories =
          conn
          |> Db.newCommand
            "SELECT * FROM memories WHERE athlete_id = $athlete_id ORDER BY created_at"
          |> Db.setParams [ "$athlete_id", sqlString(string athleteId) ]
          |> Db.Async.query mapMemory

        return Ok memories
      })

    member _.AddMemory(athleteId: Guid, memory: Memory) =
      withConn(fun conn -> task {
        let entity = {
          Id = Guid.NewGuid()
          AthleteId = athleteId
          Memory = memory
          CreatedAt = DateTime.UtcNow
        }

        do!
          conn
          |> Db.newCommand
            "INSERT INTO memories (id, athlete_id, summary, status, origin, deltas, created_at) VALUES ($id, $athlete_id, $summary, $status, $origin, $deltas, $created_at)"
          |> Db.setParams [
            "$id", sqlString(string entity.Id)
            "$athlete_id", sqlString(string athleteId)
            "$summary", sqlString memory.Summary
            "$status", sqlString(Codec.statusToken memory.Status)
            "$origin", sqlString(Codec.origin memory.Origin)
            "$deltas", sqlString(Codec.deltas memory.Deltas)
            "$created_at", sqlString(timestamp())
          ]
          |> Db.Async.exec

        return Ok entity
      })

    member _.UpdateMemory(entity: MemoryEntity) =
      withConn(fun conn -> task {
        do!
          conn
          |> Db.newCommand
            "UPDATE memories SET summary = $summary, status = $status, deltas = $deltas WHERE id = $id"
          |> Db.setParams [
            "$id", sqlString(string entity.Id)
            "$summary", sqlString entity.Memory.Summary
            "$status", sqlString(Codec.statusToken entity.Memory.Status)
            "$deltas", sqlString(Codec.deltas entity.Memory.Deltas)
          ]
          |> Db.Async.exec

        return Ok()
      })

    member _.ListPlans(athleteId: Guid) =
      withConn(fun conn -> task {
        let! plans =
          conn
          |> Db.newCommand
            "SELECT * FROM plans WHERE athlete_id = $athlete_id ORDER BY created_at"
          |> Db.setParams [ "$athlete_id", sqlString(string athleteId) ]
          |> Db.Async.query mapPlan

        return Ok plans
      })

    member _.AddPlan(insert: PlanInsert, plan: Plan) =
      withConn(fun conn -> task {
        let entity = {
          Id = Guid.NewGuid()
          AthleteId = insert.AthleteId
          Title = plan.Title
          Kind = insert.Kind
          Weeks = plan.Weeks
          Counts = insert.Counts
          Raw = Briple.Trainer.Core.Plan.Writer.write plan
          CreatedAt = DateTime.UtcNow
        }

        do!
          conn
          |> Db.newCommand
            "INSERT INTO plans (id, athlete_id, title, kind, weeks, counts, raw, created_at) VALUES ($id, $athlete_id, $title, $kind, $weeks, $counts, $raw, $created_at)"
          |> Db.setParams [
            "$id", sqlString(string entity.Id)
            "$athlete_id", sqlString(string insert.AthleteId)
            "$title", sqlString entity.Title
            "$kind", sqlString entity.Kind
            "$weeks", sqlInt32 entity.Weeks
            "$counts", sqlString(Codec.counts entity.Counts)
            "$raw", sqlString entity.Raw
            "$created_at", sqlString(timestamp())
          ]
          |> Db.Async.exec

        return Ok entity
      })

    member _.FindPlan(id: Guid) =
      withConn(fun conn -> task {
        let! plan =
          conn
          |> Db.newCommand "SELECT * FROM plans WHERE id = $id"
          |> Db.setParams [ "$id", sqlString(string id) ]
          |> Db.Async.querySingle mapPlan

        return Ok(ValueOption.ofOption plan)
      })

    member _.UpdatePlanRaw(id: Guid, raw: string) =
      withConn(fun conn -> task {
        do!
          conn
          |> Db.newCommand "UPDATE plans SET raw = $raw WHERE id = $id"
          |> Db.setParams [
            "$id", sqlString(string id)
            "$raw", sqlString raw
          ]
          |> Db.Async.exec

        return Ok()
      })

    member _.AddRequest(athleteId: Guid, kind: string, payload: string) =
      withConn(fun conn -> task {
        do!
          conn
          |> Db.newCommand
            "INSERT INTO requests (id, athlete_id, kind, payload, created_at) VALUES ($id, $athlete_id, $kind, $payload, $created_at)"
          |> Db.setParams [
            "$id", sqlString(string(Guid.NewGuid()))
            "$athlete_id", sqlString(string athleteId)
            "$kind", sqlString kind
            "$payload", sqlString payload
            "$created_at", sqlString(timestamp())
          ]
          |> Db.Async.exec

        return Ok()
      })

let sqliteStore(dbPath: string) : IStore = SqliteStore(dbPath) :> IStore
