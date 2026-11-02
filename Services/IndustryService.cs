using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Entities;
using TradeGame.Events;

namespace TradeGame.Services;

/// <summary>
/// 产业服务：玩家在各城建造作坊与仓库。
/// 规则要点：
/// - 作坊只能生产**本城盛产的货物**（货物产地表里包含该城），且货物等级不得超过城市规模（城市等级），
///   所以城市升级后才会出现更高等级的作坊。
/// - 每城作坊数量上限 = 城市等级 × 2（Lv1/2/3 → 2/4/6 座），同一种货物可以重复建多座。
/// - 建作坊前必须先建本城仓库：产出必须有地方存放。
/// - 仓库每城一座，Lv1/2/3 容量 200/400/700，建造与升级分别花费 1500/3000/6000。
/// - 仓库既存放作坊产出，也存放玩家从商队卸下的货物，所有货物共用容量；仓库满时作坊暂停生产。
/// - 生产只在「天」推进时结算（目前只有旅行会推进天数），与行情事件、行会刷新同一节奏。
/// - 建造/升级建筑会一次性给城市增加繁荣与玩家声望；仓库自动卖出也会给城市增加繁荣与声望，
///   其贡献为玩家手动卖出的 80%。
/// </summary>
public sealed class IndustryService
{
    /// <summary>每级城市规模提供的作坊位数量。</summary>
    public const int SlotsPerLevel = 2;

    /// <summary>建造/升级建筑时，造价中有多少比例转化为城市繁荣度。</summary>
    private const double BuildProsperityRate = 0.1;

    /// <summary>建造/升级建筑时，每多少金币造价转化为 1 点本城声望。</summary>
    private const int BuildCostPerReputation = 500;

    /// <summary>各等级作坊的造价：一级 3500、二级 7000、三级 12000。</summary>
    private static readonly int[] BuildCostsByTier = { 0, 3500, 7000, 12000 };

    /// <summary>仓库从当前等级升到下一级的费用：建 Lv1 1500、Lv2 3000、Lv3 6000。</summary>
    private static readonly int[] WarehouseCosts = { 1500, 3000, 6000 };

    private readonly Player _player;
    private readonly IReadOnlyList<City> _cities;

    /// <summary>货物表，用于按固定顺序遍历库存并取货物名字。</summary>
    private readonly IReadOnlyList<Good> _goods;

    /// <summary>货物 id → 货物，供配方输入查名字与基础价。</summary>
    private readonly Dictionary<string, Good> _goodById;

    /// <summary>交易服务，自动卖出时按本城实时卖价成交并推低当地价格；市场采购原料也走它。</summary>
    private readonly MarketService _market;

    /// <summary>游戏事件总线：建筑让城市升级时广播 <see cref="CityLevelUpEvent"/>，触发行会刷新等联动。</summary>
    private readonly GameEventBus _gameEvents;

    /// <summary>各城已建作坊：key 为城市名。</summary>
    private readonly Dictionary<string, List<Workshop>> _workshops = new();

    /// <summary>各城仓库：key 为城市名，未建造时等级为 0。</summary>
    private readonly Dictionary<string, Warehouse> _warehouses = new();

    /// <summary>本次待汇报的产出量：key 为「城市 货物」。</summary>
    private readonly Dictionary<string, int> _produced = new();

    /// <summary>最近一次产出是否因仓库已满而中断。</summary>
    private bool _overflow;

    /// <summary>本次待汇报的缺料少产件数。</summary>
    private int _starved;

    /// <summary>本次待汇报的自动卖出记录。</summary>
    private readonly List<string> _autoSellLog = new();

    public IndustryService(Player player, IReadOnlyList<City> cities, IReadOnlyList<Good> goods,
                           MarketService market, GameEventBus gameEvents)
    {
        _player = player;
        _cities = cities;
        _goods = goods;
        _market = market;
        _gameEvents = gameEvents;
        _goodById = goods.ToDictionary(g => g.Id);

        foreach (var city in cities)
        {
            _workshops[city.Name] = new List<Workshop>();
            _warehouses[city.Name] = new Warehouse();
        }
    }

