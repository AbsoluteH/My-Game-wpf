using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Entities;
using TradeGame.Strategies;

namespace TradeGame.Services;

/// <summary>
/// 行会服务：每座城市都有本地行会，同时发布最多 3 条任务。
/// 任务分两类：
///   - 收购任务：货物种类不受限制，只要本城不盛产（倍率 &lt;1 或不经营）且别处买得到即可，玩家从别处采购后运来；
///   - 运货任务：必须是本城生产的货物，且本城等级与本城声望都已解锁，玩家买入后运到指定城市交付。
///     新货物解锁后会自动进入运货任务池。
/// 玩家接受后该位置不会立即补货，要在 3-5 天后才出现新任务；同一城市同一种货物还有 8 天冷却，
/// 因此不能连续接同一种货的任务，任务货物也会随时间变化。
/// 城市升级时会立即换新一轮任务（<see cref="RefreshCity"/>），任务的数量、报酬与可收货物等级都跟着城市等级走。
/// 声望除了靠交付任务，也可以向本城行会捐赠金币获得（<see cref="Donate"/>）。
/// 任务有期限，过期即作废。声望是本城解锁商品的第二条线（另一条是城市等级）。
/// </summary>
public sealed class GuildService
{
    /// <summary>每座城市同时发布的任务数。</summary>
    private const int OffersPerCity = 3;

    /// <summary>任务被接走或交付后，该位置补货所需的最少/最多天数。</summary>
    private const int RefillMinDays = 3;
    private const int RefillMaxDays = 5;

    /// <summary>同一城市同一种货物的接单冷却（天）。</summary>
    private const int SameGoodCooldownDays = 8;

    /// <summary>玩家同时可接的任务上限。</summary>
    private const int MaxActiveMissions = 5;

    /// <summary>向本城行会捐赠一次换来的声望。</summary>
    public const int DonateReputation = 30;

    /// <summary>首次捐赠的花费，之后每次递增 <see cref="DonateCostStep"/>。</summary>
    public const int DonateBaseCost = 500;

    private const int DonateCostStep = 100;

    private readonly Player _player;
    private readonly IReadOnlyList<City> _cities;
    private readonly IReadOnlyList<Good> _goods;
    private readonly IPricingStrategy _pricing;
    private readonly Random _rng;

    /// <summary>各城当前可接受的任务：key 为城市名。</summary>
    private readonly Dictionary<string, List<GuildMission>> _offers = new();

    /// <summary>各城各货物的冷却截止日：key 为城市名，再按货物 id。</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _cooldown = new();

    /// <summary>玩家在各城累计捐赠的次数，用于计算下一次捐赠的价格。</summary>
    private readonly Dictionary<string, int> _donateCount = new();

    /// <summary>待补货的位置：到达 DueDay 才生成新任务。</summary>
    private readonly List<RefillSlot> _pendingRefills = new();

    /// <summary>玩家已接受、尚未交付的任务。</summary>
    private readonly List<GuildMission> _active = new();

    private sealed class RefillSlot
    {
        public City City { get; init; } = null!;
        public int DueDay { get; init; }
    }

    /// <param name="seed">随机种子，保证同一局的任务生成可复现。</param>
    public GuildService(Player player, IReadOnlyList<City> cities, IReadOnlyList<Good> goods,
                        IPricingStrategy pricing, int seed = 20261029)
    {
        _player = player;
        _cities = cities;
        _goods = goods;
        _pricing = pricing;
        _rng = new Random(seed);

        foreach (var city in _cities)
        {
            _offers[city.Name] = new List<GuildMission>();
            _cooldown[city.Name] = new Dictionary<string, int>();
            Replenish(city);
        }
    }

    /// <summary>某城当前发布的任务。</summary>
    public IReadOnlyList<GuildMission> Offers(City city) => _offers[city.Name];

    /// <summary>玩家已接受的所有任务（跨城市）。</summary>
    public IReadOnlyList<GuildMission> ActiveMissions => _active;

