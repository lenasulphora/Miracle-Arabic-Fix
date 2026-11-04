namespace Miracle
{
    // Decides whether one clipboard change is allowed into the input box in CORRECT mode.
    internal sealed class ClipboardGate
    {
        private uint _handled;
        private bool _primed;

        // True only for a new clipboard change that happened while the game was foreground.
        public bool Observe(uint sequence, bool gameForeground)
        {
            // Sequence 0 means the API call failed, so only the foreground check decides.
            if (sequence == 0) return gameForeground;

            if (_primed && sequence == _handled) return false;

            _handled = sequence;
            _primed = true;
            return gameForeground;
        }

        // Forgets the last handled sequence (used by tests and manual resets).
        public void Reset()
        {
            _handled = 0;
            _primed = false;
        }
    }
}
