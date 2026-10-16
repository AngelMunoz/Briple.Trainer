module Briple.Trainer.Views.Routes

open System
open Avalonia.Controls
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
open Briple.Trainer.Views

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
          | Error e -> Log.Error(e, "athlete save failed")
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

let build
  (env:
    #IProvideStore & #IProvideStores & #IProvideCulture & #IProvideLanguage & #IProvideHost & #IProvideAgent & #IProvideCredentials)
  : Routes =
  // the shell opens on the login when there is no working token yet
  let initial =
    if (env :> IProvideAgent).AgentAvailable then
      "/"
    else
      "/login"

  Routes(initialUri = initial)
    .Children(
      Route("athletes", "/", (fun context nav -> athletesPage(env, nav))),
      Route("login", "/login", (fun context nav -> loginPage(env, nav))),
      Route(
        "new-athlete",
        "/athletes/new",
        (fun context nav -> newAthletePage(env, nav))
      ),
      Route(
        "athlete",
        "/athletes/:athleteId<guid>",
        fun context nav -> asyncEx {
          match context with
          | GuidParam "athleteId" athleteId ->
            return! detailPage(env, athleteId, nav)
          | _ -> return notFound()
        }
      ),
      Route(
        "generate",
        "/athletes/:athleteId<guid>/generate",
        fun context nav -> asyncEx {
          match context with
          | GuidParam "athleteId" athleteId ->
            return! wizardPage(env, athleteId, nav)
          | _ -> return notFound()
        }
      ),
      Route(
        "review",
        "/athletes/:athleteId<guid>/review/:planId<guid>",
        fun context nav -> asyncEx {
          match context with
          | GuidParam "planId" planId -> return! reviewPage(env, planId)
          | _ -> return notFound()
        }
      )
    )
