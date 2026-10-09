# Adamantium AUML — Rider / IntelliJ plugin

A packaged plugin so end users get everything with **zero settings**: install it and `.auml`
files are XML-highlighted and get completion + diagnostics from the Adamantium AUML language
server. Built on **LSP4IJ** (installed automatically as a dependency).

## Prerequisites

- **JDK 21** (already installed).
- **IntelliJ IDEA Community Edition** — recommended for building (it provides Gradle and the
  wrapper, and lets you run the `buildPlugin` task). You can also build from the CLI if you have
  Gradle.
- **LSP4IJ** plugin installed in Rider (already done) — needed at runtime; the build also
  references it.
- **.NET 10 SDK** on PATH — `buildPlugin` publishes the language server automatically
  (`dotnet publish`) and bundles it inside the plugin. End users need only the **.NET 10 runtime**.

## 1. Set versions (one-time)

Edit `gradle.properties`:
- `platformVersion` / `sinceBuild` — the **Rider** version the plugin is built against, which is also
  the oldest one it installs in (Rider 2026.2 → `2026.2` / `262`). The first build downloads that
  Rider's SDK.
- `lsp4ijPlugin` — set to the LSP4IJ version you have installed
  (Rider → Settings → Plugins → LSP4IJ → version).

Bump `version` in `build.gradle.kts` with every change you hand out, so the installed build can be told
apart from the previous one.

## 2. Build

**In IntelliJ IDEA Community (easiest):** open this folder (`editors/rider-auml`), let Gradle
sync, then run the Gradle task **`buildPlugin`** (Gradle tool window → Tasks → intellij platform).

**From the CLI** (needs the Gradle wrapper — IDEA creates it, or run `gradle wrapper` once):
```
./gradlew buildPlugin
```

Either way the result is: `build/distributions/adamantium-auml-<version>.zip`.

**Tests:** `./gradlew testIdea`. They run in IntelliJ IDEA of the same version: Rider's test fixture needs a solution
and its backend, and what they check - typing in `.auml` files, the highlighting filter - is the platform's XML, the
same in both.

## 3. Install in Rider

Settings → Plugins → ⚙ → **Install Plugin from Disk…** → select the zip → restart.

Open any `.auml` file: it should be XML-highlighted, and completion (`Ctrl+Space`) + red-squiggle
diagnostics should work — **no manual file-type or server configuration**.

## Notes

- The **language server is bundled inside the plugin**: `buildPlugin` runs `dotnet publish`
  (framework-dependent) and packs the output as `/server/server.zip`; on first use the plugin
  unpacks it to a per-version cache dir and launches it. End users need only the **.NET 10 runtime**.
- For local development, set the `ADAMANTIUM_AUML_SERVER` environment variable to a built
  `Adamantium.UI.LanguageServer.exe` to skip the bundled copy.
- After changing the server, rebuild the plugin (the publish reruns) and bump its version. The cached copy
  is keyed by the plugin version and the bundled server's content hash, so it is re-extracted by itself.

## Live preview

- A project on the **`Adamantium.UI` package** needs no setup. Every build writes `obj/adamantium.designer.json`,
  which names the designer host the package carries and the project's own build output. The plugin runs that host
  with `dotnet exec` on the project's dependency list, from a copy of the output, so the build is never locked. Build
  the project once before the first preview.
- A project on the **UI's own sources** has no such file. Set `ADAMANTIUM_DESIGNER_HOST` to a built
  `Adamantium.UI.Designer.Host.exe`. The `runIde` sandbox sets it by itself.

## Publishing to JetBrains Marketplace

1. **First version, by hand.** Sign in to plugins.jetbrains.com and upload `build/distributions/adamantium-auml-<version>.zip`
   under the Adamantium Studio organization. JetBrains reviews a new plugin before it appears.
2. **Later versions.** Create a token in the Marketplace profile (My Tokens), then run
   `PUBLISH_TOKEN=<token> ./gradlew publishPlugin`.

Update `<change-notes>` in `plugin.xml` for every published version: Marketplace shows them on the plugin page and in
the IDE's update dialog.
