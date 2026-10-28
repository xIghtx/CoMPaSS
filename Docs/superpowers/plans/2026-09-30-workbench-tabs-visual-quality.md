# CoMPaSS Tabbed Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reorganize the Unity IMGUI workbench into five clear tabs and improve legibility, responsive layout, map clarity, and first-run guidance with a charcoal-gray visual system.

**Architecture:** Keep the existing `MapfWorkbenchController` partial class and simulation state as the single owner of experiment state. Add tab navigation and page composition, then move existing guided, setup, replay, campaign, report, and guide controls into their matching views without changing MAPF algorithms or resetting execution state during tab changes.

**Tech Stack:** Unity 6.4.0f1, C#, Unity IMGUI, existing CoMPaSS MAPF algorithms and report exporters.

**Spec:** `Docs/superpowers/specs/2026-09-30-workbench-tabs-visual-quality-design.md`

## Global Constraints

- Keep Unity 6.4.0f1 compatibility.
- Keep runtime UI on Unity IMGUI; do not migrate to UI Toolkit or split the UI into scenes.
- Keep MAPF algorithms, run models, and report formats unchanged.
- Keep five tabs: 시작·프리셋, 실험 설정, 실행·리플레이, 결과, 사용법.
- Preserve current experiment and replay state across tab changes.
- Use charcoal and layered gray surfaces with restrained warm/green status accents; blue must not be the primary accent.
- Keep existing SVG and high-resolution PNG exports.
- Do not add or run automated tests as part of this plan.

## Files and Responsibilities

- Modify `Assets/Scripts/UI/MapfWorkbenchController.cs`: shared UI palette and font style, active tab state, header/nav rendering, and remove the single whole-window scale that degrades text at some sizes.
- Create `Assets/Scripts/UI/MapfWorkbenchTabs.cs`: tab enum, page router, tab-local scroll state, and top-level responsive page compositions.
- Modify `Assets/Scripts/UI/MapfWorkbenchGuide.cs`: beginner start/preset and help content, with clear next-step actions.
- Modify `Assets/Scripts/UI/MapfWorkbenchExecution.cs`: split continuous-campaign configuration from progress display without changing execution logic.
- Modify `Assets/Scripts/UI/MapfWorkbenchReports.cs`: show report and image export controls in Results, retaining current exporter calls.
- Modify `Assets/Scripts/UI/MapfWorkbenchResearch.cs`: keep playback map zoom/follow controls within the playback view and preserve their state.

## Review Focus

These are important edge conditions to inspect during implementation. Per the explicit no-test constraint above, this plan adds no automated test files or test commands.

- Window resized below the previous 1500×850 layout baseline: navigation, primary action, and page content remain reachable.
- DPI missing or zero: font and control sizing use a stable fallback.
- Switch tabs during planning, playback, CBS replay, or report capture: existing operation and state are not reset or duplicated.
- No result exists: Results offers a clear action to start an example instead of an empty panel.
- Dense 64×64 map and 32 agents: zoom controls and selection remain usable while map overview is available.

---

### Task 1: Add shared tab navigation, charcoal theme, and responsive sizing

**Files:**
- Modify: `Assets/Scripts/UI/MapfWorkbenchController.cs`
- Create: `Assets/Scripts/UI/MapfWorkbenchTabs.cs`

**Interfaces:**
- Produces `private enum WorkbenchTab { Start, Setup, Playback, Results, Help }`.
- Produces `private WorkbenchTab activeWorkbenchTab` with `Start` as the initial view.
- Produces `private void DrawWorkbenchNavigation()` and `private void DrawWorkbenchTab()` as the shared navigation and dispatch entry points.
- Consumes existing state fields and page methods from the controller partials.

- [ ] Add the `WorkbenchTab` enum, active tab, and independent scroll positions in `MapfWorkbenchTabs.cs`.
- [ ] Replace `DrawHeader`'s guided/research mode toggle with five labeled tabs and a persistent compact run-status indicator.
- [ ] Replace the unconditional sidebar/workspace layout in `OnGUI` with `DrawWorkbenchTab()` while preserving shared execution update and preview refresh behavior.
- [ ] Replace the one-size `GUI.matrix` viewport scale with a safe responsive layout strategy, including a nonzero-DPI fallback and a minimum readable font/control size.
- [ ] Centralize charcoal, panel, border, primary text, muted text, selected, success, warning, and conflict colors; remove blue-led styling while keeping status meaning legible without color alone.

