# AI Coding Agent Instructions: SAUDICO Federate

**Purpose:** Optimize AI coding agent performance for the SAUDICO Federate project, minimizing token usage while maximizing code quality and architectural compliance.

**Version:** 1.0  
**Target:** LLM-based coding assistants (GitHub Copilot, Cursor, Claude, etc.)

---

## 🎯 Core Directive

**"Preserve Architecture, Minimize Tokens, Maximize Safety."**

When generating code for this project, you must strictly adhere to the established layered architecture, threading models, and safety guarantees defined in `MASTER_MIND.md`. Do not reinvent patterns; reuse existing implementations.

---

## 🧠 Context Optimization Strategy

### 1. Reference Only, Don't Paste
- **DO NOT** paste entire files into the chat context.
- **DO** reference specific file paths and line numbers when asking for changes.
- **Example:** "Update `src/Export/ViewBuilder.cs` lines 45-60 to add exclusion for OST_SecuritySystems."

### 2. Use Symbolic References
- Refer to classes and methods by name only. The agent should infer implementation from existing code.
- **Example:** "Implement `IAccAuthenticationService` in `AccAuthenticationService.cs` following the pattern of `PkceService`."

### 3. Leverage Existing Tests
- When unsure of behavior, refer to existing unit tests in `/tests` rather than asking for explanations.
- **Example:** "Check `ViewBuilderTests.cs` for the expected visibility logic before modifying."

### 4. One Change Per Prompt
- Isolate tasks to reduce context switching and hallucination.
- **Bad:** "Refactor the engine, fix the ACC auth, and update the UI."
- **Good:** "Fix the null reference exception in `Engine.cs` Phase 2 cleanup."

---

## 🏗️ Architectural Guardrails (Non-Negotiable)

### 1. Threading Boundaries
- **Rule:** Never generate code that accesses Revit API objects (`Document`, `Element`, `View`) outside `IExternalEventHandler.Execute()`.
- **Forbidden:** `await Task.Run(() => { doc.Regenerate(); })`
- **Allowed:** `await Task.Run(() => { http.GetAsync(); })` (Background thread)

### 2. ACC Isolation
- **Rule:** `src/SAUDICO.Federate.ACC` must NEVER reference `Autodesk.Revit.DB` or `Autodesk.Revit.UI`.
- **Check:** If adding a new file to ACC, verify imports contain only `System.*`, `Newtonsoft.Json`, or `System.Net.Http`.

### 3. Read-Only Guarantee
- **Rule:** No `Save`, `SaveAs`, `SynchronizeWithCentral`, `Publish`, or `RelinquishOwnership`.
- **Validation:** Before submitting code, mentally scan for these keywords. If found, reject and refactor.

### 4. Exception Containment
- **Rule:** All job-level operations must be wrapped in try-catch to prevent batch interruption.
- **Pattern:**
  ```csharp
  try { /* Operation */ }
  catch (Exception ex) {
      job.State = Failed;
      job.ErrorMessage = ex.Message;
      Log.Error(ex, "Job {JobId} failed", job.Id);
  }
  ```

---

## 📝 Code Generation Guidelines

### 1. Reuse Over Rewrite
- Before writing new code, search for existing similar implementations.
- **Example:** Need a new setting? Copy the pattern in `ExportSettings.cs` rather than inventing a new config system.

### 2. Minimal Diff Principle
- Generate only the changed lines, not the entire file.
- **Prompt:** "Show me the diff for adding `ExcludeSecuritySystems` to `ExportSettings`."

### 3. Explicit Typing
- Avoid `var` when the type isn't obvious from the right-hand side. Explicit types aid agent understanding and reduce ambiguity.
- **Preferred:** `NavisworksExportOptions options = new NavisworksExportOptions();`

### 4. Comment Intent, Not Mechanics
- Add comments explaining *why* a workaround exists (e.g., "Disabled due to Navisworks geometry bug"), not *what* the code does.

---

## 🚫 Common Pitfalls to Avoid

| Pitfall | Correction |
| :--- | :--- |
| **Global Annotation Hiding** | Do NOT use `view.AreAnnotationCategoriesHidden = true`. It breaks NWC export. Use specific category hiding instead. |
| **Invalid Navisworks Options** | Do NOT use `CurrentViewOnly` or `DivideViews`. Use `ExportScope.View` and `DivideFileIntoLevels`. |
| **Cloud Model Detach** | Do NOT use `DetachFromCentralOption` for ACC models. Must be `DoNotDetach`. |
| **Blocking UI Thread** | Do NOT perform HTTP calls or File I/O on the Revit API thread. Offload to `Task.Run`. |
| **Silent Failures** | Do NOT use empty `catch {}` blocks. Always log or rethrow. |

---

## 🔍 Verification Checklist (Pre-Submit)

Before finalizing any code generation, run this mental check:
1. [ ] **Architecture:** Does this respect the Layered Hexagonal structure?
2. [ ] **Threading:** Are Revit API calls confined to the External Event handler?
3. [ ] **Safety:** Is there any risk of saving/syncing the source model?
4. [ ] **ACC:** Does this introduce any Revit references into the ACC module?
5. [ ] **Error Handling:** Will a failure here stop the entire batch?
6. [ ] **Tokens:** Did I reference existing code instead of pasting it?

---

## 💡 Example Prompts for Maximum Efficiency

### Scenario 1: Adding a New Export Setting
**Efficient Prompt:**
> "Add a boolean property `IncludeRebar` to `ExportSettings.cs` (follow existing pattern). Then update `ViewBuilder.cs` to hide `OST_Rebar` if false, using `HideCategoryIfSupported` helper. Update `settings.json` template."

**Inefficient Prompt:**
> "Here is the whole ExportSettings class... [pastes 500 lines]... please add a property for rebar and tell me how to hide it in the view builder which looks like this... [pastes 800 lines]"

### Scenario 2: Fixing a Crash
**Efficient Prompt:**
> "In `Engine.cs`, Phase 2 (Document Opening), if `Opener.Open` throws for an ACC model, the batch stops. Wrap the call in a try-catch that sets `job.State = Failed` and continues to the next job. Preserve the exception message in `job.ErrorMessage`."

**Inefficient Prompt:**
> "My app crashes when opening ACC models. Here is the stack trace... [pastes trace]... and here is the Engine file... [pastes file]... fix it."

### Scenario 3: Refactoring for .NET 8
**Efficient Prompt:**
> "Update `Revit2025/Revit2025.csproj` to use .NET 8.0 SDK style. Ensure `Directory.Build.props` conditional compilation includes `NET8_0` define. Verify no .NET Framework-specific APIs are used in `Core`."

---

## 📚 Key Files for Context Anchoring

When starting a task, anchor your understanding on these files:
- **Architecture:** `MASTER_MIND.md` (Read first)
- **Entry Point:** `src/Revit2025/Host.cs`
- **Core Logic:** `src/Export/Engine.cs`
- **ACC Boundary:** `src/SAUDICO.Federate.ACC/Authentication/ApsAuthenticationService.cs`
- **Data Model:** `src/Core/Models.cs`

---

## 🏁 Final Instruction

**"When in doubt, read `MASTER_MIND.md` Section 3 (Workflow Engine) and Section 6 (Security Guarantees). Do not guess architectural intent."**

This document serves as the single source of truth for AI agents working on SAUDICO Federate. Adherence ensures high-quality, safe, and efficient code generation.
