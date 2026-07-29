# Luoxia-Unity

Luoxia 2D client (Unity 2022.3 LTS + URP). Server authority lives in **Luoxia-Engine**; this project is the Client Runtime Host (UI, SessionReplica, Bridge transport).

## Requirements

- Unity **2022.3.62f3** (or compatible 2022.3 LTS)
- Optional: running Engine at `http://127.0.0.1:8000` for real `client.ready` / commands

## Open

1. Open `C:\Ai\Luoxia-Unity` (or this clone) in Unity Hub.
2. Open scene `Assets/Scenes/MainWorld.unity`.
3. Game view: **1080×1920** portrait (menu **Luoxia → Display → Set Game View 1080x1920**).
4. Play with bootstrap mode **MockOnly** for offline UI.

## Layout

```
Assets/Scripts/Luoxia/
  App/          Bootstrap, intent router, portrait policy
  Contracts/    SessionView / bridge DTOs (Newtonsoft)
  Net/          HttpBridgeTransport, envelope factory
  Session/      SessionReplica
  UI/           MainWorld screen, feature panels, widgets
  Editor/       UI builder + portrait setup menus
Assets/Art/UI/Map/   Design slices from Engine UI pack
Assets/Prefabs/UI/   List item prefabs
```

## Menus

| Menu | Purpose |
|------|---------|
| Luoxia / UI / Build Main World Screen | Rebuild scene + prefabs + wire slices |
| Luoxia / Display / Apply Portrait Project Settings | Portrait PlayerSettings + Game View |
| Luoxia / Display / Set Game View 1080x1920 | Editor Game resolution |

## Engine connect

On `LuoxiaClientBootstrap`:

- `MockOnly` — local SessionView
- `EngineReadyOnly` — set `sessionId` + `engineBaseUrl`, send `client.ready`
- `EngineWithInitialView` — paste ServerEnvelope JSON, optional ready

## License

Private / team use unless otherwise stated.
