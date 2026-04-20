using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Judgment;

namespace Gameplay.Characters
{
    /// <summary>
    /// 混沌法师「混乱」（技能 Id=120）：模拟使用一件道具/武器。
    /// 载荷：ItemId = 模拟池道具 Id（刀1/血包2/枪3/锤4/荆棘素6/炸弹7/净化9），
    /// TargetPlayerId = 效果目标（自身类道具传自己）。
    /// 执行顺序（S1/S2）：池/目标校验 → 扣 1 AP → 反噬判定（对手 = 敌人目标；
    /// 无敌人目标 = 数组序第一存活他人；无他人则视为胜）→ 败：自扣 1 理智、效果不结算
    /// （理智归零时入队死亡结算，不做直扣存活）。
    /// 豁免：地理/持有限制（S1）；荆棘素从自身位置放；枪免弹药；地狱的子民不拦截（法术模拟）。
    /// </summary>
    public class ChaosSimulateCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int ItemId { get; set; }
        public int TargetPlayerId { get; set; }

        public ChaosSimulateCommand() { }
        public ChaosSimulateCommand(int tick, int playerId, int itemId, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; ItemId = itemId; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;
            if (c.Definition?.Id != SkillSystem.ChaosMageCharacterId) return;

            if (!IsSimulatable(ItemId)) return; // 池外：不扣 AP

            bool selfItem = IsSelfItem(ItemId);
            if (selfItem)
            {
                if (TargetPlayerId != PlayerId) return;
            }
            else
            {
                Character target = state.GetCharacterByPlayer(TargetPlayerId);
                if (target == null || target.IsDead || target.PlayerId == c.PlayerId) return;
            }

            // 扣 AP（施放行为发生；判定败也照扣——S2）
            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // 反噬判定对手：敌人道具的目标；否则数组序第一存活他人（地狱呓语先例）。
            // 无目标分支复用 GhostAbyss.FindFirstOtherAlive——存活他人按 Sanity > 0 裁决，
            // 与 DamagePipeline 死亡判据同源：4AP 混沌同回合击杀后继续模拟自用道具时，
            // DeathCommand 未结算的尸体（!IsDead 且 Sanity=0）不得参与判定（无他人视为胜）。
            Character rival = null;
            if (!selfItem)
                rival = state.GetCharacterByPlayer(TargetPlayerId);
            if (rival == null)
                rival = GhostAbyss.FindFirstOtherAlive(state, c);
            bool win = rival == null || JudgmentDuel.Resolve(state, c, rival);
            if (!win)
            {
                c.ASC.ModifyAttributeCurrentValue(AttributeConstants.Sanity, -1f); // 反噬自损
                if (c.Sanity <= 0f)
                {
                    // 自损致死须走统一死亡结算（引擎非伤害直扣先例均钳 0.5 防致死，
                    // 反噬直扣可能到 0）：对照 DamagePipeline 死亡出口入队 DeathCommand，
                    // LocalLockstep 在本趟 ExecuteAll 内完成死亡结算。
                    state.Commands.Enqueue(new DeathCommand(state.CurrentTick, c.PlayerId, -1));
                }
                return;
            }

            Simulate(state, c, ItemId, TargetPlayerId);
        }

        /// <summary>模拟池候选 Id 单源表（规格 3.2：刀1/血包2/枪3/锤4/荆棘素6/炸弹7/净化9；
        /// 排除核弹 5/应急装置 8/海幻刀 12/圣盾 13/重质量炮）。UI 候选遍历与成员判定
        /// 共用本表，禁止另设池表/硬编码上限——改白名单只动这里（静态只读数组引用安全）。</summary>
        public static readonly int[] SimPool = { 1, 2, 3, 4, 6, 7, 9 };

        /// <summary>模拟池成员判定（与 SimPool 同源扫描；命令执行路径零堆分配）。</summary>
        public static bool IsSimulatable(int itemId)
        {
            for (int i = 0; i < SimPool.Length; i++)
                if (SimPool[i] == itemId) return true;
            return false;
        }

        /// <summary>自身类道具：血包(2)/荆棘素(6)/炸弹(7)/净化(9)。</summary>
        public static bool IsSelfItem(int itemId)
        {
            switch (itemId)
            {
                case 2: case 6: case 7: case 9: return true;
                default: return false;
            }
        }

        /// <summary>按 itemId 复刻 ItemCommands 各效果管线（模拟不消耗实物；Hot Path 零堆分配）。</summary>
        private static void Simulate(WorldState state, Character c, int itemId, int targetPlayerId)
        {
            switch (itemId)
            {
                case 1: // 刀：近战物理（豁免地理，直接入管线）
                case 4: // 施工锤：近战物理（不做投掷/拆除）
                case 3: // 枪：远程物理（免弹药、免地理、免藏匿特判——模拟攻击直入管线）
                {
                    // 目标按 PlayerId 解析为 Character（伤害管线按 Character.Id 定位）
                    Character target = state.GetCharacterByPlayer(targetPlayerId);
                    if (target == null || target.IsDead) return; // 目标在判定后失效：效果不结算
                    // 伤害数值单一来源 = ItemRegistry.UseEffect（GetSanityDamage），与
                    // UseItemCommand 同源——改动数值须改注册表，禁止在此另设伤害常量
                    float damage = ItemRegistry.GetSanityDamage(ItemRegistry.Get(itemId));
                    if (damage <= 0f) return; // 注册表缺伤害定义（数据缺失）：效果不结算
                    DamagePipeline.ApplyDamage(state, c.Id, target.Id, damage, DamageType.Physical);
                    break;
                }
                case 2: // 血包：回 3 理智（复用道具 UseEffect GE，钳 MaxSanity）
                    ApplyItemEffect(c, ItemRegistry.Get(2));
                    break;
                case 6: // 荆棘素：自身挂 3 回合荆棘标签（GE 105）+ 全局 DOT 计数器 = 3
                    ApplyItemEffect(c, ItemRegistry.Get(6));
                    state.ThornPotionTurnsRemaining = 3;
                    break;
                case 7: // 触发式炸弹：当前地点安放陷阱
                    TrapSystem.Plant(state, c.CurrentLocationId, 7);
                    break;
                case 9: // 净化药剂：清负面 + 挂增益（实验室 0 AP 特例不生效——混乱统一 1 AP）
                    PurifierEffect.Apply(state, c);
                    break;
            }
        }

        /// <summary>道具 UseEffect GE 通用挂载（血包/荆棘素路径，同 UseItemCommand 分支）。</summary>
        private static void ApplyItemEffect(Character c, ItemDef item)
        {
            if (item == null || item.UseEffect == null) return;
            GameplayEffectSpec spec = c.ASC.RentSpec();
            spec.Definition = item.UseEffect;
            spec.SourceId = c.Id;
            spec.TargetId = c.Id;
            c.ASC.ApplyGameplayEffect(spec);
        }
    }
}
