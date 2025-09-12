namespace Core.GAS
{
    public static class GameplayEventTypes
    {
        // ASC internal events (1-50)
        public const int OnDamageTaken   = 1;
        public const int OnHealReceived  = 2;
        public const int OnGEApplied     = 3;
        public const int OnGERemoved     = 4;
        public const int OnDeath         = 5;
        public const int OnShieldBroken  = 6;
        public const int OnShieldGranted = 7;

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
        public const int DamageDealt       = 60;

        // Item / location system events (61-80)
        /// <summary>SourceId=发射者角色Id, IntArg1: 1=判定成功 0=失败。</summary>
        public const int NukeLaunched  = 61;
        /// <summary>TargetId=触发者角色Id(-1=远程引爆), IntArg1=地点Id。</summary>
        public const int TrapTriggered = 62;
        /// <summary>TargetId=受荆棘反噬的角色Id。</summary>
        public const int ThornDotTick  = 63;
        /// <summary>IntArg1=室内手牌, IntArg2=室外手牌, Magnitude: 0=平局 1=室内受创 2=室外受创。</summary>
        public const int TidePulse     = 64;
        /// <summary>SourceId=炮手角色Id, TargetId=目标角色Id。</summary>
        public const int TurretFired   = 65;
    }
}
