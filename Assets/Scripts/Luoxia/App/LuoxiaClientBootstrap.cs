using System.Collections;
using Luoxia.Contracts;
using Luoxia.Net;
using Luoxia.Session;
using Luoxia.UI.Core;
using Luoxia.UI.Screens;
using UnityEngine;

namespace Luoxia.App
{
    /// <summary>
    /// Composition root. Real path: gateway injects session_id (+ optional initial SessionView),
    /// then client.ready / resync against Engine POST /api/client-envelope.
    /// </summary>
    public sealed class LuoxiaClientBootstrap : MonoBehaviour
    {
        public enum SessionSourceMode
        {
            /// <summary>Local preview only — does not hit Engine.</summary>
            MockOnly,

            /// <summary>Use injected/gateway SessionView, then ready against Engine.</summary>
            EngineWithInitialView,

            /// <summary>Attach session id only; fetch authoritative view via client.ready.</summary>
            EngineReadyOnly
        }

        [Header("UI")]
        [SerializeField] private MainWorldScreen mainWorldScreen;

        [Header("Session source")]
        [SerializeField] private SessionSourceMode mode = SessionSourceMode.MockOnly;
        [SerializeField] private string engineBaseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string sessionId;
        [SerializeField] private string worldId;
        [Tooltip("Optional full ServerEnvelope JSON array or single envelope; used to seed SessionView.")]
        [SerializeField] [TextArea(4, 12)] private string initialServerEnvelopesJson;
        [SerializeField] private bool sendClientReadyOnStart = true;

        private SessionReplica _replica;
        private CommandGate _gate;
        private DialogueSelection _selection;
        private BridgeSessionClient _bridge;
        private ClientEnvelopeFactory _factory;
        private PlayerIntentRouter _intents;

        private void Awake()
        {
            _replica = new SessionReplica();
            _gate = new CommandGate();
            _selection = new DialogueSelection();
            _factory = new ClientEnvelopeFactory();

            if (mainWorldScreen == null)
            {
                mainWorldScreen = FindObjectOfType<MainWorldScreen>();
            }

            if (mode == SessionSourceMode.MockOnly)
            {
                _intents = new PlayerIntentRouter(_replica, _gate, _selection, null, _factory, this, worldId);
                WireUi();
                _replica.Bootstrap(MockSession.Create(), 0);
                _replica.ApplyFullView(_replica.CurrentView, 0);
                return;
            }

            var transport = new HttpBridgeTransport(engineBaseUrl);
            _bridge = new BridgeSessionClient(transport, _replica, _gate, _factory);
            _intents = new PlayerIntentRouter(_replica, _gate, _selection, _bridge, _factory, this, worldId);
            WireUi();
            StartCoroutine(ConnectEngine());
        }

        private void WireUi()
        {
            if (mainWorldScreen == null)
            {
                Debug.LogError("[Bootstrap] MainWorldScreen missing");
                return;
            }

            mainWorldScreen.Configure(_replica, _intents, _selection);
        }

        private IEnumerator ConnectEngine()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("[Bootstrap] sessionId required for Engine modes");
                yield break;
            }

            SessionViewDto seeded = null;
            var serverSeq = 0;
            if (!string.IsNullOrWhiteSpace(initialServerEnvelopesJson))
            {
                if (TryParseInitialView(initialServerEnvelopesJson, out seeded, out serverSeq))
                {
                    Debug.Log($"[Bootstrap] seeded SessionView rev={seeded.view_revision} seq={serverSeq}");
                }
                else
                {
                    Debug.LogWarning("[Bootstrap] failed to parse initialServerEnvelopesJson");
                }
            }

            _bridge.AttachSession(sessionId, seeded, serverSeq);

            if (!sendClientReadyOnStart)
            {
                yield break;
            }

            var task = _bridge.SendReadyAsync();
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                Debug.LogError($"[Bootstrap] client.ready failed: {task.Exception?.GetBaseException().Message}");
                yield break;
            }

            if (task.Result != null)
            {
                Debug.Log($"[Bootstrap] ready ok view_revision={task.Result.view_revision}");
            }
            else if (!_replica.HasView)
            {
                Debug.LogWarning("[Bootstrap] ready returned no session.view");
            }
        }

        public static bool TryParseInitialView(string json, out SessionViewDto view, out int serverSequence)
        {
            view = null;
            serverSequence = 0;
            try
            {
                var batch = BridgeJson.DeserializeServerBatch(json);
                for (var i = 0; i < batch.Count; i++)
                {
                    var env = batch[i];
                    var extracted = BridgeJson.TryExtractSessionView(env);
                    if (extracted != null)
                    {
                        view = extracted;
                        serverSequence = env.sequence;
                        return true;
                    }
                }

                // Allow raw SessionView JSON as convenience for tools.
                view = BridgeJson.Deserialize<SessionViewDto>(json);
                return view != null && !string.IsNullOrEmpty(view.session_id);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(ex.Message);
                return false;
            }
        }
    }

    internal static class MockSession
    {
        public static SessionViewDto Create()
        {
            return new SessionViewDto
            {
                contract_version = "world-runtime.v1",
                record_type = "session.view",
                session_id = "00000000-0000-4000-8000-000000000001",
                view_revision = 1,
                basis_token = new string('b', 32),
                player_entity_id = "00000000-0000-4000-8000-0000000000aa",
                world_time = new LogicalTimeDto
                {
                    clock_id = "riverside_calendar",
                    tick = 0,
                    calendar_label = "第一日·清晨"
                },
                day_cycle = new DayCycleStateDto
                {
                    day = 1,
                    phase = "player",
                    phase_revision = 0
                },
                event_budget = new EventBudgetViewDto
                {
                    day = 1,
                    capacity = 3,
                    spent = 0,
                    remaining = 3
                },
                render_nodes = new System.Collections.Generic.List<RenderNodeDto>
                {
                    new RenderNodeDto
                    {
                        node_id = "scene_main",
                        node_kind = "scene",
                        slot_id = "main_scene"
                    }
                },
                event_cards = new System.Collections.Generic.List<EventCardViewDto>
                {
                    new EventCardViewDto
                    {
                        event_card_id = "00000000-0000-4000-8000-0000000000c1",
                        day = 1,
                        title = LocalizedTextDto.FromZh("云烨的疑虑"),
                        summary = LocalizedTextDto.FromZh("云烨似乎发现了什么异常……"),
                        event_cost = new EventCostDto { amount = 1 },
                        status = "available"
                    },
                    new EventCardViewDto
                    {
                        event_card_id = "00000000-0000-4000-8000-0000000000c2",
                        day = 1,
                        title = LocalizedTextDto.FromZh("后山异动"),
                        summary = LocalizedTextDto.FromZh("后山出现了异常的灵力波动。"),
                        event_cost = new EventCostDto { amount = 1 },
                        status = "available"
                    }
                },
                dialogues = new System.Collections.Generic.List<DialogueViewDto>(),
                goal_plans = new System.Collections.Generic.List<GoalPlanViewDto>(),
                notices = new System.Collections.Generic.List<NoticeDto>()
            };
        }
    }
}
