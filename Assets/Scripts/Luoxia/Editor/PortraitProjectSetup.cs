#if UNITY_EDITOR
using System;
using System.Reflection;
using Luoxia.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.Editor
{
    /// <summary>
    /// One-click portrait project setup: Player Settings, Game View, active CanvasScaler.
    /// </summary>
    public static class PortraitProjectSetup
    {
        public const int DesignWidth = PortraitScreenPolicy.DesignWidth;
        public const int DesignHeight = PortraitScreenPolicy.DesignHeight;

        [MenuItem("Luoxia/Display/Apply Portrait Project Settings")]
        public static void ApplyAll()
        {
            try
            {
                ApplyPlayerSettings();
                FixActiveCanvasScalers();
                EnsurePortraitPolicyOnMainCanvas();
                try
                {
                    EnsureGameViewSize(DesignWidth, DesignHeight, "Luoxia Portrait 1080x1920");
                    TrySetGameViewSize(DesignWidth, DesignHeight);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Luoxia] Game View size setup skipped: {ex.Message}");
                }

                AssetDatabase.SaveAssets();
                Debug.Log($"[Luoxia] Portrait applied: {DesignWidth}x{DesignHeight}, orientation=Portrait, windowed standalone");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Luoxia] Apply portrait failed: {ex}");
            }
        }

        [MenuItem("Luoxia/Display/Set Game View 1080x1920")]
        public static void SetGameViewOnly()
        {
            try
            {
                EnsureGameViewSize(DesignWidth, DesignHeight, "Luoxia Portrait 1080x1920");
                TrySetGameViewSize(DesignWidth, DesignHeight);
                Debug.Log("[Luoxia] Game View switched to 1080x1920");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Luoxia] Set Game View failed: {ex.Message}");
            }
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.defaultScreenWidth = DesignWidth;
            PlayerSettings.defaultScreenHeight = DesignHeight;
            PlayerSettings.defaultWebScreenWidth = DesignWidth;
            PlayerSettings.defaultWebScreenHeight = DesignHeight;

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;
        }

        public static void FixActiveCanvasScalers()
        {
            var scalers = UnityEngine.Object.FindObjectsOfType<CanvasScaler>(true);
            foreach (var scaler in scalers)
            {
                var so = new SerializedObject(scaler);
                so.FindProperty("m_UiScaleMode").enumValueIndex = (int)CanvasScaler.ScaleMode.ScaleWithScreenSize;
                var res = so.FindProperty("m_ReferenceResolution");
                res.vector2Value = new Vector2(DesignWidth, DesignHeight);
                so.FindProperty("m_ScreenMatchMode").enumValueIndex = (int)CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                so.FindProperty("m_MatchWidthOrHeight").floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(scaler);
            }

            if (scalers.Length > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            }
        }

        public static void EnsurePortraitPolicyOnMainCanvas()
        {
            var canvas = GameObject.Find("MainWorldCanvas");
            if (canvas == null)
            {
                return;
            }

            if (canvas.GetComponent<PortraitScreenPolicy>() == null)
            {
                Undo.AddComponent<PortraitScreenPolicy>(canvas);
                EditorUtility.SetDirty(canvas);
            }
        }

        public static void EnsureGameViewSize(int width, int height, string label)
        {
            var editorAsm = typeof(UnityEditor.Editor).Assembly;
            var sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
            if (sizesType == null)
            {
                return;
            }

            var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instanceProp = singletonType.GetProperty("instance");
            var gameViewSizes = instanceProp.GetValue(null, null);
            var currentGroup = sizesType.GetProperty("currentGroup").GetValue(gameViewSizes, null);
            var groupType = currentGroup.GetType();
            var getTotalCount = groupType.GetMethod("GetTotalCount");
            var getGameViewSize = groupType.GetMethod("GetGameViewSize");
            var addCustomSize = groupType.GetMethod("AddCustomSize");

            var total = (int)getTotalCount.Invoke(currentGroup, null);
            for (var i = 0; i < total; i++)
            {
                var size = getGameViewSize.Invoke(currentGroup, new object[] { i });
                var sizeType = size.GetType();
                var w = (int)sizeType.GetProperty("width").GetValue(size, null);
                var h = (int)sizeType.GetProperty("height").GetValue(size, null);
                if (w == width && h == height)
                {
                    return;
                }
            }

            var gameViewSizeType = editorAsm.GetType("UnityEditor.GameViewSize");
            var gameViewSizeTypeEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
            var fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
            var ctor = gameViewSizeType.GetConstructor(new[]
            {
                gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string)
            });
            var newSize = ctor.Invoke(new object[] { fixedResolution, width, height, label });
            addCustomSize.Invoke(currentGroup, new[] { newSize });
        }

        public static void TrySetGameViewSize(int width, int height)
        {
            var editorAsm = typeof(UnityEditor.Editor).Assembly;
            var gameViewType = editorAsm.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                return;
            }

            var gameView = EditorWindow.GetWindow(gameViewType);
            var sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
            var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var instanceProp = singletonType.GetProperty("instance");
            var gameViewSizes = instanceProp.GetValue(null, null);
            var currentGroup = sizesType.GetProperty("currentGroup").GetValue(gameViewSizes, null);
            var groupType = currentGroup.GetType();
            var getTotalCount = groupType.GetMethod("GetTotalCount");
            var getGameViewSize = groupType.GetMethod("GetGameViewSize");
            var total = (int)getTotalCount.Invoke(currentGroup, null);

            var index = -1;
            for (var i = 0; i < total; i++)
            {
                var size = getGameViewSize.Invoke(currentGroup, new object[] { i });
                var sizeType = size.GetType();
                var w = (int)sizeType.GetProperty("width").GetValue(size, null);
                var h = (int)sizeType.GetProperty("height").GetValue(size, null);
                if (w == width && h == height)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            var selectedSizeIndex = gameViewType.GetProperty(
                "selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (selectedSizeIndex != null && selectedSizeIndex.CanWrite)
            {
                selectedSizeIndex.SetValue(gameView, index, null);
            }
            else
            {
                var method = gameViewType.GetMethod(
                    "SizeSelectionCallback",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                method?.Invoke(gameView, new object[] { index, null });
            }

            gameView.Repaint();
        }
    }
}
#endif
