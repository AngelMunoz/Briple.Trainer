module Briple.Trainer.Strings

// UI text lives in .NET resx: the neutral resource is Spanish (the app
// default) and `en` is a satellite. Core never emits localizable prose;
// every string the UI renders comes from here with an explicit culture.
// Reading with an explicit culture is deliberate: items queued on the
// dispatcher run under the enqueuer's captured ExecutionContext, so the
// ambient thread culture is not stable inside view factories.

open System.Globalization
open System.Reflection
open System.Resources

/// The languages the app ships.
[<Struct>]
type Language =
  | Es
  | En

module Language =
  /// The culture of a language.
  val toCulture: language: Language -> CultureInfo

  /// The other language.
  val inline toggle: Language -> Language

  /// Reads a settings token ("es"/"en"); anything unknown reads as Es.
  val ofToken: text: string -> Language

  /// Writes the settings token of a language.
  val toToken: language: Language -> string

/// The text of a key under a culture; a missing key renders as the key
/// itself so an untranslated entry is visible instead of silent.
val get: culture: CultureInfo * key: string -> string

/// A formatted entry; `String.Format` over the culture's text.
val getf: culture: CultureInfo * key: string * args: obj list -> string

/// Every key and value of the resx under a culture; the source of the
/// localization resource dictionary.
val all: culture: CultureInfo -> (string * string) list

