module Briple.Trainer.Views.Routes

open System
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

let inline go(nav: INavigable<Control>, url: string) : unit =
  asyncEx {
    match! nav.Navigate url with
    | Ok() -> ()
    | Error e -> Log.Error("navigation to {Url} failed: {Error}", url, string e)
  }
  |> Async.StartImmediate

let inline notFound() : Control = TextBlock(Text = "not found")

/// Ok keeps the values; an Error logs and reads as empty, so a failed
/// load degrades to an empty page instead of crashing the navigation.
let inline orEmpty(what: string) : Result<'a list, exn> -> 'a list =
  function
  | Ok xs -> xs
  | Error e ->
    Log.Error(e, "{Que} failed", what)
    []

/// A guid route parameter as an active pattern: `match ctx with GuidParam
/// athleteId "athleteId" -> ...`.
[<return: Struct>]
let inline (|GuidParam|_|) (name: string) (ctx: RouteContext) =
  ctx.getParam<Guid> name

/// The athletes page: the component over the live list; no data loads
/// here, the store fills it.
let athletesPage
  (env: #IProvideStores & #IProvideCulture, nav: INavigable<Control>)
  : Control =
  AthleteList(
    {
      Culture = env.Culture
      Athletes = env.Stores.Athletes.Observe()
    },
    fun event ->
      match event with
      | AthleteList.NewRequested -> go(nav, "/athletes/new")
      | AthleteList.OpenRequested id -> go(nav, $"/athletes/{id}")
  )

let newAthletePage
  (env: #IProvideStores & #IProvideCulture, nav: INavigable<Control>)
  : Control =
  NewAthleteForm(
    { Culture = env.Culture },
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
    athleteId: Guid,
    nav: INavigable<Control>
  ) : Async<Control> = asyncEx {
  let! athlete = env.Store.FindAthlete athleteId

  match athlete with
  | Error e ->
    Log.Error(e, "athlete load failed")
    return TextBlock(Text = string e.Message)
  | Ok ValueNone -> return notFound()
  | Ok(ValueSome athlete) ->
    // the page subscribes to the plan store; the emissions fill it
    return
      Athletes.AthleteDetailPage(
        {
          Culture = env.Culture
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
    athleteId: Guid,
    nav: INavigable<Control>
  ) : Async<Control> = asyncEx {
  // point-in-time snapshots: the wizard works over what exists now
  let! athlete = env.Store.FindAthlete athleteId
  let! memories = env.Store.ListMemories athleteId
  let! plans = env.Store.ListPlans athleteId

  let entities = memories |> orEmpty "memory list"

  let proposals = entities |> List.filter(fun e -> e.Memory.Status = Proposed)

  let previous =
    plans |> orEmpty "plan list" |> List.tryLast |> ValueOption.ofOption

  // what this generation carries: the athlete-scoped confirmations plus
  // the plan-scoped ones of the plan being adjusted
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
          Culture = env.Culture
          Store = env.Store
          Memories = env.Stores.Memories
          Plans = env.Stores.Plans
          Agent = env
          Athlete = athlete
          Confirmed = confirmed
          Proposals = proposals
          Previous = previous
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
    planId: Guid
  ) : Async<Control> = asyncEx {
  let! plan = env.Stores.Plans.Find planId

  match plan with
  | Ok(ValueSome entity) ->
    return
      Review.ReviewPage {
        Culture = env.Culture
        Plans = env.Stores.Plans
        Memories = env.Stores.Memories
        Agent = env
        Host = env.Host
        Entity = entity
      }
  | _ -> return notFound()
}

let loginPage
  (env: #IProvideCulture & #IProvideCredentials, nav: INavigable<Control>)
  : Control =
  Login.LoginPage {
    Culture = env.Culture
    Save = env.SaveCredentials
    OnDone = fun () -> go(nav, "/")
  }

let settingsPage
  (
    env: #IProvideCulture & #IProvideLanguage & #IProvideTheme & #IProvideAgent,
    nav: INavigable<Control>
  ) : Control =
  Settings.SettingsPage {
    Culture = env.Culture
    Language = env.Language
    Theme = env.Theme
    AgentAvailable = env.AgentAvailable
    OnLanguage = env.SetLanguage
    OnTheme =
      fun theme ->
        env.SetTheme theme
        applyTheme theme
    OpenLogin = fun () -> go(nav, "/login")
  }

/// The agent gate, shared by every data route: the app's data sits
/// behind the provider token, and without one the navigation sends the
/// user to connect it. Only the login and the settings stay open. The
/// routes carry no cache, so the guard runs on every activation.
let requiresAgent(env: #IProvideAgent) : GuardResponse =
  if env.AgentAvailable then
    GuardResponse.Continue
  else
    GuardResponse.Redirect "/login"

let build(env: RouteDeps<'T>) : IRouter<Control> =
  // the boot fires at the athletes list; the guard decides whether that
  // lands or the user goes to connect the provider first.
  //
  // The table is definitions built with the DSL and composed with pipes:
  // guards (and cache, later) attach the same way on every route. The
  // router is the library's own; the shell mounts it in a RouterOutlet.
  let inline gate r =
    r |> Navs.Route.canActivate(fun _ _ -> requiresAgent env)

  let table: RouteDefinition<Control> list = [
    Navs.Route.define<Control>(
      "athletes",
      "/",
      fun _ nav -> athletesPage(env, nav)
    )
    |> gate

    Navs.Route.define<Control>(
      "login",
      "/login",
      fun _ nav -> loginPage(env, nav)
    )

    Navs.Route.define<Control>(
      "new-athlete",
      "/athletes/new",
      fun _ nav -> newAthletePage(env, nav)
    )
    |> gate

    Navs.Route.define<Control>(
      "athlete",
      "/athletes/:athleteId<guid>",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "athleteId" athleteId ->
          return! detailPage(env, athleteId, nav)
        | _ -> return notFound()
      }
    )
    |> gate

    Navs.Route.define<Control>(
      "generate",
      "/athletes/:athleteId<guid>/generate",
      fun context nav -> asyncEx {
        match context with
        | GuidParam "athleteId" athleteId ->
          return! wizardPage(env, athleteId, nav)
        | _ -> return notFound()
      }
    )
    |> gate

    Navs.Route.define<Control>(
      "review",
      "/athletes/:athleteId<guid>/review/:planId<guid>",
      fun context _ -> asyncEx {
        match context with
        | GuidParam "planId" planId -> return! reviewPage(env, planId)
        | _ -> return notFound()
      }
    )
    |> gate

    Navs.Route.define<Control>(
      "settings",
      "/settings",
      fun _ nav -> settingsPage(env, nav)
    )
  ]

  // the router logs through the app's Serilog pipeline, so its level
  // follows the app's configuration, not a private default
  let router =
    AvaloniaRouter(table, logger = env.LoggerFactory.CreateLogger "Navs.Router")

  // the boot fires at the athletes list; the guard decides whether that
  // lands or the user goes to connect the provider first
  go(router, "/")

  router