    /// <summary>某城作坊数量上限：城市规模（等级）× 2。</summary>
    public static int MaxWorkshops(City city) => city.Level * SlotsPerLevel;

    /// <summary>某城已建的作坊。</summary>
    public IReadOnlyList<Workshop> Workshops(City city) => _workshops[city.Name];

    /// <summary>某城的仓库（可能还没建造，用 <see cref="Warehouse.IsBuilt"/> 判断）。</summary>
    public Warehouse WarehouseOf(City city) => _warehouses[city.Name];

    /// <summary>某城是否已建仓库。</summary>
    public bool HasWarehouse(City city) => _warehouses[city.Name].IsBuilt;

    /// <summary>建一座作坊的花费（按货物等级）。</summary>
    public static int BuildCost(Good good) => BuildCostsByTier[Math.Clamp(good.Tier, 1, 3)];

    /// <summary>某城仓库升到下一级的费用；已满级时返回 0。</summary>
    public static int NextWarehouseCost(Warehouse warehouse) =>
        warehouse.IsMaxLevel ? 0 : WarehouseCosts[warehouse.Level];

    /// <summary>某种货物能否在本城建作坊的原因说明；空串表示可以建。</summary>
    public string BuildBlockReason(City city, Good good)
    {
        if (!good.CanBuildWorkshop)
        {
            return $"{good.Name} 是舶来品，只能从口岸城市进口转售，不能建造作坊";
        }

        if (!good.IsProducedIn(city.Name))
        {
            return $"{city.Name} 不产出 {good.Name}，只能生产本城盛产的货物";
        }

        if (city.Level < good.WorkshopCityLevel)
        {
            string category = GoodCategories.Name(good.Category);
            int floor = GoodCategories.WorkshopFloor(good.Category);
            return city.Level < floor
                ? $"{category}作坊需要城市 Lv{floor}（当前 Lv{city.Level}）"
                : $"作坊规模不足：生产 {good.Name} 需城市 Lv{good.WorkshopCityLevel}（当前 Lv{city.Level}）";
        }

        if (!HasWarehouse(city))
        {
            return $"{city.Name} 还没有仓库，请先建造仓库";
        }

        if (_workshops[city.Name].Count >= MaxWorkshops(city))
        {
            return $"{city.Name} 作坊位已满（{MaxWorkshops(city)} 座，城市升级后可扩建）";
        }

        int cost = BuildCost(good);
        if (_player.Gold < cost)
        {
            return $"金币不足：建造 {good.Name}作坊需要 {cost}，现有 {_player.Gold}";
        }

        return "";
    }

    /// <summary>在当前城市建一座指定货物的作坊。</summary>
    public bool Build(City city, Good good, out string message)
    {
        string reason = BuildBlockReason(city, good);
        if (reason.Length > 0)
        {
            message = reason;
            return false;
        }

        int cost = BuildCost(good);
        _player.Spend(cost, GoldChangeReason.IndustryInvestment);
        _workshops[city.Name].Add(new Workshop(good, _player.Day));
        string reward = RewardCity(city, cost);

        message = $"在 {city.Name} 建成 {good.Name}作坊，花费 {cost} 金币，"
                + $"日产 {Workshop.DailyOutputFor(good)} 件（作坊 {_workshops[city.Name].Count}/{MaxWorkshops(city)}）"
                + reward;
        return true;
    }

    /// <summary>建造或升级当前城市的仓库。</summary>
    public bool UpgradeWarehouse(City city, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (warehouse.IsMaxLevel)
        {
            message = $"{city.Name} 仓库已达最高等级 Lv{Warehouse.MaxLevel}（容量 {warehouse.Capacity}）";
            return false;
        }

        int cost = NextWarehouseCost(warehouse);
        if (_player.Gold < cost)
        {
            message = $"仓库{(warehouse.IsBuilt ? "升级" : "建造")}需要 {cost} 金币，现有 {_player.Gold}";
            return false;
        }

        int oldCapacity = warehouse.Capacity;
        _player.Spend(cost, GoldChangeReason.IndustryInvestment);
        warehouse.Upgrade();
        string reward = RewardCity(city, cost);

        message = warehouse.Level == 1
            ? $"在 {city.Name} 建成仓库，花费 {cost} 金币，容量 {warehouse.Capacity} 单位"
            : $"{city.Name} 仓库升到 Lv{warehouse.Level}，花费 {cost} 金币，容量 {oldCapacity} → {warehouse.Capacity} 单位";
        message += reward;
        return true;
    }

