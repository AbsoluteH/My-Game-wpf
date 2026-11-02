using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Events;

namespace TradeGame.Entities;

/// <summary>
/// 玩家商队实体：所在城市、金币、载重、舱内货物、游戏天数，以及在各城的声望。
/// 只保存状态和最小操作，不包含交易/旅行等业务规则。
/// </summary>
public sealed class Player
{
    /// <summary>初始金币。</summary>
    public const int StartingGold = 1000;

    /// <summary>商队总载重（单位数），所有货物合计不能超过它。</summary>
    public const int DefaultCapacity = 100;

    /// <summary>扩容后的载重上限。</summary>
    public const int MaxCapacity = 300;

    /// <summary>声望最高等级。</summary>
    public const int MaxReputationLevel = 3;

    /// <summary>各等级所需的累计声望：索引为当前等级，所以 Lv1 需 60、Lv2 需 200、Lv3 需 500。</summary>
    private static readonly int[] ReputationThresholds = { 0, 60, 200, 500 };

    public Player(City startingCity, GameEventBus gameEvents)
    {
        CurrentCity = startingCity;
        _gameEvents = gameEvents;
    }

    /// <summary>所在城市，旅行成功后由旅行服务修改。</summary>
    public City CurrentCity { get; set; }

    public int Gold { get; private set; } = StartingGold;

    /// <summary>商队载重上限，初始 100，可花钱扩容到 <see cref="MaxCapacity"/>。</summary>
    public int Capacity { get; private set; } = DefaultCapacity;

    /// <summary>已扩容的次数，用于计算下一次扩容的价格。</summary>
    public int ExpansionCount { get; private set; }

    /// <summary>游戏内天数，初始第 1 天。</summary>
    public int Day { get; private set; } = 1;

    /// <summary>舱内货物：key 为商品 id，value 为数量。</summary>
    public Dictionary<string, int> Cargo { get; } = new();

    /// <summary>各货物的持仓总成本：key 为商品 id，value 为这批货的买入总花费，用于计算平均成本。</summary>
    private readonly Dictionary<string, int> _cargoCost = new();

    /// <summary>已装载的货物总单位数。</summary>
    public int CargoUnits => Cargo.Values.Sum();

    /// <summary>剩余可装载单位数。</summary>
    public int FreeCapacity => Capacity - CargoUnits;

    private readonly GameEventBus _gameEvents;

    public void Spend(int amount, GoldChangeReason reason)
    {
        if (amount > 0)
        {
            ChangeGold(-amount, reason);
        }
    }

    public void Earn(int amount, GoldChangeReason reason)
    {
        if (amount > 0)
        {
            ChangeGold(amount, reason);
        }
    }

    /// <summary>直接把金币设为指定值（仅作弊台使用），负数按 0 处理。</summary>
    public void SetGold(int amount, GoldChangeReason reason)
    {
        int before = Gold;
        Gold = Math.Max(0, amount);
        if (Gold != before)
        {
            _gameEvents.Publish(new GoldChangedEvent(Gold - before, before, Gold, reason, Day));
        }
    }

    /// <summary>金币的唯一写入口：改完立刻广播，保证所有经济系统都能收到同一份通知。</summary>
    private void ChangeGold(int delta, GoldChangeReason reason)
    {
        int before = Gold;
        Gold += delta;
        _gameEvents.Publish(new GoldChangedEvent(delta, before, Gold, reason, Day));
    }

    /// <summary>
    /// 扩容商队载重，不超过 <see cref="MaxCapacity"/>。
    /// 已满上限时返回 false，调用方不应扣钱。
    /// </summary>
    public bool ExpandCapacity(int units)
    {
        if (Capacity >= MaxCapacity)
        {
            return false;
        }

        Capacity = Math.Min(MaxCapacity, Capacity + units);
        ExpansionCount++;
        return true;
    }

    /// <summary>把货物放进舱内，同种货物累加数量，并按买入单价累加持仓总成本。</summary>
    public void AddCargo(string goodId, int quantity, int unitCost)
    {
        Cargo[goodId] = Cargo.GetValueOrDefault(goodId) + quantity;
        _cargoCost[goodId] = _cargoCost.GetValueOrDefault(goodId) + unitCost * quantity;
    }