    /// <summary>向某城行会捐赠一次的花费：首次 500，之后每次 +100，避免用钱一口气买满声望。</summary>
    public int DonateCost(City city) => DonateBaseCost + DonateCostStep * _donateCount.GetValueOrDefault(city.Name);

    /// <summary>
    /// 城市升级后立即刷新：撤下该城还没被接走的旧任务（含待补货位置），
    /// 立刻按新的城市等级重新发布一轮。玩家已接受的任务不受影响。
    /// </summary>
    public string RefreshCity(City city, int day)
    {
        List<GuildMission> offers = _offers[city.Name];
        bool hadPending = _pendingRefills.Any(r => r.City == city);
        if (offers.Count == 0 && !hadPending)
        {
            return "";
        }

        offers.Clear();
        _pendingRefills.RemoveAll(r => r.City == city);

        for (int i = 0; i < OffersPerCity && ReplenishOne(city, day); i++)
        {
            // 逐步补满，补不满说明当前可发的货物都在冷却
        }

        return $"{city.Name} 行会已按 Lv{city.Level} 重新发布 {offers.Count} 条任务";
    }

    /// <summary>向本城行会捐赠金币换取声望（花费随捐赠次数递增）。</summary>
    public bool Donate(out string message)
    {
        City city = _player.CurrentCity;
        int oldLevel = _player.ReputationLevel(city);

        if (oldLevel >= Player.MaxReputationLevel)
        {
            message = $"{city.Name} 声望已达最高 Lv{Player.MaxReputationLevel}，不必再捐赠";
            return false;
        }

        int cost = DonateCost(city);
        if (_player.Gold < cost)
        {
            message = $"捐赠需要 {cost} 金币，现有 {_player.Gold}";
            return false;
        }

        _player.Spend(cost);
        _donateCount[city.Name] = _donateCount.GetValueOrDefault(city.Name) + 1;
        _player.AddReputation(city, DonateReputation);

        message = $"向 {city.Name} 行会捐赠 {cost} 金币，声望 +{DonateReputation}";
        string unlocks = DescribeReputationUnlocks(city, oldLevel);
        if (unlocks.Length > 0)
        {
            message += $"（{unlocks}）";
        }

        return true;
    }

    /// <summary>接受一条当前城市发布的任务。</summary>
    public bool Accept(GuildMission mission, out string message)
    {
        City city = _player.CurrentCity;

        if (mission.Origin != city)
        {
            message = $"该任务由 {mission.Origin.Name} 行会发布，需到当地接受";
            return false;
        }

        List<GuildMission> offers = _offers[city.Name];
        if (!offers.Contains(mission))
        {
            message = "该任务已失效，请稍后再看";
            return false;
        }

        if (_active.Any(m => m.Origin == city && m.Good.Id == mission.Good.Id))
        {
            message = $"{city.Name} 已有同种货物（{mission.Good.Name}）的任务在进行，交付后再接";
            return false;
        }

        int cooldownUntil = CooldownUntil(city, mission.Good);
        if (_player.Day < cooldownUntil)
        {
            message = $"{mission.Good.Name} 的任务冷却中，还需 {cooldownUntil - _player.Day} 天";
            return false;
        }

        if (_active.Count >= MaxActiveMissions)
        {
            message = $"最多同时接受 {MaxActiveMissions} 个任务，请先交付已有任务";
            return false;
        }

        offers.Remove(mission);
        _active.Add(mission);
        _cooldown[city.Name][mission.Good.Id] = _player.Day + SameGoodCooldownDays;

        // 不立即补货：该位置要过几天才刷新，避免无限刷任务
        _pendingRefills.Add(new RefillSlot
        {
            City = city,
            DueDay = _player.Day + RefillMinDays + _rng.Next(0, RefillMaxDays - RefillMinDays + 1),
        });

        message = $"接受 {mission.RouteText}：{mission.Quantity} 件 {mission.Good.Name}，"
                + $"截止第 {mission.DeadlineDay} 天，报酬 {mission.RewardGold} 金币、声望 +{mission.RewardReputation}";
        return true;
    }