    /// <summary>
    /// 建造/升级建筑后给城市的一次性回报：繁荣 = 造价 ×10%，声望 = 造价 ÷500。
    /// 如果因此让城市升级，广播 <see cref="CityLevelUpEvent"/> 并把新解锁的商品拼进提示。
    /// 房产（<see cref="HomeService"/>）的购置与升级也复用这一份口径，避免两套规则。
    /// </summary>
    public string RewardCity(City city, int cost)
    {
        int prosperity = Math.Max(1, (int)Math.Round(cost * BuildProsperityRate));
        int reputation = Math.Max(1, cost / BuildCostPerReputation);
        int oldLevel = city.Level;

        city.AddProsperity(prosperity);
        bool reputationLeveled = _player.AddReputation(city, reputation);

        string text = $"，{city.Name} 繁荣 +{prosperity}、声望 +{reputation}";

        if (reputationLeveled)
        {
            text += $"（声望升到 Lv{_player.ReputationLevel(city)}）";
        }

        if (city.Level > oldLevel)
        {
            string unlocks = string.Join("、", _goods
                .Where(g => city.SellsGood(g.Id)
                            && g.Tier > oldLevel
                            && g.Tier <= city.Level
                            && g.ReputationTier <= _player.ReputationLevel(city))
                .Select(g => g.Name));

            text += unlocks.Length > 0
                ? $"（城市升到 Lv{city.Level}，解锁 {unlocks}）"
                : $"（城市升到 Lv{city.Level}）";

            _gameEvents.Publish(new CityLevelUpEvent(city, oldLevel, city.Level, _player.Day));
        }

        return text;
    }

