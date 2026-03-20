using Core.GAS;

namespace Gameplay.Characters
{
    /// <summary>13 角色 Def 目录（id → 工厂），Simulation 层专用（快照反序列化重建角色用）。</summary>
    public static class CharacterDefCatalog
    {
        public static CharacterDef Get(int id)
        {
            switch (id)
            {
                case 1: return GuardianDef.Create();
                case 2: return CharmSpiritDef.Create();
                case 3: return NormalPersonDef.Create();
                case 4: return SwordGhostDef.Create();
                case 5: return MagicianDef.Create();
                case 6: return TravelerDef.Create();
                case 7: return TimeThiefDef.Create();
                case 8: return ListenerDef.Create();
                case 9: return SeaMirageDef.Create();
                case 10: return WoodSpiritDef.Create();
                case 11: return FormationMasterDef.Create();
                case 12: return PaladinDef.Create();
                case 13: return AgentDef.Create();
                case 14: return ChaosMageDef.Create();
                default: return null;
            }
        }
    }
}
