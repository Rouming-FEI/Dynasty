using System;
using System.IO;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Items;
using Gameplay.Judgment;
using Gameplay.Locations;
using Gameplay.Movement;

namespace Core.Sync
{
    /// <summary>
    /// WorldState 全量快照序列化（断线重连地基）。
    /// 格式：[Magic:0x57534E50 'WSNP'][Version:int=1][ComputeStateHash:long][payload...]
    /// BinaryWriter/BinaryReader + MemoryStream，Core 层零网络依赖；
    /// 反序列化完整重建后重算 ComputeStateHash 与头部比对，不一致抛 InvalidDataException（快照自校验）。
    /// 序列化范围与 ComputeStateHash 对齐（详见 P6 计划 Task 9 payload 表），并额外覆盖
    /// 哈希未纳入但重建必需的字段（随机种子、LastJudgment、TurnManager、心雾/血印/本能/冷却/被动计时器等）。
    /// </summary>
    public static class WorldStateSerializer
    {
        private const int Magic = 0x57534E50; // 'WSNP'
        /// <summary>当前序列化格式版本。</summary>
        private const int CurrentVersion = 1;
        /// <summary>从快照读取的任何长度兜底上限（约 100 万；一局快照远小于此，防损坏数据携带巨大正数导致分配 OOM）。</summary>
        private const int MaxArrayLength = 1 << 20;

        /// <summary>分配前校验从快照读取的长度：仅允许 [0, cap]。
        /// cap 为字段语义上限（玩家数/GE 数等），默认兜底 1M；对象密集型数组（每元素 300B+ 堆分配）
        /// 即使 1M 条也会直接 OOM，故关键字段必须按语义收紧。</summary>
        private static void CheckLength(int len, string what, int cap = MaxArrayLength)
        {
            if (len < 0 || len > cap)
                throw new InvalidDataException($"{what} 长度非法: {len}");
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        public static byte[] Serialize(WorldState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    w.Write(Magic);
                    w.Write(CurrentVersion);
                    w.Write(state.ComputeStateHash());
                    WriteGlobal(state, w);
                    for (int i = 0; i < state.Characters.Length; i++)
                        WriteCharacter(state.Characters[i], w);
                }
                return ms.ToArray();
            }
        }

        public static WorldState Deserialize(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            ItemRegistry.Initialize();   // 道具表懒初始化（幂等），背包重建依赖
            using (var ms = new MemoryStream(bytes, false))
            using (var r = new BinaryReader(ms))
            {
                int magic = r.ReadInt32();
                if (magic != Magic)
                    throw new InvalidDataException($"不是有效的 WorldState 快照（magic=0x{magic:X8}）");
                int version = r.ReadInt32();
                if (version != CurrentVersion)
                    throw new InvalidDataException($"不支持的快照版本 {version}（当前 {CurrentVersion}）");
                long expectedHash = r.ReadInt64();

                WorldState state = ReadGlobal(r);
                for (int i = 0; i < state.Characters.Length; i++)
                    state.Characters[i] = ReadCharacter(r);

                long actualHash = state.ComputeStateHash();
                if (actualHash != expectedHash)
                    throw new InvalidDataException(
                        $"快照哈希自校验失败（期望 {expectedHash}，实际 {actualHash}）——数据损坏或字段遗漏");
                return state;
            }
        }

        // ------------------------------------------------------------------
        // 全局段
        // ------------------------------------------------------------------

