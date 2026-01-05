using System;
using System.Collections.Generic;
using Gameplay.Characters;

namespace UI.Presenter
{
    /// <summary>13 角色 Def 目录（id → Create() 工厂），供选角画面使用。</summary>
    public static class CharacterCatalog
    {
        private static readonly Dictionary<int, Func<CharacterDef>> Factories = new Dictionary<int, Func<CharacterDef>>
        {
            { 1,  GuardianDef.Create },
            { 2,  CharmSpiritDef.Create },
            { 3,  NormalPersonDef.Create },
            { 4,  SwordGhostDef.Create },
            { 5,  MagicianDef.Create },
            { 6,  TravelerDef.Create },
            { 7,  TimeThiefDef.Create },
            { 8,  ListenerDef.Create },
            { 9,  SeaMirageDef.Create },
            { 10, WoodSpiritDef.Create },
            { 11, FormationMasterDef.Create },
            { 12, PaladinDef.Create },
            { 13, AgentDef.Create },
        };

        private static readonly int[] AllIds =
            { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };

        public static CharacterDef Create(int id) => Factories[id]();
        public static int[] GetAllIds() => (int[])AllIds.Clone();
        public static string[] GetNames()
        {
            var names = new string[AllIds.Length];
            for (int i = 0; i < AllIds.Length; i++)
                names[i] = Factories[AllIds[i]]().Name;
            return names;
        }
    }
}
