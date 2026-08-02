# Luoxia Unity Client

## Ownership

| Layer | Owner | This project |
|---|---|---|
| World truth / rules / packets | **Luoxia-Engine** | never reimplement |
| JSON contracts | `Luoxia-Engine/contracts` | deserialize + display |
| 2D UI / prefabs / Host | **Luoxia-Unity** | Session replica, intents, rendering |

## Layering (not a heavy MVC framework)

```
App (composition root)
  BridgeSessionClient + HttpBridgeTransport  → Engine POST /api/client-envelope
  SessionReplica                            → last full SessionView (+ dialogue.reply merge)
  PresentationRouter                        → presentation.frame / stage.* / dialogue.reply
  PlayerIntentRouter (IPlayerIntentSink)    → UI intents → ClientEnvelope
  MainWorldScreen
    HudWidget* / FeaturePanel* / Immersion overlays*
```

## Immersion shell (generic)

Driven only by `SessionView.lore`, `render_nodes`, and Bridge presentation messages. No plot strings in Host.

| Feature | Trigger | Notes |
|---|---|---|
| Character dossier | portrait / interaction_anchor click | Requires `lore` profile/hearsay for that `subject_entity_id`; else no entry |
| Location arrival | `player_location_entity_id` change | Scene crossfade + `lore_kind=arrival` chapter |
| Speaker portrait | dialogue selection / last turn | Matches portrait `render_nodes` by subject |
| EventCard open | `presentation.frame` → `narrative.show` | Page-turning segment queue |
| Stage shell | `stage.open/update/close` | Fullscreen overlay + visible_context text |
| Nightfall | `day_cycle.day` increment | `lore_kind=nightfall` chapter |

`SessionView.lore` / `player_location_entity_id` are client-forward DTOs; Engine SessionViewProjector may still need to ship them. UI is null/empty-safe.

## Engine modes (LuoxiaClientBootstrap)

**No Mock / offline play.** Missing `sessionId` → Fatal.

| Mode | Behavior |
|---|---|
| `EngineWithInitialView` | Provision seeds ServerEnvelope JSON, then `client.ready` |
| `EngineReadyOnly` | `sessionId` + `client.ready` against `engineBaseUrl` |

Local play open: first-time `Luoxia/Play/Configure Local Provision`; daily one-click `Luoxia/Play/Provision Local And Play` (ensures Engine + `start:provision`, provisions, saves, enters Play). Provision-only remains `Luoxia/Play/Provision Local`. **Host opens worlds only via the Deployment provision gateway contract** (`Luoxia-Deployment/contracts/provision-gateway.v1.md`); never through Engine `client-envelope`. Pack selection is Deployment-owned — Host never hardcodes `pack_id`, world/story names, or content branches. Bridge uses Engine `POST /api/client-envelope`.

`runtime.kernel.model_dispatch_ambiguous` during provision is terminal (gateway contract): player copy 「开局未完成：世界导演未能就位…」; retry = new Provision Local only (never Resync/poll the blocked model).

### ProtocolError.recoverability → Host behavior

Host maps `protocol.error.recoverability` uniquely — **never** special-case by `code` strings outside this table:

| recoverability | Host behavior |
|---|---|
| `retry` | Idempotent resend of the **same** ClientEnvelope / `command_id` **once**; second failure → user-visible prompt (no silent loop) |
| `resync` | Silent `session.resync_request`, then rebuild UI from the new SessionView |
| `reconnect` | Rebuild session connection (new HTTP transport + re-attach), then resync/ready |
| `fatal` | Terminal full-screen error (same style as provision fail); only path back is Provision / open |

Wire: `BridgeSessionClient.HandleProtocolError` → deferred recovery → Bootstrap / `MainWorldScreen` / `SessionFatalOverlay`.

### LocalizedText locale Lookup

Host display locale = the same value sent as `dialogue.start` / `dialogue.continue` `locale` and provision `player_name.locale` (Bootstrap `playerLocale` / EditorPrefs `Luoxia.Provision.PlayerLocale`).