        internal static void WriteGlobal(WorldState state, BinaryWriter w)
        {
            w.Write(state.PlayerCount);
            w.Write(state.CurrentTick);
            w.Write(state.CurrentTurn);
            w.Write((byte)state.Phase);
            w.Write((int)state.Random.Seed);   // seed 实际来源为 int（WorldState 构造参数），写侧截断对齐读侧 (int)
            w.Write(state.Random.State);
            WriteIntArray(w, state.LocationTrapCounts);
            w.Write(state.ThornPotionTurnsRemaining);
            WriteBoolArray(w, state.MovedThisTurn);
            w.Write(state.WoodSeedPlayer);
            w.Write(state.WoodSeedLocation);
            w.Write(state.PaladinAegisUsed);
            w.Write(state.GhostCloneActive);
            w.Write(state.GhostCloneSanity);

            // LastAllocations
            w.Write(state.LastAllocations.Length);
            for (int i = 0; i < state.LastAllocations.Length; i++)
                WriteChip(w, state.LastAllocations[i]);

            // HasJudgmentResult + LastJudgment
            w.Write(state.HasJudgmentResult);
            if (state.HasJudgmentResult)
            {
                JudgmentResult jr = state.LastJudgment;
                w.Write(jr.WinnerHand);
                w.Write(jr.LoserHand);
                w.Write(jr.NeutralHand);
                w.Write(jr.IsGlobalFlat);
                w.Write(jr.WinnerCount);
                w.Write(jr.LoserCount);
                int roleLen = jr.Roles != null ? jr.Roles.Length : 0;
                w.Write(roleLen);
                for (int i = 0; i < roleLen; i++)
                    w.Write((byte)jr.Roles[i]);
                WriteIntArray(w, jr.EffectiveChips);
                WriteIntArray(w, jr.APGained);
            }

            // TurnManager（null = 未开局，写默认值）
            TurnManager tm = state.TurnManager;
            w.Write(tm != null ? tm.ConsecutiveFlatCount : 0);
            WriteIntArray(w, tm != null ? tm.ActionOrder : null);
            w.Write(tm != null ? tm.CurrentActorIndex : 0);

            // Locations runtimeItems（分组表示：count + (ItemId, Count)...）
            int locationCount = state.Locations.LocationCount;
            w.Write(locationCount);
            for (int i = 0; i < locationCount; i++)
            {
                LocatedItem[] items = state.Locations.GetLocatedItems(i);
                w.Write(items.Length);
                for (int j = 0; j < items.Length; j++)
                {
                    w.Write(items[j].ItemId);
                    w.Write(items[j].Count);
                }
            }

            // LocationRules：typeByte(0无/1Hideout/2Lab/3Outpost) + LocationId + LabRule 附加字段
            LocationRule[] rules = state.Locations.Rules;
            w.Write(rules.Length);
            for (int i = 0; i < rules.Length; i++)
                WriteRule(rules[i], i, w);
        }

        private static void WriteRule(LocationRule rule, int index, BinaryWriter w)
        {
            if (rule is HideoutRule)
            {
                w.Write((byte)1);
                w.Write(rule.LocationId);
            }
            else if (rule is LabRule lab)
            {
                w.Write((byte)2);
                w.Write(lab.LocationId);
                w.Write(lab.DoorOpen);
                w.Write(lab.SwitchOn);
                w.Write(lab.ThresholdRemoved);
                w.Write(lab.TideTurnsElapsed);
            }
            else if (rule is OutpostRule)
            {
                w.Write((byte)3);
                w.Write(rule.LocationId);
            }
            else
            {
                w.Write((byte)0);
                w.Write(index);
            }
        }

        // ------------------------------------------------------------------
        // 每角色段
        // ------------------------------------------------------------------

        internal static void WriteCharacter(Character c, BinaryWriter w)
        {
            if (c == null) { w.Write(false); return; }
            w.Write(true);
            w.Write(c.Definition != null ? c.Definition.Id : 0);
            w.Write(c.Id);
            w.Write(c.PlayerId);
            WriteString(w, c.Name);
            w.Write(c.CurrentLocationId);

            // Inventory
            w.Write(c.Inventory.EquippedWeapon != null ? c.Inventory.EquippedWeapon.Id : 0);
            w.Write(c.Inventory.EquippedWeaponAmmo);
            w.Write(c.Inventory.Consumables.Count);
            for (int i = 0; i < c.Inventory.Consumables.Count; i++)
                w.Write(c.Inventory.Consumables[i].Id);

            // ASC
            AbilitySystemComponent asc = c.ASC;
            for (int a = 1; a <= AttributeConstants.Count; a++)
            {
                w.Write(asc.GetAttributeBaseValue(a));
                w.Write(asc.GetAttributeValue(a));
            }
            w.Write(asc.Tags.Mask);
            w.Write(asc.ActiveEffectCount);
            for (int e = 0; e < asc.ActiveEffectCount; e++)
                WriteEffectSpec(asc.GetActiveEffect(e), w);
            w.Write(asc.GetGuardianCharge());
            w.Write(asc.GetMistLayers());
            w.Write(asc.GetBloodSeals());
            w.Write(asc.GetInstinct());
            w.Write(asc.GetLastDamageTurn());
            w.Write(asc.CooldownSlotCount);
            for (int cd = 0; cd < asc.CooldownSlotCount; cd++)
                w.Write(asc.GetCooldown(cd));
            w.Write(asc.PassiveCount);
            for (int p = 0; p < asc.PassiveCount; p++)
                w.Write(asc.GetPassiveTimer(p));
        }

