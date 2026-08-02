#if UNITY_EDITOR
using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Luoxia.Editor
{
    /// <summary>
    /// Fable #5: provision / model_dispatch_ambiguous is a terminal player-visible failure.
    /// Retry means a brand-new provision (new world). Never poll or auto-resend the blocked model.
    /// </summary>
    internal static class ProvisionFaultPresentation
    {
        public const string ModelDispatchAmbiguousCode = "runtime.kernel.model_dispatch_ambiguous";

        public const string PlayerCopy =
            "开局未完成：世界导演未能就位，本次开局已作废。你可以重新开始一局。";

        public static bool IsModelDispatchAmbiguous(string codeOrBody)
        {
            return !string.IsNullOrEmpty(codeOrBody)
                && codeOrBody.IndexOf(ModelDispatchAmbiguousCode, StringComparison.Ordinal) >= 0;
        }

        public static bool TryParseFaultBody(string body, out ProvisionFault fault)
        {
            fault = default;
            if (string.IsNullOrWhiteSpace(body))
            {
                return false;
            }

            var trimmed = body.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var root = JObject.Parse(trimmed);
                    var code = root.Value<string>("code");
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        code = root.Value<string>("failure_code");
                    }

                    var message = root.Value<string>("message");
                    string requestId = null;
                    string worldId = null;
                    var details = root["details"] as JObject;
                    if (details != null)
                    {
                        requestId = details.Value<string>("request_id");
                        worldId = details.Value<string>("world_id");
                        if (string.IsNullOrWhiteSpace(code)
                            && details.Value<string>("failure_code") is { Length: > 0 } nested)
                        {
                            code = nested;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(code) && IsModelDispatchAmbiguous(trimmed))
                    {
                        code = ModelDispatchAmbiguousCode;
                    }

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return false;
                    }

                    fault = new ProvisionFault(code.Trim(), message, requestId, worldId, trimmed);
                    return true;
                }
                catch (Exception)
                {
                    // Fall through to plain-text parse.
                }
            }

            if (!IsModelDispatchAmbiguous(trimmed))
            {
                return false;
            }

            string textCode = ModelDispatchAmbiguousCode;
            string textMessage = null;
            var colon = trimmed.IndexOf(':');
            if (colon > 0)
            {
                var head = trimmed.Substring(0, colon).Trim();
                if (IsModelDispatchAmbiguous(head))
                {
                    textCode = head;
                    textMessage = trimmed.Substring(colon + 1).Trim();
                }
            }

            fault = new ProvisionFault(
                textCode,
                textMessage,
                ExtractTagged(trimmed, "request_id"),
                ExtractTagged(trimmed, "world_id"),
                trimmed);
            return true;
        }

        public static string FormatDetailLines(ProvisionFault fault)
        {
            var sb = new StringBuilder();
            sb.Append("code=").Append(fault.Code ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fault.RequestId))
            {
                sb.Append("\nrequest_id=").Append(fault.RequestId);
            }

            if (!string.IsNullOrWhiteSpace(fault.WorldId))
            {
                sb.Append("\nworld_id=").Append(fault.WorldId);
            }

            if (!string.IsNullOrWhiteSpace(fault.Message))
            {
                sb.Append("\nmessage=").Append(fault.Message);
            }

            return sb.ToString();
        }

        public static string FormatPlayAcceptReport(ProvisionFault fault)
        {
            return "Play Accept FAILED\n"
                + "terminal_failure=model_dispatch_ambiguous\n"
                + "player_copy=" + PlayerCopy + "\n"
                + "recoverability=abandoned_new_provision_only\n"
                + FormatDetailLines(fault) + "\n"
                + "raw_body=\n" + (fault.RawBody ?? string.Empty) + "\n";
        }

        public static void ShowAmbiguousEditorDialog(ProvisionFault fault, Action onRestartProvision)
        {
            AmbiguousFailureWindow.Open(fault, onRestartProvision);
        }

        private static string ExtractTagged(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var marker = key + "=";
            var idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                marker = "\"" + key + "\":";
                idx = text.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return null;
                }

                var start = idx + marker.Length;
                while (start < text.Length && char.IsWhiteSpace(text[start]))
                {
                    start++;
                }

                if (start < text.Length && text[start] == '"')
                {
                    start++;
                    var end = text.IndexOf('"', start);
                    return end > start ? text.Substring(start, end - start) : null;
                }

                return null;
            }

            var from = idx + marker.Length;
            var stop = from;
            while (stop < text.Length
                   && !char.IsWhiteSpace(text[stop])
                   && text[stop] != ','
                   && text[stop] != ';'
                   && text[stop] != '}')
            {
                stop++;
            }

            return stop > from ? text.Substring(from, stop - from).Trim() : null;
        }

        internal readonly struct ProvisionFault
        {
            public ProvisionFault(
                string code,
                string message,
                string requestId,
                string worldId,
                string rawBody)
            {
                Code = code;
                Message = message;
                RequestId = requestId;
                WorldId = worldId;
                RawBody = rawBody;
            }

            public string Code { get; }
            public string Message { get; }
            public string RequestId { get; }
            public string WorldId { get; }
            public string RawBody { get; }
        }

        private sealed class AmbiguousFailureWindow : EditorWindow
        {
            private ProvisionFault _fault;
            private Action _onRestart;
            private bool _showDetail = true;
            private Vector2 _scroll;

            public static void Open(ProvisionFault fault, Action onRestartProvision)
            {
                var window = CreateInstance<AmbiguousFailureWindow>();
                window.titleContent = new GUIContent("开局未完成");
                window.minSize = new Vector2(480f, 260f);
                window._fault = fault;
                window._onRestart = onRestartProvision;
                window.ShowUtility();
                window.Focus();
            }

            private void OnGUI()
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(PlayerCopy, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(
                    "本次开局已作废，不可恢复 ambiguous world。重试 = 全新 Provision Local（新 world）。禁止轮询或自动重发被阻塞的模型调用。",
                    MessageType.Warning);

                _showDetail = EditorGUILayout.Foldout(_showDetail, "详情 (code / request_id / world_id)", true);
                if (_showDetail)
                {
                    _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(90f));
                    EditorGUILayout.TextArea(
                        FormatDetailLines(_fault)
                        + (string.IsNullOrWhiteSpace(_fault.RawBody)
                            ? string.Empty
                            : "\n\n" + _fault.RawBody),
                        GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重新开始一局（Provision Local）", GUILayout.Height(28f)))
                    {
                        var restart = _onRestart;
                        Close();
                        restart?.Invoke();
                    }

                    if (GUILayout.Button("关闭", GUILayout.Height(28f)))
                    {
                        Close();
                    }
                }
            }
        }
    }
}
#endif
