using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Entities;
using TradeGame.Events;
using TradeGame.Strategies;

namespace TradeGame.Services;

/// <summary>
/// 随机行情事件服务：不定期让某座城市的某种货物价格大幅上浮或下调。
/// 规则要点：
/// - 事件只对城市正在经营的货物生效（本地有市场的货才会被行情牵动）。
/// - 大跌有硬下限：跌完的买价不得低于“该货最便宜产地的当前买价 ×1.02”，
///   避免出现「产地买进、事件城市卖出反而亏钱」的倒挂。
/// - 同时最多存在 <see cref="MaxActive"/> 个事件，避免行情过多导致价格失控。
/// - 事件生成后不一定被玩家知道，需要在酒馆花金币打听（<see cref="Reveal"/>）。
/// </summary>
public sealed class MarketEventService
{
    /// <summary>在酒馆打听一次消息的花费（金币）。</summary>
    public const int GossipCost = 20;

    private const int SpawnMinDays = 4;
    private const int SpawnMaxDays = 10;
    private const int DurationMinDays = 8;
    private const int DurationMaxDays = 15;
    private const int MaxActive = 3;
    private const double MinDelta = 0.25;
    private const double MaxDelta = 0.50;

    /// <summary>大跌后的价格至少要是产地价的这个倍数，留一点运费空间。</summary>
    private const double SlumpFloorRate = 1.02;

    /// <summary>低于这个可跌幅度的城市不再发大跌事件（它本来就贴着产地价）。</summary>
    private const double MinMeaningfulDrop = 0.20;

    private readonly List<MarketEvent> _active = new();
    private readonly List<MarketEvent> _known = new();
    private readonly IReadOnlyList<City> _cities;
    private readonly IReadOnlyList<Good> _goods;
    private readonly IPricingStrategy _pricing;
    private readonly GameEventBus _gameEvents;
    private readonly Random _rng;

    private int _nextSpawnDay;
    private int _nextId = 1;

    public MarketEventService(IReadOnlyList<City> cities, IReadOnlyList<Good> goods,
                              IPricingStrategy pricing, GameEventBus gameEvents, Random? rng = null)
    {
        _cities = cities;
        _goods = goods;
        _pricing = pricing;
        _gameEvents = gameEvents;
        _rng = rng ?? new Random();
        _nextSpawnDay = 3 + _rng.Next(0, 4);
    }

    /// <summary>当前正在生效的事件。</summary>
    public IReadOnlyList<MarketEvent> ActiveEvents => _active;

    /// <summary>玩家已经打听到的消息（事件结束后仍保留，便于回看）。</summary>
    public IReadOnlyList<MarketEvent> KnownEvents => _known;

    /// <summary>还没被玩家打听到、且仍在生效的事件数量。</summary>
    public int UnrevealedCount => _active.Count(e => !e.Discovered);

    /// <summary>推进到指定日期：清除到期事件恢复原价，并按排期生成新事件。</summary>
    public void AdvanceTo(int day)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            MarketEvent ev = _active[i];
            if (day > ev.EndDay)
            {
                _active.RemoveAt(i);
                ev.City.EventMultipliers.Remove(ev.Good.Id);
                ev.City.PriceFloors.Remove(ev.Good.Id);
                _pricing.UpdatePrice(ev.City, ev.Good);

                // 价格恢复常态后再广播结束事件，订阅方看到的是恢复后的状态
                _gameEvents.Publish(new MarketShockEndedEvent(ev.Id, ev.City, ev.Good, ev.Kind,
                                                              ev.Delta, ev.StartDay, ev.EndDay, day));
            }
        }

        if (day >= _nextSpawnDay && _active.Count < MaxActive)
        {
            Spawn(day);
        }

        if (day >= _nextSpawnDay)
        {
            _nextSpawnDay = day + SpawnMinDays + _rng.Next(0, SpawnMaxDays - SpawnMinDays + 1);
        }
    }

    /// <summary>查某城某货是否有正在生效的事件。</summary>
    public MarketEvent? ActiveIn(City city, string goodId) =>
        _active.FirstOrDefault(e => e.City == city && e.Good.Id == goodId);

    /// <summary>
    /// 在酒馆打听消息：优先揭晓本城的事件，其次是最快结束的事件。
    /// 没有可打听的消息时返回 null（调用方不应扣钱）。
    /// </summary>
    public MarketEvent? Reveal(City askingCity)
    {
        MarketEvent? pick = _active
            .Where(e => !e.Discovered)
            .OrderByDescending(e => e.City == askingCity)
            .ThenBy(e => e.EndDay)
            .FirstOrDefault();

        if (pick == null)
        {
            return null;
        }

        pick.MarkDiscovered();
        _known.Add(pick);
        return pick;
    }

    /// <summary>随机挑一个“城市 + 该城经营的货物”制造行情事件。</summary>
    private bool Spawn(int day)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            City city = _cities[_rng.Next(_cities.Count)];
            string[] goodIds = city.Multipliers.Keys.ToArray();
            if (goodIds.Length == 0)
            {
                continue;
            }

            string goodId = goodIds[_rng.Next(goodIds.Length)];
            if (city.EventMultipliers.ContainsKey(goodId))
            {
                continue;
            }

            Good good = _goods.First(g => g.Id == goodId);
            double current = Math.Max(1, city.Prices[goodId]);
            bool surge = _rng.Next(0, 100) < 55;
            double delta = MinDelta + _rng.NextDouble() * (MaxDelta - MinDelta);
            int priceFloor = 0;

            if (!surge)
            {
                // 大跌幅度不能突破“产地价 ×1.02”这条线
                int floor = _cities.Where(c => c.SellsGood(goodId))
                                   .Min(c => (int)_pricing.TotalBuyCost(c, good, 1));
                double maxDrop = 1 - floor * SlumpFloorRate / current;
                if (maxDrop < MinMeaningfulDrop)
                {
                    surge = true; // 已经贴着产地价，改成涨价
                }
                else
                {
                    delta = Math.Min(delta, maxDrop);
                    priceFloor = (int)Math.Ceiling(floor * SlumpFloorRate);
                }
            }

            int duration = DurationMinDays + _rng.Next(0, DurationMaxDays - DurationMinDays + 1);
            var ev = new MarketEvent(_nextId++, city, good,
                                     surge ? MarketEventKind.Surge : MarketEventKind.Slump,
                                     delta, day, day + duration);

            city.EventMultipliers[goodId] = ev.Multiplier;
            if (priceFloor > 0)
            {
                // 事件期间任何一天的价格重算都必须守住这条下限
                city.PriceFloors[goodId] = priceFloor;
            }

            _pricing.UpdatePrice(city, good);
            _active.Add(ev);

            _gameEvents.Publish(new MarketShockStartedEvent(ev.Id, city, good, ev.Kind, ev.Delta,
                                                            ev.StartDay, ev.EndDay, day));
            return true;
        }

        return false;
    }
}
