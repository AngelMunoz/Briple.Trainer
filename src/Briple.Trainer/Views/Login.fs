module Briple.Trainer.Views.Login

// The provider login page: the DeepSeek token and nothing else — the
// provider name, endpoint and model come from Env.defaultSettings. The
// token goes through the env credentials capability; when the OS vault
// fails, the token falls back to settings.json and the page answers with
// the huge insecure warning, the reason, and an explicit continue.

open System
open System.Globalization

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open NXUI.Extensions

open MVUish.Avalonia

open Briple.Trainer
open Briple.Trainer.Components.Fields
open Briple.Trainer.Env
open Briple.Trainer.Strings
open Briple.Trainer.Theme

[<Struct>]
type LoginPageProps = {
  Culture: CultureInfo
  Save: string -> Result<CredentialSave, string>
  OnDone: unit -> unit
}

// --- the page state ---

/// What the last save produced; the vault-failed case is the fallback the
/// page must shout about before the user continues.
type SaveState =
  | Idle
  | VaultSaved
  | VaultFailed of reason: string
  | SaveFailed of reason: string

  override this.ToString() =
    match this with
    | Idle -> "Idle"
    | VaultSaved -> "VaultSaved"
    | VaultFailed reason -> $"VaultFailed: {reason}"
    | SaveFailed reason -> $"SaveFailed: {reason}"

type Model = { Token: string; State: SaveState }

type Msg =
  | SetToken of string
  | SaveRequested
  | ContinueInsecure

let init(props: LoginPageProps) = { Token = ""; State = Idle }, Cmd.none

/// The update closes over the done callback: a secured save continues
/// immediately; the insecure one waits for the explicit continue.
let update (props: LoginPageProps) (msg: Msg) (model: Model) =
  match msg with
  | SetToken value -> { model with Token = value }, Cmd.none

  // one save attempt per terminal state: a double click or a revisit of
  // the cached page must not write the credential twice
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

// --- the view ---

/// The huge insecure warning: red block, the failure cause, what it
/// means that the token sits in plain text, and the way out.
let insecureWarning
  (culture: CultureInfo, reason: string, dispatch: Dispatch<Msg>)
  : Control =
  StackPanel()
    .Spacing(8.0)
    .Margin(0.0, 12.0)
    .Children(
      TextBlock()
        .Text(get(culture, Keys.loginInsecureTitle))
        .FontFamily(font)
        .FontSize(20.0)
        .FontWeight(FontWeight.Bold)
        .Foreground(Brushes.White)
        .Background(errorBrush)
        .Padding(12.0, 8.0)
      :> Control,
      TextBlock()
        .Text(getf(culture, Keys.loginInsecureBody, [ reason :> obj ]))
        .FontFamily(font)
        .FontSize(14.0)
        .TextWrapping(TextWrapping.Wrap)
        .Foreground(errorBrush)
      :> Control,
      iconPrimaryButton(
        get(culture, Keys.loginContinue),
        Icons.alertCircle,
        fun () -> dispatch ContinueInsecure
      )
    )

let outcomeView
  (culture: CultureInfo, state: SaveState, dispatch: Dispatch<Msg>)
  : Control =
  match state with
  | Idle -> TextBlock() :> Control
  | VaultSaved ->
    TextBlock()
      .Text(get(culture, Keys.loginSaved))
      .FontFamily(font)
      .Foreground(muted)
    :> Control
  | VaultFailed reason -> insecureWarning(culture, reason, dispatch)
  | SaveFailed reason ->
    TextBlock()
      .Text(getf(culture, Keys.loginFailed, [ reason :> obj ]))
      .FontFamily(font)
      .Foreground(errorBrush)
    :> Control

let view
  (props: LoginPageProps)
  (model: IObservable<Model>)
  (dispatch: Dispatch<Msg>)
  : Control =
  let culture = props.Culture
  let t key = get(culture, key)

  // TextBox masked by hand: Avalonia 12 has no PasswordBox control, the
  // mask rides on the same TextChanged pipeline every field uses
  let tokenEntry =
    TextBox()
      .FontFamily(font)
      .CornerRadius(0.0)
      .Padding(8.0, 4.0)
      .PasswordChar(char "•")
      .PlaceholderText(t Keys.loginToken)
      .OnText(fun _ observed ->
        observed
        |> Observable.map(fun value ->
          value |> Option.ofObj |> Option.defaultValue "")
        |> Observable.subscribe(SetToken >> dispatch)
        |> ignore)

  let complete =
    model
    |> Observable.map(fun m ->
      m.State = Idle && not(String.IsNullOrWhiteSpace m.Token))

  let saveCommand =
    let save =
      iconCommand(t Keys.loginSave, Icons.key, fun () -> dispatch SaveRequested)

    save.IsEnabled(complete)

  // the outcome area follows the save state stream
  let outcome =
    model
    |> Observable.map(fun m -> box(outcomeView(culture, m.State, dispatch)))

  let outcomeHost = ContentControl()

  outcomeHost.Bind(ContentControl.ContentProperty :> AvaloniaProperty, outcome)
  |> ignore

  fluentPage {
    Title = t Keys.loginTitle
    Commands = [ saveCommand ]
    Footer = noFooter
    Body =
      StackPanel()
        .Spacing(12.0)
        .Children(
          field(t Keys.loginToken, (fun () -> tokenEntry :> Control)),
          outcomeHost
        )
    Measure = 560.0
  }

type LoginPage(props: LoginPageProps) as this =
  inherit UserControl()

  do
    let program = Program.mkProgram init (update props) (view props)

    this.Content <- Program.runWith props program