module Keys =
  [<Literal>]
  val appTitle: string = "app.title"

  [<Literal>]
  val navAthletes: string = "nav.athletes"

  [<Literal>]
  val navSettings: string = "nav.settings"

  [<Literal>]
  val commonBack: string = "common.back"

  [<Literal>]
  val commonCancel: string = "common.cancel"

  [<Literal>]
  val errorFriendly: string = "error.friendly"

  [<Literal>]
  val athletesTitle: string = "athletes.title"

  [<Literal>]
  val athletesEmpty: string = "athletes.empty"

  [<Literal>]
  val athletesNew: string = "athletes.new"

  [<Literal>]
  val athleteName: string = "athlete.name"

  [<Literal>]
  val athleteGender: string = "athlete.gender"

  [<Literal>]
  val athleteGoals: string = "athlete.goals"

  [<Literal>]
  val athleteSave: string = "athlete.save"

  [<Literal>]
  val athleteCancel: string = "athlete.cancel"

  [<Literal>]
  val genderMale: string = "gender.male"

  [<Literal>]
  val genderFemale: string = "gender.female"

  [<Literal>]
  val goalHypertrophy: string = "goal.hypertrophy"

  [<Literal>]
  val goalWeightLoss: string = "goal.weightloss"

  [<Literal>]
  val goalCardio: string = "goal.cardio"

  [<Literal>]
  val goalActivation: string = "goal.activation"

  [<Literal>]
  val goalStrength: string = "goal.strength"

  [<Literal>]
  val memoriesTitle: string = "memories.title"

  [<Literal>]
  val memoriesEmpty: string = "memories.empty"

  [<Literal>]
  val memoriesConfirm: string = "memories.confirm"

  [<Literal>]
  val memoriesDiscard: string = "memories.discard"

  [<Literal>]
  val memoriesAddMark: string = "memories.addMark"

  [<Literal>]
  val memoriesConsolidateAgent: string = "memories.consolidateAgent"

  [<Literal>]
  val memoryScopePlan: string = "memory.scope.plan"

  [<Literal>]
  val memoryScopeAthlete: string = "memory.scope.athlete"

  [<Literal>]
  val markTarget: string = "mark.target"

  [<Literal>]
  val markText: string = "mark.text"

  [<Literal>]
  val exemptionText: string = "exemption.text"

  [<Literal>]
  val labelLight: string = "label.light"

  [<Literal>]
  val labelHeavy: string = "label.heavy"

  [<Literal>]
  val labelPain: string = "label.pain"

  [<Literal>]
  val labelNoEquipment: string = "label.noequipment"

  [<Literal>]
  val plansTitle: string = "plans.title"

  [<Literal>]
  val plansGenerate: string = "plans.generate"

  [<Literal>]
  val plansReview: string = "plans.review"

  [<Literal>]
  val plansOpen: string = "plans.open"

  [<Literal>]
  val wizardTitle: string = "wizard.title"

  [<Literal>]
  val wizardStepMemories: string = "wizard.step.memories"

  [<Literal>]
  val wizardStepBrief: string = "wizard.step.brief"

  [<Literal>]
  val wizardWeeks: string = "wizard.weeks"

  [<Literal>]
  val wizardBack: string = "wizard.back"

  [<Literal>]
  val wizardGoal: string = "wizard.goal"

  [<Literal>]
  val wizardFrequency: string = "wizard.frequency"

  [<Literal>]
  val wizardEmphasis: string = "wizard.emphasis"

  [<Literal>]
  val freq2: string = "freq.2"

  [<Literal>]
  val freq3: string = "freq.3"

  [<Literal>]
  val freq4: string = "freq.4"

  [<Literal>]
  val freq5: string = "freq.5"

  [<Literal>]
  val freq6: string = "freq.6"

  [<Literal>]
  val emphasisArms: string = "emphasis.arms"

  [<Literal>]
  val emphasisShoulders: string = "emphasis.shoulders"

  [<Literal>]
  val emphasisChest: string = "emphasis.chest"

  [<Literal>]
  val emphasisBack: string = "emphasis.back"

  [<Literal>]
  val emphasisLegs: string = "emphasis.legs"

  [<Literal>]
  val emphasisGlutes: string = "emphasis.glutes"

  [<Literal>]
  val emphasisCore: string = "emphasis.core"

  [<Literal>]
  val emphasisFullBody: string = "emphasis.fullBody"

  [<Literal>]
  val emphasisSquat: string = "emphasis.squat"

  [<Literal>]
  val emphasisDeadlift: string = "emphasis.deadlift"

  [<Literal>]
  val emphasisBenchPress: string = "emphasis.benchPress"

  [<Literal>]
  val emphasisOverheadPress: string = "emphasis.overheadPress"

  [<Literal>]
  val emphasisOlympicLifts: string = "emphasis.olympicLifts"

  [<Literal>]
  val emphasisPush: string = "emphasis.push"

  [<Literal>]
  val emphasisPull: string = "emphasis.pull"

  [<Literal>]
  val emphasisFullBodyCircuit: string = "emphasis.fullBodyCircuit"

  [<Literal>]
  val emphasisHiit: string = "emphasis.hiit"

  [<Literal>]
  val emphasisStrengthCardio: string = "emphasis.strengthCardio"

  [<Literal>]
  val emphasisContinuousZone2: string = "emphasis.continuousZone2"

  [<Literal>]
  val emphasisIntervalHiit: string = "emphasis.intervalHiit"

  [<Literal>]
  val emphasisMixed: string = "emphasis.mixed"

  [<Literal>]
  val emphasisMobility: string = "emphasis.mobility"

  [<Literal>]
  val emphasisStability: string = "emphasis.stability"

  [<Literal>]
  val emphasisMovementPatterns: string = "emphasis.movementPatterns"

  [<Literal>]
  val wizardGenerating: string = "wizard.generating"

  [<Literal>]
  val wizardGenerate: string = "wizard.generate"

  [<Literal>]
  val wizardNext: string = "wizard.next"

  [<Literal>]
  val wizardConfirm: string = "wizard.confirm"

  [<Literal>]
  val wizardDiscard: string = "wizard.discard"

  [<Literal>]
  val wizardDiscardTitle: string = "wizard.discardTitle"

  [<Literal>]
  val wizardDiscardBody: string = "wizard.discardBody"

  [<Literal>]
  val wizardRetry: string = "wizard.retry"

  [<Literal>]
  val wizardExit: string = "wizard.exit"

  [<Literal>]
  val wizardCancelGeneration: string = "wizard.cancelGeneration"

  [<Literal>]
  val reviewTitle: string = "review.title"

  [<Literal>]
  val reviewSave: string = "review.save"

  [<Literal>]
  val reviewExport: string = "review.export"

  [<Literal>]
  val reviewSaved: string = "review.saved"

  [<Literal>]
  val reviewExported: string = "review.exported"

  [<Literal>]
  val reviewInvalid: string = "review.invalid"

  [<Literal>]
  val reviewNoteTitle: string = "review.noteTitle"

  [<Literal>]
  val reviewNotePlaceholder: string = "review.notePlaceholder"

  [<Literal>]
  val reviewNoteInterpret: string = "review.noteInterpret"

  [<Literal>]
  val reviewNoteSurface: string = "review.noteSurface"

  [<Literal>]
  val reviewNoteRejected: string = "review.noteRejected"

  [<Literal>]
  val reviewNoteFailed: string = "review.noteFailed"

  [<Literal>]
  val reviewNoteFull: string = "review.noteFull"

  [<Literal>]
  val planGuide: string = "plan.guide"

  [<Literal>]
  val planLaps: string = "plan.laps"

  [<Literal>]
  val planWeek: string = "plan.week"

  [<Literal>]
  val planReps: string = "plan.reps"

  [<Literal>]
  val planRir: string = "plan.rir"

  [<Literal>]
  val planRest: string = "plan.rest"

  [<Literal>]
  val planNotes: string = "plan.notes"

  [<Literal>]
  val planEdit: string = "plan.edit"

  [<Literal>]
  val planDone: string = "plan.done"

  [<Literal>]
  val planRevert: string = "plan.revert"

  [<Literal>]
  val settingsMissing: string = "settings.missing"

  [<Literal>]
  val settingsLanguage: string = "settings.language"

  [<Literal>]
  val settingsLangEs: string = "settings.lang.es"

  [<Literal>]
  val settingsLangEn: string = "settings.lang.en"

  [<Literal>]
  val settingsTheme: string = "settings.theme"

  [<Literal>]
  val settingsThemeSystem: string = "settings.theme.system"

  [<Literal>]
  val settingsThemeLight: string = "settings.theme.light"

  [<Literal>]
  val settingsThemeDark: string = "settings.theme.dark"

  [<Literal>]
  val settingsProvider: string = "settings.provider"

  [<Literal>]
  val settingsConnected: string = "settings.connected"

  [<Literal>]
  val settingsConnect: string = "settings.connect"

  [<Literal>]
  val settingsMissingToken: string = "settings.missingToken"

  [<Literal>]
  val loginTitle: string = "login.title"

  [<Literal>]
  val loginToken: string = "login.token"

  [<Literal>]
  val loginSave: string = "login.save"

  [<Literal>]
  val loginSaved: string = "login.saved"

  [<Literal>]
  val loginInsecureTitle: string = "login.insecureTitle"

  [<Literal>]
  val loginInsecureBody: string = "login.insecureBody"

  [<Literal>]
  val loginFailed: string = "login.failed"

  [<Literal>]
  val loginContinue: string = "login.continue"

  [<Literal>]
  val errorTitle: string = "error.title"

  // --- wizard violations: typed cases to one localized line each ---

  [<Literal>]
  val violationGenderMismatch: string = "violation.genderMismatch"

  [<Literal>]
  val violationWeeks: string = "violation.weeks"

  [<Literal>]
  val violationVariants: string = "violation.variants"

  [<Literal>]
  val violationVariantWeeks: string = "violation.variantWeeks"

  [<Literal>]
  val violationFrequency: string = "violation.frequency"

  [<Literal>]
  val violationRir: string = "violation.rir"

  [<Literal>]
  val violationMissingRir: string = "violation.missingRir"

  [<Literal>]
  val violationCount: string = "violation.count"

  [<Literal>]
  val violationFormatWarning: string = "violation.formatWarning"

  [<Literal>]
  val dataUnreadableJson: string = "data.unreadableJson"

  [<Literal>]
  val dataBadGender: string = "data.badGender"

  [<Literal>]
  val dataMissingTitle: string = "data.missingTitle"

  [<Literal>]
  val dataBadWeeks: string = "data.badWeeks"

  [<Literal>]
  val dataEmptyVariantId: string = "data.emptyVariantId"

  [<Literal>]
  val dataMissingWeekNumber: string = "data.missingWeekNumber"

  [<Literal>]
  val dataMissingOrder: string = "data.missingOrder"

  [<Literal>]
  val dataUnknownDay: string = "data.unknownDay"

  [<Literal>]
  val dataEmptyExerciseName: string = "data.emptyExerciseName"

  [<Literal>]
  val advisoryNotPureRange: string = "advisory.notPureRange"

  [<Literal>]
  val advisoryUnknownUnit: string = "advisory.unknownUnit"

  [<Literal>]
  val advisoryMultipleExercises: string = "advisory.multipleExercises"

  [<Literal>]
  val advisoryRepsWithoutEffort: string = "advisory.repsWithoutEffort"

  [<Literal>]
  val advisoryTimedWithRir: string = "advisory.timedWithRir"

  [<Literal>]
  val reviewUnsaved: string = "review.unsaved"

  [<Literal>]
  val wizardApplies: string = "wizard.applies"

  [<Literal>]
  val wizardMode: string = "wizard.mode"

  [<Literal>]
  val wizardModeAdjust: string = "wizard.mode.adjust"

  [<Literal>]
  val wizardModeNew: string = "wizard.mode.new"

  [<Literal>]
  val wizardStepOf: string = "wizard.stepOf"

  [<Literal>]
  val guardAthleteTitle: string = "guard.athlete.title"

  [<Literal>]
  val guardAthleteBody: string = "guard.athlete.body"

  [<Literal>]
  val guardGenerationTitle: string = "guard.generation.title"

  [<Literal>]
  val guardGenerationBody: string = "guard.generation.body"

  [<Literal>]
  val guardPlanTitle: string = "guard.plan.title"

  [<Literal>]
  val guardPlanBody: string = "guard.plan.body"

  [<Literal>]
  val guardUnsavedTitle: string = "guard.unsaved.title"

  [<Literal>]
  val guardUnsavedBody: string = "guard.unsaved.body"

  [<Literal>]
  val guardLeave: string = "guard.leave"

/// One user-facing failure line: the action that failed and the cause,
/// clipped to a readable length.
val friendly: culture: CultureInfo * action: string * reason: string -> string