### Task 2: Build the Start and Experiment Setup pages

**Files:**
- Modify: `Assets/Scripts/UI/MapfWorkbenchTabs.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchGuide.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchController.cs`

**Interfaces:**
- Produces `private void DrawStartTab()` and `private void DrawSetupTab()`.
- Produces `private void DrawCampaignSetupControls()` from the current configuration portion of `DrawCampaignControls()`.
- Reuses `UseGuidedExample(int kind)`, `GenerateMap()`, `RandomizeScenario()`, `RandomizeAgentPositions()`, `DrawPrimalConnection()`, `DrawAgentEditor()`, and existing integer-field edit logic.
- Start-to-setup action sets `activeWorkbenchTab = WorkbenchTab.Setup`; setup-to-run action sets `activeWorkbenchTab = WorkbenchTab.Playback` then invokes `RunSelected()`.

- [ ] Present the three existing examples as clearly named preset cards with their purpose, expected lesson, and one “예제로 실험 시작” action.
- [ ] Show a short numbered first-run flow and the map/agent randomization choices without exposing advanced controls on the Start page.
- [ ] Move map, obstacle, seed, agent, algorithm, search-limit, and advanced controls into a vertically scrollable Setup page; keep preview and map editing alongside settings where the available width permits.
- [ ] Separate basic and advanced settings and retain all existing configuration controls and values.
- [ ] Put fixed-algorithm continuous experiment parameters in Setup, keeping the current algorithm fixed across a campaign.
- [ ] Make the primary action explicit: navigate to Playback and start the selected algorithm; preserve a visible path back to settings.

### Task 3: Build the Playback page and state-preserving map workspace

**Files:**
- Modify: `Assets/Scripts/UI/MapfWorkbenchTabs.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchController.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchResearch.cs`

**Interfaces:**
- Produces `private void DrawPlaybackTab()`.
- Reuses `DrawPlaybackBar()`, `DrawGuidedToolbar()`, `DrawResearchToolbar()`, `DrawGrid()`, `DrawLiveInspector()`, `followSelectedAgent`, and existing timeline/CBS replay state.

- [ ] Move replay controls, map, legend, selected-agent decision details, CBS trial replay, and current run status into Playback.
- [ ] Make the map use remaining content space with a persistent “전체 지도” control and existing wheel zoom/pan/follow actions.
- [ ] Ensure viewport resize and tab navigation change only the view and do not reset execution, selected agent, current step, or replay mode.
- [ ] Adapt the side inspector and map layout to narrow windows by collapsing the inspector into a scrollable lower/secondary section instead of clipping it.
- [ ] Keep the run status and a clear route to Results visible after completion.

### Task 4: Build Results and Help pages, then integrate reports and campaigns

**Files:**
- Modify: `Assets/Scripts/UI/MapfWorkbenchTabs.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchGuide.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchController.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchExecution.cs`
- Modify: `Assets/Scripts/UI/MapfWorkbenchReports.cs`

**Interfaces:**
- Produces `private void DrawResultsTab()` and `private void DrawHelpTab()`.
- Produces `private void DrawCampaignProgress()` from the progress/resume portion of the current `DrawCampaignControls()`.
- Reuses existing `DrawResults()`, `DrawCampaignProgress()`, `DrawReportControls()`, `ExportMap`, `ExportReportBundle`, and report directory/status messages.

- [ ] Place last-run summary, comparison, collision/recovery/CBS logs, and continuous-experiment progress/resume on Results with independent scrolling.
- [ ] Move SVG, PNG, CSV, JSON, and HTML export actions to Results; keep existing formats and add clear save-location/completion feedback in the same page.
- [ ] Show a useful no-results state with an example-start shortcut when no experiment has been run.
- [ ] Create a concise Help page covering the first run, map legend, map controls, agent selection/follow, algorithm differences, and result export.
- [ ] Make all five tabs reachable on a narrow viewport and preserve page-local scroll offsets between tab switches.

### Task 5: Update user-facing project notes

**Files:**
- Modify: `README.md`
- Modify: `구현 기록.md`

**Interfaces:**
- No runtime interface changes.

- [ ] Update launch/use instructions to match the five-tab flow and explain where results and exports appear.
- [ ] Record the UI and visual-quality changes in the implementation log after they are implemented.

---
