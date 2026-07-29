using System;

namespace Luoxia.UI.Core
{
    public sealed class DialogueSelection : IDialogueSelection
    {
        private DialogueTarget? _current;

        public DialogueTarget? Current => _current;

        public event Action<DialogueTarget?> Changed;

        public void Select(DialogueTarget target)
        {
            _current = target;
            Changed?.Invoke(_current);
        }

        public void Clear()
        {
            if (_current == null)
            {
                return;
            }

            _current = null;
            Changed?.Invoke(null);
        }
    }
}
