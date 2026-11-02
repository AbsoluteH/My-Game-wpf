using System;
using System.Collections.Generic;
using TradeGame.Entities;
using TradeGame.Events;

namespace TradeGame.Services;

/// <summary>
/// 家园服务：玩家在各城购置与升级房产，并把舱内货物存进房产 / 取回家。
/// 规则要点：
/// - 每城一处房产，Lv1/2/3 造价 6000/11000/18000，比同等级作坊更贵（3500/7000/12000）。
/// - 房产等级不得超过城市规模：购买 Lv2 房产要求城市 Lv2，Lv3 同理。
/// - 容量 Lv1/2/3 = 200/350/500 单位，与仓库容量各自独立；房产库存不参与仓库自动卖出、也不进市场。
/// - 购置/升级与仓库、作坊一样给本城一次性回报（繁荣 = 造价 ×10%，声望 = 造价 ÷500），复用
///   <see cref="IndustryService.RewardCity"/>，因此城市升级、行会刷新的联动与建作坊完全一致。
/// - 当前版本只收一次性购置费，没有每日维护费；后续可在此基础上扩展维护费、婚姻、家族与奢侈品。
/// </summary>
public sealed class HomeService
{
    /// <summary>房产从当前等级升到下一级的费用：购置 Lv1 6000、升级到 Lv2 11000、Lv3 18000。</summary>
    private static readonly int[] EstateCosts = { 6000, 11000, 18000 };

    private readonly Player _player;
    private readonly IReadOnlyList<Good> _goods;

    /// <summary>建造回报与城市升级提示复用产业服务，避免两套口径。</summary>
    private readonly IndustryService _industry;

    /// <summary>各城房产：key 为城市名，未购置时等级为 0。</summary>
    private readonly Dictionary<string, Estate> _estates = new();

    public HomeService(Player player, IReadOnlyList<City> cities, IReadOnlyList<Good> goods, IndustryService industry)
    {
        _player = player;
        _goods = goods;
        _industry = industry;

        foreach (var city in cities)
        {
            _estates[city.Name] = new Estate();
        }
    }

    /// <summary>某城的房产（可能还没购置，用 <see cref="Estate.IsOwned"/> 判断）。</summary>
    public Estate EstateOf(City city) => _estates[city.Name];

    /// <summary>某城房产升到下一级的费用；已满级时返回 0。</summary>
    public static int NextCost(Estate estate) =>
        estate.IsMaxLevel ? 0 : EstateCosts[estate.Level];

    /// <summary>在当前城市购置或升级房产；不满足条件时说明原因并返回 false。</summary>
    public bool Buy(City city, out string message)
    {
        Estate estate = _estates[city.Name];

        if (estate.IsMaxLevel)
        {
            message = $"{city.Name} 的房产已达最高等级 Lv{Estate.MaxLevel}（容量 {estate.Capacity}）";
            return false;
        }

        int nextLevel = estate.Level + 1;
        if (city.Level < nextLevel)
        {
            message = $"城市规模不足：购置 Lv{nextLevel} 房产需要 {city.Name} 达到 Lv{nextLevel}（当前 Lv{city.Level}）";
            return false;
        }

        int cost = NextCost(estate);
        if (_player.Gold < cost)
        {
            message = $"购置/升级房产需要 {cost} 金币，现有 {_player.Gold}";
            return false;
        }

        int oldCapacity = estate.Capacity;
        _player.Spend(cost, GoldChangeReason.PropertyInvestment);
        estate.Upgrade();
        string reward = _industry.RewardCity(city, cost);

        message = estate.Level == 1
            ? $"在 {city.Name} 购置房产 Lv1，花费 {cost} 金币，可存放 {estate.Capacity} 单位货物"
            : $"{city.Name} 房产升到 Lv{estate.Level}，花费 {cost} 金币，容量 {oldCapacity} → {estate.Capacity} 单位";
        message += reward;
        return true;
    }

    /// <summary>把舱内货物存进当前城市的房产。</summary>
    public bool Store(City city, Good good, int quantity, out string message)
    {
        Estate estate = _estates[city.Name];

        if (!estate.IsOwned)
        {
            message = $"{city.Name} 还没有房产，无法存放货物";
            return false;
        }

        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        int owned = _player.Cargo.GetValueOrDefault(good.Id);
        if (quantity > owned)
        {
            message = $"货物不足，{good.Name} 仅有 {owned}";
            return false;
        }

        if (quantity > estate.Free)
        {
            message = $"{city.Name} 房产剩余容量不足（还可存放 {estate.Free} 单位）";
            return false;
        }

        _player.RemoveCargo(good.Id, quantity);
        estate.Add(good.Id, quantity);

        message = $"{good.Name} ×{quantity} 已存入 {city.Name} 房产"
                + $"（{estate.Used} / {estate.Capacity}）";
        return true;
    }

    /// <summary>从当前城市的房产取货装进船舱。</summary>
    public bool Retrieve(City city, Good good, int quantity, out string message)
    {
        Estate estate = _estates[city.Name];

        if (!estate.IsOwned)
        {
            message = $"{city.Name} 还没有房产";
            return false;
        }

        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        int stored = estate.QuantityOf(good.Id);
        if (quantity > stored)
        {
            message = $"房产里的 {good.Name} 不足，仅有 {stored}";
            return false;
        }

        if (quantity > _player.FreeCapacity)
        {
            message = $"载重不足，还可装载 {_player.FreeCapacity} 单位";
            return false;
        }

        estate.Remove(good.Id, quantity);

        // 与仓库取货一致：房产里的货没有买入成本，按舱内现有持仓均价入账，避免把平均成本摊薄成假象
        _player.AddCargo(good.Id, quantity, _player.AverageCost(good.Id));

        message = $"从 {city.Name} 房产取出 {good.Name} ×{quantity}"
                + $"（房产 {estate.Used} / {estate.Capacity}，载重 {_player.CargoUnits} / {_player.Capacity}）";
        return true;
    }
}
