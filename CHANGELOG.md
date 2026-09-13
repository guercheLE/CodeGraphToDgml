# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). Each entry links the Marketplace release to the git tag.

## [Unreleased]

### Fixed

- Traverse Down (DGML and sequence diagram) now follows virtual and abstract overrides in derived types using `Overrides` links, as the README already described.
- `Overrides` links have a category definition and a style; they used to render as unstyled default links.
- Appending into a DGML document that has no `Styles` element now adds the styles instead of leaving the graph unstyled forever.
- The VSIX manifest version and `Vsix.Version` had drifted from `Directory.Build.props`; the version is now generated from a single source at build time.
- README and requirements document refreshed (Roslyn project in the layout, output location under `.vs`, References has no depth limit).

### Added

- Marketplace metadata in the VSIX manifest: icon, preview image, license, more-info and release-notes links, tags.
- The DGML editor component is declared as an installation prerequisite, and a one-time warning explains how to install it when it is missing at run time.
- GitHub Actions workflow: build, test, VSIX artifact, GitHub release on `v*` tags, and a manually approved Marketplace publish job.
- Central package management (`Directory.Packages.props`), `.editorconfig`, Dependabot configuration, and this changelog.

## [0.13.2] - 2026-07-19

### Added

- Sponsorship callout in the README and `FUNDING.yml`.

## [0.13.1] - 2026-07-17

### Fixed

- Sequence diagrams: split-frame and root participants are declared in every continuation part.

## [0.13.0] - 2026-07-17

### Changed

- Default "Max messages per diagram" lowered from 100 to 60.

### Fixed

- Sequence diagrams: the message cap is enforced inside oversized calls and tiny tail parts are merged.

## [0.12.0] - 2026-07-03

### Added

- Sequence diagrams: oversized sub-parts are split recursively (1C -> 1C1/1C2).
- Sequence diagrams: return arrows are labeled with the call name and declared return type.

### Changed

- Generated DGML and Markdown output is written under `{SolutionDir}\.vs\CodeGraphToDgml\` instead of `%TEMP%`.
- DGML target selection, message boxes, and node building are shared across the operation services.

## [0.11.0] - 2026-07-02

### Added

- Call-graph detection of constructors, event raises, property writes, and wrapped delegates.

### Fixed

- Sequence diagrams: activations are balanced in split parts and the parent-call arrow spans the split.

## [0.10.0] - 2026-06-30

### Added

- `CodeGraphToDgml.Roslyn` project so call-graph logic is testable without Visual Studio.
- Detection of deferred calls, nesting of fluent chains, and self-calls for well-known framework methods.
- Sequence diagrams: business-flow-aware splitting and a «Caller» activation bar for entry methods.

## [0.9.1] - 2026-06-26

### Fixed

- Sequence diagrams: activation bars in split sub-segments and chained call order.

## [0.9.0] - 2026-06-26

### Added

- Sequence diagrams: global numbering, flow-based segments, and continuation banners.

## [0.8.0] - 2026-06-26

### Added

- Sequence diagrams: large diagrams are split into depth-range sections.

### Changed

- Default "Max participants per diagram" lowered from 50 to 15.

### Fixed

- Progress dialog getting stuck, unresponsive cancel, and Mermaid `maxTextSize` exceeded.

## [0.7.0] - 2026-06-26

### Added

- Sequence diagrams: interface resolution, correct return arrows and call order, `autonumber` option.

## [0.6.0] - 2026-06-26

### Added

- Sequence diagrams: stacked activation bars option.

### Changed

- Compiler warnings and messages cleaned up.

## [0.5.0] - 2026-06-25

### Added

- "Traverse Down to Sequence Diagram" command producing Mermaid Markdown or HTML.
- External assembly nodes grouped under an "External Symbols" container.
- DGML schema gaps closed across all commands.

## [0.4.6] - 2026-06-24

### Changed

- SDK-style VSIX build via `VSSDKBuildToolsAutoSetup`.

### Fixed

- Reversed `Contains` link when the default namespace wraps an ancestor namespace.

## [0.4.0] - 2026-04-24

### Changed

- DGML document picker rewritten in WPF.
- Namespace handling and containment links hardened (0.4.1 to 0.4.5 follow-up fixes).

## [0.3.0] - 2026-04-20

### Added

- "Traverse Down to DGML" command with interface implementation and override links.
- `IDgmlSchemaProvider` for categories and styles; cancellable wait dialog for progress.
- Container nodes can start collapsed.

## [0.1.0] - 2026-04-13

### Added

- Initial release: "Traverse Up to DGML" and "All References to DGML" from the editor context menu, with append/replace into an existing DGML document.

[Unreleased]: https://github.com/guercheLE/CodeGraphToDgml/compare/v0.13.2...HEAD
[0.13.2]: https://github.com/guercheLE/CodeGraphToDgml/compare/v0.13.1...v0.13.2
[0.13.1]: https://github.com/guercheLE/CodeGraphToDgml/compare/v0.13.0...v0.13.1
[0.13.0]: https://github.com/guercheLE/CodeGraphToDgml/releases/tag/v0.13.0
[0.12.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/616ba9b
[0.11.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/f70750f
[0.10.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/6bae639
[0.9.1]: https://github.com/guercheLE/CodeGraphToDgml/commit/f6e6088
[0.9.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/a395549
[0.8.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/f7db0ef
[0.7.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/7d38c22
[0.6.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/d39b2c5
[0.5.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/9ae6c89
[0.4.6]: https://github.com/guercheLE/CodeGraphToDgml/commit/dc8d273
[0.4.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/997b40a
[0.3.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/044a663
[0.1.0]: https://github.com/guercheLE/CodeGraphToDgml/commit/563c6d1
