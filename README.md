# Luoxia-Unity

Luoxia 2D client (Unity 2022.3 LTS + URP). Server authority lives in **Luoxia-Engine**; this project is the Client Runtime Host (UI, SessionReplica, Bridge transport).

## Requirements

- Unity **2022.3.62f3** (or compatible 2022.3 LTS)
- Required: Engine at `http://127.0.0.1:8000` + Deployment provision at `:8010`

## Open

1. Open `C:\Ai\Luoxia-Unity` (or this clone) in Unity Hub.
2. Open scene `Assets/Scenes/MainWorld.unity`.
3. Game view: **1080×1920** portrait (menu **Luoxia → Display → Set Game View 1080x1920**).
4. **禁止离线 / Mock**。先起 Engine + `npm run start:provision`，再 **Luoxia → Play → Configure Local Provision**（首次），然后 **Luoxia → Play → Provision Local**，再 Enter Play。Host **只通过 Deployment provision gateway 合同**（`Luoxia-Deployment/contracts/provision-gateway.v1.md`）开世界；不绑定具体内容包；开哪包由 Deployment provision 决定。

## StreamingAssets / LuoxiaHash

`Assets/StreamingAssets/LuoxiaHash` is an **opaque Deployment export** (hash files + `hash-index.json`). Host resolves sprites **only** by `content_hash` from SessionView / render nodes — never by `asset_id` strings or pack story.

When the active content pack’s presentation assets change, re-export from Deployment:

```bash
cd ../Luoxia-Deployment
npm run export:unity-hash
```

Do not hand-edit paths as identity; hash is the only key. Empty index is legal (misses show explicit errors, no fake art).

**Provision Local** opens whatever pack Deployment provision serves. Host never hardcodes pack story, world names, or content-pack branches. AP `daily_capacity` and card costs come from ContentBundle `event_budget`; Host only projects `SessionView.event_budget` and never hardcodes capacity or spend tables.

If provision fails with `runtime.kernel.model_dispatch_ambiguous`（世界导演未能就位）：本局作废，Editor 显示明确失败并只允许重新 **Provision Local**（全新 world）；禁止轮询/自动重发被阻塞的模型。Play Accept 将同款文案写入 `Artifacts/play-accept/report.txt`。

## Layout

```
Assets/Scripts/Luoxia/
  App/          Bootstrap, intent router, portrait policy
  Assets/       StreamingAssetsHashSpriteResolver (hash → Sprite)
  Contracts/    SessionView / bridge DTOs (Newtonsoft)
  Net/          HttpBridgeTransport, envelope factory
  Session/      SessionReplica
  UI/           MainWorld screen, feature panels, widgets
  Editor/       UI builder + portrait setup menus
Assets/Art/UI/Map/   Design slices from Engine UI pack
Assets/Prefabs/UI/   List item prefabs
Assets/StreamingAssets/LuoxiaHash/   Deployment export (opaque)
```

## Menus

| Menu | Purpose |
|------|---------|
| Luoxia / Play / Configure Local Provision | EditorPrefs for loopback provision port/secret |
| Luoxia / Play / Provision Local | Call Deployment `/provision/new-play` → seed bootstrap |
| Luoxia / UI / Play Accept Main World (send+confirm+map) | Batch Play Mode accept (dialogue+card+map+end-day+day2) |
| Luoxia / UI / Accept Main World Screen | Structural (no Play) accept |
| Luoxia / UI / Build Main World Screen | Rebuild scene + prefabs + wire slices |
| Luoxia / UI / Ensure MVP Shell | Patch open MainWorld: endDay / toast / map panel / strip missing scripts |
| Luoxia / UI / Ensure Immersion Shell | Warn if ImmersiveShell missing |
| Luoxia / Assets / Ensure Hash Index Scaffold | Create empty `StreamingAssets/LuoxiaHash` index |
| Luoxia / Display / Apply Portrait Project Settings | Portrait PlayerSettings + Game View |
| Luoxia / Display / Set Game View 1080x1920 | Editor Game resolution |

## Engine connect

On `LuoxiaClientBootstrap`（仅 Engine）:

- `EngineWithInitialView` — Provision 写入 session + 首包 SessionView，再 `client.ready`
- `EngineReadyOnly` — 仅 sessionId，靠 `client.ready` 拉权威 View  
无 sessionId 时全屏 Fatal，不进入可玩状态。

## License

Private / team use unless otherwise stated.