    /// <summary>把舱内货物卸进当前城市的仓库。</summary>
    public bool Store(City city, Good good, int quantity, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (!warehouse.IsBuilt)
        {
            message = $"{city.Name} 还没有仓库，无法卸货";
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

        if (quantity > warehouse.Free)
        {
            message = $"{city.Name} 仓库剩余容量不足（还可存放 {warehouse.Free} 单位）";
            return false;
        }

        _player.RemoveCargo(good.Id, quantity);
        warehouse.Add(good.Id, quantity);

        message = $"{good.Name} ×{quantity} 已存入 {city.Name} 仓库"
                + $"（{warehouse.Used} / {warehouse.Capacity}）";
        return true;
    }

    /// <summary>从当前城市的仓库取货装进船舱。</summary>
    public bool Retrieve(City city, Good good, int quantity, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (!warehouse.IsBuilt)
        {
            message = $"{city.Name} 还没有仓库";
            return false;
        }

        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        int stored = warehouse.QuantityOf(good.Id);
        if (quantity > stored)
        {
            message = $"仓库里的 {good.Name} 不足，仅有 {stored}";
            return false;
        }

        if (quantity > _player.FreeCapacity)
        {
            message = $"载重不足，还可装载 {_player.FreeCapacity} 单位";
            return false;
        }

        warehouse.Remove(good.Id, quantity);

        // 作坊产出与卸下来的货没有买入成本，按舱内现有持仓的均价入账，避免把平均成本摊薄成假象
        _player.AddCargo(good.Id, quantity, _player.AverageCost(good.Id));

        message = $"从 {city.Name} 仓库取出 {good.Name} ×{quantity}"
                + $"（仓库 {warehouse.Used} / {warehouse.Capacity}，载重 {_player.CargoUnits} / {_player.Capacity}）";
        return true;
    }

    /// <summary>
    /// 全部卸货：把舱内每种货物按仓库剩余容量尽可能存入本城仓库。
    /// 容量不足时只卸能放下的部分，剩余货物留在舱内并在提示里说明。
    /// </summary>
    public bool StoreAll(City city, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (!warehouse.IsBuilt)
        {
            message = $"{city.Name} 还没有仓库，无法卸货";
            return false;
        }

        int stored = 0;      // 成功卸下的总单位数
        int kinds = 0;       // 涉及的货物种类数
        int leftInCargo = 0; // 因仓库放不下而留在舱内的单位数

        foreach (var good in _goods)
        {
            int owned = _player.Cargo.GetValueOrDefault(good.Id);
            if (owned <= 0)
            {
                continue;
            }

            int moved = Math.Min(owned, warehouse.Free);
            if (moved > 0)
            {
                _player.RemoveCargo(good.Id, moved);
                warehouse.Add(good.Id, moved);
                stored += moved;
                kinds++;
            }

            leftInCargo += owned - moved;
        }

        if (stored == 0)
        {
            message = warehouse.Free <= 0
                ? $"{city.Name} 仓库已满（{warehouse.Used} / {warehouse.Capacity}），舱内货物一件也放不下"
                : "舱内没有可卸下的货物";
            return false;
        }

        message = $"全部卸货：{kinds} 种货物共 {stored} 单位存入 {city.Name} 仓库"
                + $"（{warehouse.Used} / {warehouse.Capacity}）";

        if (leftInCargo > 0)
        {
            message += $"；舱内还剩 {leftInCargo} 单位，仓库放不下";
        }

        return true;
    }

    /// <summary>
    /// 全部取货：把本城仓库里的每种货物按剩余载重尽可能装进船舱。
    /// 载重不够时只装能装下的部分，剩余货物留在仓库并在提示里说明。
    /// </summary>
    public bool RetrieveAll(City city, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (!warehouse.IsBuilt)
        {
            message = $"{city.Name} 还没有仓库，没有货可取";
            return false;
        }

        int loaded = 0;         // 成功装舱的总单位数
        int kinds = 0;          // 涉及的货物种类数
        int leftInWarehouse = 0; // 因载重不足而留在仓库的单位数

        foreach (var good in _goods)
        {
            int stored = warehouse.QuantityOf(good.Id);
            if (stored <= 0)
            {
                continue;
            }

            int moved = Math.Min(stored, _player.FreeCapacity);
            if (moved > 0)
            {
                warehouse.Remove(good.Id, moved);

                // 仓库货物没有买入成本，按舱内现有持仓均价入账，避免平均成本被摊薄
                _player.AddCargo(good.Id, moved, _player.AverageCost(good.Id));
                loaded += moved;
                kinds++;
            }

            leftInWarehouse += stored - moved;
        }

        if (loaded == 0)
        {
            message = _player.FreeCapacity <= 0
                ? $"载重已满（{_player.CargoUnits} / {_player.Capacity}），仓库货物一件也装不下"
                : "仓库里没有可取出的货物";
            return false;
        }

        message = $"全部取货：{kinds} 种货物共 {loaded} 单位装进船舱"
                + $"（载重 {_player.CargoUnits} / {_player.Capacity}）";

        if (leftInWarehouse > 0)
        {
            message += $"；仓库还剩 {leftInWarehouse} 单位，载重不足";
        }

        return true;
    }

    /// <summary>
    /// 设置某城仓库的自动卖出节奏。没建仓库时不能设置（没有地方存放产出，也就没有可卖的东西）。
    /// </summary>
    public bool SetAutoSell(City city, AutoSellMode mode, out string message)
    {
        Warehouse warehouse = _warehouses[city.Name];

        if (!warehouse.IsBuilt)
        {
            message = $"{city.Name} 还没有仓库，无法设置自动卖出";
            return false;
        }

        warehouse.AutoSell = mode;

        message = mode switch
        {
            AutoSellMode.Daily => $"{city.Name} 仓库：每天结算产出后自动卖出本地货物",
            AutoSellMode.Every7Days => $"{city.Name} 仓库：每 7 天（第 7/14/21… 天）结算产出后自动卖出本地货物",
            _ => $"{city.Name} 仓库：已关闭自动卖出",
        };
        return true;
    }

    /// <summary>
    /// 当天的自动卖出：在产出结算之后执行，把仓库里**本城作坊正在生产的货物**全部卖给本城市场。
    /// 走的是真实卖出流程，会推低当地卖价并给城市增加繁荣度。
    /// </summary>
    private void AutoSellIn(City city, int day)
    {
        Warehouse warehouse = _warehouses[city.Name];
        var mode = warehouse.AutoSell;

        if (!warehouse.IsBuilt || mode == AutoSellMode.None)
        {
            return;
        }

        if (mode == AutoSellMode.Every7Days && day % 7 != 0)
        {
            return;
        }

        int goldBefore = _player.Gold;
        var sold = new List<string>();

        // 只卖本地作坊产出的那几种货（同种多座作坊合并处理），避免把玩家特意存放的其他货物卖掉
        foreach (var good in _workshops[city.Name].Select(w => w.Good).GroupBy(g => g.Id).Select(g => g.First()))
        {
            int quantity = warehouse.QuantityOf(good.Id);
            if (quantity <= 0)
            {
                continue;
            }

            if (_market.SellToCity(city, good, quantity, SaleSource.WarehouseAutoSell, out _))
            {
                warehouse.Remove(good.Id, quantity);
                sold.Add($"{good.Name} ×{quantity}");
            }
        }

        if (sold.Count == 0)
        {
            return;
        }

        string cycle = mode == AutoSellMode.Daily ? "每天" : "每 7 天";
        _autoSellLog.Add($"自动卖出（{cycle}）{city.Name} {string.Join("、", sold)}，收入 {_player.Gold - goldBefore} 金币");
    }

    /// <summary>
    /// 设置某座作坊的原料供给方式与每日采购上限。
    /// 原料作坊没有配方输入，不需要设置。
    /// </summary>
    public bool SetSupply(Workshop workshop, SupplyMode mode, int dailyLimit, out string message)
    {
        if (workshop.Good.Inputs.Count == 0)
        {
            message = $"{workshop.Good.Name} 是{GoodCategories.Name(workshop.Good.Category)}，不需要原料供给设置";
            return false;
        }

        if (dailyLimit < 0)
        {
            message = "每日采购上限不能为负数";
            return false;
        }

        workshop.Supply = mode;
        workshop.DailyPurchaseLimit = dailyLimit;

        message = mode == SupplyMode.SelfSupply
            ? $"{workshop.Good.Name}作坊：每天从本城仓库取原料，缺料停产"
            : $"{workshop.Good.Name}作坊：每天按本城买价采购原料，上限 {dailyLimit} 金币/天";
        return true;
    }

    /// <summary>
    /// 推进一天：各城作坊先取得原料再生产，产于仓库的原料当天就能供给加工工坊，
    /// 随后按仓库设置自动卖出，仓库空位不足的部分暂停生产。
    /// </summary>
    public void AdvanceTo(int day)
    {
        foreach (var city in _cities)
        {
            List<Workshop> workshops = _workshops[city.Name];
            if (workshops.Count == 0)
            {
                continue;
            }

            // 原料 → 加工品 → 成品：当天新产出的原料可以立刻被下一段吃掉，链条在同一城内自然衔接
            foreach (var workshop in workshops
                         .OrderBy(w => (int)w.Good.Category == (int)GoodCategory.Raw ? 0
                                     : w.Good.Category == GoodCategory.Processed ? 1 : 2))
            {
                Produce(city, workshop);
            }

            // 产出结算完之后再卖，卖价反映的是当天最新的城市库存与行情
            AutoSellIn(city, day);
        }
    }

    /// <summary>结算一座作坊的一天：先取得配方输入，再产出到本城仓库。</summary>
    private void Produce(City city, Workshop workshop)
    {
        Warehouse warehouse = _warehouses[city.Name];
        int wanted = workshop.DailyOutput;
        int room = warehouse.Free;
        int target = Math.Min(wanted, room);

        // 仓库放不下：记一次满仓，但当天仍尽量产出能放下的部分
        if (room < wanted)
        {
            _overflow = true;
        }

        if (target <= 0)
        {
            return;
        }

        // 输入不足时按可满足的件数减产：件数 = min(各输入可用量 / 单件消耗)
        int possible = target;
        foreach (var input in workshop.Good.Inputs)
        {
            int available = workshop.Supply == SupplyMode.SelfSupply
                ? warehouse.QuantityOf(input.GoodId)
                : city.Stock[input.GoodId];
            possible = Math.Min(possible, available / input.Quantity);
        }

        // 市场采购还要看每日预算：逐步下调产量直到买得起
        possible = workshop.Supply == SupplyMode.MarketPurchase
            ? Math.Min(possible, AffordableOutput(city, workshop, possible))
            : possible;

        if (possible <= 0)
        {
            _starved += target;
            return;
        }

        // 先校验全部原料再统一扣/买：同种原料在配方里重复出现时按总量判断，
        // 避免扣掉第一种原料后第二种不足，已花的钱和已消耗的货都白费
        var required = new Dictionary<string, int>();
        foreach (var input in workshop.Good.Inputs)
        {
            required[input.GoodId] = required.GetValueOrDefault(input.GoodId) + input.Quantity * possible;
        }

        foreach (var (goodId, needed) in required)
        {
            int available = workshop.Supply == SupplyMode.SelfSupply
                ? warehouse.QuantityOf(goodId)
                : city.Stock[goodId];

            if (available < needed)
            {
                _starved += target;
                return;
            }
        }

        // 市场采购还要确认总价付得起，避免买了一半才发现金币不够
        if (workshop.Supply == SupplyMode.MarketPurchase)
        {
            int quote = required.Sum(r => _market.EstimateBuyCost(city, _goodById[r.Key], r.Value));
            if (_player.Gold < quote)
            {
                _starved += target;
                return;
            }
        }

        // 再扣/买原料，最后入库产出
        foreach (var (goodId, needed) in required)
        {
            Good inputGood = _goodById[goodId];

            if (workshop.Supply == SupplyMode.SelfSupply)
            {
                warehouse.Remove(inputGood.Id, needed);
            }
            else
            {
                _market.BuyForIndustry(city, inputGood, needed, out _, out _);
            }
        }

        warehouse.Add(workshop.Good.Id, possible);
        string key = $"{city.Name} {workshop.Good.Name}";
        _produced[key] = _produced.GetValueOrDefault(key) + possible;

        if (possible < target)
        {
            _starved += target - possible;
        }
    }

    /// <summary>
    /// 市场采购模式下，按每日预算能买到原料的最大产出件数；买不起时逐件下调。
    /// </summary>
    private int AffordableOutput(City city, Workshop workshop, int maxOutput)
    {
        int budget = Math.Min(workshop.DailyPurchaseLimit, _player.Gold);

        for (int output = maxOutput; output > 0; output--)
        {
            int cost = 0;
            foreach (var input in workshop.Good.Inputs)
            {
                cost += _market.EstimateBuyCost(city, _goodById[input.GoodId], input.Quantity * output);
            }

            if (cost <= budget)
            {
                return output;
            }
        }

        return 0;
    }

    /// <summary>取走并清空自上次以来的产出与自动卖出汇总，供旅行提示拼接；没有内容时返回空串。</summary>
    public string TakeProductionReport()
    {
        string text = _produced.Count > 0
            ? "作坊产出：" + string.Join("、", _produced.Select(p => $"{p.Key} ×{p.Value}"))
            : "";

        if (_overflow)
        {
            text += (text.Length > 0 ? "；" : "") + "部分作坊因仓库已满暂停生产";
        }

        if (_starved > 0)
        {
            text += (text.Length > 0 ? "；" : "") + $"因缺料少产 {_starved} 件";
        }

        if (_autoSellLog.Count > 0)
        {
            text += (text.Length > 0 ? "；" : "") + string.Join("；", _autoSellLog);
        }

        _produced.Clear();
        _overflow = false;
        _starved = 0;
        _autoSellLog.Clear();
        return text;
    }
}
