using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeGame.Entities;

/// <summary>
/// 城市房产（家园）：玩家在某城购置的宅邸，等级 1-3，容量 200 / 350 / 500 单位。
/// 房产与仓库**独立存放**：房产里的货物不进市场、不参与仓库自动卖出，只作为玩家的私人储物与长期金币去处。
/// 后续的婚姻、家族与奢侈品（文玩、名画等）都会挂在房产上，因此房产是这些系统的容器。
/// </summary>
public sealed class Estate
{
    /// <summary>房产最高等级。</summary>
    public const int MaxLevel = 3;

    /// <summary>各等级的储物容量，索引即等级（0 表示还没购置）。</summary>
    private static readonly int[] Capacities = { 0, 200, 350, 500 };

    /// <summary>房产等级 0-3，0 表示尚未购置。</summary>
    public int Level { get; private set; }

    /// <summary>是否已购置。</summary>
    public bool IsOwned => Level > 0;

    public bool IsMaxLevel => Level >= MaxLevel;

    /// <summary>总储物容量（单位数）。</summary>
    public int Capacity => Capacities[Level];

    /// <summary>下一级的容量（已满级时等于当前容量）。</summary>
    public int NextLevelCapacity => Capacities[Math.Min(Level + 1, MaxLevel)];

    /// <summary>房产内的货物：key 为商品 id，value 为数量。</summary>
    public Dictionary<string, int> Stock { get; } = new();

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
