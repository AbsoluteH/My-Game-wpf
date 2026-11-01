using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeGame.Entities;

/// <summary>
/// 城市定义。坐标只用于计算两地距离（进而换算成天数与路费）；
/// Multipliers 表示该城经营哪些商品、以及各自的产销倍率：大于 1 是产区（便宜），小于 1 是缺区（昂贵）。
/// 只有出现在 Multipliers 里的商品，该城市场才会卖出；但任何商品该城都会收购（未经营的按保守倍率定价）。
/// 城市有等级（1-3）：玩家交易与投资会积累繁荣度，升级后解锁更高等级的商品。
/// 同时保存运行时的库存、平衡库存和当前价格。
/// </summary>
public sealed class City
{
    /// <summary>城市最高等级。</summary>
    public const int MaxLevel = 3;

    /// <summary>
    /// 城市不经营某种货物时仍会照价收购，用这个缺货倍率定价。
    /// 倍率小于 1 表示本地没有货源、只能靠外来供给，因此收购价高于基准价，
    /// 但低于真正稀缺的经营城市——把货从产地运到缺货城市才能赚到钱。
    /// </summary>
    public const double UntradedMultiplier = 0.75;

    /// <summary>
    /// 升到下一级所需的累计繁荣度（索引 = 当前等级），由 Data/game-data.json 的
    /// balance.cityLevelThresholds 配置，默认 { 0, 6000, 18000 }。
    /// </summary>
    private static int[] _prosperityThresholds = { 0, 6000, 18000 };

    /// <summary>由数据加载器注入升级门槛；非法数据已在加载时校验拦截。</summary>
    public static void ConfigureProsperityThresholds(IReadOnlyList<int> thresholds)
    {
        if (thresholds.Count < MaxLevel + 1)
        {
            return;
        }

        _prosperityThresholds = thresholds.Take(MaxLevel + 1).ToArray();
    }

    public City(string name, double x, double y, Dictionary<string, double> multipliers, bool isPort)
    {
        Name = name;
        X = x;
        Y = y;
        Multipliers = multipliers;
        IsPort = isPort;
    }

    public string Name { get; }

    // 简化的地图坐标，仅用于算距离，不做真实寻路
    public double X { get; }
    public double Y { get; }

    /// <summary>是否为口岸城市：舶来品的产地只能是口岸，内陆城市只能分销。</summary>
    public bool IsPort { get; }

    /// <summary>城市产销倍率：越大于 1 越盛产（价格越低），越小于 1 越稀缺（价格越高）。</summary>
    public Dictionary<string, double> Multipliers { get; }

    /// <summary>城市等级 1-3，决定市场里能出现哪些等级的商品。</summary>
    public int Level { get; private set; } = 1;

    /// <summary>累计繁荣度：玩家在此交易或投资时增加，到达阈值即升级。</summary>
    public int Prosperity { get; private set; }

    /// <summary>升到下一级所需的累计繁荣度；已满级时返回 0。</summary>
    public int ProsperityNeeded => Level < MaxLevel ? _prosperityThresholds[Level] : 0;

    /// <summary>该城是否经营这种货物（由产销倍率表决定，与等级无关）。</summary>
    public bool SellsGood(string goodId) => Multipliers.ContainsKey(goodId);

    /// <summary>
    /// 取该货物的本地产销倍率；城市不经营它时返回缺货倍率（本地没有货源，收购价比基准高）。
    /// </summary>
    public double MultiplierFor(string goodId) =>
        Multipliers.TryGetValue(goodId, out double multiplier) ? multiplier : UntradedMultiplier;

    /// <summary>
    /// 该城当前是否可以买入这种货物：本地经营它、城市等级达到商品要求、且玩家在本城声望达到要求。
    /// 声望由玩家在本城的行会任务积累，和城市等级是两条独立的解锁线。
    /// </summary>
    public bool CanTrade(Good good, int reputationLevel) =>
        Level >= good.Tier && reputationLevel >= good.ReputationTier && SellsGood(good.Id);

    /// <summary>增加繁荣度并自动升级，返回本次是否发生了升级（可连升多级）。</summary>
    public bool AddProsperity(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        Prosperity += amount;

        bool leveled = false;
        while (Level < MaxLevel && Prosperity >= _prosperityThresholds[Level])
        {
            Level++;
            leveled = true;
        }

        return leveled;
    }

    /// <summary>当前库存：玩家买入会减少，卖出会增加，库存变化会反作用于价格。</summary>
    public Dictionary<string, int> Stock { get; } = new();

    /// <summary>平衡库存：库存偏离它时价格产生修正，每天库存也会向它缓慢回归。</summary>
    public Dictionary<string, int> Equilibrium { get; } = new();

    /// <summary>当前每件商品的买价（卖价在此基础上打折扣）。</summary>
    public Dictionary<string, int> Prices { get; } = new();

    /// <summary>随机行情事件造成的临时价格倍率（货物 id → 倍率），没有事件时按 1.0 处理。</summary>
    public Dictionary<string, double> EventMultipliers { get; } = new();

    /// <summary>该货物当前的行情事件倍率：>1 表示暴涨、&lt;1 表示大跌，无事件时为 1.0。</summary>
    public double EventMultiplierFor(string goodId) =>
        EventMultipliers.TryGetValue(goodId, out double multiplier) ? multiplier : 1.0;

    /// <summary>行情事件期间的硬性价格下限（货物 id → 最低买价），0 表示无限制。</summary>
    public Dictionary<string, int> PriceFloors { get; } = new();

    /// <summary>取某货物的最低允许买价；0 表示不做限制。</summary>
    public int PriceFloorFor(string goodId) =>
        PriceFloors.TryGetValue(goodId, out int floor) ? floor : 0;

    /// <summary>两点间的欧氏距离，作为“路程”的抽象单位。</summary>
    public double DistanceTo(City other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    /// <summary>把距离换算成行进天数：每 40 距离单位走 1 天，至少 1 天。</summary>
    public int DaysTo(City other) => Math.Max(1, (int)Math.Ceiling(DistanceTo(other) / 40.0));
}
