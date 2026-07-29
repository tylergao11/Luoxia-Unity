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
  SessionReplica                            → last full SessionView
  PlayerIntentRouter (IPlayerIntentSink)    → UI intents → ClientEnvelope
  MainWorldScreen
    HudWidget* / FeaturePanel* / ListItemView*
```

## Engine modes (LuoxiaClientBootstrap)

| Mode | Behavior |
|---|---|
| `MockOnly` | Local fake SessionView (no network) |
| `EngineWithInitialView` | Paste ServerEnvelope JSON (or SessionView), then optional `client.ready` |
| `EngineReadyOnly` | `sessionId` + `client.ready` against `engineBaseUrl` |

Set Inspector:

- `engineBaseUrl` e.g. `http://127.0.0.1:8000`
- `sessionId` from gateway / admin session open
- `worldId` required for `map.move`
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

- Asset resolve by `content_hash`
- Map destination picker UI
- Real client_build_digest from CI
