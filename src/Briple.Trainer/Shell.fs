module Briple.Trainer.Shell

open System
open System.Globalization
open System.Reactive.Subjects

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media

open NXUI.Extensions

open FSharp.Control.Reactive
open IcedTasks
open Navs
open Navs.Avalonia

open Briple.Trainer.Env
open Briple.Trainer.Localization
open Briple.Trainer.Navigation
open Briple.Trainer.Strings
open Briple.Trainer.Theme
open Briple.Trainer.Views

[<Struct>]
type NavItem = {
  Key: string
  Target: string
  Icon: StreamGeometry
}

type NavRow = {
  Item: NavItem
  Active: IObservable<bool>
  IsOpen: IObservable<bool>
  OnSelect: unit -> unit
}

type Pane = {
  CurrentUrl: IObservable<string>
  IsOpen: IObservable<bool>
  Toggle: unit -> unit
  Navigate: string -> unit
}

// Builds one back control per page. The router supplies the navigable
// that the page belongs to, so the control calls that router's history.
let leadOf (canBack: IObservable<bool>) (nav: INavigable) : unit -> Control =
  fun () ->
    let button =
      Button()
        .Content(PathIcon(Data = Icons.arrowLeft).Width(16.0).Height(16.0))
        .IsEnabled(canBack)
        .OnClickHandler(fun _ _ -> Routes.back nav)
        .Classes("chrome", "back")

    // the Fluent tooltip content is a control; its text follows the
    // language through the normal binding
    let tip = TextBlock().Text(Localization.text Keys.commonBack)

    ToolTip.SetTip(button, tip)
    button

let activeOf
  (currentUrl: IObservable<string>, target: string)
  : IObservable<bool> =
  currentUrl
  |> Observable.map(fun url -> Sections.sectionOf url = ValueSome target)

let navRow(row: NavRow) : Control =
  Button()
    .Content(
      DockPanel()
        .Children(
          // the selection bar: the Fluent accent strip on the rail
          Border().Width(3.0).Background(accent).IsVisible(row.Active),
          Border()
            .Width(48.0)
            .Child(
              PathIcon(Data = row.Item.Icon)
                .HorizontalAlignment(HorizontalAlignment.Center)
            ),
          TextBlock()
            .Text(Localization.text row.Item.Key)
            .VerticalAlignment(VerticalAlignment.Center)
            .IsVisible(row.IsOpen)
        )
    )
    .HorizontalContentAlignment(HorizontalAlignment.Left)
    .Foreground(
      row.Active |> Observable.map(fun on -> if on then accent else text)
    )
    .FontWeight(
      row.Active
      |> Observable.map(fun on ->
        if on then FontWeight.SemiBold else FontWeight.Normal)
    )
    .OnClickHandler(fun _ _ -> row.OnSelect())
    .Classes("nav-item")

let navItems: NavItem list = [
  {
    Key = Keys.navAthletes
    Target = "/"
    Icon = Icons.accountGroup
  }
]

let pane(context: Pane) : Control =
  let rowOf(item: NavItem) =
    navRow {
      Item = item
      Active = activeOf(context.CurrentUrl, item.Target)
      IsOpen = context.IsOpen
      OnSelect = fun () -> context.Navigate item.Target
    }

  // settings rides the pane footer; its label is a resource too
  let settingsHost =
    rowOf {
      Key = Keys.navSettings
      Target = "/settings"
      Icon = Icons.settings
    }

  // the pane toggle: the only way in and out of the compact rail
  let toggle =
    Button()
      .Content(
        Border()
          .Width(48.0)
          .Child(
            PathIcon(Data = Icons.menu)
              .HorizontalAlignment(HorizontalAlignment.Center)
          )
      )
      .HorizontalContentAlignment(HorizontalAlignment.Left)
      .OnClickHandler(fun _ _ -> context.Toggle())
      .Classes("chrome")

  let items =
    ItemsControl()
      .ItemTemplate(
        FuncDataTemplate<NavItem>((fun item _ -> rowOf item), false)
      )
      .ItemsSource(navItems)

  DockPanel()
    .Children(
      settingsHost.DockBottom(),
      StackPanel()
        .Children(
          toggle,
          TextBlock()
            .Text(Localization.text Keys.appTitle)
            .Classes("pane-title")
            .Margin(16.0, 8.0, 16.0, 12.0)
            .IsVisible(context.IsOpen),
          items
        )
    )

let titleOf(culture: CultureInfo, url: string) : string =
  let app = get(culture, Keys.appTitle)

  match Sections.sectionOf url with
  | ValueSome "/" -> $"{app} — {get(culture, Keys.navAthletes)}"
  | ValueSome "/settings" -> $"{app} — {get(culture, Keys.navSettings)}"
  | _ -> app

