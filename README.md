# CallHierarchyToDgml

Call Hierarchy to DGML is a Visual Studio extension that traverses callers upward from the symbol at the editor caret and writes the result into a DGML graph.

## Features

- traverses upward from C# and Visual Basic methods, properties, and events
- limits traversal by maximum depth and node count
- filters properties, events, external symbols, and generated code
- appends to or replaces an open DGML document
- creates a temporary DGML document when no target document is open
- reports progress in the status bar and a cancellable modal dialog
- writes execution details to a dedicated Output window pane

## Solution Layout

- `src/CallHierarchyToDgml.Core`: dependency-free graph and DGML logic
- `src/CallHierarchyToDgml.Vsix`: Visual Studio host integration and Roslyn traversal
- `tests/CallHierarchyToDgml.Tests`: unit tests for the portable core library

## Build

Build the extension from the Release configuration to produce the VSIX package:

```powershell
dotnet build .\CallHierarchyToDgml.sln -c Release
```

The repository includes both `CallHierarchyToDgml.sln` and `CallHierarchyToDgml.slnx`.

The generated VSIX is written to `src\CallHierarchyToDgml.Vsix\bin\Release\net48\CallHierarchyToDgml.Vsix.vsix`.

## Manual Test Matrix

| Area | Scenario | Expected Result |
|---|---|---|
| Command visibility | Open a `.cs` or `.vb` file and right-click in the editor | `Traverse Up to DGML` is visible and enabled |
| Command filtering | Open a non-C# and non-VB file and right-click in the editor | Command is hidden or disabled |
| Supported symbols | Place caret on a method, property, and event in separate checks | Traversal starts successfully for each supported symbol kind |
| Unsupported caret target | Place caret on whitespace, namespace, class name, or local variable | Informational message is shown and no traversal runs |
| Depth limit | Set `Maximum depth` to `1` and run traversal on a symbol with multiple caller levels | Only direct callers are included |
| Node limit | Set `Maximum node count` to `1` or `2` and run traversal | Traversal stops when the node cap is reached |
| Symbol inclusion | Toggle `Include properties`, `Include events`, `Include external symbols`, and `Include generated code` | Result graph respects each option |
| DGML append mode | Open an existing DGML file, choose append mode, run the command twice | Existing graph content is preserved and duplicate nodes/links are not added |
| DGML replace mode | Open an existing DGML file, choose replace mode, run the command | Existing node/link content is replaced by the new traversal result |
| Target selection | With one or more DGML docs open, test `AlwaysAsk`, `AlwaysCreateNewTemporary`, and `ReuseActiveIfOpen` | Target document behavior matches the selected option |
| Temp document creation | Run the command when no DGML document is open | A temp `CallHierarchy-{timestamp}.dgml` document is created in `%TEMP%` |
| Window activation | Toggle `Activate result document` and rerun | DGML document is activated only when the option is enabled |
| Progress and cancel | Start a traversal large enough to observe progress, then cancel | Status bar updates are shown and cancellation stops the operation cleanly |
| Output logging | Run traversal with `Show detailed output` enabled | Output pane shows resolved symbol, counts, target document, and errors when applicable |
| Packaging | Build Release and inspect the output folder | A VSIX is produced and can be installed into a VS 2022 Experimental Instance |