    /// <summary>交付任务：必须身处交付城市、货物足够且未过期。</summary>
    public bool Deliver(GuildMission mission, out string message)
    {
        City origin = mission.Origin;

        if (_player.CurrentCity != mission.TargetCity)
        {
            message = $"需前往 {mission.TargetCity.Name} 才能交付该任务";
            return false;
        }

        if (!_active.Contains(mission))
        {
            message = "该任务不在进行中";
            return false;
        }

        if (_player.Day > mission.DeadlineDay)
        {
            _active.Remove(mission);
            message = $"任务已过期（截止第 {mission.DeadlineDay} 天），委托取消";
            return false;
        }

        int owned = _player.Cargo.GetValueOrDefault(mission.Good.Id);
        if (owned < mission.Quantity)
        {
            message = $"{mission.Good.Name} 不足，需要 {mission.Quantity}，现有 {owned}";
            return false;
        }

        int oldLevel = _player.ReputationLevel(origin);
        _player.RemoveCargo(mission.Good.Id, mission.Quantity);
        _player.Earn(mission.RewardGold);
        bool leveled = _player.AddReputation(origin, mission.RewardReputation);
        _active.Remove(mission);

        message = $"交付 {mission.Good.Name} ×{mission.Quantity}，获得 {mission.RewardGold} 金币，"
                + $"{origin.Name} 声望 +{mission.RewardReputation}";

        if (leveled)
        {
            message += $"（{DescribeReputationUnlocks(origin, oldLevel)}）";
        }

        return true;
    }

    /// <summary>声望升级后新解锁的本城商品提示，例如“声望升到 Lv3，解锁 丝绸、珍珠”；没有新解锁时只报等级。</summary>
    private string DescribeReputationUnlocks(City city, int oldLevel)
    {
        int newLevel = _player.ReputationLevel(city);
        if (newLevel <= oldLevel)
        {
            return "";
        }

        string unlocks = string.Join("、", _goods
            .Where(g => city.SellsGood(g.Id)
                        && g.Tier <= city.Level
                        && g.ReputationTier > oldLevel
                        && g.ReputationTier <= newLevel)
            .Select(g => g.Name));

        return unlocks.Length > 0
            ? $"声望升到 Lv{newLevel}，解锁 {unlocks}"
            : $"声望升到 Lv{newLevel}";
    }

    /// <summary>
    /// 时间推进到指定天数：作废过期任务、处理到期的补货位置，返回需要提示玩家的文本（可为空）。
    /// </summary>
    public string AdvanceTo(int day)
    {
        var expired = _active.Where(m => day > m.DeadlineDay).ToList();
        foreach (var mission in expired)
        {
            _active.Remove(mission);
        }

        // 过期的挂牌任务也从行会撤下，过一天再补新的（保证货物会随时间变化）
        foreach (var city in _cities)
        {
            int removed = _offers[city.Name].RemoveAll(m => day > m.DeadlineDay);
            if (removed > 0)
            {
                _pendingRefills.Add(new RefillSlot { City = city, DueDay = day + 1 });
            }
        }

        var ready = _pendingRefills.Where(r => day >= r.DueDay).ToList();
        foreach (var slot in ready)
        {
            _pendingRefills.Remove(slot);
            if (!ReplenishOne(slot.City, day))
            {
                // 当前没有可发的任务（货物都在冷却），过一天再试
                _pendingRefills.Add(new RefillSlot { City = slot.City, DueDay = day + 1 });
            }
        }

        return expired.Count == 0
            ? ""
            : "任务过期：" + string.Join("、", expired.Select(m => $"{m.Origin.Name} {m.Good.Name} ×{m.Quantity}"));
    }

    private int CooldownUntil(City city, Good good) => _cooldown[city.Name].GetValueOrDefault(good.Id);

