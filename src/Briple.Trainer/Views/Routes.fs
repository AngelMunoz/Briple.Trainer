module Briple.Trainer.Views.Routes

open System
open System.Threading

open Avalonia.Controls
open Microsoft.Extensions.Logging
open Navs
open Navs.Avalonia

open IcedTasks

open Serilog

open Briple.Trainer.Components
open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.NewAthleteForm
open Briple.Trainer.Components.Page
open Briple.Trainer.Core.Memories
open Briple.Trainer.Core.Requests
open Briple.Trainer.Env
open Briple.Trainer.Navigation
open Briple.Trainer.Persistence
open Briple.Trainer.Stores
open Briple.Trainer.Strings
open Briple.Trainer.Views

type RouteDeps<'T
  when 'T :> IProvideStore
  and 'T :> IProvideStores
  and 'T :> IProvideCulture
  and 'T :> IProvideLanguage
  and 'T :> IProvideTheme
  and 'T :> IProvideHost
  and 'T :> IProvideAgent
  and 'T :> IProvideCredentials
  and 'T :> IProvideLogging> = 'T

let inline go(nav: INavigable, url: string) : unit =
  asyncEx {
    match! nav.Navigate url CancellationToken.None with
    | Ok _ -> ()
    | Error e -> Log.Error("navigation to {Url} failed: {Error}", url, string e)
  }
  |> Async.StartImmediate

let inline back(nav: INavigable) : unit =
  asyncEx {
    match! nav.Back () CancellationToken.None with
    | Ok _ -> ()
    | Error e -> Log.Error("back navigation failed: {Error}", string e)
  }
  |> Async.StartImmediate

let inline notFound() : Control = TextBlock(Text = "not found")

let inline orEmpty(what: string) : Result<'a list, exn> -> 'a list =
  function
  | Ok xs -> xs
  | Error e ->
    Log.Error(e, "{Que} failed", what)
    []

[<return: Struct>]
let inline (|GuidParam|_|) (name: string) (ctx: RouteContext) =
  ctx.GetParam<Guid>(name)

/// The live culture a page reads.
let inline cultureOf(env: #IProvideCulture) : PageCulture = {
  Current = fun () -> env.Culture
  Changed = env.CultureChanged
}

let athletesPage
  (
    env: #IProvideStores & #IProvideCulture,
    lead: unit -> Control,
    nav: INavigable
  ) : Control =
  AthleteList(
    {
      Culture = cultureOf env
      Lead = ValueSome lead
      Athletes = env.Stores.Athletes.Observe()
    },
    fun event ->
      match event with
      | AthleteList.NewRequested -> go(nav, "/athletes/new")
      | AthleteList.OpenRequested id -> go(nav, $"/athletes/{id}")
  )

let newAthletePage
  (
    env: #IProvideStores & #IProvideCulture,
    lead: unit -> Control,
    gate: PageGate,
    nav: INavigable
  ) : Control =
  NewAthleteForm(
    {
      Culture = cultureOf env
      Lead = ValueSome lead
      Gate = gate
    },
    fun event ->
      match event with
      | NewAthleteForm.Saved athlete ->
        asyncEx {
          match! env.Stores.Athletes.Add athlete with
          | Ok _ -> go(nav, "/")
          | Error e ->
            Log.Error(e, "athlete save failed")

            let action = get(env.Culture, Keys.athleteSave)

            Briple.Trainer.Toasts.show(
              action,
              friendly(env.Culture, action, e.Message),
              true
            )
        }
        |> Async.StartImmediate
      | NewAthleteForm.Cancelled -> go(nav, "/")
  )

let detailPage
  (
    env: #IProvideStore & #IProvideStores & #IProvideCulture,
    lead: unit -> Control,
    athleteId: Guid,
    nav: INavigable
  ) : Async<Control> = asyncEx {
  let! athlete = env.Store.FindAthlete athleteId

  match athlete with
  | Error e ->
    Log.Error(e, "athlete load failed")
    return TextBlock(Text = string e.Message)
  | Ok ValueNone -> return notFound()
  | Ok(ValueSome athlete) ->
    return
      Athletes.AthleteDetailPage(
        {
          Culture = cultureOf env
          Lead = ValueSome lead
          Plans = env.Stores.Plans
          Athlete = athlete
        },
        fun event ->
          match event with
          | Athletes.Event.GenerateRequested ->
            go(nav, $"/athletes/{athleteId}/generate")
          | Athletes.Event.ReviewRequested planId ->
            go(nav, $"/athletes/{athleteId}/review/{planId}")
      )
}

