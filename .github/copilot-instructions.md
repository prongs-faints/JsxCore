# Copilot instructions for JsxCore

JsxCore is a TSX/JSX view engine for ASP.NET Core. Views are `.tsx` files compiled by the native (Go) TypeScript 7 compiler, served as plain ES modules with no bundler, and rendered by Preact (embedded) or React, on the client, on the server (Jint, in-process), or both. See `CONTRIBUTING.md` and `docs/` for the full story.

## Build, test, run

The .NET SDK (8, 9 and 10) is the only prerequisite. JsxCore restores npm packages itself, so no Node/npm is needed.

```bash
dotnet build JsxCore.slnx
dotnet test test/JsxCore.Tests                       # whole suite, net10.0, about a minute

# A single test, class or folder
dotnet test test/JsxCore.Tests --filter "FullyQualifiedName~RenderModeTests.ServerMode_ViewIsRequested_ReturnsFinishedMarkupWithNoMountScript"
dotnet test test/JsxCore.Tests --filter "FullyQualifiedName~JsxCore.Tests.Unit.PackageManagement"

dotnet run --project samples/SampleApp               # also SampleApp.React, SampleApp.Tailwind, SampleApp.ServerRender
```

- There is no separate linter: `.editorconfig` plus `EnforceCodeStyleInBuild` make style violations build errors.
- A few tests run real `npm ci` against lock files JsxCore writes; they skip themselves when npm is absent (`Fixtures/RealNpm.cs`). Install Node if changing the package client.
- CI (`.github/workflows/build.yml`) builds and tests on Linux **and** Windows; paths and process invocation are the usual cross-platform breakages.
- Samples consume the targets from source (`ProjectReference` + explicit `Import`), so they do not exercise the package layout. If you change packaging, `dotnet pack src/JsxCore -c Release -o /tmp/feed`, consume it from a scratch project, and clear `~/.nuget/packages/jsxcore/<version>` between attempts.

## Architecture

| Project | Role |
|---|---|
| `src/JsxCore` | The library: compilation pipeline, rendering, hosting/middleware, MVC `IViewEngine`, embedded runtime under `Assets/` |
| `src/JsxCore.PackageManagement` | Native npm client plus primitives shared by everything. **Must never reference `JsxCore`, Jint or ASP.NET Core**; shared code moves *down* into this project, into the folder matching its namespace |
| `src/JsxCore.Tool` | Build-time commands (`provision`, `types`, `tsconfig`, `assets`, `minify`, `analyse`) invoked by `src/JsxCore/build/JsxCore.targets`. Ships inside the `JsxCore` package |
| `src/JsxCore.Npm` | The `dotnet npm` CLI tool (separate `JsxCore.Npm` package), built only on `PackageManagement` |
| `src/JsxCore.Analyzers` | Source generators: view-location annotations and recording registered globals |

Key cross-cutting ideas:

- **Targets decide when, the tool decides what.** Anything needing parsing, probing or JSON construction belongs in C# in `JsxCore.Tool` (with tests), not in MSBuild property functions in `JsxCore.targets`.
- **Build pipeline** (`src/JsxCore/Compilation/Pipeline`): `BuildPipeline` runs ordered `IBuildStep`s grouped as `Steps/Prepare` → `Gather` → `Compile` → `Emit`. Order is load bearing (runtime types and generated model types must exist before `tsc` runs). Each step can contribute a fingerprint; together with the JsxCore version these form the **build id**.
- **Build id in the URL path**: assets are served from `/_jsx/v{buildId}/...` with a one-year immutable cache. Anything that changes output (including minification on/off) must feed the fingerprint, or browsers will cache stale content.
- **Same compiled files for client and server**: `tsc` emits to `obj/JsxCore/js/` with `rewriteRelativeImportExtensions`; the browser resolves the module graph natively (import map for bare specifiers), and Jint loads the same files for server rendering, with .NET globals injected as real CLR objects.
- **Embedded runtime**: `src/JsxCore/Assets/**` (`*.js`, `*.json`, `*.ts`) are embedded resources served straight from the assembly; only `.d.ts` files and `tsconfig.base.json` are also packed to `buildTransitive/` because the out-of-process build needs them as files.
- Generated output lives under `obj/JsxCore/` in consuming projects; `Views/tsconfig.json` is generated for editors and only overwritten while it still contains the `jsxcore-generated` marker.
- Build diagnostics use `JSX000n` codes; every one is documented in `docs/troubleshooting.md`.

## Tests

xUnit + Shouldly, in `test/JsxCore.Tests`:

- `Unit/`: pieces in isolation.
- `Component/`: a real host, the real TypeScript compiler and real Jint. **The compiler is deliberately not mocked.** Use `JsxProjectFixture.Create()` (throwaway project in a temp dir, `AddView(...)`), `HostedViews.Project()` for the shared small view tree, and `JsxTestHost.StartAsync(project)` for HTTP.
- `TestSuiteBootstrap` restores the repo's root `package.json` (TypeScript, esbuild, preact, react…) via a module initializer before any test runs.
- Test names follow `Subject_Condition_ExpectedOutcome`, e.g. `ClientMode_ViewIsRequested_ReturnsShellWithSerialisedModelAndNoMarkup`.

## Conventions

- **Comments say why, not what**, usually the failure that made a line necessary. Comments that restate code get removed. XML docs only where they add something (`CS1591` is suppressed on purpose).
- **British spelling** in prose and identifiers (serialise, behaviour, licence) unless a .NET/web API name dictates otherwise.
- Behavioural changes need tests, a line under an `Unreleased` heading at the top of `CHANGELOG.md`, and `docs/` updates in the same change.
- Package version lives once in `src/Directory.Build.props` (shared by the shipped projects only); the release workflow overrides it from the tag.
- **Docs** follow `docs/style-guide.md`: one fact has one owning page (see its table) and is linked from elsewhere; no em-dashes (`grep -n "—" docs/*.md` must be empty); no "we"; reference pages in third person, tutorials in second person; each page starts with `← [Documentation index](README.md)`.
