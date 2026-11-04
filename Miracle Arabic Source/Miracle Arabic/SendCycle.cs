namespace Miracle
{
    // What the window must tell the user about a finished send cycle.
    public enum CycleNotice { None, Sent, SentDirect, TargetMissing, FocusFailed, Unverified, Failed }

    // Result of one send cycle: everything the window needs to keep the input box correct.
    public readonly struct CycleResult
    {
        // A newer cycle already owns the input box, so this result applies nothing.
        public bool Stale { get; }

        // The message was confirmed sent, so the input box is left alone.
        public bool Confirmed { get; }

        // The game may have received the message but the outcome is unknown (recovery is offered).
        public bool Uncertain { get; }

        // Text pushed back into the input box automatically, or null to leave it as is.
        public string? Refill { get; }

        public CycleNotice Notice { get; }

        internal CycleResult(bool stale, bool confirmed, bool uncertain, string? refill, CycleNotice notice)
        {
            Stale = stale; Confirmed = confirmed; Uncertain = uncertain; Refill = refill; Notice = notice;
        }
    }

    internal sealed class SendCycle
    {
        private long _current;
        private string _recoverText = "";
        private bool _recoverUncertain;

        // The message waiting for the user to restore it, if any.
        public string RecoverText => _recoverText;

        // True while the saved message is an unconfirmed delivery, so recovery is offered.
        public bool RecoverUncertain => _recoverUncertain;

        // Opens a new cycle and returns its token; earlier cycles lose the right to touch the input box.
        public long Begin() => ++_current;

        // Settles the cycle for the given token; an old token is reported as Stale and changes nothing.
        public CycleResult Complete(long token, string text, SendOutcome outcome)
        {
            if (token != _current)
                return new CycleResult(stale: true, confirmed: false, uncertain: false, refill: null, CycleNotice.None);

            if (outcome.Confirmed)
            {
                // The message is used up: a sent message is never offered for recovery.
                ClearRecovery();
                return new CycleResult(false, true, false, null,
                    outcome.Result == SendResult.SentDirect ? CycleNotice.SentDirect : CycleNotice.Sent);
            }

            var notice = NoticeFor(outcome);

            if (string.IsNullOrEmpty(text))
            {
                ClearRecovery();
                return new CycleResult(false, false, false, null, notice);
            }

            if (outcome.SafeToReuse)
            {
                // Nothing was delivered, so reusing the text cannot duplicate a send.
                ClearRecovery();
                return new CycleResult(false, false, false, text, notice);
            }

            // Something was delivered but not confirmed: keep it aside and let the user decide.
            _recoverText = text;
            _recoverUncertain = true;
            return new CycleResult(false, false, true, null, notice);
        }

        // Hands over the saved message and clears the slot; used by the RESTORE button.
        public string TakeRecoverText()
        {
            string t = _recoverText;
            ClearRecovery();
            return t;
        }

        // Drops the saved message, for example when the user replaced it with new text.
        public void ClearRecovery()
        {
            _recoverText = "";
            _recoverUncertain = false;
        }

        private static CycleNotice NoticeFor(SendOutcome outcome) => outcome.Result switch
        {
            SendResult.TargetNotFound => CycleNotice.TargetMissing,
            SendResult.FocusFailed => CycleNotice.FocusFailed,
            _ when outcome.Uncertain => CycleNotice.Unverified,
            _ => CycleNotice.Failed
        };
    }
}