let wizardPage
  (
    env: #IProvideStore & #IProvideStores & #IProvideCulture & #IProvideAgent,
    lead: unit -> Control,
    gate: PageGate,
    athleteId: Guid,
    nav: INavigable
  ) : Async<Control> = asyncEx {
  let! athlete = env.Store.FindAthlete athleteId
  let! memories = env.Store.ListMemories athleteId
  let! plans = env.Store.ListPlans athleteId

  let entities = memories |> orEmpty "memory list"

  let proposals = entities |> List.filter(fun e -> e.Memory.Status = Proposed)

  let previous =
    plans |> orEmpty "plan list" |> List.tryLast |> ValueOption.ofOption

  let confirmed =
    relevantMemories(
      entities |> List.map(fun entity -> entity.Memory, entity.PlanId),
      previous |> ValueOption.map _.Id
    )

  match athlete with
  | Ok(ValueSome athlete) ->
    return
      Generate.WizardPage(
        {
          Culture = cultureOf env
          Lead = ValueSome lead
          Store = env.Store
          Memories = env.Stores.Memories
          Plans = env.Stores.Plans
          Agent = env
          Athlete = athlete
          Confirmed = confirmed
          Proposals = proposals
          Previous = previous
          Gate = gate
        },
        fun outcome ->
          match outcome with
          | Generate.PlanFinished planId ->
            go(nav, $"/athletes/{athleteId}/review/{planId}")
          | Generate.WizardCancelled -> go(nav, $"/athletes/{athleteId}")
      )
  | _ -> return notFound()
}

let reviewPage
  (
    env: #IProvideStores & #IProvideCulture & #IProvideHost & #IProvideAgent,
    lead: unit -> Control,
    gate: PageGate,
    planId: Guid
  ) : Async<Control> = asyncEx {
  let! plan = env.Stores.Plans.Find planId

  match plan with
  | Ok(ValueSome entity) ->
    return
      Review.ReviewPage {
        Culture = cultureOf env
        Lead = ValueSome lead
        Plans = env.Stores.Plans
        Memories = env.Stores.Memories
        Agent = env
        Host = env.Host
        Entity = entity
        Gate = gate
      }
  | _ -> return notFound()
}

let loginPage
  (
    env: #IProvideCulture & #IProvideCredentials,
    lead: unit -> Control,
    nav: INavigable
  ) : Control =
  Login.LoginPage {
    Culture = cultureOf env
    Lead = ValueSome lead
    Save = env.SaveCredentials
    OnDone = fun () -> go(nav, "/")
  }

let settingsPage
  (
    env: #IProvideCulture & #IProvideLanguage & #IProvideTheme & #IProvideAgent,
    lead: unit -> Control,
    nav: INavigable
  ) : Control =
  Settings.SettingsPage {
    Culture = cultureOf env
    Lead = ValueSome lead
    Language = fun () -> env.Language
    Theme = fun () -> env.Theme
    AgentAvailable = fun () -> env.AgentAvailable
    OnLanguage = env.SetLanguage
    OnTheme =
      fun theme ->
        env.SetTheme theme
        applyTheme theme
    OpenLogin = fun () -> go(nav, "/login")
  }

