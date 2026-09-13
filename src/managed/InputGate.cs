namespace RaftPianoReborn
{
    internal static class InputGate
    {
        // Raft uses MenuType.Piano while the local player is seated. It is the
        // playing surface, not an unrelated menu that should suppress notes.
        // Keep this policy separate so tests exercise the production decision.
        public static string BlockReason(bool seated, bool focused, bool chatSelected, MenuType menu)
        {
            if (!seated) return "not_seated";
            if (!focused) return "game_unfocused";
            if (chatSelected) return "chat_selected";
            if (menu != MenuType.None && menu != MenuType.Piano) return "menu_" + menu;
            return null;
        }
    }
}
