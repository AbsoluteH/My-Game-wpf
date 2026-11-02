using System;
using System.Collections.Generic;
using TradeGame.Entities;
using TradeGame.Events;
using TradeGame.Strategies;

namespace TradeGame.Services;

/// <summary>
/// 旅行服务：扣路费、逐天推进时间、切所在地。
/// 每推进一天，所有城市库存向平衡值回归并重算价格，然后广播 <see cref="NewDayEvent"/>。
/// </summary>
public sealed class TravelService
{
    /// <summary>每行进一天的路费（金币）。</summary>
    public const int DailyTravelCost = 15;

    /// <summary>每日库存向平衡值回归的比例。</summary>
    private const double StockRevertRate = 0.1;

    /// <summary>库存上限，防止极端值。</summary>
    private const int MaxStock = 9999;

    private readonly Player _player;
    private readonly IReadOnlyList<City> _cities;
    private readonly IReadOnlyList<Good> _goods;
    private readonly IPricingStrategy _pricing;
    private readonly GameEventBus _gameEvents;

    public TravelService(Player player, IReadOnlyList<City> cities, IReadOnlyList<Good> goods,
                         IPricingStrategy pricing, GameEventBus gameEvents)
    {
        _player = player;
        _cities = cities;
        _goods = goods;
        _pricing = pricing;
        _gameEvents = gameEvents;
    }

    /// <summary>从当前城市前往目标城市的全部路费（天数 × 每日路费）。</summary>
    public int TravelCost(City destination) =>
        _player.CurrentCity.DaysTo(destination) * DailyTravelCost;

    /// <summary>
    /// 前往目标城市：先扣路费，再逐天推进时间，最后切换所在城市。
    /// 路费不足或已在本地时返回失败。
    /// </summary>
    public bool Travel(City destination, out string message)
    {
        if (destination == _player.CurrentCity)
        {
            message = "已经在此城市";
            return false;
        }

        int days = _player.CurrentCity.DaysTo(destination);
        int fee = TravelCost(destination);
        if (_player.Gold < fee)
        {
            message = $"路费不足，需要 {fee} 金币，现有 {_player.Gold}";
            return false;
        }

        _player.Spend(fee, GoldChangeReason.TravelFee);
        City from = _player.CurrentCity;

        // 旅行消耗的天数同样会让市场发生变化
        for (int i = 0; i < days; i++)
        {
            AdvanceDay();
        }

        _player.CurrentCity = destination;
        _pricing.UpdatePrices(destination, _goods); // 抵达后再算一次，消除旅途末端的时间差

        // 到站事件：行会刷新、界面切换等跨系统逻辑统一从这里接入
        _gameEvents.Publish(new ArrivedAtCityEvent(from, destination, days, _player.Day));

        message = $"行进 {days} 天抵达 {destination.Name}，路费 {fee} 金币";
        return true;
    }

    /// <summary>
    /// 推进一天：所有城市库存以 10% 的比例向平衡库存回归（至少移动 1 个单位），
    /// 然后重新计算所有城市的价格。
    /// </summary>
    private void AdvanceDay()
    {
        _player.AdvanceDay();

        foreach (var city in _cities)
        {
            foreach (var good in _goods)
            {
                int equilibrium = city.Equilibrium[good.Id];
                int stock = city.Stock[good.Id];

                // 按比例向平衡值靠拢，模拟本地生产与消费
                int delta = (int)Math.Round((equilibrium - stock) * StockRevertRate);
                if (stock != equilibrium && delta == 0)
                {
                    delta = Math.Sign(equilibrium - stock); // 差距很小时也至少走 1，避免永远卡住
                }

                city.Stock[good.Id] = Math.Max(0, Math.Min(MaxStock, stock + delta));
            }

            _pricing.UpdatePrices(city, _goods);
        }

        // 每日行情事件：到期的恢复原价，按排期生成新的波动
        _gameEvents.Publish(new NewDayEvent(_player.Day));
    }
}
