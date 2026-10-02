# Changelog

Notable changes to the Adamantium UI packages, in the form of [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
All packages share one version.

## Unreleased

### Added

- `AdamantiumDesignerHost` names the designer host the build points the IDE's live preview to, in place of the one the
  package carries.
- `Adamantium.UI.Source.targets` in the repository root: a project in another repository builds on a checkout of this one
  instead of the package - the same references, generators and designer manifest - by importing it in place of the
  `PackageReference`.
- `AumlDirectiveInfo.Values`: the words a directive's value may be, for one that takes a fixed few (`x:KeepAlive`,
  `x:Load`, `x:Shared`, `x:CreateInDesignTime`). The build checks `x:KeepAlive` against the same list.

### Changed

- `IResolvedMember` has `IsStatic` and `IsPublic`.

### Fixed

- Completion of `x:` directives in the language server: after `{` and `{x:` it offers `x:Null`, `x:Static` and `x:Type`;
  inside `{x:Static}` a type and then its public static fields and properties, base types included; the values of
  `x:Load`, `x:Shared`, `x:CreateInDesignTime` and `x:KeepAlive`. All of these offered nothing before.

- `DockingArea` and `PaneHost` threw "Invalid size returned for Measure" when offered unbounded space - an `Auto` row
  of a `Grid`, a `StackPanel`, a `ScrollViewer`. They now measure to what their panes need.

## [0.1.0-alpha] - 2026-10-02

The first public alpha.

[0.1.0-alpha]: https://github.com/AdamantiumStudio/AdamantiumUI/releases/tag/v0.1.0-alpha
