using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Entities;
using TradeGame.Events;
using TradeGame.Strategies;

namespace TradeGame.Services;

/// <summary>
/// 国家服务：管理各国好感对应的国家任务（统一为“收购”）。
/// 每国按好感等级挂牌 1~3 条任务：
///   Lv1（好感 30）→ 低档；Lv2（100）→ 中档；Lv3（200）→ 高档。
/// 各国城市较少，任务统一为收购：货物送到该国首都交付，货源不限国家，玩家可从任意已解锁城市采购。
/// 国家任务只能在本国城市接取；
/// 同一国家同时只能进行 1 条，避免用同一批货反复刷好感。
/// </summary>
public sealed class NationService
{
    /// <summary>同一国家同时可进行的国家任务数。</summary>
    private const int MaxActivePerNation = 1;

    /// <summary>国家任务报酬相对采购成本的加成。</summary>
    private const double RewardMarginRate = 1.35;

    /// <summary>国家任务的收购数量：索引 = 档位。</summary>
    private static readonly int[] CargoQuantities = { 0, 150, 220, 300 };

    /// <summary>各档完成后获得的国家好感。</summary>
    private static readonly int[] RewardFavors = { 0, 15, 25, 40 };

    /// <summary>期限在国内赶路天数之外预留的缓冲：档位越高越紧急。</summary>
    private static readonly int[] DeadlineBuffers = { 0, 6, 5, 4 };

    /// <summary>国家任务的唯一类别：收购。</summary>
    private const string MissionTitle = "收购";

    private readonly Player _player;
    private readonly IReadOnlyList<Nation> _nations;
    private readonly IReadOnlyList<City> _cities;
    private readonly IReadOnlyList<Good> _goods;
    private readonly IPricingStrategy _pricing;
    private readonly GameEventBus _gameEvents;
    private readonly Random _rng;

    /// <summary>各国当前挂牌的国家任务：key 为国家名。</summary>
    private readonly Dictionary<string, List<GuildMission>> _offers = new();

    /// <summary>玩家已接受、尚未交付的国家任务。</summary>
    private readonly List<GuildMission> _active = new();

    private int _nextId = 1;

    public NationService(Player player, IReadOnlyList<Nation> nations, IReadOnlyList<City> cities,
                         IReadOnlyList<Good> goods, IPricingStrategy pricing,
                         GameEventBus gameEvents, int seed = 20261102)
    {
        _player = player;
        _nations = nations;
        _cities = cities;
        _goods = goods;
        _pricing = pricing;
        _gameEvents = gameEvents;
        _rng = new Random(seed);

        foreach (var nation in _nations)
        {
            _offers[nation.Name] = new List<GuildMission>();
            Replenish(nation, _player.Day);
        }
    }

    /// <summary>某国当前挂牌的国家任务（该国所有城市共享同一份）。</summary>
    public IReadOnlyList<GuildMission> Offers(Nation nation) => _offers[nation.Name];

    /// <summary>玩家已接受的所有国家任务（跨国家）。</summary>
    public IReadOnlyList<GuildMission> ActiveMissions => _active;

    /// <summary>
    /// 好感升级后立即刷新该国未接任务：按新解锁的档位补发任务。
    /// 返回需要提示玩家的文本；没有新增任务时返回空串。
    /// </summary>
    public string Refresh(Nation nation, int day)
    {
        _offers[nation.Name].Clear();
        int added = Replenish(nation, day);
        return added > 0
            ? $"{nation.Name} 开放了 {added} 条新的国家任务（好感 Lv{_player.NationFavorLevel(nation)}）"
            : "";
    }

    /// <summary>接受一条本国挂牌的国家任务。</summary>
    public bool Accept(GuildMission mission, out string message)
    {
        Nation? nation = mission.Nation;
        if (nation == null)
        {
            message = "该任务不是国家任务";
            return false;
        }

        if (_player.CurrentCity.Nation != nation)
        {
            message = $"该任务由 {nation.Name} 发布，需在{nation.Name}的城市接取";
            return false;
        }

        List<GuildMission> offers = _offers[nation.Name];
        if (!offers.Contains(mission))
        {
            message = "该国家任务已失效，请稍后再看";
            return false;
        }

        if (_active.Any(m => m.Nation == nation))
        {
            message = $"{nation.Name} 已有国家任务在进行，交付后再接";
            return false;
        }

        if (_player.NationFavorLevel(nation) < mission.Tier)
        {
            message = $"接取该档国家任务需要 {nation.Name} 好感 Lv{mission.Tier}";
            return false;
        }

        offers.Remove(mission);
        _active.Add(mission);

        // 接走后立即用同档的新货物补位，避免每国只剩经常空缺的位置
        ReplenishOne(nation, mission.Tier, _player.Day);

        message = $"接受 {nation.Name} 国家任务（{mission.Title}）：{mission.Quantity} 件 {mission.Good.Name}，"
                + $"送到 {mission.TargetCity.Name}，截止第 {mission.DeadlineDay} 天，"
                + $"报酬 {mission.RewardGold} 金币、好感 +{mission.RewardFavor}";
        return true;
    }

