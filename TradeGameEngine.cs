using System.Collections.Generic;
using System.Linq;
using TradeGame.Data;
using TradeGame.Entities;
using TradeGame.Services;
using TradeGame.Strategies;

namespace TradeGame;

/// <summary>
/// 游戏门面：组装实体、策略与服务，并向界面提供统一的只读状态和操作入口。
/// 界面层只依赖本类，不直接接触内部服务。
/// </summary>
public sealed class TradeGameEngine
{
    /// <summary>一次城市投资的花费（金币）。</summary>
    public const int InvestCost = 1000;

    /// <summary>一次城市投资换来的繁荣度。</summary>
    private const int InvestProsperity = 1000;

    /// <summary>一次商队扩容增加的载重单位。</summary>
    public const int ExpandCapacityUnit = 25;

    /// <summary>首次扩容的花费，之后每次递增 <see cref="ExpandCostStep"/>。</summary>
    private const int ExpandBaseCost = 1500;

    private const int ExpandCostStep = 800;

    public TradeGameEngine()
    {
        Goods = GameData.CreateGoods();
        Cities = GameData.CreateCities();
        GameData.InitializeMarkets(Cities, Goods);

        Player = new Player(Cities[0]);
        Pricing = new SupplyDemandPricingStrategy();

        // 先生成开局价格：行会任务生成时要按实际行情估价，所以必须在创建行会服务之前完成
        foreach (var city in Cities)
        {
            Pricing.UpdatePrices(city, Goods);
        }

        Market = new MarketService(Player, Pricing, Goods);
        Events = new MarketEventService(Cities, Goods, Pricing);
        Industry = new IndustryService(Player, Cities, Goods, Market);

        // 旅行时每推进一天都会驱动行情事件（到期恢复原价、按排期生成新波动）与产业产出
        TravelService = new TravelService(Player, Cities, Goods, Pricing, day =>
        {
            Events.AdvanceTo(day);
            Industry.AdvanceTo(day);
        });
        Guild = new GuildService(Player, Cities, Goods, Pricing);
    }

    /// <summary>玩家商队实体（金币、载重、货物、天数、所在城市）。</summary>
    public Player Player { get; }

    /// <summary>当前使用的定价策略，可替换为其他实现。</summary>
    public IPricingStrategy Pricing { get; }

    /// <summary>交易服务。</summary>
    public MarketService Market { get; }

    /// <summary>旅行服务。</summary>
    public TravelService TravelService { get; }

    /// <summary>行会服务：各城任务发布、接受与交付。</summary>
    public GuildService Guild { get; }

    /// <summary>随机行情事件服务：某城某货不定期暴涨/大跌，消息可在酒馆打听。</summary>
    public MarketEventService Events { get; }

    /// <summary>产业服务：各城作坊、作坊产出与本城仓库。</summary>
    public IndustryService Industry { get; }

    public IReadOnlyList<Good> Goods { get; }
    public IReadOnlyList<City> Cities { get; }

    // ---------- 以下为界面直接使用的便捷属性/方法，转发给内部实体与服务 ----------

    public City CurrentCity => Player.CurrentCity;
    public int Gold => Player.Gold;
    public int Capacity => Player.Capacity;
    public int Day => Player.Day;
    public IReadOnlyDictionary<string, int> Cargo => Player.Cargo;
    public int CargoUnits => Player.CargoUnits;
    public int FreeCapacity => Player.FreeCapacity;

    /// <summary>当前城市等级。</summary>
    public int CityLevel => CurrentCity.Level;

    /// <summary>当前城市累计繁荣度。</summary>
    public int CityProsperity => CurrentCity.Prosperity;

    /// <summary>当前城市升到下一级所需繁荣度（满级时为 0）。</summary>
    public int CityProsperityNeeded => CurrentCity.ProsperityNeeded;

    /// <summary>玩家在当前城市的累计声望。</summary>
    public int CityReputation => Player.Reputation(CurrentCity);

    /// <summary>玩家在当前城市的声望等级 0-3。</summary>
    public int CityReputationLevel => Player.ReputationLevel(CurrentCity);

    /// <summary>当前城市升到下一声望等级所需累计声望（满级时为 0）。</summary>
    public int CityReputationNeeded => Player.ReputationNeeded(CurrentCity);

