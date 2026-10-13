# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The solution and all projects live under the `v2rayN/` subdirectory (not the repo root). SDK: .NET 10 (`10.0.1xx`).

```bash
# Tests (TUnit, Microsoft.Testing.Platform runner — see global.json)
dotnet test --project ./v2rayN/ServiceLib.Tests -c Release

# Single test / single class (MTP filters)
dotnet test --project ./v2rayN/ServiceLib.Tests -c Release -- --filter "Name==ShouldRunAsSudo_TunLaunchOnNonWindows_RequiresElevation"
dotnet test --project ./v2rayN/ServiceLib.Tests -c Release -- --filter "FullyQualifiedName~FmtHandlerTests"
dotnet run --project ./v2rayN/ServiceLib.Tests -c Release -- --list-tests   # list test names

# Build the app (as CI does): SelfContained publish per RID
dotnet publish ./v2rayN/v2rayN/v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -o ./out
dotnet publish ./v2rayN/v2rayN.Desktop/v2rayN.Desktop.csproj -c Release -r osx-arm64 -p:SelfContained=true -o ./out

# Whole solution
dotnet build ./v2rayN/v2rayN.sln -c Release
```

Product version lives in `v2rayN/Directory.Build.props`; all NuGet package versions are centralized in `v2rayN/Directory.Packages.props` (CPM).

## Architecture

A proxy GUI client (Xray / sing-box / mihomo and others) for Windows/Linux/macOS. Core principle: **all logic in ServiceLib, two UI shells on top of shared ViewModels**.

- **ServiceLib** — everything: configs, share-URI parsers, core config generation, singleton managers, ViewModels (ReactiveUI + `[Reactive]` source generators). References no UI framework.
- **v2rayN** — WPF (Windows-only, MaterialDesign). **v2rayN.Desktop** — Avalonia (cross-platform, Semi.Avalonia). The view layers mirror each other 1:1; dialogs go through `AppManager.WindowDialog` (an `IWindowDialog` implementation) + `ICloseable`; VM→View mapping lives in each project's `SimpleViewLocator`.
- **AmazTool** — self-upgrade helper (zip extraction with a path-traversal guard). **GlobalHotKeys** — vendored Win32 library. **ServiceLib.UdpTest** — UDP tests (NTP/DNS/STUN/MCBE) over SOCKS5 UDP ASSOCIATE. **ServiceLib.Tests** — TUnit.

### Main execution flow

`MainWindowViewModel.Reload()` is the central loop: `CoreConfigContextBuilder.BuildAll` (node validation, groups, pre-socks for TUN) → `CoreConfigHandler.GenerateClientConfig` → `CoreManager.LoadCore` (core process start/stop; sudo on non-Windows when TUN is on — decided from the `CoreConfigContext.IsTunEnabled` snapshot, never the live config) → `SysProxyHandler.UpdateSysProxy`. Concurrent Reloads are coalesced by a semaphore with `_hasNextReloadJob`.

### Core config generation

`Services/CoreConfig/{V2ray,Singbox}/*` — partial classes; each `Gen*` method mutates a C# mirror of the JSON schema deserialized from an embedded sample resource (`ServiceLib/Sample/*`, constants `Global.*Sample*`). Final modifiers: custom outbounds are injected via `{{tag}}/{{detour}}/{{interface}}` placeholders; a FullConfigTemplate is merged on top. Groups (PolicyGroup/ProxyChain) recursively expand into selector/urltest (sing-box) or observatory/balancer (Xray); chains wire outbounds through `detour` / `sockopt.dialerProxy`.

### Data