    /// <summary>
    /// 从舱内取出货物；装空后移除该条目，避免界面出现 0 数量的行。
    /// 总成本按卖出数量占比同步扣减，剩余货物的平均成本保持不变。
    /// </summary>
    public void RemoveCargo(string goodId, int quantity)
    {
        int owned = Cargo.GetValueOrDefault(goodId);
        int remaining = owned - quantity;

        int totalCost = _cargoCost.GetValueOrDefault(goodId);
        int removedCost = owned > 0 ? (int)Math.Round(totalCost * (quantity / (double)owned)) : 0;

        if (remaining > 0)
        {
            Cargo[goodId] = remaining;
            _cargoCost[goodId] = totalCost - removedCost;
        }
        else
        {
            Cargo.Remove(goodId);
            _cargoCost.Remove(goodId);
        }
    }

    /// <summary>某货物的平均买入成本（四舍五入到整数金币），没有持仓时返回 0。</summary>
    public int AverageCost(string goodId)
    {
        int quantity = Cargo.GetValueOrDefault(goodId);
        return quantity > 0
            ? (int)Math.Round(_cargoCost.GetValueOrDefault(goodId) / (double)quantity)
            : 0;
    }

    public void AdvanceDay() => Day++;

    // ---------- 城市声望：按城市名分别记录，来源是本城行会任务 ----------

    /// <summary>各城累计声望：key 为城市名。</summary>
    private readonly Dictionary<string, int> _reputation = new();

    /// <summary>玩家在某城的累计声望。</summary>
    public int Reputation(City city) => _reputation.GetValueOrDefault(city.Name);

    /// <summary>玩家在某城的声望等级 0-3。</summary>
    public int ReputationLevel(City city)
    {
        int points = Reputation(city);
        int level = 0;
        while (level < MaxReputationLevel && points >= ReputationThresholds[level + 1])
        {
            level++;
        }

        return level;
    }

    /// <summary>升到下一级所需累计声望；已满级返回 0。</summary>
    public int ReputationNeeded(City city) =>
        ReputationLevel(city) < MaxReputationLevel
            ? ReputationThresholds[ReputationLevel(city) + 1]
            : 0;

    /// <summary>增加某城声望，返回本次是否升级（可连升多级）。</summary>
    public bool AddReputation(City city, int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        int oldLevel = ReputationLevel(city);
        _reputation[city.Name] = Reputation(city) + amount;
        int newLevel = ReputationLevel(city);

        if (newLevel > oldLevel)
        {
            _gameEvents.Publish(new ReputationLevelUpEvent(city, oldLevel, newLevel, Reputation(city), Day));
        }

        return newLevel > oldLevel;
    }

    // ---------- 国家好感：按国家分别记录，来源是在该国城市贸易与完成国家任务 ----------

    /// <summary>各国累计好感：key 为国家名。</summary>
    private readonly Dictionary<string, int> _nationFavor = new();

    /// <summary>玩家在某国的累计好感。</summary>
    public int NationFavor(Nation nation) => _nationFavor.GetValueOrDefault(nation.Name);

    /// <summary>玩家在某国的好感等级 0-3；0 级表示尚未与该国建立关系。</summary>
    public int NationFavorLevel(Nation nation) => nation.LevelFor(NationFavor(nation));

    /// <summary>升到下一级所需累计好感；已满级返回 0。</summary>
    public int NationFavorNeeded(Nation nation) =>
        NationFavorLevel(nation) < Nation.MaxLevel
            ? nation.FavorThresholdFor(NationFavorLevel(nation) + 1)
            : 0;

    /// <summary>增加某国好感，返回本次是否升级（可连升多级）。</summary>
    public bool AddNationFavor(Nation nation, int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        int oldLevel = NationFavorLevel(nation);
        _nationFavor[nation.Name] = NationFavor(nation) + amount;
        int newLevel = NationFavorLevel(nation);

        if (newLevel > oldLevel)
        {
            _gameEvents.Publish(new NationFavorLevelUpEvent(nation, oldLevel, newLevel, NationFavor(nation), Day));
        }

        return newLevel > oldLevel;
    }
}