/// The agent gate, shared by every data route.
let requiresAgent(env: #IProvideAgent) : GuardResult =
  if env.AgentAvailable then
    GuardResult.Continue
  else
    GuardResult.Redirect "/login"

/// The URL family that decides the leave dialog.
type PageKind =
  | FormPage
  | WizardPage
  | ReviewPage
  | PlainPage

let kindOf(url: string) : PageKind =
  if url = "/athletes/new" then
    FormPage
  elif url.Contains("/generate", StringComparison.Ordinal) then
    WizardPage
  elif url.Contains("/review/", StringComparison.Ordinal) then
    ReviewPage
  else
    PlainPage

[<Struct>]
type ShellRoutes = {
  Router: IRouter<Control>
  AskLeave: string -> Async<bool>
}

let build(env: RouteDeps<'T>, leadOf: INavigable -> (unit -> Control)) =
  let inline gate r =
    r |> Route.canActivate(fun _ _ -> requiresAgent env)

  // the leave decision, all of it in one place: the page kind and the
  // phase decide the dialog, and a confirmed leave stops a running job
  let decide(url: string) : PageGate -> PagePhase -> Async<bool> =
    let kind = kindOf url

    // the dialog follows the language at the moment it opens
    let ask titleKey bodyKey =
      let culture = env.Culture
      ValueSome(get(culture, titleKey), get(culture, bodyKey))

    let dialogOf(phase: PagePhase) : (string * string) voption =
      match kind, phase with
      | FormPage, Ready
      | WizardPage, Ready
      | ReviewPage, Ready
      | PlainPage, _ -> ValueNone
      | FormPage, _ -> ask Keys.guardAthleteTitle Keys.guardAthleteBody
      | WizardPage, Working ->
        ask Keys.guardGenerationTitle Keys.guardGenerationBody
      | WizardPage, UnsavedChanges -> ask Keys.guardPlanTitle Keys.guardPlanBody
      | ReviewPage, _ -> ask Keys.guardUnsavedTitle Keys.guardUnsavedBody

    fun exitGate phase -> async {
      match dialogOf phase with
      | ValueNone -> return true
      | ValueSome(title, body) ->
        let culture = env.Culture

        let! sure =
          Briple.Trainer.Dialogs.confirmAsync {
            Title = title
            Body = body
            Accept = get(culture, Keys.guardLeave)
            Cancel = get(culture, Keys.commonCancel)
          }

        if not sure then
          return false
        else
          // a confirmed leave stops a running generation or a
          // running note reading
          match kind, phase with
          | WizardPage, Working -> exitGate.Push PageCommand.CancelRun
          | ReviewPage, Working -> exitGate.Push PageCommand.CancelRun
          | _ -> ()

          return true
    }

  let gates = PageGates decide

  // every activation marks the gate, cached or not; pages reconcile
  // their state from that signal
  let inline shown r =
    r
    |> Route.canActivate(fun _ next ->
      gates.Gate(next.Path).MarkShown()
      GuardResult.Continue)

  let inline confirmLeave r =
    r
    |> Route.canDeactivateAsync(fun from _ -> async {
      match from with
      | ValueSome active ->
        let! allowed = gates.Gate(active.Path).AskLeave()

        return if allowed then GuardResult.Continue else GuardResult.Stop
      | ValueNone -> return GuardResult.Continue
    })

  let table: RouteDefinition<Control> list = [
    Route.define<Control>(
      "athletes",
      "/",
      fun _ nav -> athletesPage(env, leadOf nav, nav)
    )
    |> gate
    |> shown

    Route.define<Control>(
      "login",
      "/login",
      fun _ nav -> loginPage(env, leadOf nav, nav)
    )
    |> Route.cache CachePolicy.NoCache

    Route.define<Control>(
      "new-athlete",
      "/athletes/new",
      fun ctx nav -> newAthletePage(env, leadOf nav, gates.Gate(ctx.Path), nav)
    )
    |> Route.cache CachePolicy.NoCache
    |> confirmLeave
    |> gate
    |> shown

    Route.define<Control>(
      "athlete",
      "/athletes/:athleteId<guid>",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "athleteId" athleteId ->
          return! detailPage(env, leadOf nav, athleteId, nav)
        | _ -> return notFound()
      }
    )
    |> gate
    |> shown

    Route.define<Control>(
      "generate",
      "/athletes/:athleteId<guid>/generate",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "athleteId" athleteId ->
          return!
            wizardPage(
              env,
              leadOf nav,
              gates.Gate(context.Path),
              athleteId,
              nav
            )
        | _ -> return notFound()
      }
    )
    // a wizard is one visit: the page resets on entry anyway, the
    // strategy only keeps the router from holding the old run
    |> Route.cache CachePolicy.NoCache
    |> confirmLeave
    |> gate
    |> shown

    Route.define<Control>(
      "review",
      "/athletes/:athleteId<guid>/review/:planId<guid>",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "planId" planId ->
          return! reviewPage(env, leadOf nav, gates.Gate(context.Path), planId)
        | _ -> return notFound()
      }
    )
    // a fresh review on every visit: the stored Raw is the baseline and
    // a confirmed leave drops the draft, so no cached draft can come back
    |> Route.cache CachePolicy.NoCache
    |> confirmLeave
    |> gate
    |> shown

    Route.define<Control>(
      "settings",
      "/settings",
      fun _ nav -> settingsPage(env, leadOf nav, nav)
    )
    // the provider status and the preferences are read live; a fresh
    // page on every visit keeps them honest
    |> Route.cache CachePolicy.NoCache
    |> shown
  ]

  let router =
    AvaloniaRouter(table, logger = env.LoggerFactory.CreateLogger "Navs.Router")

  go(router, "/")

  {
    Router = router
    AskLeave = fun url -> gates.Gate(url).AskLeave()
  }
