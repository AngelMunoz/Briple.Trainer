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

/// The text of a key under a culture; a missing key renders as the key
/// itself so an untranslated entry is visible instead of silent.
val get: culture: CultureInfo * key: string -> string

/// A formatted entry; `String.Format` over the culture's text.
val getf: culture: CultureInfo * key: string * args: obj list -> string

module Keys =
  [<Literal>]
  val appTitle: string = "app.title"

  [<Literal>]
  val navAthletes: string = "nav.athletes"

  [<Literal>]
  val navLanguage: string = "nav.language"

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
  val athleteVariants: string = "athlete.variants"

  [<Literal>]
  val athleteGoals: string = "athlete.goals"

  [<Literal>]
  val athleteFocus: string = "athlete.focus"

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
  val wizardFocus: string = "wizard.focus"

  [<Literal>]
  val wizardRir: string = "wizard.rir"

  [<Literal>]
  val wizardCounts: string = "wizard.counts"

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
  val settingsMissing: string = "settings.missing"

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
  val errorTitle: string = "error.title"
