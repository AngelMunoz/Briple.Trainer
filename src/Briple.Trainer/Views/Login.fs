module Briple.Trainer.Views.Login

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia
open FSharp.Control.Reactive

open Briple.Trainer
open Briple.Trainer.Components
open Briple.Trainer.Components.Fields
open Briple.Trainer.Components.Page
open Briple.Trainer.Env
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type LoginPageProps = {
  Culture: PageCulture
  Lead: Control voption
  Save: string -> Result<CredentialSave, string>
  OnDone: unit -> unit
}

type SaveState =
  | Idle
  | VaultSaved
  | VaultFailed of reason: string
  | SaveFailed of reason: string

type Model = { Token: string; State: SaveState }

type Msg =
  | SetToken of string
  | SaveRequested
  | ContinueInsecure

let init(_: LoginPageProps) = { Token = ""; State = Idle }, Cmd.none

let update (props: LoginPageProps) (msg: Msg) (model: Model) =
  match msg with
  | SetToken value -> { model with Token = value }, Cmd.none

  // one save attempt per terminal state: a double click or a revisit
  // must not write the credential twice
  | SaveRequested when model.State = Idle ->
    let state =
      match props.Save model.Token with
      | Ok CredentialSave.Secured ->
        props.OnDone()
        VaultSaved
      | Ok(CredentialSave.Insecure reason) -> VaultFailed reason
      | Error reason -> SaveFailed reason

    { model with State = state }, Cmd.none

  | SaveRequested -> model, Cmd.none

  | ContinueInsecure ->
    props.OnDone()
    model, Cmd.none

/// The insecure fallback as a severity band: what failed, what plain
/// text means, and the explicit way out. The static labels are resources;
/// the reason is the composed text.
let insecureWarning
  (culture: CultureInfo, reason: string, dispatch: Dispatch<Msg>)
  : Control =
  Border()
    .Background(errorTonal)
    .CornerRadius(4.0)
    .Padding(12.0)
    .Margin(0.0, 12.0)
    .Child(
      StackPanel()
        .Spacing(8.0)
        .Children(
          StackPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8.0)
            .Children(
              glyph(Icons.alertCircle, 16.0, errorBrush),
              TextBlock()
                .Text(Localization.text Keys.loginInsecureTitle)
                .Classes("body-strong", "danger")
            ),
          TextBlock()
            .Text(getf(culture, Keys.loginInsecureBody, [ reason :> obj ]))
            .Classes("caption", "danger")
            .TextWrapping(TextWrapping.Wrap),
          hyperlinkButtonKey(
            Keys.loginContinue,
            fun () -> dispatch ContinueInsecure
          )
        )
    )

let outcomeView
  (culture: CultureInfo, state: SaveState, dispatch: Dispatch<Msg>)
  : Control =
  match state with
  | Idle -> TextBlock()
  | VaultSaved -> labelKey Keys.loginSaved
  | VaultFailed reason -> insecureWarning(culture, reason, dispatch)
  | SaveFailed reason ->
    TextBlock()
      .Text(getf(culture, Keys.loginFailed, [ reason :> obj ]))
      .Classes("body", "danger")

/// The page: static text comes from resources; the token is the model;
/// only the outcome band rebuilds when the culture or the state changes.
let view
  (props: LoginPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  // TextBox masked by hand: Avalonia 12 has no PasswordBox control
  let tokenEntry =
    TextBox()
      .CornerRadius(4.0)
      .Padding(8.0, 4.0)
      .PasswordChar(char "•")
      .Text(model |> Observable.map _.Token)
      .PlaceholderText(Localization.text Keys.loginToken)
      .OnTextChangedHandler(fun args _ ->
        args.Text
        |> Option.ofObj
        |> Option.defaultValue ""
        |> SetToken
        |> dispatch)

  let complete =
    model
    |> Observable.map(fun m ->
      m.State = Idle && not(String.IsNullOrWhiteSpace m.Token))

  let saveCommand =
    let save =
      iconPrimaryButtonKey(
        Keys.loginSave,
        Icons.key,
        fun () -> dispatch SaveRequested
      )

    save.IsEnabled(complete)

  let outcomeHost =
    ContentControl()
      .Content(
        (Observable.combineLatest model props.Culture.Changed
         |> Observable.map(fun (current, culture) ->
           outcomeView(culture, current.State, dispatch)))
          .ToBinding()
      )

  fluentPage {
    Lead = props.Lead
    Title = pageTitleKey Keys.loginTitle
    Commands = [ saveCommand ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(12.0)
        .Children(
          fieldKey(Keys.loginToken, (fun () -> tokenEntry)),
          outcomeHost
        )
    Measure = 560.0
  }

type LoginPage(props: LoginPageProps) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props) (view props)

    this.Content <- Program.runWith props program
