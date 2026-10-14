# AGENTS.md

Briple.Trainer is a desktop app for personal trainers: F# on .NET 10 with
Avalonia, split into a Core library that owns the training-plan format and the
agent-assisted plan generation, and a desktop consumer. No backend; BYOK
providers (GLM, DeepSeek, Meta Muse), all OpenAI-compatible.

## Imperatives

1. **NEVER PUSH WITHOUT PERMISSION.** Always ask before pushing to the remote. A previous permission push for one set of work DOES NOT MEAN ALWAYS PUSH AFTER. Permissions are granted per set of work, not for the session.
2. **NEVER FORCE PUSH.** Tell the user they have to force push instead of you.
3. **USE YOUR EDIT TOOL FOR FILE EDITS**: Do not use sed, python, perl or any other scripting language or cli command to write/update files and its contents.
4. **Always run `dotnet fantomas .` before committing code.** Format all F# files before staging.
5. **Prefer `Option.*` (or `ValueOption`) combinators over nested matches.** When threading optional values, chain `Option.map` / `bind` / `filter` / `iter` / `defaultValue` / `orElse` instead of hand-rolling `match ... with | Some x -> ... | None -> ...` ladders. Match only when the two branches carry substantially different logic, not to unwrap and re-wrap.
6. Pull requests made with the `gh` command use a markdown file as the PR body, not inline escaped markdown.
7. New F# files **MUST** be paired with its own signature file for app and library code, tests and samples do not require signature files.
8. Comments go to the signature files. implementation quirk comments may stay in implementation files.
9. Report to me only in ASD-STE100 Simplified Technical English, or plain Spanish when I write in Spanish.
10. For non-reports, do not use LLM jargon, phrases that are textual noise without actual value. Speak simple plain, concise and direct english.

## Commands

- `dotnet build Briple.Trainer.slnx` - builds everything; the repo gate
  requires zero errors and zero warnings.
- `dotnet run --project tests/Briple.Trainer.Core.Tests` - runs the Expecto
  suite; append `-- --summary` for a summary or `-- --filter <name>` for a
  subset.
- `dotnet fantomas check .` / `dotnet fantomas .` - format check / format
  (`dotnet tool restore` installs the tool).

## Architecture

- `src/Briple.Trainer.Core/` - the format layer: `Types.fs` (domain),
  `Parser.fs` (XParsec; line classifier + recursive block assembler),
  `Projection.fs` (pure date to session). Core never references the consumer
  app or any UI package.
- `src/Briple.Trainer/` - the Avalonia desktop consumer. No SukiUI: strict
  Metro theme on top of SimpleTheme (CornerRadius 0, one accent, Segoe UI),
  NXUI for UI authored in code.
- `tests/Briple.Trainer.Core.Tests/` - the Expecto suite over Core.
- `Directory.Packages.props` - central package management; every package
  version lives there, never in a project file.
- Generation flow: no chat interface. A wizard with confirmations. Only the
  text format is deterministic; counts, RIR, laps travel as request
  parameters and the validator checks them against the output.
- Persistence: SQLite through Donald. The confirmed plan's Raw text
  (verbatim) is the source of truth.

## Testing

1. **Assert behavior, not plumbing.** Test through the surface that is meant
   to survive: parse real file text with `parsePlan` and assert on the `Plan`
   tree, call the projection with dates and assert on the session. Never
   assert on parser combinator internals. A test must not freeze transitional
   machinery.
2. **Regression tests prove both directions.** A regression test for a bug
   must fail before the fix and pass after it. Run it against the unfixed
   code before you keep it.
3. **Test-owned data only.** Fixtures live in
   `tests/Briple.Trainer.Core.Tests/fixtures`; every expected value derives
   from them. A test that needs a plan shape the fixture does not have
   authors it with the synthetic builders or an inline string, never app boot
   state.
4. **The API is never shaped for tests.** Do not add flags, getters, or test
   hooks to domain code so a test can assert. Domain functions return what
   the app reads; a rejected input warns through `ParsedPlan` or fails
   through `ParseError`. Tests accommodate the API, never the reverse.
5. **No dev shortcuts, no test-only hooks.** Name and trigger the behavior
   through the production surface. No test-only ids, attributes, or direct
   state writes so a test can reach in.
6. A new test module needs one `Compile` entry in the test project and one
   `testList` registered in `Program.fs`.