    /// <summary>
    /// 生成并放入一条任务；找不到合适货物时不补，返回 false。
    /// 收购与运货两类任务混在一起随机抽取。
    /// </summary>
    private bool ReplenishOne(City city, int day)
    {
        List<GuildMission> offers = _offers[city.Name];
        if (offers.Count >= OffersPerCity)
        {
            return true;
        }

        GuildMission? mission = CreateMission(city, day);
        if (mission == null)
        {
            return false;
        }

        offers.Add(mission);
        return true;
    }

    /// <summary>开局时把某城的任务一次性补满。</summary>
    private void Replenish(City city)
    {
        while (ReplenishOne(city, _player.Day))
        {
            if (_offers[city.Name].Count >= OffersPerCity)
            {
                break;
            }
        }
    }

    /// <summary>某货物是否还能在本城发任务：没在冷却，且当前任务里没有同种货。</summary>
    private bool IsAvailable(City city, Good good, int day) =>
        day >= CooldownUntil(city, good)
        && !_offers[city.Name].Any(m => m.Good.Id == good.Id);

    /// <summary>
    /// 在收购与运货两类候选里随机生成一条任务。
    /// 收购：本城不盛产（倍率 &lt;1 或不经营）的货物，别处买得到即可，但**货物等级不超过本城当前等级**，
    ///       所以城市升级后才会出现更高等级的收购委托（正好配合任务金额随等级上涨）；
    /// 运货：必须是本城生产的货物，且本城已解锁（等级 + 声望），新货解锁后自动进入任务池。
    /// 数量按“目标货值”反推（随城市等级放大），保证开局接得起、升级后金额更大。
    /// </summary>
    private GuildMission? CreateMission(City city, int day)
    {
        var importGoods = _goods
            .Where(g => g.Tier <= city.Level
                        && city.MultiplierFor(g.Id) < 1.0
                        && _cities.Any(c => c != city && c.CanTrade(g, _player.ReputationLevel(c)))
                        && IsAvailable(city, g, day))
            .ToList();

        // 运货任务必须在本地买得到，且货物是本地生产的：城市等级与玩家在本城的声望都要满足该货的解锁条件
        var transportGoods = _goods
            .Where(g => g.IsProducedIn(city.Name)
                        && city.CanTrade(g, _player.ReputationLevel(city))
                        && IsAvailable(city, g, day))
            .ToList();

        var candidates = new List<GuildMission>();

        foreach (var good in importGoods)
        {
            GuildMission? mission = BuildImport(city, good, day);
            if (mission != null)
            {
                candidates.Add(mission);
            }
        }

        foreach (var good in transportGoods)
        {
            // 目的地是“本城盛产、别处稀缺”的城市：把货运到缺货的地方才卖得上价。
            // 若该货在所有城市都不稀缺，则退回到任意其他城市，保证任务仍可生成。
            var destinations = _cities.Where(c => c != city && c.MultiplierFor(good.Id) < 1.0).ToList();
            if (destinations.Count == 0)
            {
                destinations = _cities.Where(c => c != city).ToList();
            }

            candidates.Add(BuildTransport(city, good, destinations[_rng.Next(destinations.Count)], day));
        }

        return candidates.Count == 0 ? null : WeightedPick(candidates);
    }

    /// <summary>
    /// 按货物等级的立方加权抽取：等级越高越容易被抽中。
    /// 这样城市升级后，挂牌任务会明显偏向新解锁的高等级货物
    /// （Lv1/Lv2/Lv3 权重为 1/8/27），而不是永远只发早期的大宗货物。
    /// </summary>
    private GuildMission WeightedPick(List<GuildMission> candidates)
    {
        double total = candidates.Sum(m => Math.Pow(m.Good.Tier, 3));
        double roll = _rng.NextDouble() * total;

        foreach (var mission in candidates)
        {
            roll -= Math.Pow(mission.Good.Tier, 3);
            if (roll <= 0)
            {
                return mission;
            }
        }

        return candidates[^1];
    }