- `Config` (guiNConfig.json) — application state, ~25 item classes in `Models/Configs/`.
- SQLite `guiNDB.db` (sqlite-net-e) — ProfileItem/SubItem/RoutingItem/DNSItem/FullConfigTemplateItem/ProfileExItem/ServerStatItem; access via `SQLiteHelper.Instance`.
- `ProfileItem.ProtoExtra`/`TransportExtra` — JSON strings lazily deserialized into `ProtocolExtraItem`/`TransportExtraItem` records (with a cache). ConfigVersion migrations V2→V3→V4 live in `AppManager.MigrateProfileExtra` — add new protocol fields to the records, not to flat columns (the flat ones are `[Obsolete]`).
- Share-URI parsers — `Handler/Fmt/*`; importing any mixture — the cascade in `ConfigHandler.AddBatchServers` (base64 → plain → SIP008 → WireGuard conf → v2rayn:// inner → custom full-config).

### Cross-cutting conventions

- All UI strings go through `ResUI.*` (resx, 9 cultures; keys in `ServiceLib/Resx/`).
- Platform branches — `Utils.IsWindows()/IsLinux()/IsMacOS()` plus `[SupportedOSPlatform]` guards; P/Invoke stays inside the dedicated files.
- Singletons via `Lazy<T>` (`AppManager`, `CoreManager`, `ProfileExManager`, `StatisticsManager`, ...).
- Async feedback to the UI — the `Func<bool, string, Task> updateFunc` callback threaded down the stack; logging — `Logging.SaveLog` (a try/catch + log pattern in almost every method).
- Cross-layer events — `EventChannel<T>` (Rx) via the static `AppEvents`; between VMs — `EventChannel` properties on the VMs.
- ServiceLib namespaces are globally imported via each project's `GlobalUsings.cs`.
- Data paths: `StartupPath()` (env `V2RAYN_LOCAL_APPLICATION_DATA_V2=1` → LocalApplicationData), subdirs `guiConfigs/`, `bin/<core>/`, `binConfigs/`, `guiTemps/`.
- Tests: `CoreConfigTestFactory` swaps `_config` into AppManager via reflection; new core-config tests build on it.

## Tools: when to use what

- **fetch** — live content of a specific URL: web docs, changelogs, specs, raw HTML, localhost (WebFetch can't reach those). Not for library docs (that's context7) and not for "where to read" lookups (that's web search).
- **context7** — BEFORE writing any code against a library/framework (Avalonia, ReactiveUI, TUnit, sqlite-net, Downloader): exact signatures, configuration, version migrations. Even if you "remember the API" — the model's data may be stale. Order: `resolve-library-id`, then `query-docs`; one query — one concept. Not for business logic or general C# concepts.
- **serena** — semantic navigation and edits: `find_symbol`, `find_referencing_symbols`, `replace_symbol_body` — instead of grep+sed wherever exact symbol boundaries matter (this repo has same-named VMs/Views across two UI projects and partial classes in the config generators). File overview — `get_symbols_overview` rather than reading the whole file; at the start of a coding task — `initial_instructions`.
- **claude-mem** — memory across sessions. BEFORE design, debugging, review — `mem_search`: the repo architecture is already captured (projects/models, handler-layer, managers, core-config-generation, viewmodels, ui-layers). Don't re-solve what's solved. Durable lessons — `save_memory` (after a duplicate search); current to-do lists — `work_state_write`.

## Epics & tasks: use `br` (beads)

- Epics, tasks, and dependencies are tracked with the `br` CLI (agent-first issue tracker; SQLite + JSONL under `.beads/`). Do NOT use `tm`/bd or ad-hoc file trackers.
- Create: `br create "Title" -t epic|feature|task -p 1 --description-file <md>` — long markdown bodies always via `--description-file`, never inline shell quoting.
- Link: `br dep add <issue> <depends-on>` (issue becomes blocked by depends-on; epic parenting = task depends on epic). Inspect: `br dep tree <id>`, `br ready`, `br epic status`, `br list`.
- Close: `br close <id>`; search: `br search "<text>"`; comments: `br comments add <id> -t "<text>"`.
- Epic descriptions are immutable design contracts (requirements/anti-patterns); tasks adapt to reality and are created iteratively as work progresses, never as an upfront tree.
- `.beads/` stays local — exclude via `.git/info/exclude`, not `.gitignore` (avoid upstream diffs).

## Process

- **USE PARALLEL AGENTS WHEREVER POSSIBLE**: launch independent tasks/investigations/reviews as a single message with multiple Agent calls. Only in parallel where there are no shared files or state.
- Commits, code, and identifiers — in English. Commit attribution: `Co-Authored-By: Claude Code <noreply@anthropic.com>`.
- Principles: SOLID + KISS; code style — match the surroundings (explicit types, C# 12+ pattern expressions, records for immutable models).
- Changes touching the share-URI format or core config generation must come with a round-trip test in `ServiceLib.Tests/Fmt` / `ServiceLib.Tests/CoreConfig` — the existing suite enforces that every protocol in `Global.ProtocolShares` is covered by an export→import case.
