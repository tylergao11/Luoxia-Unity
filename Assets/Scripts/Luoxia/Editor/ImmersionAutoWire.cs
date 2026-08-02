#if UNITY_EDITOR
using System.IO;
using Luoxia.UI.Features;
using Luoxia.UI.Immersion;
using Luoxia.UI.Screens;
using Luoxia.UI.Widgets;
using UnityEditor;
using UnityEngine;

namespace Luoxia.Editor
{
    /// <summary>
    /// Check-only Ensure menus — never FindOrCreate / rebuild chrome at edit time.
    /// Scene ownership is MainWorldUiBuilder only.
    /// </summary>
    public static class ImmersionAutoWire
    {
        private const string HashIndexDir = "Assets/StreamingAssets/LuoxiaHash";
        private const string HashIndexPath = HashIndexDir + "/hash-index.json";
        private const string HashReadmePath = HashIndexDir + "/README.md";

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.delayCall += () =>
            {
                if (Object.FindObjectOfType<MainWorldScreen>() == null)
                {
                    return;
                }

                WarnIfMissing();
            };
        }

        [MenuItem("Luoxia/UI/Ensure Immersion Shell")]
        public static void EnsureImmersionWired()
        {
            if (Object.FindObjectOfType<MainWorldScreen>() == null)
            {
                Debug.LogWarning("[Luoxia] No MainWorldScreen in open scenes. Run Luoxia/UI/Build Main World Screen.");
                return;
            }

            WarnIfMissing();
        }

        [MenuItem("Luoxia/UI/Ensure MVP Shell")]
        public static void EnsureMvpShell()
        {
            // Hash scaffold is asset plumbing, not UI chrome construction.
            EnsureHashIndexScaffold();

            var screen = Object.FindObjectOfType<MainWorldScreen>();
            if (screen == null)
            {
                Debug.LogWarning("[Luoxia] Open MainWorld scene first, or run Luoxia/UI/Build Main World Screen.");
                return;
            }

            WarnIfMissing();
        }

        [MenuItem("Luoxia/Assets/Ensure Hash Index Scaffold")]
        public static void EnsureHashIndexScaffold()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StreamingAssets"))
            {
                AssetDatabase.CreateFolder("Assets", "StreamingAssets");
            }

            if (!AssetDatabase.IsValidFolder(HashIndexDir))
            {
                AssetDatabase.CreateFolder("Assets/StreamingAssets", "LuoxiaHash");
            }

            if (!File.Exists(HashIndexPath))
            {
                File.WriteAllText(HashIndexPath,
                    "{\n  \"schema_version\": 1,\n  \"entries\": {}\n}\n");
            }

            if (!File.Exists(HashReadmePath))
            {
                File.WriteAllText(HashReadmePath,
@"# LuoxiaHash

Opaque Deployment export: local `content_hash` → relative path under this folder.
Host resolves only by content hash — never by asset_id or pack story.

## Replace when content changes

```bash
npm run export:unity-hash
```

(from Luoxia-Deployment; opens whatever pack provision serves)

## hash-index.json

```json
{
  ""schema_version"": 1,
  ""entries"": {
    ""<64-char-sha256-hex>"": ""files/<hash>.png""
  }
}
```

- Key：引擎合同 `AssetContentRef.content_hash`（小写 hex SHA-256）
- Value：相对本目录的文件路径（正斜杠）
- Optional entry_list / asset_id are operator metadata only
- 未命中：UI 显示显式错误标记，不使用假图冒充成功
- 空索引合法：Host 可启动，任何 hash 解析均为 miss
");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Luoxia] Hash index scaffold ready at {HashIndexDir}");
        }

        private static void WarnIfMissing()
        {
            var issues = 0;
            issues += WarnMissing<MainWorldScreen>("MainWorldScreen");
            issues += WarnMissing<ImmersiveShellController>("ImmersiveShell");
            issues += WarnMissing<SessionFatalOverlay>("SessionFatalOverlay");
            issues += WarnMissing<FeatureSwipeNavigator>("FeatureSwipeNavigator");
            issues += WarnMissing<EventCardConfirmPanel>("EventCardConfirmPanel");
            issues += WarnMissing<EndDayConfirmPanel>("EndDayConfirmPanel");
            issues += WarnMissing<CommandFeedbackHud>("CommandFeedbackHud");
            issues += WarnMissing<MapDestinationPanel>("MapDestinationPanel");
            issues += WarnMissing<NarrativeFramePlayer>("NarrativeFramePlayer");
            issues += WarnMissing<ArrivalLoreOverlay>("ArrivalLoreOverlay");
            issues += WarnMissing<NightCurtainOverlay>("NightCurtainOverlay");

            var screen = Object.FindObjectOfType<MainWorldScreen>();
            if (screen != null)
            {
                var so = new SerializedObject(screen);
                issues += WarnNullRef(so, "fatalOverlay", "MainWorldScreen.fatalOverlay");
                issues += WarnNullRef(so, "endDayButton", "MainWorldScreen.endDayButton");
                issues += WarnNullRef(so, "dialoguePanel", "MainWorldScreen.dialoguePanel");
                issues += WarnNullRef(so, "eventPanel", "MainWorldScreen.eventPanel");
                issues += WarnNullRef(so, "eventCardConfirmPanel", "MainWorldScreen.eventCardConfirmPanel");
                issues += WarnNullRef(so, "endDayConfirmPanel", "MainWorldScreen.endDayConfirmPanel");
                issues += WarnNullRef(so, "immersiveShell", "MainWorldScreen.immersiveShell");
            }

            if (issues == 0)
            {
                Debug.Log("[Luoxia] MVP / Immersion shell refs look present (check-only).");
            }
            else
            {
                Debug.LogWarning(
                    $"[Luoxia] {issues} missing ref(s). Run menu Luoxia/UI/Build Main World Screen — Ensure does not construct chrome.");
            }
        }

        private static int WarnMissing<T>(string label) where T : Object
        {
            if (Object.FindObjectOfType<T>(true) != null)
            {
                return 0;
            }

            Debug.LogWarning($"[Luoxia] Missing {label}. Run Luoxia/UI/Build Main World Screen.");
            return 1;
        }

        private static int WarnNullRef(SerializedObject so, string field, string label)
        {
            var prop = so.FindProperty(field);
            if (prop != null && prop.objectReferenceValue != null)
            {
                return 0;
            }

            Debug.LogWarning($"[Luoxia] Unassigned {label}. Run Luoxia/UI/Build Main World Screen.");
            return 1;
        }
    }
}
#endif
