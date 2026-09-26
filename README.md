# 劫狱贿赂（Bribe the Guards）子功能

在劫狱冷却菜单（`prison_break_cool_down`）中新增一个“贿赂狱卒”按钮，玩家可花钱缩短劫狱冷却。

## 功能说明

- **入口**：进入敌方据点地牢尝试劫狱失败后，会进入 `prison_break_cool_down` 菜单（提示“high alert”）。
  本功能在该菜单新增按钮 `Bribe the guards ({BRIBE_COST}{GOLD_ICON})`，位置与原版 `Leave` 同级。
- **点击效果**：
  1. 额外扣除一笔金币（见“费用”）。
  2. 把当前据点的劫狱冷却从原版 7 天**降低到 0.5 天**（约半天）。
  3. 像真正劫狱一样，给主角增加**流氓（Roguery）技能经验**（与原版劫狱成功奖励一致）。
  4. 弹黄字提示：“The guards took your money and went to smooth things over...”。
- **费用**：复制原版劫狱消耗公式独立重写后 ×1.5，不调用游戏原方法。
  公式：`基础1000 + 取整( 赎金估值/2000 × 城镇Security × 40 − 流氓技能×10 )，再 ×1.5`。
  可在 `PrisonBreakBribeBehavior.cs` 顶部常量 `BribeCostMultiplier`、`BribeCoolDownInDays` 调整。
- **治安度**：本版本**未**改动 `Town.Security`（按需求保留，未来需要可在此扩展）。

## 关键可调参数（PrisonBreakBribeBehavior.cs）

| 常量 | 含义 | 默认 |
|---|---|---|
| `BribeCoolDownInDays` | 贿赂后剩余冷却天数 | `0.5` |
| `BribeCostMultiplier` | 费用倍率（相对原版劫狱消耗） | `1.5` |

## 实现要点

- 通过 `CampaignEvents.OnSessionLaunchedEvent` 在会话启动时，用 `CampaignGameStarter.AddGameMenuOption` 注入菜单按钮（与原版 `PrisonBreakCampaignBehavior` 注入方式一致）。
- 冷却改写：通过 `Harmony.Traverse` 反射读取/写入原版 `PrisonBreakCampaignBehavior._coolDownData` 私有字典，不新增任何存档字段。
- 流氓经验：`Campaign.Current.Models.PrisonBreakModel.GetRogueryRewardOnPrisonBreak(prisonerHero, true)` + `Hero.MainHero.AddSkillXp`。

## 日志

- 日志类：`PrisonBreakBribeDebugLog`（仿 `EquipmentAffixSystem/Debug/AffixLifecycleDebugLog` 实现）。
- 输出文件：`Modules/New_ZZZF/prison_break_bribe_debug.log`。
- 记录内容：菜单注入、按钮显示（费用/金币）、扣费、降冷却、加经验、以及取不到原版实例的失败（ERROR）。
- 日志全程 try/catch + lock，不影响游戏逻辑。

## 如何关闭 / 删除（可随时拆分）

1. **仅关闭**：删除 `SubModule.cs` 中 `InitializeGameStarter` 里的
   `campaignGameStarter.AddBehavior(new New_ZZZF.PrisonBreakBribe.PrisonBreakBribeBehavior());` 一行。
2. **彻底移除**：删除整个 `PrisonBreakBribe/` 文件夹（含 `PrisonBreakBribeBehavior.cs`、`PrisonBreakBribeDebugLog.cs`、`README.md`）。
   删除后原版劫狱冷却逻辑自动恢复，不影响存档。