`LocalizedTextDto.Resolve` uses RFC 4647 Lookup: exact → truncate language-range subtags → prefix match on available keys; if no match and the map has **exactly one** key, use it; otherwise visible `「[缺失本地化]」`. Never dictionary-order guess.

Set Inspector:

- `engineBaseUrl` e.g. `http://127.0.0.1:8000`
- `sessionId` from gateway / admin session open
- `worldId` required for `map.move`
- `playerLocale` required Host display / dialogue locale
- `initialServerEnvelopesJson` optional ServerEnvelope array from server

## UI assets

- Source: `Luoxia-Engine/UI/assets/map` → `Assets/Art/UI/Map`
- Menu **Luoxia/UI/Reimport Map Sprites (9-slice)** for borders
- Menu **Luoxia/UI/Build Main World Screen** builds scene + list prefabs

## Display (portrait)

| Setting | Value |
|---|---|
| Design resolution | **1080 × 1920** (9:16) |
| Default orientation | Portrait only |
| CanvasScaler | Scale With Screen Size, match **width** |
| Design root | `DesignRoot` 1080×1920 under canvas |
| CJK font | `Assets/Art/UI/Fonts/LuoxiaCJKSource.ttf` (SimHei) via uGUI Text |
| Standalone | Windowed 1080×1920 |
| Runtime | `PortraitScreenPolicy` on MainWorldCanvas |

Menu: **Luoxia / UI / Build Main World Screen** (rewires all map slices)  
Menu: **Luoxia / Display / Apply Portrait Project Settings**  
Menu: **Luoxia / Display / Set Game View 1080x1920**

### UI art mapping (map pack)

Used for main shell: minimap cloud ring + map face, sun/weather, event badge, bottom gradient/mist/lotus, tab chrome, input 9-slice, send/smile, avatar frames, event row separator + choice button.

Reserved for later **event detail modal**: `panel_event_modal_9slice`, `button_event_close`, `deco_event_title`, dividers, postpone, overlay fade, chat badge.

## Next

- Real client_build_digest from CI
- StageModule Host timeline path (beyond StageShell outcome buttons)
- Full RFC 8785 JCS for evidence_digest (MVP uses compact JSON UTF-8 SHA-256)

## MVP polish (current)

| Item | Behavior |
|---|---|
| Client sequence | Sole owner: `ClientEnvelopeFactory.AllocateSequence` (Bridge + Intent share one factory) |
| `interaction_kind` | Required on `dialogue.start/continue`; default `dialogue` |
| Cross-day dialogue | `dialogue.continue` only if thread `day` matches `day_cycle.day`; else Host issues `dialogue.start` |
| CommandGate UI | Pending disables send / card / end-day / map; failures via `CommandFeedbackHud` |
| Stage outcome | `StageShellOverlay` buttons from `visible_context.outcome_options` / `outcomes` → `stage.outcome_proposal` |
| Tab marker | Follows active feature tab |
| End day | `endDayButton` → `player_day.end` |
| Dead chrome | Smile / AP+ are Image-only (no Button) |
| Map | Destination list from visible arrival lore → `map.move` |
| Anchors | Diff-reuse by `node_id` |
| Fade | Feature / Dossier / Narrative 0.15–0.25s |
| Content hash | `StreamingAssets/LuoxiaHash` = opaque Deployment export; resolve only by `content_hash`; miss = visible error |

### StreamingAssets / LuoxiaHash

Opaque Deployment export (`hash-index.json` + `files/<hash>.*`). Replace when the served content pack’s presentation assets change:

```bash
cd ../Luoxia-Deployment && npm run export:unity-hash
```

`StreamingAssetsHashSpriteResolver` looks up digests only — never `asset_id` prefixes or pack-specific paths. Index metadata may list opaque ids for operators; Host code must ignore them. Empty index is legal.

Menus: **Luoxia/UI/Ensure MVP Shell**, **Luoxia/Assets/Ensure Hash Index Scaffold**, **Luoxia/UI/Build Main World Screen**