    /// <summary>当前城市市场里可以买入的商品：本地经营、城市等级与玩家声望都达标。</summary>
    public IReadOnlyList<Good> TradeableGoods => Goods
        .Where(g => CurrentCity.CanTrade(g, CityReputationLevel))
        .ToList();

    /// <summary>当前城市经营但还没解锁的商品，用于提示还差什么条件。</summary>
    public IReadOnlyList<Good> LockedGoods => Goods
        .Where(g => CurrentCity.SellsGood(g.Id) && !CurrentCity.CanTrade(g, CityReputationLevel))
        .OrderBy(g => g.Tier)
        .ThenBy(g => g.ReputationTier)
        .ToList();

    /// <summary>某货物在当前城市还差什么才能买入，例如“城市 Lv2 + 声望 Lv3”。</summary>
    public string UnlockRequirement(Good good)
    {
        var parts = new List<string>();
        if (CityLevel < good.Tier)
        {
            parts.Add($"城市 Lv{good.Tier}");
        }

        if (CityReputationLevel < good.ReputationTier)
        {
            parts.Add($"声望 Lv{good.ReputationTier}");
        }

        return string.Join(" + ", parts);
    }

    public int BuyPrice(Good good) => Market.BuyPrice(good);
    public int SellPrice(Good good) => Market.SellPrice(good);
    public int MaxBuyable(Good good) => Market.MaxBuyable(good);
    public int MaxSellable(Good good) => Market.MaxSellable(good);

    /// <summary>某货物的持仓平均买入成本，用于背包展示。</summary>
    public int AverageCost(Good good) => Player.AverageCost(good.Id);

    public bool Buy(Good good, int quantity, out string message) => Market.Buy(good, quantity, out message);

    /// <summary>
    /// 卖出货物。卖出会累积城市繁荣度，可能让城市当场升级；
    /// 升级后立即换掉该城行会里还没被接走的旧任务，按新等级重新发布。
    /// </summary>
    public bool Sell(Good good, int quantity, out string message)
    {
        int oldLevel = CurrentCity.Level;
        bool ok = Market.Sell(good, quantity, out message);
        if (!ok)
        {
            return false;
        }

        if (CurrentCity.Level > oldLevel)
        {
            string guildNews = Guild.RefreshCity(CurrentCity, Player.Day);
            if (guildNews.Length > 0)
            {
                message += "；" + guildNews;
            }
        }

        return true;
    }

    public int TravelCost(City destination) => TravelService.TravelCost(destination);

    /// <summary>旅行：成功抵达后推进行会任务（作废过期任务、刷新到期委托）。</summary>
    public bool Travel(City destination, out string message)
    {
        bool ok = TravelService.Travel(destination, out message);
        if (!ok)
        {
            return false;
        }

        string guildNews = Guild.AdvanceTo(Player.Day);
        if (guildNews.Length > 0)
        {
            message += "；" + guildNews;
        }

        // 旅途中的作坊产出汇总到旅行提示里
        string industryNews = Industry.TakeProductionReport();
        if (industryNews.Length > 0)
        {
            message += "；" + industryNews;
        }

        return true;
    }

    /// <summary>当前城市行会发布的任务。</summary>
    public IReadOnlyList<GuildMission> CurrentGuildOffers => Guild.Offers(CurrentCity);

    /// <summary>玩家已接受的所有行会任务。</summary>
    public IReadOnlyList<GuildMission> ActiveMissions => Guild.ActiveMissions;

    public bool AcceptMission(GuildMission mission, out string message) =>
        Guild.Accept(mission, out message);

    public bool DeliverMission(GuildMission mission, out string message) =>
        Guild.Deliver(mission, out message);

    // ---------- 酒馆与行情事件 ----------

    /// <summary>在酒馆打听一次消息的花费（金币）。</summary>
    public const int TavernCost = MarketEventService.GossipCost;

    /// <summary>本城某货物当前的行情标记，例如“↑35%”“↓42%”；没有事件时返回空串。</summary>
    public string TrendFor(Good good)
    {
        MarketEvent? ev = Events.ActiveIn(CurrentCity, good.Id);
        return ev == null
            ? ""
            : $"{(ev.Kind == MarketEventKind.Surge ? "↑" : "↓")}{ev.Delta:P0}";
    }

