# Contributing

Thanks for your interest in CodeGraphToDgml. Bug reports, ideas and pull requests are welcome.

## Reporting bugs and asking for features

Open an [issue](https://github.com/guercheLE/CodeGraphToDgml/issues) with:

- the extension and Visual Studio versions,
- the command you ran and the symbol at the caret,
- the smallest C# or Visual Basic snippet that reproduces the problem, and what you expected to see.

Security problems go through the private channel described in [SECURITY.md](SECURITY.md), not through issues.

## Repository layout

| Project | Target | Builds on |
| ------- | ------ | --------- |
| `src/CodeGraphToDgml.Core` | netstandard2.0, no dependencies: graph model, DGML and Mermaid serializers | any OS |
| `src/CodeGraphToDgml.Roslyn` | netstandard2.0: call-site walkers, filters, dispatch | any OS |
| `src/CodeGraphToDgml.Vsix` | net48: Visual Studio commands, options, providers | Windows with the VS SDK |
| `tests/CodeGraphToDgml.Tests` | net10.0, MSTest: tests for Core and Roslyn | any OS |

## Building and testing

The libraries and tests build with the .NET 10 SDK on Windows, Linux or macOS:

```bash
dotnet test tests/CodeGraphToDgml.Tests/CodeGraphToDgml.Tests.csproj
```

With code coverage (Cobertura report under `TestResults/`):

```bash
dotnet test tests/CodeGraphToDgml.Tests/CodeGraphToDgml.Tests.csproj --settings tests/CodeGraphToDgml.Tests/coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
```

The VSIX needs Windows with Visual Studio 2022 and the *Visual Studio extension development* workload:

```powershell
msbuild CodeGraphToDgml.sln /t:Restore,Build /p:Configuration=Release
```

Press F5 on the VSIX project to try the extension in the experimental instance.

## Coding rules

- Follow `.editorconfig` (file-scoped namespaces, `var`, 4-space indent).
- `Nullable` is enabled everywhere and warnings are errors in Core, Roslyn and the tests: the build must stay at zero warnings.
- Package versions live only in `Directory.Packages.props`; the extension version lives only in `<Version>` in `Directory.Build.props`. Never edit a version in the manifest or in `source.extension.cs`.
- Keep Core free of dependencies and keep Roslyn free of Visual Studio types, so both stay testable without Visual Studio.
- Any change to call-site detection must keep the golden ordering tests (`FindCalleesAsync_GoldenOrdering_*`) green; they lock the order callees appear in.
- Changes to the Mermaid serializer must keep the legacy (no control flow) output byte-identical; the serializer tests guard this.
- C# and Visual Basic support move together: a new call shape in one walker needs its counterpart and a test in the other.

## Pull requests

1. Branch from `main`.
2. Add or update tests with the change.
3. Run the full test suite locally.
4. Use [Conventional Commits](https://www.conventionalcommits.org/) with a scope, for example `feat(sequence): ...`, `fix(vsix): ...`, `test(roslyn): ...`.
5. Add an entry under `## [Unreleased]` in `CHANGELOG.md` for user-visible changes.

CI (`.github/workflows/build.yml`) builds and tests the libraries on Linux and Windows and builds the VSIX on Windows; a pull request needs it green before merge.

By contributing you agree that your contribution is licensed under the project's [license](LICENSE.txt).
