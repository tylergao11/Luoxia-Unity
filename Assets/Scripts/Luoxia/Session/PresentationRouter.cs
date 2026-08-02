using System;
using Luoxia.Contracts;

namespace Luoxia.Session
{
    /// <summary>
    /// Exhaustive ServerMessage router (architecture: Presentation Router).
    /// Does not invent world content; only deserializes and fans out known types.
    /// </summary>
    public interface IPresentationBus
    {
        event Action<PresentationFrameDto> PresentationFrameReceived;
        event Action<StageOpenDto> StageOpened;
        event Action<StageUpdateDto> StageUpdated;
        event Action<StageCloseDto> StageClosed;
        event Action<DialogueReplyDto> DialogueReplyReceived;
        event Action<CommandResultDto> CommandResultReceived;
        event Action<ProtocolErrorDto> ProtocolErrorReceived;
        event Action<string> UnknownMessageReceived;
    }

    public sealed class PresentationRouter : IPresentationBus
    {
        public event Action<PresentationFrameDto> PresentationFrameReceived;
        public event Action<StageOpenDto> StageOpened;
        public event Action<StageUpdateDto> StageUpdated;
        public event Action<StageCloseDto> StageClosed;
        public event Action<DialogueReplyDto> DialogueReplyReceived;
        public event Action<CommandResultDto> CommandResultReceived;
        public event Action<ProtocolErrorDto> ProtocolErrorReceived;
        public event Action<string> UnknownMessageReceived;

        /// <summary>
        /// Returns true when the envelope was a known presentation / protocol message
        /// (not session.view — caller still owns SessionView application).
        /// </summary>
        public bool TryDispatchNonView(ServerEnvelopeDto envelope)
        {
            if (envelope?.message == null)
            {
                return false;
            }

            var type = BridgeJson.MessageType(envelope);
            if (string.IsNullOrEmpty(type) || type == "session.view")
            {
                return false;
            }

            switch (type)
            {
                case "presentation.frame":
                {
                    var frame = BridgeJson.TryExtractPresentationFrame(envelope);
                    if (frame != null)
                    {
                        PresentationFrameReceived?.Invoke(frame);
                        return true;
                    }

                    break;
                }
                case "stage.open":
                {
                    var open = BridgeJson.TryExtractStageOpen(envelope);
                    if (open != null)
                    {
                        StageOpened?.Invoke(open);
                        return true;
                    }

                    break;
                }
                case "stage.update":
                {
                    var update = BridgeJson.TryExtractStageUpdate(envelope);
                    if (update != null)
                    {
                        StageUpdated?.Invoke(update);
                        return true;
                    }

                    break;
                }
                case "stage.close":
                {
                    var close = BridgeJson.TryExtractStageClose(envelope);
                    if (close != null)
                    {
                        StageClosed?.Invoke(close);
                        return true;
                    }

                    break;
                }
                case "dialogue.reply":
                {
                    var reply = BridgeJson.TryExtractDialogueReply(envelope);
                    if (reply != null)
                    {
                        DialogueReplyReceived?.Invoke(reply);
                        return true;
                    }

                    break;
                }
                case "command.result":
                {
                    var result = BridgeJson.TryExtractCommandResult(envelope);
                    if (result != null)
                    {
                        CommandResultReceived?.Invoke(result);
                        return true;
                    }

                    break;
                }
                case "protocol.error":
                {
                    var error = BridgeJson.TryExtractProtocolError(envelope);
                    if (error != null)
                    {
                        ProtocolErrorReceived?.Invoke(error);
                        return true;
                    }

                    break;
                }
                default:
                    UnknownMessageReceived?.Invoke(type);
                    return true;
            }

            UnknownMessageReceived?.Invoke(type ?? "unparseable");
            return true;
        }
    }
}