    /// <summary>交付国家任务：必须身处交付城市、货物足够且未过期。</summary>
    public bool Deliver(GuildMission mission, out string message)
    {
        Nation? nation = mission.Nation;
        if (nation == null)
        {
            message = "该任务不是国家任务";
            return false;
        }

        if (_player.CurrentCity != mission.TargetCity)
        {
            message = $"需前往 {mission.TargetCity.Name} 才能交付该国家任务";
            return false;
        }

        if (!_active.Contains(mission))
        {
            message = "该国家任务不在进行中";
            return false;
        }

        if (_player.Day > mission.DeadlineDay)
        {
            _active.Remove(mission);
            message = $"国家任务已过期（截止第 {mission.DeadlineDay} 天），委托取消";
            return false;
        }

        int owned = _player.Cargo.GetValueOrDefault(mission.Good.Id);
        if (owned < mission.Quantity)
        {
            message = $"{mission.Good.Name} 不足，需要 {mission.Quantity}，现有 {owned}";
            return false;
        }

        _player.RemoveCargo(mission.Good.Id, mission.Quantity);
        _player.Earn(mission.RewardGold, GoldChangeReason.NationReward);
        bool leveled = _player.AddNationFavor(nation, mission.RewardFavor);
        _active.Remove(mission);

        message = $"完成 {nation.Name} 国家任务（{mission.Title}）：交付 {mission.Good.Name} ×{mission.Quantity}，"
                + $"{mission.RewardGold} 金币，好感 +{mission.RewardFavor}";

        if (leveled)
        {
            message += $"（好感升到 Lv{_player.NationFavorLevel(nation)}）";
        }

        _gameEvents.Publish(new GuildMissionCompletedEvent(mission, mission.RewardGold, 0, _player.Day));
        return true;
    }

    /// <summary>
    /// 时间推进到指定天数：作废过期任务、立即补发过期挂牌的位置，返回需要提示玩家的文本。
    /// </summary>
    public string AdvanceTo(int day)
    {
        var expired = _active.Where(m => day > m.DeadlineDay).ToList();
        foreach (var mission in expired)
        {
            _active.Remove(mission);
        }

        foreach (var nation in _nations)
        {
            if (_offers[nation.Name].RemoveAll(m => day > m.DeadlineDay) > 0)
            {
                Replenish(nation, day);
            }
        }

        return expired.Count == 0
            ? ""
            : "国家任务过期：" + string.Join("、",
                expired.Select(m => $"{m.Nation?.Name ?? m.Origin.Name} {m.Good.Name} ×{m.Quantity}"));
    }

    /// <summary>按当前好感等级补满缺失档位的任务，返回新增条数。</summary>
    private int Replenish(Nation nation, int day)
    {
        int added = 0;
        for (int tier = 1; tier <= Nation.MaxLevel; tier++)
        {
            if (_player.NationFavorLevel(nation) < tier)
            {
                continue;
            }

            if (ReplenishOne(nation, tier, day))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>给某国的某一档补一条任务；该档已有挂牌或找不到合适货物时不补。</summary>
    private bool ReplenishOne(Nation nation, int tier, int day)
    {
        List<GuildMission> offers = _offers[nation.Name];
        if (offers.Any(m => m.Tier == tier))
        {
            return false;
        }

        GuildMission? mission = CreateMission(nation, tier, day);
        if (mission == null)
        {
            return false;
        }

        offers.Add(mission);
        return true;
    }

    /// <summary>
    /// 生成一条国家任务：按档位选数量与好感奖励，货物必须在首都以外有玩家能买到的货源。
    /// 期限 = 往返最便宜货源的天数 + 档位缓冲（高档更紧急）。
    /// </summary>
    private GuildMission? CreateMission(Nation nation, int tier, int day)
    {
        City delivery = CapitalOf(nation);
        var offers = _offers[nation.Name];

        var candidates = _goods
            .Where(g => CheapestSource(delivery, g) != null
                        && !offers.Any(m => m.Good.Id == g.Id))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        Good good = candidates[_rng.Next(candidates.Count)];
        City source = CheapestSource(delivery, good)!;
        int quantity = CargoQuantities[tier];
        int roundTripDays = delivery.DaysTo(source) * 2;
        int buyCost = _pricing.TotalBuyCost(source, good, quantity);
        int rewardGold = (int)Math.Round(buyCost * RewardMarginRate)
                       + roundTripDays * TravelService.DailyTravelCost;
        int deadlineDay = day + roundTripDays + DeadlineBuffers[tier];

        return new GuildMission(_nextId++, GuildMissionType.Nation, delivery, delivery, good,
                                quantity, rewardGold, 0, deadlineDay,
                                nation, RewardFavors[tier], MissionTitle, tier);
    }

    /// <summary>国家任务的交付地：该国第一座城市（数据顺序中的首个城市视为首都）。</summary>
    private City CapitalOf(Nation nation) => _cities.First(c => c.Nation == nation);

    /// <summary>玩家能买到该货、且价格最低的城市（不限国家，排除交付城本身）。没有可用货源时返回 null。</summary>
    private City? CheapestSource(City delivery, Good good)
    {
        var candidates = _cities
            .Where(c => c != delivery && c.CanTrade(good, _player.ReputationLevel(c)))
            .OrderBy(c => c.Prices[good.Id])
            .ThenBy(c => delivery.DaysTo(c))
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }
}
