using System.Collections.Generic;
using System.Linq;

namespace TradeGame.Entities;

/// <summary>
/// 城市仓库：玩家在某城建造的存储设施，容量随等级提升（Lv1 200、Lv2 400、Lv3 700 单位）。
/// 同时存放作坊的每日产出和玩家从商队卸下的货物，所有货物共用同一份容量。
/// </summary>
public sealed class Warehouse
{
    /// <summary>仓库最高等级。</summary>
    public const int MaxLevel = 3;

    /// <summary>各等级的容量，索引即等级（0 表示还没建）。</summary>
    private static readonly int[] Capacities = { 0, 200, 400, 700 };

    /// <summary>仓库等级 0-3，0 表示尚未建造。</summary>
    public int Level { get; private set; }

    /// <summary>是否已建造。</summary>
    public bool IsBuilt => Level > 0;

    public bool IsMaxLevel => Level >= MaxLevel;

    /// <summary>总容量（单位数）。</summary>
    public int Capacity => Capacities[Level];

    /// <summary>下一级的容量（已满级时等于当前容量）。</summary>
    public int NextLevelCapacity => Capacities[System.Math.Min(Level + 1, MaxLevel)];

    /// <summary>库存：key 为商品 id，value 为数量。</summary>
    public Dictionary<string, int> Stock { get; } = new();

    /// <summary>
    /// 自动卖出节奏：每天结算产出后把仓库里本地货物卖给本城市场，或每 7 天卖一次，或关闭。
    /// 由玩家在产业页或总览窗口修改。
    /// </summary>
    public AutoSellMode AutoSell { get; set; } = AutoSellMode.None;

    /// <summary>已占用的容量。</summary>
    public int Used => Stock.Values.Sum();

    /// <summary>剩余可存放单位数。</summary>
    public int Free => Capacity - Used;

    public int QuantityOf(string goodId) => Stock.GetValueOrDefault(goodId);

    /// <summary>升一级，已满级时返回 false。</summary>
    public bool Upgrade()
    {
        if (IsMaxLevel)
        {
            return false;
        }

        Level++;
        return true;
    }

    /// <summary>存入货物（调用方负责校验容量）。</summary>
    public void Add(string goodId, int quantity)
    {
        Stock[goodId] = QuantityOf(goodId) + quantity;
    }

    /// <summary>取出货物；取空后移除该条目，避免界面出现 0 数量的行。</summary>
    public void Remove(string goodId, int quantity)
    {
        int remaining = QuantityOf(goodId) - quantity;
        if (remaining > 0)
        {
            Stock[goodId] = remaining;
        }
        else
        {
            Stock.Remove(goodId);
        }
    }
}
