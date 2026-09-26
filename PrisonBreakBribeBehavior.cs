using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace New_ZZZF.PrisonBreakBribe
{
    /// <summary>
    /// 独立功能：在劫狱冷却菜单（prison_break_cool_down）中新增一个“贿赂狱卒”按钮。
    /// 点击后额外扣除一笔金币（= 游戏原本劫狱消耗 ×1.5），将当前据点的劫狱冷却降低到 0.5 天，
    /// 并像真正劫狱一样增加主角的流氓（Roguery）技能经验。
    ///
    /// 本功能完全独立，可随时删除：
    ///   1. 删除本文件夹；
    ///   2. 在 SubModule.cs 的 InitializeGameStarter 中去掉 AddBehavior(new PrisonBreakBribeBehavior()) 一行。
    /// 删除后原版劫狱冷却逻辑自动恢复（冷却改写通过反射原版私有字段实现，不新增任何存档字段）。
    /// </summary>
    public class PrisonBreakBribeBehavior : CampaignBehaviorBase
    {
        // 贿赂后劫狱冷却剩余时间（天）。0.5 天即约半天。
        private const float BribeCoolDownInDays = 0.5f;

        // 贿赂费用是原版劫狱消耗的倍率。
        private const float BribeCostMultiplier = 1.5f;

        // 菜单项 id（与冷却菜单的 leave 同级）。
        private const string OptionId = "zzzf_bribe_guards_reduce_cooldown";

        // 缓存当前据点的估算贿赂费用，供 on_condition 显示与 on_consequence 扣费使用。
        private int _cachedBribeCost;

        public override void RegisterEvents()
        {
            // 与原版 PrisonBreakCampaignBehavior 同样的注入时机：会话启动时把按钮挂到冷却菜单。
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(
                this, new Action<CampaignGameStarter>(OnSessionLaunched));
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 不引入任何存档字段，删除本功能不影响存档。
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(
                "prison_break_cool_down",
                OptionId,
                "{=ZZZF_BRIBE_GUARDS}Bribe the guards ({BRIBE_COST}{GOLD_ICON})",
                new GameMenuOption.OnConditionDelegate(OnBribeCondition),
                new GameMenuOption.OnConsequenceDelegate(OnBribeConsequence),
                false,
                1,   // 与 leave(-1) 同区域，排在其后
                false,
                null);

            PrisonBreakBribeDebugLog.Info("Injected 'Bribe the guards' option into prison_break_cool_down menu.");
        }

        // ===== 按钮显示条件：处于冷却中 + 金币足够 =====
        private bool OnBribeCondition(MenuCallbackArgs args)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null)
                return false;

            // 仅在确实处于劫狱冷却时显示本按钮。
            if (!IsInPrisonBreakCoolDown(settlement))
                return false;

            // 估算贿赂费用（基于原版劫狱消耗公式重新实现，再 ×1.5）。
            _cachedBribeCost = ComputeBribeCost(settlement);

            PrisonBreakBribeDebugLog.Info(
                $"Bribe option shown at '{settlement.Name}' (settlement={settlement.StringId}). " +
                $"Estimated bribe cost = {_cachedBribeCost}, player gold = {Hero.MainHero.Gold}.");

            // 把费用文本写进按钮字符串中的 {BRIBE_COST} 变量（MBTextManager 全局文本变量机制）。
            MBTextManager.SetTextVariable("BRIBE_COST", _cachedBribeCost);

            bool enoughGold = Hero.MainHero.Gold >= _cachedBribeCost;
            args.IsEnabled = enoughGold;
            if (!enoughGold)
            {
                args.Tooltip = new TextObject("{=ZZZF_NO_GOLD}You don't have enough money.", null);
            }
            return true;
        }

        // ===== 按钮点击：扣费 + 降低冷却 + 加流氓经验 =====
        private void OnBribeConsequence(MenuCallbackArgs args)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            if (settlement == null)
            {
                PrisonBreakBribeDebugLog.Warn("OnBribeConsequence: Settlement.CurrentSettlement is null, abort.");
                return;
            }

            int cost = ComputeBribeCost(settlement);
            if (Hero.MainHero.Gold < cost)
            {
                PrisonBreakBribeDebugLog.Warn(
                    $"OnBribeConsequence: not enough gold at '{settlement.Name}'. need={cost}, have={Hero.MainHero.Gold}. abort.");
                return;
            }

            // 1) 额外扣除金币（给无主，即消耗掉）。
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, cost, false);
            PrisonBreakBribeDebugLog.Info(
                $"Bribed at '{settlement.Name}' (settlement={settlement.StringId}). " +
                $"Gold deducted = {cost}, remaining gold = {Hero.MainHero.Gold}.");

            // 2) 将当前据点的劫狱冷却降低到 0.5 天（通过反射改写原版私有字段）。
            ReduceCoolDownTo(settlement, BribeCoolDownInDays);
            PrisonBreakBribeDebugLog.Info(
                $"Cooldown reduced to {BribeCoolDownInDays} day(s) for '{settlement.Name}'.");

            // 3) 增加流氓技能经验（与原版劫狱成功时一致）。
            float xp = GrantRogueryXpLikePrisonBreak(settlement);
            PrisonBreakBribeDebugLog.Info(
                $"Roguery XP granted = {xp} to {Hero.MainHero.Name}.");

            // 4) 文字提示：狱卒收了钱，让你等待一段时间去打点。
            InformationManager.DisplayMessage(new InformationMessage(
                new TextObject("{=ZZZF_BRIBE_DONE}The guards took your money and went to smooth things over. You'll have to wait a short while before they'll talk to you again.").ToString(),
                Colors.Yellow));

            // 回到冷却菜单（此时冷却已缩短到 0.5 天，可再次点击继续缩短）。
            GameMenu.SwitchToMenu("prison_break_cool_down");
        }

        // ===== 费用计算：复制原版公式后独立重写，再乘倍率 =====
        // 原版 GetPrisonBreakStartCost（DefaultPrisonBreakModel）逻辑：
        //   num = ceil( PrisonerRansomValue / 2000 * Town.Security * 40 - Roguery*10 )
        //   num = num < 100 ? 0 : num/100*100
        //   return num + 1000
        // 这里重新实现同样的算法，并额外乘以 BribeCostMultiplier，不调用游戏原方法。
        private int ComputeBribeCost(Settlement settlement)
        {
            Hero prisonerHero = GetFirstHeroPrisoner(settlement);
            if (prisonerHero == null)
            {
                // 冷却期通常仍有俘虏；若取不到则用基础值估算。
                return (int)MathF.Round(1000f * BribeCostMultiplier);
            }

            int rogueSkill = Hero.MainHero.GetSkillValue(DefaultSkills.Roguery);
            float security = settlement.Town != null ? settlement.Town.Security : 50f;
            int ransom = Campaign.Current.Models.RansomValueCalculationModel.PrisonerRansomValue(prisonerHero.CharacterObject, null);

            float raw = (ransom / 2000f) * security * 40f - (float)(rogueSkill * 10);
            int num = (int)MathF.Ceiling(raw);
            num = num < 100 ? 0 : (num / 100 * 100);
            int baseCost = num + 1000;

            return (int)MathF.Round(baseCost * BribeCostMultiplier);
        }

        // ===== 流氓经验：与原版劫狱成功时一致 =====
        private float GrantRogueryXpLikePrisonBreak(Settlement settlement)
        {
            Hero prisonerHero = GetFirstHeroPrisoner(settlement);
            // 使用与原版 OpenPrisonBreakMission 相同的奖励模型（成功值）。
            float xp = Campaign.Current.Models.PrisonBreakModel.GetRogueryRewardOnPrisonBreak(
                prisonerHero ?? Hero.MainHero, true);
            Hero.MainHero.HeroDeveloper.AddSkillXp(DefaultSkills.Roguery, xp, true, true);
            return xp;
        }

        // ===== 取当前据点监狱中第一名英雄俘虏 =====
        private Hero GetFirstHeroPrisoner(Settlement settlement)
        {
            if (settlement == null)
                return null;

            var roster = settlement.Party.PrisonRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                var element = roster.GetElementCopyAtIndex(i);
                if (element.Character != null && element.Character.IsHero)
                    return element.Character.HeroObject;
            }
            return null;
        }

        // ===== 判断当前据点是否处于劫狱冷却 =====
        private bool IsInPrisonBreakCoolDown(Settlement settlement)
        {
            var dict = GetCoolDownData();
            if (dict == null)
                return false;
            if (dict.TryGetValue(settlement, out CampaignTime t))
                return !t.IsPast; // 未过期即在冷却中
            return false;
        }

        // ===== 将冷却降低到指定天数（反射改写原版 _coolDownData） =====
        private void ReduceCoolDownTo(Settlement settlement, float days)
        {
            var dict = GetCoolDownData();
            if (dict == null)
                return;
            CampaignTime reduced = CampaignTime.Now + CampaignTime.DaysFromNow(days);
            if (dict.ContainsKey(settlement))
                dict[settlement] = reduced;
            else
                dict.Add(settlement, reduced);
        }

        // 通过 Harmony Traverse 读取原版 PrisonBreakCampaignBehavior 的私有字段 _coolDownData。
        // 注意：Campaign 只提供泛型 GetCampaignBehavior<T>()，跨程序集拿不到强类型，
        // 因此用 GetCampaignBehaviors<CampaignBehaviorBase>() 遍历并按类型名匹配。
        private Dictionary<Settlement, CampaignTime> GetCoolDownData()
        {
            var campaign = Campaign.Current;
            if (campaign == null)
            {
                PrisonBreakBribeDebugLog.Error("GetCoolDownData: Campaign.Current is null.");
                return null;
            }

            object instance = null;
            foreach (var behavior in campaign.GetCampaignBehaviors<CampaignBehaviorBase>())
            {
                if (behavior != null && behavior.GetType().FullName == "SandBox.CampaignBehaviors.PrisonBreakCampaignBehavior")
                {
                    instance = behavior;
                    break;
                }
            }

            if (instance == null)
            {
                PrisonBreakBribeDebugLog.Error(
                    "GetCoolDownData: PrisonBreakCampaignBehavior instance not found. " +
                    "Bribe cooldown reduction will NOT work.");
                return null;
            }
            return Traverse.Create(instance).Field<Dictionary<Settlement, CampaignTime>>("_coolDownData").Value;
        }
    }
}
