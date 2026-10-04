# Contributing to Adamantium UI

Adamantium UI is in alpha: the API changes between releases, and so may the code a change touches. Bug reports, fixes
and ideas are all welcome.

## Before you start

- **Questions** go to [Discussions](https://github.com/AdamantiumStudio/AdamantiumUI/discussions).
- **Bugs and ideas** go to [issues](https://github.com/AdamantiumStudio/AdamantiumUI/issues/new/choose). Search
  first: a comment on an existing issue helps more than a second one.
- **Anything larger than a small fix** - a new control, a new public API, a change in how something works - starts as
  an issue, so the approach is agreed on before the work is done.

## Building

Requirements:

- Windows 10 or 11, x64;
- the .NET SDK named in [`global.json`](global.json);
- to run the sandbox and the GPU tests, a GPU that meets the [requirements](README.md#getting-started). Building and
  the other tests need no GPU.

```
git clone https://github.com/AdamantiumStudio/AdamantiumUI.git
cd AdamantiumUI
dotnet build AdamantiumUI.sln -c Debug
```

The output goes to `artifacts/bin/<Configuration>/<target framework>/`.

**The engine.** `main` builds against the engine's `master`, as CI does: with
[AdamantiumEngine](https://github.com/AdamantiumStudio/AdamantiumEngine) cloned beside this repository, in
`../AdamantiumEngine`, the projects reference the engine's source, so a change on either side is seen at once. A clone
of this repository alone builds against the published engine packages, of the version in
[`Directory.Build.props`](Directory.Build.props) - which `main` may have outgrown until the next release, since packages
are published only at a release, the engine's first. `-p:UseEngineSource=false` builds against the packages even with
the engine beside. A change that needs both is two pull requests: the engine's first, then this repository's, once the
engine's is merged.

**The sandbox.** `Adamantium.UI.Sandbox` is the application the framework is developed and tried in:

```
dotnet run --project Adamantium.UI.Sandbox -c Debug
```

The live preview runs the designer host, which builds into a folder of its own. After a change to shared code,
`./rebuild.ps1` rebuilds the sandbox and the host together, so the preview does not run stale code.

**Tests.**

```
dotnet build AdamantiumUI.Tests.sln -c Debug
dotnet test Tests/Adamantium.XamlTests/Adamantium.XamlTests.csproj -c Debug --no-build
dotnet test Tests/Adamantium.UITests/Adamantium.UITests.csproj -c Debug --no-build
dotnet test Tests/Adamantium.UI.GraphicsTests/Adamantium.UI.GraphicsTests.csproj -c Debug --no-build
```

CI has no GPU: it leaves out `Adamantium.UI.GraphicsTests` and the tests marked `[Category("Gpu")]`, so run them
yourself for a change in rendering. Close the sandbox first: while it runs, the rendering tests fail.

**The Rider plugin** is in [`editors/rider-auml`](editors/rider-auml), with its own README on building and running it.

## Pull requests

- Branch from `main` and target `main`, one change per pull request.
- The `build` check must pass: it builds both solutions with `-warnaserror` and runs the tests that need no GPU.
- A fix comes with a test that fails without it, wherever the defect can be reproduced in one.
- A change to the Rider plugin raises its `version` in `editors/rider-auml/build.gradle.kts`.
- The commit message says what the change does and why.

## Code style

Match the code around the change. In new and changed code:

- nullable reference types are off: no `string?`, no `!`, no `#nullable enable`;
- file-scoped namespaces, and one top-level type per file, named after it;
- private fields at the top of the class;
- braces around the body of every `if`, `for`, `foreach` and `while`, on lines of their own;
- collection expressions: `[a, b]` and `[]`, not `new[] { a, b }` or `Array.Empty<T>()`;
- American spelling, in names and in comments;
- comments in English and short, on the public API;
- in AUML, one attribute per line on an element with more than one; a `Setter` keeps `Property` and `Value` on one
  line.

## What we do not accept

- A workaround that hides a defect instead of fixing its cause: a check that skips the bad case, a delay that hides a
  race.
- Reformatting or restyling of code the change does not otherwise touch.
- A new dependency without an issue first: its license must be compatible with Apache-2.0, and it goes into
  [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
- Demo content in the project templates: they create what every application keeps, and nothing to delete.

## License

A contribution is licensed under [Apache-2.0](LICENSE), as section 5 of the license says; there is no separate
agreement to sign.
