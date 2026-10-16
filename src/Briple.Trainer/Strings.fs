module Briple.Trainer.Strings

open System.Globalization
open System.Reflection
open System.Resources

[<Struct>]
type Language =
  | Es
  | En

module Language =
  let toCulture language =
    match language with
    | Es -> CultureInfo "es"
    | En -> CultureInfo "en"

  let inline toggle language =
    match language with
    | Es -> En
    | En -> Es

let resourceManager =
  lazy
    ResourceManager("Briple.Trainer.Strings", Assembly.GetExecutingAssembly())

let get(culture: CultureInfo, key: string) : string =
  resourceManager.Force().GetString(key, culture)
  |> Option.ofObj
  |> Option.defaultValue key

let getf(culture: CultureInfo, key: string, args: obj list) : string =
  System.String.Format(culture, get(culture, key), List.toArray args)

module Keys =
  [<Literal>]
  let appTitle = "app.title"

  [<Literal>]
  let navAthletes = "nav.athletes"

  [<Literal>]
  let navLanguage = "nav.language"

  [<Literal>]
  let athletesTitle = "athletes.title"

  [<Literal>]
  let athletesEmpty = "athletes.empty"

  [<Literal>]
  let athletesNew = "athletes.new"

  [<Literal>]
  let athleteName = "athlete.name"

  [<Literal>]
  let athleteGender = "athlete.gender"

  [<Literal>]
  let athleteVariants = "athlete.variants"

  [<Literal>]
  let athleteGoals = "athlete.goals"

  [<Literal>]
  let athleteFocus = "athlete.focus"

  [<Literal>]
  let athleteSave = "athlete.save"

  [<Literal>]
  let athleteCancel = "athlete.cancel"

  [<Literal>]
  let genderMale = "gender.male"

  [<Literal>]
  let genderFemale = "gender.female"

  [<Literal>]
  let goalHypertrophy = "goal.hypertrophy"

  [<Literal>]
  let goalWeightLoss = "goal.weightloss"

  [<Literal>]
  let goalCardio = "goal.cardio"

  [<Literal>]
  let goalActivation = "goal.activation"

  [<Literal>]
  let goalStrength = "goal.strength"

  [<Literal>]
  let memoriesTitle = "memories.title"

  [<Literal>]
  let memoriesEmpty = "memories.empty"

  [<Literal>]
  let memoriesConfirm = "memories.confirm"

  [<Literal>]
  let memoriesDiscard = "memories.discard"

  [<Literal>]
  let memoriesAddMark = "memories.addMark"

  [<Literal>]
  let memoriesConsolidateAgent = "memories.consolidateAgent"

  [<Literal>]
  let markTarget = "mark.target"

  [<Literal>]
  let markText = "mark.text"

  [<Literal>]
  let exemptionText = "exemption.text"

  [<Literal>]
  let labelLight = "label.light"

  [<Literal>]
  let labelHeavy = "label.heavy"

  [<Literal>]
  let labelPain = "label.pain"

  [<Literal>]
  let labelNoEquipment = "label.noequipment"

  [<Literal>]
  let plansTitle = "plans.title"

  [<Literal>]
  let plansGenerate = "plans.generate"

  [<Literal>]
  let plansReview = "plans.review"

  [<Literal>]
  let plansOpen = "plans.open"

  [<Literal>]
  let wizardTitle = "wizard.title"

  [<Literal>]
  let wizardStepMemories = "wizard.step.memories"

  [<Literal>]
  let wizardStepBrief = "wizard.step.brief"

  [<Literal>]
  let wizardWeeks = "wizard.weeks"

  [<Literal>]
  let wizardBack = "wizard.back"

  [<Literal>]
  let wizardGoal = "wizard.goal"

  [<Literal>]
  let wizardFocus = "wizard.focus"

  [<Literal>]
  let wizardRir = "wizard.rir"

  [<Literal>]
  let wizardCounts = "wizard.counts"

  [<Literal>]
  let wizardGenerating = "wizard.generating"

  [<Literal>]
  let wizardGenerate = "wizard.generate"

  [<Literal>]
  let wizardNext = "wizard.next"

  [<Literal>]
  let wizardConfirm = "wizard.confirm"

  [<Literal>]
  let wizardDiscard = "wizard.discard"

  [<Literal>]
  let reviewTitle = "review.title"

  [<Literal>]
  let reviewSave = "review.save"

  [<Literal>]
  let reviewExport = "review.export"

  [<Literal>]
  let reviewSaved = "review.saved"

  [<Literal>]
  let reviewExported = "review.exported"

  [<Literal>]
  let reviewInvalid = "review.invalid"

  [<Literal>]
  let planGuide = "plan.guide"

  [<Literal>]
  let planLaps = "plan.laps"

  [<Literal>]
  let planWeek = "plan.week"

  [<Literal>]
  let planReps = "plan.reps"

  [<Literal>]
  let planRir = "plan.rir"

  [<Literal>]
  let planRest = "plan.rest"

  [<Literal>]
  let planNotes = "plan.notes"

  [<Literal>]
  let settingsMissing = "settings.missing"

  [<Literal>]
  let loginTitle = "login.title"

  [<Literal>]
  let loginToken = "login.token"

  [<Literal>]
  let loginSave = "login.save"

  [<Literal>]
  let loginSaved = "login.saved"

  [<Literal>]
  let loginInsecureTitle = "login.insecureTitle"

  [<Literal>]
  let loginInsecureBody = "login.insecureBody"

  [<Literal>]
  let loginFailed = "login.failed"

  [<Literal>]
  let errorTitle = "error.title"
