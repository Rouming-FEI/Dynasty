namespace Dynasty.Core
{
    public static class GameplayEventTypes
    {
        // ASC internal events (1-50)
        public const int OnDamageTaken   = 1;
        public const int OnHealReceived  = 2;
        public const int OnGEApplied     = 3;
        public const int OnGERemoved     = 4;
        public const int OnDeath         = 5;

        // Global events (51-100)
        public const int TurnStart          = 51;
        public const int JudgmentPhaseStart = 52;
        public const int JudgmentResolved   = 53;
        public const int ActionPhaseStart   = 54;
        public const int APChanged          = 55;
        public const int EntityMoved        = 56;
        public const int PlayerDied         = 57;
        public const int TurnEnd            = 58;
        public const int GameOver           = 59;
    }
}
