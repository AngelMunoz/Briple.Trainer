module Briple.Trainer.Views.Routes

open System
open Avalonia.Controls
open Avalonia.Threading
open Navs
open Navs.Avalonia

open IcedTasks

open Briple.Trainer.Components
open Briple.Trainer.Components.AthleteList
open Briple.Trainer.Components.NewAthleteForm
open Briple.Trainer.Core.Memories
open Briple.Trainer.Env
open Briple.Trainer.Persistence
open Briple.Trainer.Views

let inline go(nav: INavigable<Control>, url: string) : unit =
  asyncEx {
    match! nav.Navigate url with
    | Ok() -> ()
    | Error e -> eprintfn $"navigation to {url} failed: %O{e}"
  }
  |> Async.StartImmediate

let inline notFound() : Control = TextBlock(Text = "not found")

/// Ok keeps the values; an Error logs and reads as empty, so a failed
/// load degrades to an empty page instead of crashing the navigation.
let inline orEmpty(what: string) : Result<'a list, exn> -> 'a list =
  function
  | Ok xs -> xs
  | Error e ->
    eprintfn $"{what} failed: %O{e}"
    []

/// A guid route parameter as an active pattern: `match ctx with GuidParam
/// athleteId "athleteId" -> ...`.
[<return: Struct>]
let inline (|GuidParam|_|) (name: string) (ctx: RouteContext) =
  ctx.getParam<Guid> name

let athletesPage
  (env: #IProvideStore & #IProvideCulture, nav: INavigable<Control>)
  : Async<Control> = asyncEx {
  let! athletes = env.Store.ListAthletes()

  let loaded = athletes |> orEmpty "athlete list"

  return
    AthleteList.view {
      Culture = env.Culture
      Athletes = loaded
      OnNew = fun () -> go(nav, "/athletes/new")
      OnOpen = fun id -> go(nav, $"/athletes/{id}")
    }
}

let newAthletePage
  (env: #IProvideStore & #IProvideCulture, nav: INavigable<Control>)
  : Control =
  NewAthleteForm(
    { Culture = env.Culture },
    fun event ->
      match event with
      | NewAthleteForm.Saved athlete ->
        asyncEx {
          match! env.Store.AddAthlete athlete with
          | Ok _ -> go(nav, "/")
          | Error e -> eprintfn $"athlete save failed: %O{e}"
        }
        |> Async.StartImmediate
      | NewAthleteForm.Cancelled -> go(nav, "/")
  )

let detailPage
  (
    env: #IProvideStore & #IProvideCulture,
    athleteId: Guid,
    nav: INavigable<Control>
  ) : Async<Control> = asyncEx {
  let! athlete = env.Store.FindAthlete athleteId

  match athlete with
  | Error e ->
    eprintfn $"athlete load failed: %O{e}"
    return TextBlock(Text = string e.Message)
  | Ok ValueNone -> return notFound()
  | Ok(ValueSome athlete) ->
    let! memories = env.Store.ListMemories athleteId
    let! plans = env.Store.ListPlans athleteId

    let memories = memories |> orEmpty "memory list"
    let plans = plans |> orEmpty "plan list"

    return
      Athletes.AthleteDetailPage {
        Culture = env.Culture
        Store = env.Store
        Athlete = athlete
        Memories = memories
        Plans = plans
        OnGenerate = fun () -> go(nav, $"/athletes/{athleteId}/generate")
        OnReview =
          fun planId -> go(nav, $"/athletes/{athleteId}/review/{planId}")
      }
}

let wizardPage
  (
    env: #IProvideStore & #IProvideCulture & #IProvideAgent,
    athleteId: Guid,
    nav: INavigable<Control>
  ) : Async<Control> = asyncEx {
  let! athlete = env.Store.FindAthlete athleteId
  let! memories = env.Store.ListMemories athleteId
  let! plans = env.Store.ListPlans athleteId

  let proposals =
    memories
    |> orEmpty "memory list"
    |> List.filter(fun entity -> entity.Memory.Status = Proposed)

  let previous =
    plans |> orEmpty "plan list" |> List.tryLast |> ValueOption.ofOption

  match athlete with
  | Ok(ValueSome athlete) ->
    return
      Generate.WizardPage {
        Culture = env.Culture
        Store = env.Store
        Agent = env
        Athlete = athlete
        Proposals = proposals
        Previous = previous
        OnFinished =
          fun planId -> go(nav, $"/athletes/{athleteId}/review/{planId}")
        OnCancelled = fun () -> go(nav, $"/athletes/{athleteId}")
      }
  | _ -> return notFound()
}

let reviewPage
  (env: #IProvideStore & #IProvideCulture & #IProvideHost, planId: Guid)
  : Async<Control> = asyncEx {
  let! plan = env.Store.FindPlan planId

  match plan with
  | Ok(ValueSome entity) ->
    return
      Review.ReviewPage {
        Culture = env.Culture
        Store = env.Store
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
    #IProvideStore & #IProvideCulture & #IProvideLanguage & #IProvideHost & #IProvideAgent & #IProvideCredentials)
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