    /// <summary>
    /// 收购任务报酬按“在别处实际采购的成本 ×1.2”算（采购成本已含大额买入的滑点），再补贴来回程路费。
    /// 防套利：只有当**本城自己就是该货最便宜的来源**时才把报酬压到本城采购成本的 90% 以下，
    /// 否则玩家可以就地买入就地交付白拿钱。注意不能只看“本城是否经营该货”——
    /// 本城若只是稀缺（倍率 &lt; 1），就地买反而更贵，不存在套利，报酬不该被压。
    /// 压价后低于成本加路费的任务不发。
    /// </summary>
    private GuildMission? BuildImport(City city, Good good, int day)
    {
        City source = CheapestSource(city, good);
        int roundTripDays = city.DaysTo(source) * 2;

        int quantity = QuantityFor(good, city.Level);
        int buyCost = _pricing.TotalBuyCost(source, good, quantity);
        int rewardGold = (int)Math.Round(buyCost * 1.2)
                       + roundTripDays * TravelService.DailyTravelCost;

        if (city.SellsGood(good.Id) && city.MultiplierFor(good.Id) >= 1.0)
        {
            // 本城盛产，是最便宜的来源：压价，避免就地买入白拿报酬
            rewardGold = Math.Min(rewardGold,
                (int)Math.Round(_pricing.TotalBuyCost(city, good, quantity) * 0.9));
        }

        int cost = buyCost + roundTripDays * TravelService.DailyTravelCost;
        if (rewardGold <= cost)
        {
            return null;
        }

        // 期限覆盖来回最便宜产地的时间，再留 9~14 天余量（放宽后玩家可以先跑别的生意再接）
        int deadlineDay = day + roundTripDays + 9 + _rng.Next(0, 6);

        return new GuildMission(_nextId++, GuildMissionType.Import, city, city, good,
                                quantity, rewardGold, ReputationFor(rewardGold), deadlineDay);
    }

    /// <summary>运货任务报酬 = 本地实际采购成本 ×1.2 + 单程路费，保证跑一趟确实有赚。</summary>
    private GuildMission BuildTransport(City city, Good good, City destination, int day)
    {
        int quantity = QuantityFor(good, city.Level);
        int days = city.DaysTo(destination);

        int buyCost = _pricing.TotalBuyCost(city, good, quantity);
        int rewardGold = (int)Math.Round(buyCost * 1.2) + days * TravelService.DailyTravelCost;

        int deadlineDay = day + days + 9 + _rng.Next(0, 6);

        return new GuildMission(_nextId++, GuildMissionType.Transport, city, destination, good,
                                quantity, rewardGold, ReputationFor(rewardGold), deadlineDay);
    }

    /// <summary>某城对某货的当前实际买入价（含产地折扣与当前供需，不含大额买入的滑点）。</summary>
    private static double BuyPriceIn(City city, Good good) => city.Prices[good.Id];

    /// <summary>玩家在别的城市能买到该货时，采购价最低的那座城市（只算玩家已解锁的城市）。</summary>
    private City CheapestSource(City city, Good good) =>
        _cities.Where(c => c != city && c.CanTrade(good, _player.ReputationLevel(c)))
               .OrderBy(c => BuyPriceIn(c, good))
               .ThenBy(c => city.DaysTo(c))
               .First();

    /// <summary>
    /// 按“目标货值”反推数量：目标货值随城市等级上涨（每升一级约 +70%），
    /// 所以城市升级后出现的货物更贵、任务的总金额也更大。
    /// 数量限制在 3~70 件：下限保证任务有意义，上限保证一次能装进商队载重（100）。
    /// </summary>
    private int QuantityFor(Good good, int cityLevel)
    {
        double scale = 1 + 0.7 * Math.Max(0, cityLevel - 1);
        int targetValue = (int)((350 + _rng.Next(0, 400)) * scale);
        return Math.Clamp(targetValue / Math.Max(1, good.BasePrice), 3, 70);
    }

    /// <summary>任务声望奖励与任务总金额挂钩：金额越高声望越多，限制在 6~30。</summary>
    private int ReputationFor(int rewardGold) =>
        (int)Math.Clamp(4 + rewardGold / 55.0, 6, 30);

    private int _nextId = 1;
}
