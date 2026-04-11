## Functional Requirements (FR)

### Core Traversal
| # | Requirement |
|---|-------------|
| FR-01 | Traverse **upward** through a caller hierarchy from the symbol at the editor caret |
| FR-02 | Use Visual Studio/Roslyn APIs/SDKs against the current workspace/solution |
| FR-03 | Support symbols that are **methods, properties, and events** |
| FR-04 | Stop traversal when depth reaches **MaxDepth** |
| FR-05 | Stop and return when graph reaches **MaxNodeCount** |
| FR-06 | Allow user to **include/exclude properties** from traversal |
| FR-07 | Allow user to **include/exclude events** from traversal |
| FR-08 | Allow user to **include/exclude external symbols** (referenced assemblies) |
| FR-09 | Allow user to **include/exclude generated code** symbols |
| FR-10 | **Clamp MaxDepth** to a minimum of 1 before use |
| FR-11 | **Clamp MaxNodeCount** to a minimum of 1 before use |

### Graph Model
| # | Requirement |
|---|-------------|
| FR-12 | Every node must carry: **Id, Label, Kind, optional FilePath, optional Line, optional ProjectName** |
| FR-13 | Every link must carry: **SourceId, TargetId, Category** |
| FR-14 | Graph must support **upsert semantics** for nodes (same Id replaces prior entry) |
| FR-15 | Graph must support **set semantics** for links (no duplicates) |

### DGML Serialization
| # | Requirement |
|---|-------------|
| FR-16 | Produce valid **DGML XML** conforming to `http://schemas.microsoft.com/vs/2009/dgml` |
| FR-17 | New documents must have **`GraphDirection="TopToBottom"`** |
| FR-18 | Documents must include **category definitions** for Method, Property, Event, and Calls |
| FR-19 | `<Node>` elements must include `Id`, `Label`, `Category`, and optionally `FilePath`, `Line`, `Group` |
| FR-20 | `<Link>` elements must include `Source`, `Target`, and `Category` |
| FR-21 | Support **appending** (merging) new nodes/links into an existing DGML document |
| FR-22 | Support **replacing** the full node/link set of an existing document |
| FR-23 | When appending, **skip duplicate nodes** (deduplicated by Id) |
| FR-24 | When appending, **skip duplicate links** (deduplicated by Source+Target+Category) |
| FR-25 | `CreateEmptyText()` must produce a **standalone empty DGML string** |

### Visual Studio Integration
| # | Requirement |
|---|-------------|
| FR-26 | Command **"Traverse Up to DGML"** must appear in the **editor context menu** |
| FR-27 | Command must only be **visible/enabled for `.cs` or `.vb` files** |
| FR-28 | Expose a **"General" options page** under `Tools > Options > Call Hierarchy to DGML` |
| FR-29 | Options must be grouped into **Traversal, DGML, and UI** categories |

### Document Lifecycle
| # | Requirement |
|---|-------------|
| FR-30 | When no DGML document is open, **create a temp file** in `%TEMP%` named `CallHierarchy-{timestamp}.dgml` |
| FR-31 | Three **open behaviors** must be supported: `AlwaysAsk`, `AlwaysCreateNewTemporary`, `ReuseActiveIfOpen` |
| FR-32 | When `AlwaysAsk` and documents are open, show a **document picker dialog** |
| FR-33 | Optionally **activate (bring to front)** the DGML window after update |
| FR-34 | DGML results must be opened **without auto-saving** to disk |

### Progress & Feedback
| # | Requirement |
|---|-------------|
| FR-35 | Report traversal progress in the **VS status bar** |
| FR-36 | Optionally show a **cancellable modal progress dialog** |
| FR-37 | Progress must include: **stage name, depth, node count, symbol display name** |
| FR-38 | User must be able to **cancel** an in-progress traversal |
| FR-39 | Write detailed output (symbol, counts, target doc, errors) to a dedicated **Output window pane** |
| FR-40 | Show an **informational message** when no supported symbol is at the caret |
| FR-41 | Show **exception message** in a message box on unhandled errors |

### Architecture
| # | Requirement |
|---|-------------|
| FR-42 | Use an **interface** to allow additional language providers |
| FR-43 | **Separate core logic** (portable library) from VS host integration (VSIX) |

---

## Non-Functional Requirements (NFR)

### Platform & Compatibility
| # | Requirement |
|---|-------------|
| NFR-01 | Support **Visual Studio 2022 through VS 21.x** (all editions) |
| NFR-02 | Repository must support both **.sln and .slnx** solution formats |

### Dependencies
| # | Requirement |
|---|-------------|
| NFR-03 | Core library must have **zero NuGet package dependencies** |

### Code Quality
| # | Requirement |
|---|-------------|
| NFR-04 | All projects must enable **nullable reference types** |
| NFR-05 | All projects must use **`LangVersion=latest`** |
| NFR-06 | Node/link deduplication must use **ordinal (case-sensitive)** comparison |

### Threading & Responsiveness
| # | Requirement |
|---|-------------|
| NFR-07 | Package must support **background loading** to not delay VS startup |
| NFR-08 | Roslyn traversal must run **off the UI thread**; switch to main thread only when needed |
| NFR-09 | All VS shell API calls must be **guarded for the UI thread** |

### Output Format
| # | Requirement |
|---|-------------|
| NFR-10 | Temp DGML files must be written in **UTF-8** |
| NFR-11 | XML declaration must specify **`encoding="utf-8"` and `standalone="yes"`** |
| NFR-12 | Existing DGML must be parsed with **whitespace preservation** |

### Packaging
| # | Requirement |
|---|-------------|
| NFR-13 | Final VSIX must be built from the **Release configuration** |
| NFR-14 | Extension version is **0.1.2**, published by **Luciano Evaristo Guerche** |
