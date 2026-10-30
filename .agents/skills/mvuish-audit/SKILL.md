---
name: mvuish-audit
description: Audit and review F# Avalonia code in Briple.Trainer for MVUish usage and the observable-binding patterns that can retain controls. Use when writing, reviewing, or changing Views/, Components/, Shell.fs, Theme.fs, stores, bindings, or controls that rebuild — and for any request about memory, leaks, retention, or disposal in this repo.
---

# MVUish usage and leak-pattern audit for Briple.Trainer

## Version floor

MVUish.Avalonia 2.0.0. In 2.x, `Sub<'Msg>` is a keyed list, `Program.runWithLifecycle` returns `View * IDisposable`, and `Program.runWithLifetime` ties teardown to the visual tree. Briple.Trainer pins 2.0.0.

## Why these rules exist (the mechanism)

Every observable property binding roots its control from the SOURCE:

source → observer list → binding entry → ValueStore → control.

Avalonia holds that edge strongly. NXUI's fluent setters (`.Text(obs)`, `.IsVisible(obs)`, `.Background(obs)`, …) discard the disposable that would end it. A binding ends only when the source completes, the source drops its observers, or the same property is re-bound.

MVUish 2.x `Program.runWithLifecycle` teardown completes and disposes the model `BehaviorSubject`, so page-model bindings release. Pair it with a route-owned disposable bag (`RoutePage.Lifetime`) so the route owns the lifetime. `Program.runWith` has no teardown. It is safe only for app-lifetime singletons or tests.

App-lifetime sources in Briple.Trainer:

- `AthleteStore.Observe`, `MemoryStore.Observe`, `PlanStore.Observe`
- `Env.CultureChanged`, `Env.LanguageChanged`
- `Shell` subjects (`canBack`, `currentUrl`, `paneOpen`)
- `Dialogs` pending subject and `Toasts`
- `Theme` brushes (live instances, not observables)
- `Localization` resource dictionary

These sources never complete during the process. A view binding on one retains its control for the life of the process unless the binding is disposed or the control is app-lifetime (shell, singleton overlay).

## The rules

1. **Theme tokens are brushes or classes, never observables.** Use `Theme.accent`, `Theme.text`, `Theme.muted`, `Theme.cardFill`, or the classes in `Theme.fs`. Do not bind a themed observable.
2. **Shared state lives in `Stores` and `Env`.** A view must not bind a store or env observable directly unless the control is app-lifetime. Pass a page-scoped projection, or move the value into the page model.
3. **External observables enter a page through `Sub` or the page model.** A `Props` record must not carry an app-lifetime `IObservable` for the view to bind. Page-model projections are allowed. `PageCulture.Changed` is app-lifetime today. It must become page-scoped or travel through `Sub`.
4. **View functions bind only their own model stream.** That subject dies with the page program.
5. **Pages run `Program.runWithLifecycle` and `props.Lifetime.AddDisposable lifetime`.** `Program.runWithLifetime` is only for hosts that are replaced and never re-shown. `Program.runWith` is only for shell-lifetime singletons or tests.
6. **Do not cache a page whose program uses `runWithLifecycle`.** Use `Route.cache CachePolicy.NoCache` for the list, detail, wizard, review, form, login and settings routes. Cache a page only when it restarts its program on reattach.
7. **Decoded bitmaps and other control-owned resources dispose on replace and on detach.** Briple.Trainer has no bitmap decode sites today. Keep the rule for future images.
8. **Rebuilt shell content carries an identity guard.** Content replaced from a store stream rebuilds only when the identity changes.
9. **Class bindings only on page-model streams.** Do not call `.Subscribe` or `Observable.add` in Components/ or Views/ outside a lifetime helper. The standing exceptions are `Page.trackPhase`, `Page.listenGate`, `Hub`'s scroll binding, and the shell/singleton overlays. Each disposes on detach or lives for the process.
10. **Timers stop on every path.**
11. **Items bind through `BindItemsSource`** (weak control edge, keyed diff). Row templates must not bind app-lifetime streams. A row that binds `CultureChanged` or a store `Observe` keeps its text blocks alive after the row is removed.

## The audit procedure

Run from the repo root:

```powershell
$files = Get-ChildItem src/Briple.Trainer -Recurse -File -Include *.fs
$files | Select-String -Pattern 'Program\.runWith|Program\.withSubscription|Route\.cache'
$files | Select-String -Pattern 'Culture\.Changed|CultureChanged|\.Observe\b'
$files | Select-String -Pattern '\.Subscribe\(|Observable\.add'
$files | Select-String -Pattern 'new Bitmap|DecodeToWidth'
```

For a changeset:

```powershell
git diff -U0 -- src/Briple.Trainer |
  Select-String -Pattern '^\+.*(\.Subscribe\(|Observable\.add|Program\.runWith|Route\.cache|BindItemsSource)'
```

For a whole-tree audit, search `src/Briple.Trainer/Components` and `src/Briple.Trainer/Views` directly.

Classify every hit and every new binding in changed view code:

- **Page-model projection** (source is the function's `model` argument): allowed.
- **Sub-owned** (inside `Sub.*` or `Sub.untilDetached`): allowed.
- **Shell, Dialogs, Toasts, Program.fs, app-lifetime singleton**: allowed — one instance for the process.
- **Store or env observable bound in a view, `Program.runWith` on a re-showable page, `Observable.add` outside a helper, cached stateful route, bitmap without a drop path**: a leak — report it.

Also read every changed `view` function for new bindings against anything that is not the page model, and every new `ContentControl().Content(obs)` rebuild loop for an identity guard.

## Report format

```text
LEAKS (must fix)
- <file>:<line> — <what roots what, from which source>

ALLOWED BY THE RULES
- <file>:<line> — <classification: page-model / Sub-owned / shell / singleton>

CHECKS RUN
- <the commands and reads, with hit counts>
```
