### Changed

- The markup generator and the language server read `.alang` files with the engine's `Adamantium.Localization`
  (`LanguageFileParser` moved there from the generator), so they also build from the engine's packages. The table
  generator is no longer in the UI's analyzers: it goes to an application as a dependency of `Adamantium.UI`, as the
  MVVM generator does, and from source through `Adamantium.UI.Source.targets`.
- An `AdamantiumEngineReference` takes the role `HostedGenerator` for a generator a project runs itself: from source a
  project reference, from packages the assembly in the package's analyzers, downloaded and never run on the project.
  The language server and XamlTests take the table generator so.