        /// <summary>GE 定义全量 + spec 运行时字段。</summary>
        private static void WriteEffectSpec(GameplayEffectSpec spec, BinaryWriter w)
        {
            GameplayEffect ge = spec.Definition;
            w.Write(ge.Id);
            w.Write((byte)ge.DurationType);
            w.Write(ge.DurationTurns);
            WriteModifiers(w, ge.Modifiers);
            WriteTagArray(w, ge.GrantTags);
            WriteTagArray(w, ge.RemoveTags);
            WriteTagArray(w, ge.ApplicationRequiredTags);
            WriteTagArray(w, ge.ApplicationBlockedTags);
            w.Write(ge.Period.HasValue ? (byte)1 : (byte)0);
            if (ge.Period.HasValue)
            {
                w.Write(ge.Period.Value.Interval);
                w.Write(ge.Period.Value.ExecuteCount);
            }
            w.Write((byte)ge.Stacking);
            w.Write(ge.CooldownTurns);
            w.Write((byte)ge.ExpiryPolicy);
            WriteTagArray(w, ge.ConditionTags);

            w.Write(spec.RemainingTurns);
            w.Write(spec.MagnitudeMultiplier);
            w.Write(spec.SourceId);
            w.Write(spec.TargetId);
            w.Write(spec.PeriodTick);
            w.Write(spec.PeriodRunCount);
            w.Write(spec.StackCount);
            w.Write(spec.CooldownRemaining);
        }

        // ------------------------------------------------------------------
        // 写辅助
        // ------------------------------------------------------------------

        private static void WriteChip(BinaryWriter w, ChipAllocation alloc)
        {
            w.Write(alloc.PlayerId);
            w.Write(alloc.Rock);
            w.Write(alloc.Scissors);
            w.Write(alloc.Paper);
        }

        private static void WriteModifiers(BinaryWriter w, AttributeModifier[] mods)
        {
            if (mods == null) { w.Write(0); return; }
            w.Write(mods.Length);
            for (int i = 0; i < mods.Length; i++)
            {
                w.Write(mods[i].AttributeId);
                w.Write((byte)mods[i].Op);
                w.Write(mods[i].Magnitude);
                w.Write(mods[i].Channel);
                w.Write(mods[i].DependsOn);
            }
        }

