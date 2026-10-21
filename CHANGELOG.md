# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **`add_clothing_item` now works even when VaM's runtime clothing creator is
  not initialised.** Clothing paths VaM already has in the person's library are activated
  directly (`"activated":true`) instead of being silently dropped, which is why adds used to
  "succeed" without any garment appearing.
- **`add_clothing_item` no longer lies about success.** It used to return
  `{"loading":true}` right after calling `LoadFromPath`, so an add that VaM silently dropped
  (its runtime clothing creator was never initialised because the person's Clothing tab had
  never been opened in that scene) looked identical to a working add. The tool now waits for a
  new worn item to appear and returns `ok:true` with its uid, or `ok:false` plus a
  `hint`/`libraryBefore`/`libraryAfter` explaining what to do.
- `list_clothing_items` only returned the person's whole clothing *library* (hundreds of
  unworn entries with an `active` flag). It now returns the worn items, a `worn` uid list and
  `activeCount`/`libraryCount`; `all=true` restores the full dump.
- `set_clothing_item_on` and `remove_clothing_item` accept a uid, a display name or the tail of
  a package path, and report the resulting state (`on`/`changed`, `removed` + `on` + `libraryCount`) instead of
  echoing the request. `remove_clothing_item` refuses to delete the clothing creator item.
- The clothing "creator" helper item is no longer listed, counted or treated as a garment by
  `list_clothing_items` / `add_clothing_item` (it was reported as "worn" and even as a
  successfully added item).
- `Invoke` now tolerates overloaded VaM methods instead of throwing
  `AmbiguousMatchException`, which fixes `set_hair_item_on`.

## [1.0.1] - 2026-08-21

### Changed
- Tool payloads carry real JSON types too: counts, coordinates, morph values, parameter
  min/max/default and `on`/`active` flags are numbers and booleans instead of quoted strings, and
  a null value is `null` rather than `"null"`. Timestamps and enum names stay strings, as do the
  parameters sent to the Hub API, which expects them that way.

### Fixed
- **Protocol values are now real JSON types.** VaM's SimpleJSON quotes everything it serialises,
  so `listChanged`, `subscribe`, `isError`, JSON-RPC error codes and — most importantly — the
  JSON-RPC `id` all went out as strings. Clients built on the official MCP SDK validate against
  the spec schema and match responses by id, so they rejected the handshake and fell back to the
  legacy SSE transport until they timed out. opencode could not connect at all; it can now.
- `status` reported `vaMVersion` from `Application.version`, which is the Unity player version
  ("1.0"). It now uses `SuperController.GetVersion()` and reports the actual VaM build.
- `scripts/deploy.sh` and `scripts/install-bepinex.sh` resolved `VAM_ROOT` one level too high
  (the scripts already `cd` to the repo root), so with the repo inside the VaM folder they wrote
  to the *parent* of the VaM install. Both now resolve it correctly and refuse to run against a
  directory with no `VaM_Data` in it.

## [1.0.0] - 2026-08-20

First public release: an MCP server that lives inside Virt-A-Mate as a BepInEx plugin.

### Added
- Streamable HTTP MCP transport (spec 2025-06-18) served from inside the VaM process, targeting net35.
- 63 tools covering scenes, atoms, persons, morphs, clothing, hair, poses, expressions, bone
  controls, camera, simulation, Hub browsing/downloads, skin subsurface colour and VaM plugin
  management (see [docs/TOOLS.md](docs/TOOLS.md)).
- Generic JSONStorable introspection (`list_atom_storables`, `list_storable_params`, `get_param`,
  `set_param`, `call_action`) so any VaM UI slider is reachable without a dedicated tool.
- Preset export/import, `.var` package asset scanning, and native plugin management.
- `eval_cs` escape hatch, disabled by default.
- MCP resources: `vam://status` and `vam://atoms`.
- `capture_view` can return the rendered PNG inline as an MCP image with `return_image=true`.

### Security
- The HTTP listener binds to loopback only and validates the `Origin` header (DNS-rebinding
  protection); CORS echoes only an already-validated loopback origin instead of `*`.
- Concurrent connections are capped and request sizes are bounded.

[Unreleased]: https://github.com/Rorical/VaMMCP/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/Rorical/VaMMCP/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Rorical/VaMMCP/releases/tag/v1.0.0
