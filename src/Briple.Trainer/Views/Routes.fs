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
  and 'T :> IProvideLogging
  and 'T :> IProvideDialogs
  and 'T :> IProvideToasts
  and 'T :> IProvideLocalization> = 'T

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

/// The route-owned page data: the exit gate, the disposable bag and the
/// navigable the page reports to.
[<Struct>]
type RoutePage = {
  Gate: PageGate
  Lifetime: Navs.IDisposableBag
  Nav: INavigable
}

[<Struct>]
type WizardRoute = { Page: RoutePage; AthleteId: Guid }

[<Struct>]
type ReviewRoute = { Page: RoutePage; PlanId: Guid }

[<Struct>]
type PlainRoute = {
  Lifetime: Navs.IDisposableBag
  Nav: INavigable
}

[<Struct>]
type DetailRoute = {
  Lifetime: Navs.IDisposableBag
  Nav: INavigable
  AthleteId: Guid
}

let athletesPage
  (
    env: #IProvideStores & #IProvideCulture & #IProvideDialogs & #IProvideToasts,
    route: PlainRoute,
    lead: unit -> Control
  ) : Control =
  let pageEnv: AthleteList.AthleteListEnv = {
    Observe = env.Stores.Athletes.Observe
    ObserveArchived = env.Stores.Athletes.ObserveArchived
    Snapshot = env.Stores.Athletes.Snapshot
    SnapshotArchived = env.Stores.Athletes.SnapshotArchived
    Archive = env.Stores.Athletes.Archive
    Restore = env.Stores.Athletes.Restore
    Delete = env.Stores.Athletes.Delete
    Dialogs = env.Dialogs
    Toasts = env.Toasts
    Culture = env.Culture
    CultureChanged = env.CultureChanged
  }

  AthleteList(
    pageEnv,
    {
      Lead = ValueSome lead
      Lifetime = route.Lifetime
    },
    fun event ->
      match event with
      | AthleteList.NewRequested -> go(route.Nav, "/athletes/new")
      | AthleteList.OpenRequested id -> go(route.Nav, $"/athletes/{id}")
  )