        private static void WriteTagArray(BinaryWriter w, GameplayTag[] tags)
        {
            if (tags == null) { w.Write(0); return; }
            w.Write(tags.Length);
            for (int i = 0; i < tags.Length; i++)
                w.Write(tags[i].Id);
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s ?? string.Empty);
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        private static void WriteIntArray(BinaryWriter w, int[] arr)
        {
            if (arr == null) { w.Write(-1); return; }
            w.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) w.Write(arr[i]);
        }

        private static void WriteBoolArray(BinaryWriter w, bool[] arr)
        {
            if (arr == null) { w.Write(-1); return; }
            w.Write(arr.Length);
            for (int i = 0; i < arr.Length; i++) w.Write(arr[i]);
        }

        // ------------------------------------------------------------------
        // 全局段重建
        // ------------------------------------------------------------------

        internal static WorldState ReadGlobal(BinaryReader r)
        {
            int playerCount = r.ReadInt32();
            CheckLength(playerCount, "PlayerCount", GameConfig.MaxPlayers);
            int currentTick = r.ReadInt32();
            int currentTurn = r.ReadInt32();
            TurnPhase phase = (TurnPhase)r.ReadByte();
            int seed = r.ReadInt32();
            ulong randomState = r.ReadUInt64();

            var state = new WorldState(playerCount, seed);
            state.CurrentTick = currentTick;
            state.CurrentTurn = currentTurn;
            state.Phase = phase;
            state.Random.RestoreState(randomState);

            int trapLen = r.ReadInt32();
            CheckLength(trapLen, "LocationTrapCounts", GameConfig.MaxLocations);
            var traps = new int[trapLen];
            for (int i = 0; i < trapLen; i++) traps[i] = r.ReadInt32();
            state.LocationTrapCounts = traps;

            state.ThornPotionTurnsRemaining = r.ReadInt32();
            int movedLen = r.ReadInt32();
            CheckLength(movedLen, "MovedThisTurn", GameConfig.MaxPlayers);
            var moved = new bool[movedLen];
            for (int i = 0; i < movedLen; i++) moved[i] = r.ReadBoolean();
            state.MovedThisTurn = moved;

            state.WoodSeedPlayer = r.ReadInt32();
            state.WoodSeedLocation = r.ReadInt32();
            state.PaladinAegisUsed = r.ReadBoolean();
            state.GhostCloneActive = r.ReadBoolean();
            state.GhostCloneSanity = r.ReadSingle();

            int allocLen = r.ReadInt32();
            CheckLength(allocLen, "LastAllocations", GameConfig.MaxPlayers);
            var allocs = new ChipAllocation[allocLen];
            for (int i = 0; i < allocLen; i++) allocs[i] = ReadChip(r);
            state.LastAllocations = allocs;

            state.HasJudgmentResult = r.ReadBoolean();
            if (state.HasJudgmentResult)
            {
                int winnerHand = r.ReadInt32();
                int loserHand = r.ReadInt32();
                int neutralHand = r.ReadInt32();
                bool isGlobalFlat = r.ReadBoolean();
                int winnerCount = r.ReadInt32();
                int loserCount = r.ReadInt32();
                int roleLen = r.ReadInt32();
                CheckLength(roleLen, "Roles", GameConfig.MaxPlayers);
                var roles = new JudgmentRole[roleLen];
                for (int i = 0; i < roleLen; i++) roles[i] = (JudgmentRole)r.ReadByte();
                state.LastJudgment = new JudgmentResult
                {
                    WinnerHand = winnerHand,
                    LoserHand = loserHand,
                    NeutralHand = neutralHand,
                    IsGlobalFlat = isGlobalFlat,
                    WinnerCount = winnerCount,
                    LoserCount = loserCount,
                    Roles = roles,
                    EffectiveChips = ReadIntArray(r, "EffectiveChips", GameConfig.MaxPlayers),
                    APGained = ReadIntArray(r, "APGained", GameConfig.MaxPlayers)
                };
            }

            var tm = new TurnManager();
            tm.RestoreConsecutiveFlatCount(r.ReadInt32());
            tm.RestoreActionOrder(ReadIntArray(r, "ActionOrder", GameConfig.MaxPlayers));
            tm.RestoreActorIndex(r.ReadInt32());
            state.TurnManager = tm;

            int locationCount = r.ReadInt32();
            CheckLength(locationCount, "Locations", GameConfig.MaxLocations);
            var allItems = new LocatedItem[locationCount][];
            for (int i = 0; i < locationCount; i++)
            {
                int itemCount = r.ReadInt32();
                CheckLength(itemCount, "LocatedItems", 64);   // 单地点道具组数上限（对象数组，防 OOM）
                var items = new LocatedItem[itemCount];
                for (int j = 0; j < itemCount; j++)
                    items[j] = new LocatedItem { ItemId = r.ReadInt32(), Count = r.ReadInt32() };
                allItems[i] = items;
            }
            state.Locations.RestoreRuntimeItems(allItems);

            int ruleCount = r.ReadInt32();
            CheckLength(ruleCount, "Rules", GameConfig.MaxLocations);
            var rules = new LocationRule[ruleCount];
            for (int i = 0; i < ruleCount; i++)
                rules[i] = ReadRule(r, i);
            state.Locations.Rules = rules;

            return state;
        }

        private static LocationRule ReadRule(BinaryReader r, int index)
        {
            byte type = r.ReadByte();
            int locationId = r.ReadInt32();
            switch (type)
            {
                case 0:
                    // 写侧 type=0 时写下标占位，校验其等于数组下标（LocationManager 按 LocationId 注册）
                    if (locationId != index)
                        throw new InvalidDataException($"空地点规则 LocationId 与下标不一致（读取 {locationId}，期望 {index}）");
                    return null;
                case 1:
                    if (locationId != HideoutRule.HideoutLocationId)
                        throw new InvalidDataException($"HideoutRule LocationId 非法: {locationId}");
                    return new HideoutRule();
                case 2:
                {
                    if (locationId != LabRule.LabLocationId)
                        throw new InvalidDataException($"LabRule LocationId 非法: {locationId}");
                    var lab = new LabRule();
                    lab.DoorOpen = r.ReadBoolean();
                    lab.SwitchOn = r.ReadBoolean();
                    lab.ThresholdRemoved = r.ReadBoolean();
                    lab.TideTurnsElapsed = r.ReadInt32();
                    return lab;
                }
                case 3:
                    if (locationId != OutpostRule.OutpostLocationId)
                        throw new InvalidDataException($"OutpostRule LocationId 非法: {locationId}");
                    return new OutpostRule();
                default:
                    throw new InvalidDataException($"未知地点规则类型 {type}（LocationId={locationId}）");
            }
        }

        // ------------------------------------------------------------------
        // 每角色段重建
        // ------------------------------------------------------------------

        internal static Character ReadCharacter(BinaryReader r)
        {
            bool has = r.ReadBoolean();
            if (!has) return null;

            int defId = r.ReadInt32();
            CharacterDef def = CharacterDefCatalog.Get(defId);
            if (def == null)
                throw new InvalidDataException($"未知角色 DefId {defId}，无法重建角色");
            int id = r.ReadInt32();
            int playerId = r.ReadInt32();
            string name = ReadString(r);
            int locationId = r.ReadInt32();

            var c = new Character(id, playerId, name) { CurrentLocationId = locationId };
            def.ApplyTo(c);
            c.Name = name;   // ApplyTo 会覆盖为 def.Name，用序列化值覆盖回
            for (int s = 0; s < c.Definition.Skills.Length; s++)
                c.ASC.RegisterPassive(c.Definition.Skills[s]);
            c.ASC.EnsureCooldownSlots(c.Definition.Skills.Length);

            // Inventory（直接字段赋值，不经 EquipWeapon——避免重复挂 EquipEffect 长驻 GE）
            int weaponId = r.ReadInt32();
            int ammo = r.ReadInt32();
            int consCount = r.ReadInt32();
            CheckLength(consCount, "Consumables", 16);   // MaxConsumables=4，留余量
            c.Inventory.EquippedWeapon = weaponId > 0 ? ItemRegistry.Get(weaponId) : null;
            c.Inventory.EquippedWeaponAmmo = ammo;
            c.Inventory.Consumables.Clear();
            for (int i = 0; i < consCount; i++)
            {
                ItemDef item = ItemRegistry.Get(r.ReadInt32());
                if (item != null) c.Inventory.Consumables.Add(item);
            }

            // 属性：两趟恢复 —— 先全部 SetBase（MaxSanity 等依赖项就位），再全部 SetCurrent
            // （Sanity 钳制读到已恢复的 MaxSanity；逐属性交替恢复会读到旧 MaxSanity）。
            // 读侧流只能顺序消费一次，先整体读入 base/current 再分两趟恢复。
            float[] baseValues = new float[AttributeConstants.Count];
            float[] currentValues = new float[AttributeConstants.Count];
            for (int a = 1; a <= AttributeConstants.Count; a++)
            {
                baseValues[a - 1] = r.ReadSingle();
                currentValues[a - 1] = r.ReadSingle();
            }
            for (int a = 1; a <= AttributeConstants.Count; a++)
                c.ASC.SetAttributeBaseValue(a, baseValues[a - 1]);
            for (int a = 1; a <= AttributeConstants.Count; a++)
                c.ASC.SetAttributeCurrentValue(a, currentValues[a - 1]);
            c.ASC.Tags.RestoreMask(r.ReadInt64());

            int effectCount = r.ReadInt32();
            CheckLength(effectCount, "ActiveEffects", 64);   // 对象数组（GE spec 池化），防 OOM
            for (int e = 0; e < effectCount; e++)
                c.ASC.RestoreActiveEffect(ReadEffectSpec(r, c.ASC));

            c.ASC.ModifyGuardianCharge(r.ReadInt32() - c.ASC.GetGuardianCharge());
            c.ASC.SetMistLayers(r.ReadInt32());
            c.ASC.SetBloodSeals(r.ReadInt32());
            c.ASC.ModifyInstinct(r.ReadInt32() - c.ASC.GetInstinct());
            c.ASC.MarkDamageDealt(r.ReadInt32());

            int cdCount = r.ReadInt32();
            CheckLength(cdCount, "Cooldowns", 16);
            for (int cd = 0; cd < cdCount; cd++)
                c.ASC.SetCooldown(cd, r.ReadInt32());

            int passiveCount = r.ReadInt32();
            CheckLength(passiveCount, "PassiveTimers", 8);
            var timers = new int[passiveCount];
            for (int p = 0; p < passiveCount; p++) timers[p] = r.ReadInt32();
            c.ASC.RestorePassiveTimers(timers);

            return c;
        }

        private static GameplayEffectSpec ReadEffectSpec(BinaryReader r, AbilitySystemComponent asc)
        {
            int id = r.ReadInt32();
            var durationType = (GameplayEffectDurationType)r.ReadByte();
            int durationTurns = r.ReadInt32();
            AttributeModifier[] modifiers = ReadModifiers(r);
            GameplayTag[] grantTags = ReadTagArray(r, "GrantTags");
            GameplayTag[] removeTags = ReadTagArray(r, "RemoveTags");
            GameplayTag[] appReqTags = ReadTagArray(r, "ApplicationRequiredTags");
            GameplayTag[] appBlockedTags = ReadTagArray(r, "ApplicationBlockedTags");
            bool hasPeriod = r.ReadByte() != 0;
            GameplayEffectPeriod? period = hasPeriod
                ? new GameplayEffectPeriod(r.ReadInt32(), r.ReadInt32())
                : (GameplayEffectPeriod?)null;
            var stacking = (GameplayEffectStacking)r.ReadByte();
            int cooldownTurns = r.ReadInt32();
            var expiryPolicy = (GameplayEffectExpiryPolicy)r.ReadByte();
            GameplayTag[] conditionTags = ReadTagArray(r, "ConditionTags");

            var ge = new GameplayEffect
            {
                Id = id,
                DurationType = durationType,
                DurationTurns = durationTurns,
                Modifiers = modifiers,
                GrantTags = grantTags,
                RemoveTags = removeTags,
                ApplicationRequiredTags = appReqTags,
                ApplicationBlockedTags = appBlockedTags,
                Period = period,
                Stacking = stacking,
                CooldownTurns = cooldownTurns,
                ExpiryPolicy = expiryPolicy,
                ConditionTags = conditionTags
            };

            GameplayEffectSpec spec = asc.RentSpec();
            spec.Definition = ge;
            spec.RemainingTurns = r.ReadInt32();
            spec.MagnitudeMultiplier = r.ReadSingle();
            spec.SourceId = r.ReadInt32();
            spec.TargetId = r.ReadInt32();
            spec.PeriodTick = r.ReadInt32();
            spec.PeriodRunCount = r.ReadInt32();
            spec.StackCount = r.ReadInt32();
            spec.CooldownRemaining = r.ReadInt32();
            return spec;
        }

        // ------------------------------------------------------------------
        // 读辅助
        // ------------------------------------------------------------------

        private static ChipAllocation ReadChip(BinaryReader r)
        {
            var alloc = new ChipAllocation(r.ReadInt32());
            alloc.Rock = r.ReadInt32();
            alloc.Scissors = r.ReadInt32();
            alloc.Paper = r.ReadInt32();
            return alloc;
        }

        private static AttributeModifier[] ReadModifiers(BinaryReader r)
        {
            int count = r.ReadInt32();
            CheckLength(count, "Modifiers", 256);
            var mods = new AttributeModifier[count];
            for (int i = 0; i < count; i++)
            {
                int attributeId = r.ReadInt32();
                var op = (AttributeModifierOp)r.ReadByte();
                float magnitude = r.ReadSingle();
                int channel = r.ReadInt32();
                int dependsOn = r.ReadInt32();
                mods[i] = new AttributeModifier(attributeId, op, magnitude, channel, dependsOn);
            }
            return mods;
        }

        private static GameplayTag[] ReadTagArray(BinaryReader r, string what, int cap = 256)
        {
            int count = r.ReadInt32();
            CheckLength(count, what, cap);
            var tags = new GameplayTag[count];
            for (int i = 0; i < count; i++) tags[i] = new GameplayTag(r.ReadInt32());
            return tags;
        }

        private static string ReadString(BinaryReader r)
        {
            int len = r.ReadInt32();
            CheckLength(len, "String");
            byte[] bytes = r.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidDataException("String 数据截断");
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static int[] ReadIntArray(BinaryReader r, string what, int cap = MaxArrayLength)
        {
            int len = r.ReadInt32();
            if (len == -1) return null;   // null 哨兵（TurnManager 为 null 时的 ActionOrder）
            CheckLength(len, what, cap);
            var arr = new int[len];
            for (int i = 0; i < len; i++) arr[i] = r.ReadInt32();
            return arr;
        }
    }
}
