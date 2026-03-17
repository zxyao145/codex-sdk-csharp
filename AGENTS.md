# Repository Guidelines

## Project Structure & Module Organization
- Root `csharp.slnx` ties together the SDK and samples; run commands from the repository root.
- Library code lives in `src/`, with the `OpenAI.Codex.Sdk` project (`OpenAI.Codex.Sdk.csproj`) targeting `.NET 10`. Arrange files so namespaces follow `OpenAI.Codex.*`, and group helpers under `src/Utils/` when they are reused.
- Sample apps are under `samples/`, referencing the SDK via `<ProjectReference>`. Use this folder to showcase new features and keep runnable examples up to date.
- Build artifacts (`bin/`, `obj/`) are generated in each project folder; keep them out of version control.

## Build, Test, and Development Commands
- Restore dependencies: `dotnet restore csharp.slnx`.
- Compile the SDK and samples: `dotnet build csharp.slnx --configuration Release` (use `Debug` while iterating).
- Run samples: `dotnet run --project samples/samples.csproj`.
- Run formatting: `dotnet format csharp.slnx` before sending reviews to enforce analyzer defaults.
- Tests are not yet present; when you add a test project, invoke it with `dotnet test <path-to-test-csproj>`.

## Coding Style & Naming Conventions
- Follow the nullable-enabled, implicit-using defaults already set in the csproj files; prefer `var` when the type is obvious.
- Use PascalCase for classes, interfaces, and namespaces; camelCase for locals and private fields; `_camelCase` for private readonly fields.
- Keep public APIs async-friendly and surface cancellation tokens when operations might block remote calls.
- Align new file names with the primary type they contain (`Thread.cs`, `ThreadOptions.cs`); avoid multi-type catchalls.
- Example namespace layout:
  ```csharp
  namespace OpenAI.Codex.Threads;
  ```

## Testing Guidelines
- Standardize on xUnit when introducing tests; place projects under `tests/` (e.g., `tests/OpenAI.Codex.Sdk.Tests`).
- Name test classes after the unit under test (`ThreadTests`) and methods using the `Method_Scenario_Result` style.
- Prefer deterministic fixtures and avoid mutating process-wide environment variables; wrap external calls with fakes or recorded responses.
- Ensure new features ship with unit coverage and, when feasible, an integration sample under `samples/`.

## Commit & Pull Request Guidelines
- Match the existing history: start messages with a scope or type (`[sdk]`, `fix:`) followed by a concise summary, and append the tracking issue in parentheses, e.g., `fix: handle retry jitter (#12345)`.
- Squash small fixup commits before opening a PR. Provide a clear description of behavior changes, include repro or validation steps, and attach console output for breaking changes.
- Link related issues or design docs, note any follow-up tasks, and request reviews from the SDK maintainers list in CODEOWNERS (or tag the owning team if unsure).