let newAthletePage
  (
    env: #IProvideStores & #IProvideCulture,
    page: RoutePage,
    lead: unit -> Control
  ) : Control =
  NewAthleteForm(
    {
      Lead = ValueSome lead
      Gate = page.Gate
      Lifetime = page.Lifetime
    },
    fun event ->
      match event with
      | NewAthleteForm.Saved athlete ->
        asyncEx {
          match! env.Stores.Athletes.Add athlete with
          | Ok _ -> go(page.Nav, "/")
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
      | NewAthleteForm.Cancelled -> go(page.Nav, "/")
  )

let detailPage
  (
    env:
      #IProvideStore & #IProvideStores & #IProvideCulture & #IProvideDialogs & #IProvideToasts,
    route: DetailRoute,
    lead: unit -> Control
  ) : Async<Control> = asyncEx {
  let athleteId = route.AthleteId
  let! athlete = env.Store.FindAthlete athleteId

  match athlete with
  | Error e ->
    Log.Error(e, "athlete load failed")
    return TextBlock(Text = string e.Message)
  | Ok ValueNone -> return notFound()
  | Ok(ValueSome athlete) ->
    let pageEnv: Athletes.AthleteDetailEnv = {
      Plans = env.Stores.Plans
      Archive = env.Stores.Athletes.Archive
      Restore = env.Stores.Athletes.Restore
      Delete = env.Stores.Athletes.Delete
      Dialogs = env.Dialogs
      Toasts = env.Toasts
      Culture = env.Culture
      CultureChanged = env.CultureChanged
    }

    return
      Athletes.AthleteDetailPage(
        pageEnv,
        {
          Lead = ValueSome lead
          Athlete = athlete
          Lifetime = route.Lifetime
        },
        fun event ->
          match event with
          | Athletes.Event.GenerateRequested ->
            go(route.Nav, $"/athletes/{athleteId}/generate")
          | Athletes.Event.ReviewRequested planId ->
            go(route.Nav, $"/athletes/{athleteId}/review/{planId}")
          | Athletes.Event.BackRequested -> go(route.Nav, "/")
      )
}

let wizardPage
  (
    env:
      #IProvideStore & #IProvideStores & #IProvideCulture & #IProvideAgent & #IProvideLogging & #IProvideDialogs & #IProvideToasts,
    route: WizardRoute,
    lead: unit -> Control
  ) : Async<Control> = asyncEx {
  let athleteId = route.AthleteId
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
    let pageEnv: Generate.GenerateEnv = {
      Store = env.Store
      Memories = env.Stores.Memories
      Plans = env.Stores.Plans
      Agent = env
      Logger = env.LoggerFactory.CreateLogger "Generate"
      Dialogs = env.Dialogs
      Toasts = env.Toasts
      Culture = env.Culture
      CultureChanged = env.CultureChanged
    }

    return
      Generate.WizardPage(
        pageEnv,
        {
          Lead = ValueSome lead
          Athlete = athlete
          Confirmed = confirmed
          Proposals = proposals
          Previous = previous
          Gate = route.Page.Gate
          Lifetime = route.Page.Lifetime
        },
        fun outcome ->
          match outcome with
          | Generate.PlanFinished planId ->
            go(route.Page.Nav, $"/athletes/{athleteId}/review/{planId}")
          | Generate.WizardCancelled ->
            go(route.Page.Nav, $"/athletes/{athleteId}")
      )
  | _ -> return notFound()
}

let reviewPage
  (
    env:
      #IProvideStores & #IProvideCulture & #IProvideHost & #IProvideAgent & #IProvideLogging & #IProvideToasts,
    route: ReviewRoute,
    lead: unit -> Control
  ) : Async<Control> = asyncEx {
  let planId = route.PlanId
  let! plan = env.Stores.Plans.Find planId

  match plan with
  | Ok(ValueSome entity) ->
    let pageEnv: Review.ReviewEnv = {
      Plans = env.Stores.Plans
      Memories = env.Stores.Memories
      Agent = env
      Host = env.Host
      Logger = env.LoggerFactory.CreateLogger "Review"
      Toasts = env.Toasts
      Culture = env.Culture
      CultureChanged = env.CultureChanged
    }

    return
      Review.ReviewPage(
        pageEnv,
        {
          Lead = ValueSome lead
          Entity = entity
          Gate = route.Page.Gate
          Lifetime = route.Page.Lifetime
        }
      )
  | _ -> return notFound()
}

let loginPage
  (
    env: #IProvideCulture & #IProvideCredentials,
    route: PlainRoute,
    lead: unit -> Control
  ) : Control =
  Login.LoginPage(
    env,
    {
      Lead = ValueSome lead
      OnDone = fun () -> go(route.Nav, "/")
      Lifetime = route.Lifetime
    }
  )

let settingsPage
  (
    env: #IProvideLanguage & #IProvideTheme & #IProvideAgent,
    route: PlainRoute,
    lead: unit -> Control
  ) : Control =
  let pageEnv: Settings.SettingsEnv = {
    Language = env.Language
    Theme = env.Theme
    AgentAvailable = env.AgentAvailable
  }

  Settings.SettingsPage(
    pageEnv,
    {
      Lead = ValueSome lead
      OnLanguage = env.SetLanguage
      OnTheme =
        fun theme ->
          env.SetTheme theme
          applyTheme theme
      OpenLogin = fun () -> go(route.Nav, "/login")
      Lifetime = route.Lifetime
    }
  )

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
            Danger = true
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
      fun context nav ->
        athletesPage(
          env,
          {
            Lifetime = context.Disposables
            Nav = nav
          },
          leadOf nav
        )
    )
    // the page program stops with the route lifetime; a cached control
    // would come back disposed, so every visit builds a fresh page
    |> Route.cache CachePolicy.NoCache
    |> gate
    |> shown

    Route.define<Control>(
      "login",
      "/login",
      fun context nav ->
        loginPage(
          env,
          {
            Lifetime = context.Disposables
            Nav = nav
          },
          leadOf nav
        )
    )
    |> Route.cache CachePolicy.NoCache

    Route.define<Control>(
      "new-athlete",
      "/athletes/new",
      fun ctx nav ->
        newAthletePage(
          env,
          {
            Gate = gates.Gate(ctx.Path)
            Lifetime = ctx.Disposables
            Nav = nav
          },
          leadOf nav
        )
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
          let route = {
            Lifetime = context.Disposables
            Nav = nav
            AthleteId = athleteId
          }

          return! detailPage(env, route, leadOf nav)
        | _ -> return notFound()
      }
    )
    // the page program stops with the route lifetime; a cached control
    // would come back disposed, so every visit builds a fresh page
    |> Route.cache CachePolicy.NoCache
    |> gate
    |> shown

    Route.define<Control>(
      "generate",
      "/athletes/:athleteId<guid>/generate",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "athleteId" athleteId ->
          let page = {
            Gate = gates.Gate(context.Path)
            Lifetime = context.Disposables
            Nav = nav
          }

          return!
            wizardPage(env, { Page = page; AthleteId = athleteId }, leadOf nav)
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
          let page = {
            Gate = gates.Gate(context.Path)
            Lifetime = context.Disposables
            Nav = nav
          }

          return! reviewPage(env, { Page = page; PlanId = planId }, leadOf nav)
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
      fun context nav ->
        settingsPage(
          env,
          {
            Lifetime = context.Disposables
            Nav = nav
          },
          leadOf nav
        )
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
