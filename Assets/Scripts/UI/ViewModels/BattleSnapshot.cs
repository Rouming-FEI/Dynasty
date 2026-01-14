using System.Collections.Generic;

namespace UI.ViewModels
{
    /// <summary>表现层只读快照。UI 只读此结构，不触碰 WorldState。</summary>
    public class BattleSnapshot
    {
        public int CurrentTurn;
        public string PhaseText;          // 判定阶段/行动阶段/回合结算/游戏结束
        public int AliveCount;
        public int PlayerCount;
        public bool GameOver;
        public string WinnerText;         // GameOver 时非 null
        public string JudgmentSummary;    // 开牌条文本；null = 不显示
        public bool LastJudgmentGlobalFlat; // 最近一次判定为全局平局（判定阶段提示用）
        public readonly List<CharacterRow> CharacterRows = new List<CharacterRow>();
        public readonly List<MapNodeInfo> MapNodes = new List<MapNodeInfo>();
        public readonly List<string> LogTail = new List<string>();

        public class CharacterRow
        {
            public int PlayerId;
            public string Name;
            public float Sanity;
            public float MaxSanity;
            public int ActionPoints;
            public string LocationName;   // 隐藏者 = "??"
            public bool IsDead;
            public bool IsHidden;
            public bool IsShielded;
            public bool IsCurrentActor;
            public string WeaponName;     // null = 空手
            public int WeaponAmmo;
            public readonly List<string> Tags = new List<string>();
        }

        public class MapNodeInfo
        {
            public int LocationId;
            public string Name;
            public bool HasTrap;
            public bool HasTurretOccupant;
            public bool IsCurrentActorHere;
            public readonly List<string> PlayerNames = new List<string>();
            public readonly List<string> ItemBadges = new List<string>();
        }
    }
}