    /// <summary>已经打听到的消息文本，事件结束后仍保留。</summary>
    public IReadOnlyList<string> KnownRumors =>
        Events.KnownEvents.Select(e => e.Describe(Day)).ToList();

    /// <summary>
    /// 花金币在酒馆打听一条行情消息：优先本城，其次是最快到期的事件。
    /// 没有可打听的消息时直接提示，不扣金币。
    /// </summary>
    public bool AskTavern(out string message)
    {
        if (Events.UnrevealedCount == 0)
        {
            message = "酒馆里暂时没有新消息，这轮不用花钱。";
            return false;
        }

        if (Gold < TavernCost)
        {
            message = $"打听消息需要 {TavernCost} 金币，现有 {Gold}";
            return false;
        }

        Player.Spend(TavernCost);
        MarketEvent? ev = Events.Reveal(CurrentCity);
        message = ev == null
            ? $"花了 {TavernCost} 金币，却什么都没打听到。"
            : $"酒馆消息：{ev.Describe(Day)}";
        return ev != null;
    }

    /// <summary>
    /// 花金币投资当前城市：直接增加繁荣度，加快升级与解锁商品。
    /// </summary>
    public bool InvestInCity(out string message)
    {
        City city = CurrentCity;

        if (city.Level >= City.MaxLevel)
        {
            message = $"{city.Name} 已达到最高等级 Lv{City.MaxLevel}";
            return false;
        }

        if (Gold < InvestCost)
        {
            message = $"金币不足，投资建设需要 {InvestCost}，现有 {Gold}";
            return false;
        }

        int oldLevel = city.Level;
        Player.Spend(InvestCost);
        city.AddProsperity(InvestProsperity);

        message = $"向 {city.Name} 投资 {InvestCost} 金币，繁荣 +{InvestProsperity}";

        if (city.Level > oldLevel)
        {
            string unlocks = string.Join("、", Goods
                .Where(g => city.SellsGood(g.Id)
                            && g.Tier > oldLevel
                            && g.Tier <= city.Level
                            && g.ReputationTier <= CityReputationLevel)
                .Select(g => g.Name));

            message += unlocks.Length > 0
                ? $"（升到 Lv{city.Level}，解锁 {unlocks}）"
                : $"（升到 Lv{city.Level}）";

            // 城市升级后行会立即换新：撤下旧任务，按新等级重新发布
            string guildNews = Guild.RefreshCity(city, Player.Day);
            if (guildNews.Length > 0)
            {
                message += "；" + guildNews;
            }
        }

        return true;
    }

    // ---------- 行会声望捐赠与商队扩容 ----------

    /// <summary>向本城行会捐赠一次换来的声望。</summary>
    public const int DonateReputation = GuildService.DonateReputation;

    /// <summary>当前城市下一次捐赠的花费（每次捐赠后递增 100）。</summary>
    public int DonateCost => Guild.DonateCost(CurrentCity);

    /// <summary>捐钱换声望：声望是解锁商品与运货任务的第二条线。</summary>
    public bool Donate(out string message) => Guild.Donate(out message);

    /// <summary>下一次扩容商队的花费（每次扩容后递增）。</summary>
    public int ExpandCost => ExpandBaseCost + ExpandCostStep * Player.ExpansionCount;

    /// <summary>商队是否已扩到载重上限。</summary>
    public bool CanExpandCapacity => Player.Capacity < Player.MaxCapacity;

    /// <summary>花金币扩容商队载重：一次 +25，可累计到 300。</summary>
    public bool ExpandCaravan(out string message)
    {
        if (!CanExpandCapacity)
        {
            message = $"商队载重已达上限 {Player.MaxCapacity}，无法继续扩容";
            return false;
        }

        int cost = ExpandCost;
        if (Gold < cost)
        {
            message = $"商队扩容需要 {cost} 金币，现有 {Gold}";
            return false;
        }

        int oldCapacity = Player.Capacity;
        Player.Spend(cost);
        Player.ExpandCapacity(ExpandCapacityUnit);

        message = $"商队扩容：花费 {cost} 金币，载重 {oldCapacity} → {Player.Capacity}";
        return true;
    }

    // ---------- 产业：作坊与仓库 ----------

    /// <summary>本城作坊位上限（城市规模 × 2，Lv1/2/3 → 2/4/6 座）。</summary>
    public int IndustrySlots => IndustryService.MaxWorkshops(CurrentCity);

