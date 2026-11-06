module Briple.Trainer.UI.Tests.Fixtures

// The fixtures the page-level suites share: quiet dialogs and toasts, a
// fake agent, the sample plan, and a real store over a throwaway
// database. Component.Tests and Snapshots.Tests build their pages over
// these instead of restating them per suite.

open System
open System.Globalization
open System.IO
open System.Reactive.Subjects
open System.Threading.Tasks

open Expecto
open FSharp.Control.Reactive

open Briple.Trainer.Core.Agent
open Briple.Trainer.Core.Athletes
open Briple.Trainer.Core.Plan.Types
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.Strings

// migrations under test log through the app's pipeline (silent under the
// default Serilog)
let testLogger = Briple.Trainer.Env.loggerFactory.CreateLogger "Tests"

let quietDialogs = {
  new Briple.Trainer.Env.IDialogs with
    member _.ConfirmAsync(_) = async { return true }
}

let quietToasts = {
  new Briple.Trainer.Env.IToasts with
    member _.Show(_, _, _) = ()
}

let es = Language.toCulture Es

/// A real store set over a throwaway database.
let fixtureStores() : DataStores =
  let db =
    Path.Combine(Path.GetTempPath(), $"briple-fixtures-{Guid.NewGuid():N}.db")

  match Briple.Trainer.Persistence.migrate(testLogger, db) with
  | Ok() ->
    Briple.Trainer.Stores.createStores(
      Briple.Trainer.Persistence.sqliteStore db,
      ignore
    )
  | Error e -> failtest $"fixture store migration failed: {e}"

let sampleAthlete: Athlete = {
  Name = "Pepe"
  Gender = Male
  Goals = [ Hypertrophy; Strength ]
}

let sampleRaw =
  Briple.Trainer.Core.Plan.Writer.write {
    Version = 1
    Title = "Mes 1"
    Weeks = 1
    Generated = "2026-10-15"
    Origin = "trainer"
    Blocks = [
      {
        Gender = Male
        Variants = [
          {
            Id = "3dias"
            Title = None
            Weeks = [
              {
                Number = 1
                Days = [
                  {
                    Id = "Lunes"
                    Title = Some "Empuje"
                    Notes = []
                    Exercises = [
                      {
                        Circuit = "A"
                        Laps = "3"
                        Order = 1
                        Name = "Press de banca"
                        Reps = "8-10"
                        Rir = "2"
                        Rest = "120s"
                        Notes = "-"
                      }
                    ]
                  }
                ]
              }
            ]
          }
        ]
      }
    ]
    Extras = []
  }

/// A multi-plan file in the app's own format: one block per gender, two
/// variants for the man, one for the woman. The import page reads this
/// text the way it reads a picked file.
let importRaw =
  String.concat "\n" [
    "#start plan v1"
    "#meta titulo Importable"
    "#meta semanas 2"
    "#meta generado 2026-11-06"
    "#meta origen import fixture"
    "#start genero hombre"
    "#start opcion 3dias"
    "#start semana 1"
    "#start dia Lunes"
    "#ej A|3|1|Press de banca|8-10|2|90 s|-"
    "#ej A|3|2|Remo con barra|8-10|2|90 s|-"
    "#end dia"
    "#start dia Miércoles"
    "#ej A|3|1|Sentadilla|8-10|2|90 s|-"
    "#ej A|3|2|Curl femoral|10-12|1|90 s|-"
    "#end dia"
    "#end semana"
    "#start semana 2"
    "#start dia Lunes"
    "#ej A|3|1|Press militar|8-10|2|90 s|-"
    "#ej A|3|2|Jalón al pecho|10-12|1|90 s|-"
    "#end dia"
    "#start dia Miércoles"
    "#ej A|3|1|Prensa|10-12|1|90 s|-"
    "#ej A|3|2|Face pull|12-20|0|90 s|-"
    "#end dia"
    "#end semana"
    "#end opcion"
    "#start opcion 5dias"
    "#meta titulo Cinco días"
    "#start semana 1"
    "#start dia Lunes"
    "#ej A|3|1|Press banca|6-8|2|3 min|-"
    "#end dia"
    "#start dia Martes"
    "#ej A|3|1|Remo pesado|8-10|2|3 min|-"
    "#end dia"
    "#start dia Miércoles"
    "#ej A|3|1|Sentadilla alta|6-8|2|3 min|-"
    "#end dia"
    "#end semana"
    "#end opcion"
    "#end genero"
    "#start genero mujer"
    "#start opcion 5dias"
    "#start semana 1"
    "#start dia Lunes"
    "#ej A|3|1|Hip thrust|8-12|2|2 min|-"
    "#end dia"
    "#start dia Martes"
    "#ej A|3|1|Press con mancuernas|8-12|2|2 min|-"
    "#end dia"
    "#end semana"
    "#end opcion"
    "#end genero"
    "#end plan"
  ]

/// Answers the same reading every time, without touching the network.
type FakeAgent(reading: NoteInterpretation) =
  interface Briple.Trainer.Env.IProvideAgent with
    member _.AgentAvailable = true

    member _.GenerateNew(_, _) = failwith "not used in this flow"

    member _.GenerateAdjustment(_, _) = failwith "not used in this flow"

    member _.ConsolidateMarks(_, _) = failwith "not used in this flow"

    member _.InterpretNote(_, _) = Task.FromResult(Ok reading)

let noReading: NoteInterpretation = { Memories = []; Rejections = [] }

let generateEnv
  (stores: DataStores)
  (agent: Briple.Trainer.Env.IProvideAgent)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Views.Generate.GenerateEnv = {
  Store = stores.Raw
  Memories = stores.Memories
  Plans = stores.Plans
  Agent = agent
  Logger = testLogger
  Dialogs = quietDialogs
  Toasts = quietToasts
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let reviewEnv
  (stores: DataStores)
  (agent: Briple.Trainer.Env.IProvideAgent)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Views.Review.ReviewEnv = {
  Plans = stores.Plans
  Memories = stores.Memories
  Agent = agent
  Host = ValueNone
  Logger = testLogger
  Toasts = quietToasts
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

let private noArchived = new BehaviorSubject<AthleteEntity list>(List.empty)

/// The athlete-list page env: one live roster, an empty archived list,
/// quiet deletes.
let athleteListEnv
  (athletes: BehaviorSubject<AthleteEntity list>)
  (culture: Briple.Trainer.Components.Page.PageCulture)
  : Briple.Trainer.Components.AthleteList.AthleteListEnv = {
  Observe = fun () -> athletes :> IObservable<_>
  ObserveArchived = fun () -> noArchived :> IObservable<_>
  Snapshot = fun () -> athletes.Value
  SnapshotArchived = fun () -> noArchived.Value
  Archive = fun _ -> Task.FromResult(Ok())
  Restore = fun _ -> Task.FromResult(Ok())
  Delete = fun _ -> Task.FromResult(Ok())
  Dialogs = quietDialogs
  Toasts = quietToasts
  Culture = culture.Current()
  CultureChanged = culture.Changed
}

/// The login env: the token saves to the fake vault; the culture is the
/// suite's Spanish.
type LoginEnv() =
  interface Briple.Trainer.Env.IProvideCredentials with
    member _.TokenInsecure = false

    member _.SaveCredentials(_) =
      Ok Briple.Trainer.Env.CredentialSave.Secured

  interface Briple.Trainer.Env.IProvideCulture with
    member _.Culture = es
    member _.CultureChanged = Observable.empty