let observe
  (
    canBack: BehaviorSubject<bool>,
    currentUrl: BehaviorSubject<string>,
    router: IRouter<Control>
  ) : IDisposable =
  let publish(snapshot: RouterSnapshot<Control>) =
    canBack.OnNext snapshot.History.CanGoBack

    snapshot.Route
    |> ValueOption.toOption
    |> Option.iter(fun current -> currentUrl.OnNext current.Path)

  let subscription = router.Changed |> Observable.subscribe publish
  publish router.Snapshot
  subscription

let backAction(router: IRouter<Control>) : unit -> unit =
  fun () ->
    if router.History.CanGoBack then
      Routes.back router

// The OS close honors the same phases as the route guards: cancel the
// close, ask, close again when the answer allows it.
let guardClose(window: Window, askLeave: unit -> Async<bool>) : unit =
  let mutable confirmed = false

  window.Closing.Add(fun args ->
    if not confirmed then
      args.Cancel <- true

      asyncEx {
        let! allowed = askLeave()

        if allowed then
          confirmed <- true
          window.Close()
      }
      |> Async.StartImmediate)

// The Fluent window content fills the surface behind the system
// caption. Only Windows answers the Mica chain; elsewhere the theme's
// opaque region brush stays.
let applyWindowChrome(window: Window) : unit =
  window.ExtendClientAreaToDecorationsHint <- true
  window.ExtendClientAreaTitleBarHeightHint <- 32.0

  if OperatingSystem.IsWindows() then
    window.TransparencyLevelHint <-
      ResizeArray [
        WindowTransparencyLevel.Mica
        WindowTransparencyLevel.AcrylicBlur
        WindowTransparencyLevel.Transparent
      ]

    window.Background <- Brushes.Transparent

// The tokens follow the live variant: light/dark retints every consumer
// without rebinding.
let applyThemeVariant() : unit =
  match Application.Current with
  | null -> ()
  | app ->
    Theme.applyVariant(app.ActualThemeVariant)

    app.ActualThemeVariantChanged.Add(fun _ ->
      Theme.applyVariant(app.ActualThemeVariant))

// Narrow windows snap the pane to the rail and wide ones open it, but
// only when the window crosses the line: a manual toggle survives.
let snapPane(paneOpen: BehaviorSubject<bool>, window: Window) : unit =
  let mutable wide = true

  window.SizeChanged.Add(fun size ->
    let next = size.NewSize.Width >= 700.0

    if next <> wide then
      wide <- next
      paneOpen.OnNext next)

let create(env: AppEnv) : Window =
  Localization.install env.Culture

  let languageSubscription =
    env.LanguageChanged
    |> Observable.subscribe(fun language ->
      Localization.install(Language.toCulture language))

  let canBack = new BehaviorSubject<bool>(false)
  let shell = Routes.build(env, leadOf(canBack :> IObservable<bool>))

  let currentUrl = new BehaviorSubject<string>("/")
  let routeSubscription = observe(canBack, currentUrl, shell.Router)

  let paneOpen = new BehaviorSubject<bool>(true)

  let window =
    Window(
      Title = Strings.get(env.Culture, Keys.appTitle),
      Width = 1100.0,
      Height = 800.0
    )
      .FontFamily(font)

  // the router and the resource installer are the two shell-lifetime
  // subscriptions; the window owns them and releases them on close
  window.Closed.Add(fun _ ->
    routeSubscription.Dispose()
    languageSubscription.Dispose())

  env.SetHost window
  Toasts.init(window)

  Env.applyTheme((env :> IProvideTheme).Theme)
  applyThemeVariant()
  applyWindowChrome(window)

  // the window title names the section, under the current language
  let window =
    window.Title(
      Observable.combineLatest currentUrl env.LanguageChanged
      |> Observable.map(fun (url, _) -> titleOf(env.Culture, url))
    )

  window.KeyBindings.Add(
    KeyBinding(
      Gesture = KeyGesture.Parse("Alt+Left"),
      Command = ActionCommand(backAction shell.Router)
    )
  )

  guardClose(window, fun () -> shell.AskLeave currentUrl.Value)

  let pane =
    pane {
      CurrentUrl = currentUrl
      IsOpen = paneOpen
      Toggle = fun () -> paneOpen.OnNext(not paneOpen.Value)
      Navigate = fun url -> Routes.go(shell.Router, url)
    }

  // the router outlet swaps views with the library transition
  let outlet = RouterOutlet(Router = Some shell.Router)

  let split =
    SplitView()
      .DisplayMode(SplitViewDisplayMode.CompactInline)
      .CompactPaneLength(48.0)
      .OpenPaneLength(220.0)
      .IsPaneOpen(paneOpen)
      .Pane(pane)
      .Content(outlet)

  window.Content(Grid().Children(split, Dialogs.host)) |> ignore

  snapPane(paneOpen, window)

  window
