namespace RaftPianoReborn
{
    // New note attacks can extend the motion window; holding or releasing a key
    // cannot. Keep short phrases continuous without an endless held-key loop.
    // Never restart clips or change global Animator speed per note.
    internal sealed class PlayingMotion
    {
        internal const float IdleGraceSeconds = 1.0f;
        private ulong lastAttacks;
        private float activeUntil;
        private bool hasPlayed;
        public void Reset(ulong attacks)
        {
            lastAttacks = attacks; activeUntil = 0; hasPlayed = false;
        }
        public bool Update(float now, ulong attacks, bool allowed)
        {
            if (!allowed) { Reset(attacks); return false; }
            if (attacks != lastAttacks)
            {
                lastAttacks = attacks; hasPlayed = true;
                activeUntil = now + IdleGraceSeconds;
            }
            return hasPlayed && now < activeUntil;
        }
    }
}
