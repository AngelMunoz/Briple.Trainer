module Briple.Trainer.Views.Login

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia

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
/// text means, and the explicit way out.
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
                .Text(get(culture, Keys.loginInsecureTitle))
                .Classes("body-strong", "danger")
            ),
          TextBlock()
            .Text(getf(culture, Keys.loginInsecureBody, [ reason :> obj ]))
            .Classes("caption", "danger")
            .TextWrapping(TextWrapping.Wrap),
          hyperlinkButton(
            get(culture, Keys.loginContinue),
            fun () -> dispatch ContinueInsecure
          )
        )
    )

let outcomeView
  (culture: CultureInfo, state: SaveState, dispatch: Dispatch<Msg>)
  : Control =
  match state with
  | Idle -> TextBlock()
  | VaultSaved ->
    TextBlock()
      .Text(get(culture, Keys.loginSaved))
      .Classes("caption", "secondary")
  | VaultFailed reason -> insecureWarning(culture, reason, dispatch)
  | SaveFailed reason ->
    TextBlock()
      .Text(getf(culture, Keys.loginFailed, [ reason :> obj ]))
      .Classes("body", "danger")

/// The page at one culture, over the current model.
let view
  (props: LoginPageProps)
  (culture: CultureInfo)
  (current: Model)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let t key = get(culture, key)

  // TextBox masked by hand: Avalonia 12 has no PasswordBox control
  let tokenEntry =
    TextBox()
      .CornerRadius(4.0)
      .Padding(8.0, 4.0)
      .PasswordChar(char "•")
      .Text(current.Token)
      .PlaceholderText(t Keys.loginToken)
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
      iconPrimaryButton(
        t Keys.loginSave,
        Icons.key,
        fun () -> dispatch SaveRequested
      )

    save.IsEnabled(complete)

  let outcomeHost =
    ContentControl()
      .Content(
        (model
         |> Observable.map(fun m -> outcomeView(culture, m.State, dispatch)))
          .ToBinding()
      )

  fluentPage {
    Lead = props.Lead
    Title = t Keys.loginTitle
    Commands = [ saveCommand ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(12.0)
        .Children(field(t Keys.loginToken, (fun () -> tokenEntry)), outcomeHost)
    Measure = 560.0
  }

type LoginPage(props: LoginPageProps) as this =
  inherit UserControl()

  do
    let viewWithCulture (model: IObservable<Model>) (dispatch: Dispatch<Msg>) =
      Page.cultureHost {
        Culture = props.Culture
        Model = model
        Build =
          fun culture current dispatch ->
            view props culture current model dispatch
        Dispatch = dispatch
      }

    let program = Program.mkProgram init (update props) viewWithCulture

    this.Content <- Program.runWith props program
