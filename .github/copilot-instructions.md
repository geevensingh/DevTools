# Copilot instructions for DevTools

Personal grab-bag of small C# console tools and a couple of class libraries, all
hosted in one solution: `Tools.sln`. There is no README, no CI, and no
contribution guide — these notes are the sum of the conventions you can only
learn by reading multiple `.csproj` files.

## Repository shape

- One Visual Studio solution at the root: `Tools.sln`.
- Each top-level directory is one project; the solution is a flat list of
  ~30 standalone CLI tools plus two shared libraries.
- `Utilities/` is the shared library; **most other projects reference it via
  `ProjectReference`**. Treat changes there as cross-cutting — search for
  consumers before refactoring public APIs.
- `Logging/` is a smaller shared library used by a few tools (e.g. `Wordle-Parallel`).
- `ArmorEvaluator.ps1` at the repo root is a **standalone PowerShell script**, not
  part of the solution. Don't try to compile it.
- `Tools.sln` still lists a `GitSummary` project whose folder no longer exists on
  disk. Building the full solution will warn/fail on that entry until it is
  either restored or removed from the solution. Don't "fix" this by deleting
  the entry unless asked — it's a known stale reference.
- The `DiffViewer/` folder is a **pointer stub** containing only a `README.md`
  that points at [github.com/geevensingh/DiffViewer]. DiffViewer was extracted
  into its own repo in May 2026 to support standalone releases. Don't try to
  build it from here; its history lives in the new repo (via `git filter-repo`)
  plus this repo's pre-extraction history.

## Two project styles coexist — know which one you're editing

This repo straddles a migration. **Always check the first line of the `.csproj`
before adding files or packages.**

1. **Legacy .NET Framework 4.8** (most projects, e.g. `Utilities`, `GitPrompt`,
   `ChangeLister`, `Strands`, `Cleanup`, `IdParser`, `Utilities-Tests`,
   `IdParserTests`, `HealthSpreadsheet`, `Wordle-Parallel`, ...):
   - Old-style csproj: `<Project ToolsVersion="..." xmlns="...msbuild/2003">`.
   - `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`.
   - **NuGet via `packages.config`** — not `PackageReference`. When adding a
     dependency, update both `packages.config` and the `<Reference>` `<HintPath>`
     in the csproj.
   - **New `.cs` files must be added manually** to a `<Compile Include="..." />`
     `ItemGroup`. They are not picked up by glob.
   - `LangVersion` varies per project (7.2 in `Utilities`, 9.0 in
     `Wordle-Parallel`, unset elsewhere). Match the project you're editing.
   - Most of these have a Release-only post-build step that copies the binary to
     `D:\Tools`. **Leave it in place** — it's the author's personal deploy path.

2. **SDK-style .NET 6** (`<Project Sdk="Microsoft.NET.Sdk">`):
   `ArmorEvaluator`, `WeaponEvaluator`, `Wordle`, `Wordle-Starter`,
   `ChristmasList`, `LetterLongDivision`.
   - `<TargetFramework>net6.0</TargetFramework>`, `ImplicitUsings` and
     `Nullable` enabled.
   - Use `PackageReference` (no `packages.config`).
   - File globbing is on — no manual `<Compile Include>`.
   - Asset files referenced at runtime are wired up with
     `<None Update="..."><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`.
   - `Wordle/Wordle.csproj` deliberately excludes `Program - V1.cs` /
     `Program - V2.cs` from compilation — they are kept as `<None>` for history.
     Don't "clean them up."

## Build, run, test

There are no scripted entry points — use the standard .NET tooling.

- **Build a single SDK-style project** (works from a plain shell, no VS):
  ```
  dotnet build .\WeaponEvaluator\WeaponEvaluator.csproj
  ```
- **Build a legacy net4.8 project**: prefer Visual Studio or `msbuild` from a
  Developer Command Prompt. NuGet packages must be restored first:
  ```
  nuget restore Tools.sln
  msbuild .\Utilities\Utilities.csproj
  ```
  `dotnet build` on the legacy projects only works if MSBuild + the .NET
  Framework 4.8 targeting pack are installed.
- **Build everything**: `msbuild Tools.sln` (expect the missing-`GitSummary`
  warning described above).
- **Run a tool**: SDK-style — `dotnet run --project .\Wordle\Wordle.csproj`.
  Legacy — run the produced exe from `<Project>\bin\Debug\` or `bin\Release\`.
- **VS Code tasks** (`.vscode/tasks.json`) only cover `WeaponEvaluator` and
  `ArmorEvaluator` (`build-WeaponEvaluator`, `build-ArmorEvaluator`,
  `build-all`, `publish`, `watch`). They are not a substitute for a full build.

### Tests

Both test projects (`Utilities-Tests`, `IdParserTests`) are **MSTest on
.NET Framework 4.8** (with `FluentAssertions` in `IdParserTests`). They run
under `vstest` / Visual Studio Test Explorer, not `dotnet test` (which doesn't
work on packages.config / net4.8 here).

- Run all tests in a project: `vstest.console.exe .\Utilities-Tests\bin\Debug\Utilities-Tests.dll`
  (after a successful msbuild).
- Run a **single test** by fully-qualified name:
  `vstest.console.exe .\Utilities-Tests\bin\Debug\Utilities-Tests.dll /Tests:Utilities_Tests.StringHelperTest.TestToLower`
  (or use `/TestCaseFilter:"FullyQualifiedName~TestToLower"`).
- Test classes are `[TestClass]` with `[TestMethod]`-attributed methods; use
  `Assert.AreEqual` / `FluentAssertions` to match existing style.

## Conventions worth knowing

- **Commit messages must start with the name of the app/project that was
  changed**, e.g. `SessionMonitor: fix tray icon registration`,
  `Utilities: add NormalizePath helper`, `Wordle: speed up word filtering`.
  This applies to every commit in this repo.
- `.editorconfig` deliberately downgrades `CS8618` (non-nullable field…) to
  `suggestion`. Don't promote it back to a warning.
- Solution-level configurations include `Debug|x86`, `Debug|x64`, `Debug|ARM`,
  etc. Most projects map all of them to `Any CPU`, but a few (e.g.
  `ChangeLister`) genuinely build as `x86` and `Wordle-Parallel` Debug builds
  as `x64`. Preserve the existing per-project mapping when editing the `.sln`.
- Project names with spaces exist (`Playlist Generator`). Quote paths in
  scripts.
- Namespaces follow folder names but with hyphens replaced by underscores
  (e.g. `Wordle-Parallel` ⇒ `Wordle_Parallel`, `Utilities-Tests` ⇒
  `Utilities_Tests`). Match this when adding files.
- New shared helpers belong in `Utilities/` (extension methods live under
  `Utilities/Extensions/`). Adding them there is preferred over duplicating
  utilities inside individual tools.