    /// <summary>本城已建作坊数量。</summary>
    public int IndustryUsed => Industry.Workshops(CurrentCity).Count;

    /// <summary>本城已建的作坊。</summary>
    public IReadOnlyList<Workshop> CityWorkshops => Industry.Workshops(CurrentCity);

    /// <summary>本城可建作坊的货物：本城盛产、非舶来品，且城市规模达到分类门槛（原料 Lv1 / 加工 Lv2 / 成品 Lv3）。</summary>
    public IReadOnlyList<Good> BuildableWorkshopGoods => Goods
        .Where(g => g.CanBuildWorkshop
                    && g.IsProducedIn(CurrentCity.Name)
                    && CurrentCity.Level >= g.WorkshopCityLevel)
        .ToList();

    /// <summary>某种货物能否在本城建作坊的原因说明；空串表示可以建。</summary>
    public string WorkshopBlockReason(Good good) => Industry.BuildBlockReason(CurrentCity, good);

    /// <summary>在某城建一座作坊的花费（按货物等级）。</summary>
    public int WorkshopCost(Good good) => IndustryService.BuildCost(good);

    /// <summary>某货作坊的每日产量。</summary>
    public int WorkshopDailyOutput(Good good) => Workshop.DailyOutputFor(good);

    /// <summary>在本城建造指定货物的作坊；未建仓库、作坊位已满或金币不足时失败并说明原因。</summary>
    public bool BuildWorkshop(Good good, out string message) =>
        Industry.Build(CurrentCity, good, out message);

    /// <summary>设置某座作坊的原料供给方式与每日采购上限。</summary>
    public bool SetWorkshopSupply(Workshop workshop, SupplyMode mode, int dailyLimit, out string message) =>
        Industry.SetSupply(workshop, mode, dailyLimit, out message);

    /// <summary>本城仓库（可能尚未建造）。</summary>
    public Warehouse Warehouse => Industry.WarehouseOf(CurrentCity);

    /// <summary>本城是否已建仓库（建作坊的前置条件）。</summary>
    public bool HasWarehouse => Industry.HasWarehouse(CurrentCity);

    /// <summary>本城仓库升到下一级的费用；已满级时为 0。</summary>
    public int NextWarehouseCost => IndustryService.NextWarehouseCost(Warehouse);

    /// <summary>建造或升级本城仓库。</summary>
    public bool UpgradeWarehouse(out string message) =>
        Industry.UpgradeWarehouse(CurrentCity, out message);

    /// <summary>把舱内货物卸进本城仓库。</summary>
    public bool StoreToWarehouse(Good good, int quantity, out string message) =>
        Industry.Store(CurrentCity, good, quantity, out message);

    /// <summary>从本城仓库取货装进船舱。</summary>
    public bool TakeFromWarehouse(Good good, int quantity, out string message) =>
        Industry.Retrieve(CurrentCity, good, quantity, out message);

    /// <summary>全部卸货：舱内每种货物都按本城仓库剩余容量存入，放不下的留在舱内。</summary>
    public bool StoreAllToWarehouse(out string message) =>
        Industry.StoreAll(CurrentCity, out message);

    /// <summary>全部取货：本城仓库里的货物按剩余载重全部装进船舱，装不下的留在仓库。</summary>
    public bool TakeAllFromWarehouse(out string message) =>
        Industry.RetrieveAll(CurrentCity, out message);

    /// <summary>本城仓库的自动卖出节奏。</summary>
    public AutoSellMode AutoSell => Warehouse.AutoSell;

    /// <summary>设置本城仓库的自动卖出节奏。</summary>
    public bool SetAutoSell(AutoSellMode mode, out string message) =>
        Industry.SetAutoSell(CurrentCity, mode, out message);

    /// <summary>设置指定城市仓库的自动卖出节奏（总览窗口跨城修改时使用）。</summary>
    public bool SetAutoSell(City city, AutoSellMode mode, out string message) =>
        Industry.SetAutoSell(city, mode, out message);

    /// <summary>作弊台：直接把金币设为指定值（负数按 0 处理），返回实际生效的金额。</summary>
    public int SetGold(int amount)
    {
        Player.SetGold(amount);
        return Player.Gold;
    }
}
