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

  /// Reads a settings token ("es"/"en"); anything unknown reads as Es.
  let ofToken(text: string) =
    match text with
    | "en" -> En
    | _ -> Es

  /// Writes the settings token of a language.
  let toToken(language) =
    match language with
    | Es -> "es"
    | En -> "en"

let resourceManager =
  lazy
    ResourceManager("Briple.Trainer.Strings", Assembly.GetExecutingAssembly())

let get(culture: CultureInfo, key: string) : string =
  resourceManager.Force().GetString(key, culture)
  |> Option.ofObj
  |> Option.defaultValue key

let getf(culture: CultureInfo, key: string, args: obj list) : string =
  System.String.Format(culture, get(culture, key), List.toArray args)

let all(culture: CultureInfo) : (string * string) list =
  let set = resourceManager.Force().GetResourceSet(culture, true, true)

  [
    for entry in set do
      match entry with
      | :? System.Collections.DictionaryEntry as entry ->
        match entry.Key, entry.Value with
        | (:? string as key), (:? string as value) -> key, value
        | _ -> ()
      | _ -> ()
  ]

module Keys =
  [<Literal>]
  let appTitle = "app.title"

  [<Literal>]
  let navAthletes = "nav.athletes"

  [<Literal>]
  let navSettings = "nav.settings"

  [<Literal>]
  let commonBack = "common.back"

  [<Literal>]
  let commonCancel = "common.cancel"

  [<Literal>]
  let errorFriendly = "error.friendly"

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
  let athleteGoals = "athlete.goals"

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
  let memoryScopePlan = "memory.scope.plan"

  [<Literal>]
  let memoryScopeAthlete = "memory.scope.athlete"

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
  let deltaDuration = "delta.duration"

  [<Literal>]
  let deltaZone = "delta.zone"

  [<Literal>]
  let deltaModality = "delta.modality"

  [<Literal>]
  let deltaSets = "delta.sets"

  [<Literal>]
  let deltaLoad = "delta.load"

  [<Literal>]
  let deltaVariant = "delta.variant"

  [<Literal>]
  let memoryAddAdjustment = "memory.addAdjustment"

  [<Literal>]
  let memoryAddValue = "memory.addValue"

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
  let wizardFrequency = "wizard.frequency"

  [<Literal>]
  let wizardEmphasis = "wizard.emphasis"

  [<Literal>]
  let freq2 = "freq.2"

  [<Literal>]
  let freq3 = "freq.3"

  [<Literal>]
  let freq4 = "freq.4"

  [<Literal>]
  let freq5 = "freq.5"

  [<Literal>]
  let freq6 = "freq.6"

  [<Literal>]
  let emphasisArms = "emphasis.arms"

  [<Literal>]
  let emphasisShoulders = "emphasis.shoulders"

  [<Literal>]
  let emphasisChest = "emphasis.chest"

  [<Literal>]
  let emphasisBack = "emphasis.back"

  [<Literal>]
  let emphasisLegs = "emphasis.legs"

  [<Literal>]
  let emphasisGlutes = "emphasis.glutes"

  [<Literal>]
  let emphasisCore = "emphasis.core"

  [<Literal>]
  let emphasisFullBody = "emphasis.fullBody"

  [<Literal>]
  let emphasisSquat = "emphasis.squat"

  [<Literal>]
  let emphasisDeadlift = "emphasis.deadlift"

  [<Literal>]
  let emphasisBenchPress = "emphasis.benchPress"

  [<Literal>]
  let emphasisOverheadPress = "emphasis.overheadPress"

  [<Literal>]
  let emphasisOlympicLifts = "emphasis.olympicLifts"

  [<Literal>]
  let emphasisPush = "emphasis.push"

  [<Literal>]
  let emphasisPull = "emphasis.pull"

  [<Literal>]
  let emphasisFullBodyCircuit = "emphasis.fullBodyCircuit"

  [<Literal>]
  let emphasisHiit = "emphasis.hiit"

  [<Literal>]
  let emphasisStrengthCardio = "emphasis.strengthCardio"

  [<Literal>]
  let emphasisContinuousZone2 = "emphasis.continuousZone2"

  [<Literal>]
  let emphasisIntervalHiit = "emphasis.intervalHiit"

  [<Literal>]
  let emphasisMixed = "emphasis.mixed"

  [<Literal>]
  let emphasisMobility = "emphasis.mobility"

  [<Literal>]
  let emphasisStability = "emphasis.stability"

  [<Literal>]
  let emphasisMovementPatterns = "emphasis.movementPatterns"

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
  let wizardDiscardTitle = "wizard.discardTitle"

  [<Literal>]
  let wizardDiscardBody = "wizard.discardBody"

  [<Literal>]
  let wizardRetry = "wizard.retry"

  [<Literal>]
  let wizardExit = "wizard.exit"

  [<Literal>]
  let wizardCancelGeneration = "wizard.cancelGeneration"

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
  let reviewNoteTitle = "review.noteTitle"

  [<Literal>]
  let reviewNotePlaceholder = "review.notePlaceholder"

  [<Literal>]
  let reviewNoteInterpret = "review.noteInterpret"

  [<Literal>]
  let reviewNoteSurface = "review.noteSurface"

  [<Literal>]
  let reviewNoteRejected = "review.noteRejected"

  [<Literal>]
  let reviewNoteFailed = "review.noteFailed"

  [<Literal>]
  let reviewNoteFull = "review.noteFull"

  [<Literal>]
  let reviewCircuit = "review.circuit"

  [<Literal>]
  let reviewCircuitOne = "review.circuitOne"

  [<Literal>]
  let reviewCircuitOnly = "review.circuitOnly"

  [<Literal>]
  let reviewDayBadge = "review.dayBadge"

  [<Literal>]
  let reviewExercisesOne = "review.exercises.one"

  [<Literal>]
  let reviewExercisesMany = "review.exercises.many"

  [<Literal>]
  let reviewCircuitsOne = "review.circuits.one"

  [<Literal>]
  let reviewCircuitsMany = "review.circuits.many"

  [<Literal>]
  let reviewWeekBadge = "review.weekBadge"

  [<Literal>]
  let reviewDays = "review.days"

  [<Literal>]
  let reviewContentWarning = "review.contentWarning"

  [<Literal>]
  let reviewFieldTitle = "review.fieldTitle"

  [<Literal>]
  let reviewFieldOrigin = "review.fieldOrigin"

  [<Literal>]
  let reviewFieldName = "review.fieldName"

  [<Literal>]
  let reviewFieldNotes = "review.fieldNotes"

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
  let planEdit = "plan.edit"

  [<Literal>]
  let planDone = "plan.done"

  [<Literal>]
  let planRevert = "plan.revert"

  [<Literal>]
  let settingsMissing = "settings.missing"

  [<Literal>]
  let settingsLanguage = "settings.language"

  [<Literal>]
  let settingsLangEs = "settings.lang.es"

  [<Literal>]
  let settingsLangEn = "settings.lang.en"

  [<Literal>]
  let settingsTheme = "settings.theme"

  [<Literal>]
  let settingsThemeSystem = "settings.theme.system"

  [<Literal>]
  let settingsThemeLight = "settings.theme.light"

  [<Literal>]
  let settingsThemeDark = "settings.theme.dark"

  [<Literal>]
  let settingsProvider = "settings.provider"

  [<Literal>]
  let settingsConnected = "settings.connected"

  [<Literal>]
  let settingsConnect = "settings.connect"

  [<Literal>]
  let settingsMissingToken = "settings.missingToken"

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
  let loginContinue = "login.continue"

  [<Literal>]
  let errorTitle = "error.title"

  // --- wizard violations: typed cases to one user-facing line each ---

  [<Literal>]
  let violationGenderMismatch = "violation.genderMismatch"

  [<Literal>]
  let violationWeeks = "violation.weeks"

  [<Literal>]
  let violationVariants = "violation.variants"

  [<Literal>]
  let violationVariantWeeks = "violation.variantWeeks"

  [<Literal>]
  let violationFrequency = "violation.frequency"

  [<Literal>]
  let violationRir = "violation.rir"

  [<Literal>]
  let violationMissingRir = "violation.missingRir"

  [<Literal>]
  let violationCount = "violation.count"

  [<Literal>]
  let violationFormatWarning = "violation.formatWarning"

  [<Literal>]
  let violationRows = "violation.rows"

  [<Literal>]
  let violationTimedStrength = "violation.timedStrength"

  [<Literal>]
  let violationTimedRir = "violation.timedRir"

  [<Literal>]
  let violationCircuitLaps = "violation.circuitLaps"

  [<Literal>]
  let violationCardio = "violation.cardio"

  [<Literal>]
  let dataUnreadableJson = "data.unreadableJson"

  [<Literal>]
  let dataBadGender = "data.badGender"

  [<Literal>]
  let dataMissingTitle = "data.missingTitle"

  [<Literal>]
  let dataBadWeeks = "data.badWeeks"

  [<Literal>]
  let dataEmptyVariantId = "data.emptyVariantId"

  [<Literal>]
  let dataMissingWeekNumber = "data.missingWeekNumber"

  [<Literal>]
  let dataMissingOrder = "data.missingOrder"

  [<Literal>]
  let dataUnknownDay = "data.unknownDay"

  [<Literal>]
  let dataEmptyExerciseName = "data.emptyExerciseName"

  [<Literal>]
  let advisoryNotPureRange = "advisory.notPureRange"

  [<Literal>]
  let advisoryUnknownUnit = "advisory.unknownUnit"

  [<Literal>]
  let advisoryMultipleExercises = "advisory.multipleExercises"

  [<Literal>]
  let advisoryRepsWithoutEffort = "advisory.repsWithoutEffort"

  [<Literal>]
  let advisoryTimedWithRir = "advisory.timedWithRir"

  [<Literal>]
  let reviewUnsaved = "review.unsaved"

  [<Literal>]
  let wizardApplies = "wizard.applies"

  [<Literal>]
  let wizardMode = "wizard.mode"

  [<Literal>]
  let wizardModeAdjust = "wizard.mode.adjust"

  [<Literal>]
  let wizardModeNew = "wizard.mode.new"

  [<Literal>]
  let wizardStepOf = "wizard.stepOf"

  [<Literal>]
  let guardAthleteTitle = "guard.athlete.title"

  [<Literal>]
  let guardAthleteBody = "guard.athlete.body"

  [<Literal>]
  let guardGenerationTitle = "guard.generation.title"

  [<Literal>]
  let guardGenerationBody = "guard.generation.body"

  [<Literal>]
  let guardPlanTitle = "guard.plan.title"

  [<Literal>]
  let guardPlanBody = "guard.plan.body"

  [<Literal>]
  let guardUnsavedTitle = "guard.unsaved.title"

  [<Literal>]
  let guardUnsavedBody = "guard.unsaved.body"

  [<Literal>]
  let guardLeave = "guard.leave"

/// One user-facing failure line: the action that failed and the cause,
/// clipped to a readable length. The full detail belongs to the log.
let friendly(culture: CultureInfo, action: string, reason: string) : string =
  let clipped =
    if reason.Length > 120 then
      $"{reason.Substring(0, 117)}..."
    else
      reason

  getf(culture, Keys.errorFriendly, [ action :> obj; clipped :> obj ])
