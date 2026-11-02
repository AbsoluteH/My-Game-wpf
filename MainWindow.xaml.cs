using System.Collections.ObjectModel;
using System.Windows;
using TradeGame.Entities;
using TradeGame.Services;

namespace TradeGame;

/// <summary>
/// 主窗口：只负责界面显示与用户操作，所有规则计算都交给 <see cref="TradeGameEngine"/>。
/// 界面使用原生 WPF 控件（DataGrid、ComboBox、TextBox、Button 等）。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>市场列表的一行：货物名、产地、城市库存、当前买价、当前卖价。</summary>
    private sealed class MarketRow
    {
        public string Name { get; init; } = "";

        /// <summary>产地城市，多个产地用顿号连接。</summary>
        public string Origin { get; init; } = "";

        public int Stock { get; init; }
        public int BuyPrice { get; init; }
        public int SellPrice { get; init; }

        /// <summary>本城该货的行情标记（↑35% / ↓42%），没有事件时为空。</summary>
        public string Trend { get; init; } = "";

        /// <summary>对应的商品对象，买入/卖出时用来定位商品。</summary>
        public Good? Good { get; init; }
    }

    /// <summary>舱内货物列表的一行：货物名、数量与持仓平均成本。</summary>
    private sealed class CargoRow
    {
        public string Name { get; init; } = "";
        public int Quantity { get; init; }

        /// <summary>这批货物的平均买入价，卖出时用来判断盈亏。</summary>
        public int AverageCost { get; init; }

        /// <summary>对应的商品对象，卖出时用来定位商品。</summary>
        public Good? Good { get; init; }
    }

    /// <summary>目的地下拉框的一项：显示“城市（天数，路费）”，并保留城市引用。</summary>
    private sealed class DestinationRow
    {
        public string Label { get; init; } = "";
        public City? City { get; init; }
    }

    /// <summary>行会任务列表的一行：类型、起点/交付地、货物、数量、报酬、声望、截止天数。</summary>
    private sealed class MissionRow
    {
        public string TypeName { get; init; } = "";
        public string OriginName { get; init; } = "";
        public string TargetName { get; init; } = "";
        public string GoodName { get; init; } = "";
        public int Quantity { get; init; }
        public int RewardGold { get; init; }
        public int RewardReputation { get; init; }
        public string Deadline { get; init; } = "";

        /// <summary>对应的任务对象，接受/交付时用来定位。</summary>
        public GuildMission? Mission { get; init; }
    }

    /// <summary>本城已建作坊的一行：货物、分类、配方、原料库存与供给方式。</summary>
    private sealed class WorkshopRow
    {
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Recipe { get; init; } = "";
        public string InputStock { get; init; } = "";
        public string Supply { get; init; } = "";
        public int DailyOutput { get; init; }
        public int BuiltDay { get; init; }

        /// <summary>对应的作坊对象，修改供给方式时用来定位。</summary>
        public Workshop? Workshop { get; init; }
    }

    /// <summary>可建作坊的一行：货物、分类、配方、等级、日产量、已建座数与造价。</summary>
    private sealed class BuildableRow
    {
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Recipe { get; init; } = "";
        public int Tier { get; init; }
        public int DailyOutput { get; init; }
        public int Built { get; init; }
        public int Cost { get; init; }

        /// <summary>对应的商品对象，建造时用来定位。</summary>
        public Good? Good { get; init; }
    }

    /// <summary>仓库库存的一行：货物与数量。</summary>
    private sealed class WarehouseRow
    {
        public string Name { get; init; } = "";
        public int Quantity { get; init; }

        /// <summary>对应的商品对象，取货时用来定位。</summary>
        public Good? Good { get; init; }
    }

    private readonly TradeGameEngine _game = new();

    // 各列表直接绑定到界面控件，刷新时清空重填
    private readonly ObservableCollection<MarketRow> _market = new();
    private readonly ObservableCollection<CargoRow> _cargo = new();
    private readonly ObservableCollection<DestinationRow> _destinations = new();
    private readonly ObservableCollection<MissionRow> _guildOffers = new();
    private readonly ObservableCollection<MissionRow> _nationOffers = new();
    private readonly ObservableCollection<MissionRow> _missions = new();

    /// <summary>酒馆里已经打听到的行情消息（纯文本，按时间保留）。</summary>
    private readonly ObservableCollection<string> _rumors = new();

    private readonly ObservableCollection<WorkshopRow> _workshops = new();
    private readonly ObservableCollection<BuildableRow> _buildable = new();
    private readonly ObservableCollection<WarehouseRow> _warehouse = new();

    /// <summary>本城房产库存（与仓库库存各自独立）。</summary>
    private readonly ObservableCollection<WarehouseRow> _estateStock = new();

    public MainWindow()
    {
        InitializeComponent();

        // 绑定数据源（XAML 中已关闭自动生成列，列定义写在 XAML 里）
        MarketGrid.ItemsSource = _market;
        CargoGrid.ItemsSource = _cargo;
        DestinationBox.ItemsSource = _destinations;
        GuildOfferGrid.ItemsSource = _guildOffers;
        NationOfferGrid.ItemsSource = _nationOffers;
        MyMissionGrid.ItemsSource = _missions;
        RumorList.ItemsSource = _rumors;
        WorkshopGrid.ItemsSource = _workshops;
        BuildableGrid.ItemsSource = _buildable;
        WarehouseGrid.ItemsSource = _warehouse;

        // 家园页左侧复用舱内货物列表，右侧是本城房产库存
        EstateCargoGrid.ItemsSource = _cargo;
        EstateGrid.ItemsSource = _estateStock;

        // 自动卖出节奏下拉框：固定三档，顺序与 AutoSellModes.All 一致
        foreach (var mode in AutoSellModes.All)
        {
            AutoSellBox.Items.Add(AutoSellModes.Name(mode));
        }

        AutoSellBox.SelectedIndex = 0;

        // 作坊原料供给方式下拉框：顺序与 SupplyModes.All 一致
        foreach (var mode in SupplyModes.All)
        {
            SupplyModeBox.Items.Add(SupplyModes.Name(mode));
        }

        SupplyModeBox.SelectedIndex = 0;

        Log("欢迎来到中世纪跑商原型，愿你的商队财源广进。");
        Refresh();
    }

    /// <summary>
    /// 根据当前游戏状态重建三个列表并刷新所有文字，界面上的数据都从这里来。
    /// 会尽量保留市场上已选中的货物，避免刷新后选择丢失。
    /// </summary>
    private void Refresh()
    {
        // 左上角商队状态
        DayText.Text = $"第 {_game.Day} 天（已消耗 {_game.Day - 1} 天）";
        CityText.Text = $"所在地：{_game.CurrentCity.Name}";
        CityLevelText.Text = $"城市等级：Lv{_game.CityLevel}" + (_game.CityProsperityNeeded > 0
            ? $"（繁荣 {_game.CityProsperity} / {_game.CityProsperityNeeded}）"
            : "（已满级）");
        ReputationText.Text = $"城市声望：Lv{_game.CityReputationLevel}" + (_game.CityReputationNeeded > 0
            ? $"（{_game.CityReputation} / {_game.CityReputationNeeded}）"
            : "（已满级）");
        NationText.Text = $"所属国家：{_game.CurrentNation.Name}（好感 Lv{_game.NationFavorLevel}"
            + (_game.NationFavorNeeded > 0
                ? $"，{_game.NationFavor} / {_game.NationFavorNeeded}）"
                : "，已满级）");
        GoldText.Text = $"金币：{_game.Gold}";
        LoadText.Text = $"载重：{_game.CargoUnits} / {_game.Capacity}";
        MarketTitle.Text = $"{_game.CurrentCity.Name} 市场（Lv{_game.CityLevel}）";

        // 本城还没解锁的商品提示，写明差城市等级还是差声望
        var locked = _game.LockedGoods;
        UnlockHintText.Text = locked.Count > 0
            ? "未解锁：" + string.Join("、", locked.Select(g => $"{g.Name}（{_game.UnlockRequirement(g)}）"))
            : "本城已解锁全部可经营商品。";

        InvestButton.IsEnabled = _game.CityLevel < City.MaxLevel && _game.Gold >= TradeGameEngine.InvestCost;

        // 商队扩容：显示下一次的价格与增加量，满上限或钱不够时禁用
        ExpandButton.Content = _game.CanExpandCapacity
            ? $"扩容商队（+{TradeGameEngine.ExpandCapacityUnit} 载重 / {_game.ExpandCost} 金币）"
            : $"商队已达载重上限 {Player.MaxCapacity}";
        ExpandButton.IsEnabled = _game.CanExpandCapacity && _game.Gold >= _game.ExpandCost;

        // 记录刷新前选中的商品/任务，重建后再选回去
        string? selectedId = (MarketGrid.SelectedItem as MarketRow)?.Good?.Id;
        string? selectedCargoId = (CargoGrid.SelectedItem as CargoRow)?.Good?.Id;
        int? selectedOfferId = (GuildOfferGrid.SelectedItem as MissionRow)?.Mission?.Id;
        int? selectedNationOfferId = (NationOfferGrid.SelectedItem as MissionRow)?.Mission?.Id;
        int? selectedMissionId = (MyMissionGrid.SelectedItem as MissionRow)?.Mission?.Id;
        string? selectedWarehouseId = (WarehouseGrid.SelectedItem as WarehouseRow)?.Good?.Id;
        string? selectedBuildableId = (BuildableGrid.SelectedItem as BuildableRow)?.Good?.Id;

        // 重建市场列表（只显示本城已解锁的商品，每个商品一行）
        _market.Clear();
        foreach (var good in _game.TradeableGoods)
        {
            _market.Add(new MarketRow
            {
                Good = good,
                Name = good.Name,
                Origin = string.Join("、", good.Origins),
                Stock = _game.CurrentCity.Stock[good.Id],
                BuyPrice = _game.BuyPrice(good),
                SellPrice = _game.SellPrice(good),
                Trend = _game.TrendFor(good),
            });
        }

        // 重建舱内货物列表，只显示数量大于 0 的货物
        _cargo.Clear();
        foreach (var good in _game.Goods)
        {
            int quantity = _game.Cargo.GetValueOrDefault(good.Id);
            if (quantity > 0)
            {
                _cargo.Add(new CargoRow
                {
                    Good = good,
                    Name = good.Name,
                    Quantity = quantity,
                    AverageCost = _game.AverageCost(good),
                });
            }
        }

        // 重建目的地列表：排除当前城市，并显示预计天数和路费
        _destinations.Clear();
        foreach (var city in _game.Cities)
        {
            if (city == _game.CurrentCity)
            {
                continue;
            }

            _destinations.Add(new DestinationRow
            {
                City = city,
                Label = $"{city.Name} Lv{city.Level}（{_game.CurrentCity.DaysTo(city)} 天，路费 {_game.TravelCost(city)} 金币）",
            });
        }

        // 重建本城行会任务
        GuildReputationText.Text = $"{_game.CurrentCity.Name} 行会 · 本城声望 Lv{_game.CityReputationLevel}"
            + (_game.CityReputationNeeded > 0
                ? $"（{_game.CityReputation} / {_game.CityReputationNeeded}）"
                : "（已满级）");

        // 捐赠换声望：显示本城下一次的价格，满级或钱不够时禁用
        DonateButton.Content = _game.CityReputationLevel < Player.MaxReputationLevel
            ? $"捐赠行会（{_game.DonateCost} 金币 → 声望 +{TradeGameEngine.DonateReputation}）"
            : "本城声望已满级";
        DonateButton.IsEnabled = _game.CityReputationLevel < Player.MaxReputationLevel
                                 && _game.Gold >= _game.DonateCost;

        _guildOffers.Clear();
        foreach (var mission in _game.CurrentGuildOffers)
        {
            _guildOffers.Add(ToRow(mission, withOrigin: false));
        }

        // 国家分页：当前国家的好感进度与该国挂牌的国家任务
        NationTitleText.Text = $"{_game.CurrentNation.Name} · 国家任务"
            + (_game.NationFavorNeeded > 0
                ? $"（好感 Lv{_game.NationFavorLevel}，{_game.NationFavor} / {_game.NationFavorNeeded}）"
                : $"（好感 Lv{_game.NationFavorLevel}，已满级）");

        NationHintText.Text = _game.NationFavorLevel >= Nation.MaxLevel
            ? "该国三档国家任务已全部开放。"
            : _game.NationFavorLevel == 0
                ? $"尚未与该国建立关系：好感达到 {_game.NationFavorNeeded} 点后开放第一档国家任务。"
                : $"好感达到 {_game.NationFavorNeeded} 点后开放下一档国家任务。";

        _nationOffers.Clear();
        foreach (var mission in _game.CurrentNationOffers)
        {
            _nationOffers.Add(ToRow(mission, withOrigin: false));
        }

        // 重建已接受任务列表
        _missions.Clear();
        foreach (var mission in _game.ActiveMissions)
        {
            _missions.Add(ToRow(mission, withOrigin: true));
        }

        // 酒馆：提示还有几条消息没打听到，并回显已打听到的情报
        TavernTitleText.Text = $"{_game.CurrentCity.Name} 酒馆";
        TavernButton.Content = $"打听消息（{TradeGameEngine.TavernCost} 金币）";
        int unrevealed = _game.Events.UnrevealedCount;
        TavernHintText.Text = unrevealed > 0
            ? $"还有 {unrevealed} 条消息没打听到（每条 {TradeGameEngine.TavernCost} 金币）"
            : "暂时没有可打听的新消息";

        _rumors.Clear();
        foreach (string rumor in _game.KnownRumors)
        {
            _rumors.Add(rumor);
        }

        // 产业：作坊位、仓库状态与本城库存
        IndustryTitle.Text = $"{_game.CurrentCity.Name} 产业（城市规模 Lv{_game.CityLevel} · "
                           + $"作坊 {_game.IndustryUsed} / {_game.IndustrySlots}）";

        var warehouse = _game.Warehouse;
        WarehouseText.Text = warehouse.IsBuilt
            ? $"仓库 Lv{warehouse.Level}：{warehouse.Used} / {warehouse.Capacity} 单位（剩余 {warehouse.Free}）"
            : "尚未建造仓库（建作坊前必须先建仓库）";

        WarehouseButton.Content = !warehouse.IsBuilt
            ? $"建造仓库（{_game.NextWarehouseCost} 金币 → 容量 {warehouse.NextLevelCapacity}）"
            : warehouse.IsMaxLevel
                ? $"仓库已满级（容量 {warehouse.Capacity}）"
                : $"升级仓库（{_game.NextWarehouseCost} 金币 → 容量 {warehouse.NextLevelCapacity}）";
        WarehouseButton.IsEnabled = !warehouse.IsMaxLevel && _game.Gold >= _game.NextWarehouseCost;

        // 自动卖出：显示当前节奏与下一次执行时间，没建仓库时不能设置
        AutoSellText.Text = !warehouse.IsBuilt
            ? "自动卖出：需先建仓库"
            : $"自动卖出：{AutoSellModes.Name(warehouse.AutoSell)}" + warehouse.AutoSell switch
            {
                AutoSellMode.Daily => "（每天结算产出后卖出本地货物）",
                AutoSellMode.Every7Days => $"（第 {NextAutoSellDay(7)}、{NextAutoSellDay(7) + 7}… 天结算后卖出）",
                _ => "",
            };

        AutoSellBox.IsEnabled = warehouse.IsBuilt;
        AutoSellApplyButton.IsEnabled = warehouse.IsBuilt;

        for (int i = 0; i < AutoSellModes.All.Count; i++)
        {
            if (AutoSellModes.All[i] == warehouse.AutoSell)
            {
                AutoSellBox.SelectedIndex = i;
            }
        }

        _workshops.Clear();
        foreach (var workshop in _game.CityWorkshops)
        {
            _workshops.Add(new WorkshopRow
            {
                Workshop = workshop,
                Name = workshop.Good.Name,
                Category = GoodCategories.Name(workshop.Good.Category),
                Recipe = RecipeText(workshop.Good),
                InputStock = InputStockText(workshop.Good, warehouse),
                Supply = workshop.Good.Inputs.Count == 0
                    ? "—"
                    : SupplyModes.Name(workshop.Supply) + (workshop.Supply == SupplyMode.MarketPurchase
                        ? $"，{workshop.DailyPurchaseLimit}/天"
                        : ""),
                DailyOutput = workshop.DailyOutput,
                BuiltDay = workshop.BuiltDay,
            });
        }

        _warehouse.Clear();
        foreach (var good in _game.Goods)
        {
            int quantity = warehouse.QuantityOf(good.Id);
            if (quantity > 0)
            {
                _warehouse.Add(new WarehouseRow { Good = good, Name = good.Name, Quantity = quantity });
            }
        }

        _buildable.Clear();
        foreach (var good in _game.BuildableWorkshopGoods)
        {
            _buildable.Add(new BuildableRow
            {
                Good = good,
                Name = good.Name,
                Category = GoodCategories.Name(good.Category),
                Recipe = RecipeText(good),
                Tier = good.Tier,
                DailyOutput = _game.WorkshopDailyOutput(good),
                Built = _game.CityWorkshops.Count(w => w.Good.Id == good.Id),
                Cost = _game.WorkshopCost(good),
            });
        }

        // 家园：房产等级、容量与房产库存（与仓库互不影响）
        var estate = _game.Estate;
        HomeTitle.Text = $"{_game.CurrentCity.Name} 家园（房产 {estate.Level} / {Estate.MaxLevel}）";
        EstateText.Text = estate.IsOwned
            ? $"房产 Lv{estate.Level}：{estate.Used} / {estate.Capacity} 单位（剩余 {estate.Free}）"
            : "尚未购置房产（可存放货物，也是长期金币去处）";

        if (!estate.IsMaxLevel && _game.CityLevel < estate.Level + 1)
        {
            EstateText.Text += $"（需城市 Lv{estate.Level + 1}）";
        }

        EstateButton.Content = estate.IsMaxLevel
            ? $"房产已满级（容量 {estate.Capacity}）"
            : estate.IsOwned
                ? $"升级房产（{_game.NextEstateCost} 金币 → 容量 {estate.NextLevelCapacity}）"
                : $"购置房产（{_game.NextEstateCost} 金币 → 容量 {estate.NextLevelCapacity}）";
        EstateButton.IsEnabled = !estate.IsMaxLevel
                                 && _game.CityLevel >= estate.Level + 1
                                 && _game.Gold >= _game.NextEstateCost;

        _estateStock.Clear();
        foreach (var good in _game.Goods)
        {
            int quantity = estate.QuantityOf(good.Id);
            if (quantity > 0)
            {
                _estateStock.Add(new WarehouseRow { Good = good, Name = good.Name, Quantity = quantity });
            }
        }

        // 批量卸货/取货：没仓库、没有货或没空位时直接禁用，避免点了必然失败
        StoreAllButton.IsEnabled = warehouse.IsBuilt && _cargo.Count > 0 && warehouse.Free > 0;
        TakeAllButton.IsEnabled = warehouse.IsBuilt && _warehouse.Count > 0 && _game.FreeCapacity > 0;

        // 恢复之前的选择；若没有则默认选第一行，方便直接点按钮
        if (selectedId != null)
        {
            MarketGrid.SelectedItem = _market.FirstOrDefault(r => r.Good?.Id == selectedId);
        }

        if (MarketGrid.SelectedItem == null && _market.Count > 0)
        {
            MarketGrid.SelectedIndex = 0;
        }

        if (selectedCargoId != null)
        {
            CargoGrid.SelectedItem = _cargo.FirstOrDefault(r => r.Good?.Id == selectedCargoId);
        }

        if (CargoGrid.SelectedItem == null && _cargo.Count > 0)
        {
            CargoGrid.SelectedIndex = 0;
        }

        if (DestinationBox.SelectedIndex < 0 && _destinations.Count > 0)
        {
            DestinationBox.SelectedIndex = 0;
        }

        if (selectedOfferId != null)
        {
            GuildOfferGrid.SelectedItem = _guildOffers.FirstOrDefault(r => r.Mission?.Id == selectedOfferId);
        }

        if (GuildOfferGrid.SelectedItem == null && _guildOffers.Count > 0)
        {
            GuildOfferGrid.SelectedIndex = 0;
        }

        if (selectedNationOfferId != null)
        {
            NationOfferGrid.SelectedItem = _nationOffers.FirstOrDefault(r => r.Mission?.Id == selectedNationOfferId);
        }

        if (NationOfferGrid.SelectedItem == null && _nationOffers.Count > 0)
        {
            NationOfferGrid.SelectedIndex = 0;
        }

        if (selectedMissionId != null)
        {
            MyMissionGrid.SelectedItem = _missions.FirstOrDefault(r => r.Mission?.Id == selectedMissionId);
        }

        if (MyMissionGrid.SelectedItem == null && _missions.Count > 0)
        {
            MyMissionGrid.SelectedIndex = 0;
        }

        if (selectedBuildableId != null)
        {
            BuildableGrid.SelectedItem = _buildable.FirstOrDefault(r => r.Good?.Id == selectedBuildableId);
        }

        if (BuildableGrid.SelectedItem == null && _buildable.Count > 0)
        {
            BuildableGrid.SelectedIndex = 0;
        }

        if (selectedWarehouseId != null)
        {
            WarehouseGrid.SelectedItem = _warehouse.FirstOrDefault(r => r.Good?.Id == selectedWarehouseId);
        }

        if (WarehouseGrid.SelectedItem == null && _warehouse.Count > 0)
        {
            WarehouseGrid.SelectedIndex = 0;
        }

        SyncSupplyControls();
    }

    /// <summary>把一条任务转换成界面行；已接受任务额外显示起点城市。</summary>
    private MissionRow ToRow(GuildMission mission, bool withOrigin) => new()
    {
        Mission = mission,
        TypeName = mission.Type switch
        {
            GuildMissionType.Transport => "运货",
            GuildMissionType.Nation => $"国家·{mission.Title}",
            _ => "收购",
        },
        OriginName = withOrigin ? mission.Origin.Name : "",
        TargetName = mission.Type switch
        {
            GuildMissionType.Transport => mission.Destination.Name,
            GuildMissionType.Nation => mission.TargetCity.Name,
            _ => "本地",
        },
        GoodName = mission.Good.Name,
        Quantity = mission.Quantity,
        RewardGold = mission.RewardGold,
        RewardReputation = mission.Type == GuildMissionType.Nation
            ? mission.RewardFavor
            : mission.RewardReputation,
        Deadline = mission.DeadlineDay - _game.Day <= 0
            ? $"第 {mission.DeadlineDay} 天（今天到期）"
            : $"第 {mission.DeadlineDay} 天（剩 {mission.DeadlineDay - _game.Day} 天）",
    };

    // 买入看市场列表，卖出看舱内货物列表
    private void BuyButton_Click(object sender, RoutedEventArgs e) => Buy(all: false);

    private void BuyAllButton_Click(object sender, RoutedEventArgs e) => Buy(all: true);

    private void SellButton_Click(object sender, RoutedEventArgs e) => Sell(all: false);

    private void SellAllButton_Click(object sender, RoutedEventArgs e) => Sell(all: true);

    /// <summary>
    /// 买入：以市场列表中选中的货物为准。
    /// </summary>
    /// <param name="all">true 表示按当前最多可买数量买入。</param>
    private void Buy(bool all)
    {
        if (MarketGrid.SelectedItem is not MarketRow { Good: not null } row)
        {
            Log("请先在市场列表中选择一种要买入的货物。");
            return;
        }

        Good good = row.Good;
        int quantity;
        if (all)
        {
            quantity = _game.MaxBuyable(good);
            if (quantity <= 0)
            {
                Log($"{good.Name}：金币、载重或城市库存不足，无法买入。");
                return;
            }
        }
        else if (!TryReadQuantity(out quantity))
        {
            return;
        }

        bool ok = _game.Buy(good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh(); // 只有成功才刷新界面，失败时保留当前状态
        }
    }

    /// <summary>
    /// 卖出：以舱内货物列表（玩家背包）中选中的货物为准，
    /// 与当前城市市场里选中的行无关。
    /// </summary>
    /// <param name="all">true 表示卖出该货物的全部持仓。</param>
    private void Sell(bool all)
    {
        if (CargoGrid.SelectedItem is not CargoRow { Good: not null } row)
        {
            Log("请先在舱内货物中选择一种要卖出的货物。");
            return;
        }

        Good good = row.Good;
        int quantity;
        if (all)
        {
            quantity = row.Quantity;
        }
        else if (!TryReadQuantity(out quantity))
        {
            return;
        }

        bool ok = _game.Sell(good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>读取数量输入框；非法时提示并返回 false。</summary>
    private bool TryReadQuantity(out int quantity)
    {
        if (!int.TryParse(QuantityBox.Text.Trim(), out quantity) || quantity <= 0)
        {
            Log("请输入大于 0 的整数数量。");
            return false;
        }

        return true;
    }

    /// <summary>出发按钮：把选中的目的地交给引擎，成功后刷新界面并重置下拉框选择。</summary>
    private void TravelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationBox.SelectedItem is not DestinationRow { City: not null } destination)
        {
            Log("请选择目的地。");
            return;
        }

        bool ok = _game.Travel(destination.City, out string message);
        Log(message);
        if (ok)
        {
            DestinationBox.SelectedIndex = 0;
            Refresh();
        }
    }

    /// <summary>投资建设按钮：花金币提升当前城市繁荣度，可能立即解锁新商品。</summary>
    private void InvestButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.InvestInCity(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>接受行会任务：只能接受当前城市发布的委托。</summary>
    private void AcceptMissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (GuildOfferGrid.SelectedItem is not MissionRow { Mission: not null } row)
        {
            Log("请先在本城行会任务列表中选择一条任务。");
            return;
        }

        bool ok = _game.AcceptMission(row.Mission, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>接受国家任务：只能接受当前国家在该国城市挂牌的委托。</summary>
    private void AcceptNationMissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (NationOfferGrid.SelectedItem is not MissionRow { Mission: not null } row)
        {
            Log("请先在国家任务列表中选择一条任务。");
            return;
        }

        bool ok = _game.AcceptMission(row.Mission, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>交付任务：必须身处交付城市，且舱内有足够的货物。</summary>
    private void DeliverMissionButton_Click(object sender, RoutedEventArgs e)
    {
        if (MyMissionGrid.SelectedItem is not MissionRow { Mission: not null } row)
        {
            Log("请先在已接受任务中选择一条要交付的任务。");
            return;
        }

        _game.DeliverMission(row.Mission, out string message);
        Log(message);
        Refresh(); // 交付失败也可能意味着任务过期被移除，统一刷新
    }

    /// <summary>向本城行会捐赠金币换声望。</summary>
    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.Donate(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>花金币扩容商队载重。</summary>
    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.ExpandCaravan(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>酒馆打听消息：花金币换一条行情情报，没有消息时不扣钱。</summary>
    private void TavernButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.AskTavern(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>建造或升级本城仓库（建作坊的前置条件）。</summary>
    private void WarehouseButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.UpgradeWarehouse(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>建造作坊：以可建列表里选中的货物为准。</summary>
    private void BuildWorkshopButton_Click(object sender, RoutedEventArgs e)
    {
        if (BuildableGrid.SelectedItem is not BuildableRow { Good: not null } row)
        {
            Log("请先在可建作坊列表中选择一种货物。");
            return;
        }

        bool ok = _game.BuildWorkshop(row.Good, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>配方文本：每产出一件需要的原料及数量；原料与舶来品没有配方。</summary>
    private string RecipeText(Good good)
    {
        if (good.Inputs.Count == 0)
        {
            return good.Category == GoodCategory.Imported ? "进口转售" : "—";
        }

        return string.Join(" + ", good.Inputs.Select(i => $"{GoodName(i.GoodId)}×{i.Quantity}")) + " → 1";
    }

    /// <summary>配方原料在本城仓库的现存数量，供玩家判断会不会缺料停产。</summary>
    private string InputStockText(Good good, Warehouse warehouse) =>
        good.Inputs.Count == 0
            ? "—"
            : string.Join("、", good.Inputs.Select(i => $"{GoodName(i.GoodId)} {warehouse.QuantityOf(i.GoodId)}"));

    private string GoodName(string goodId) =>
        _game.Goods.FirstOrDefault(g => g.Id == goodId)?.Name ?? goodId;

    /// <summary>选中作坊时，把它的供给方式与采购上限同步到下方控件。</summary>
    private void WorkshopGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        SyncSupplyControls();

    /// <summary>按当前选中的作坊刷新供给控件；没有选中或选中的是原料作坊时禁用。</summary>
    private void SyncSupplyControls()
    {
        Workshop? workshop = (WorkshopGrid.SelectedItem as WorkshopRow)?.Workshop;

        if (workshop == null || workshop.Good.Inputs.Count == 0)
        {
            SupplyModeBox.IsEnabled = false;
            SupplyLimitBox.IsEnabled = false;
            SupplyApplyButton.IsEnabled = false;
            SupplyText.Text = workshop == null
                ? "作坊供给：请选中上方一座加工品/成品作坊"
                : $"{workshop.Good.Name} 是{GoodCategories.Name(workshop.Good.Category)}，无需原料供给";
            return;
        }

        SupplyModeBox.IsEnabled = true;
        SupplyLimitBox.IsEnabled = true;
        SupplyApplyButton.IsEnabled = true;
        SupplyLimitBox.Text = workshop.DailyPurchaseLimit.ToString();
        SupplyText.Text = $"作坊供给：{workshop.Good.Name}（{RecipeText(workshop.Good)}）";

        for (int i = 0; i < SupplyModes.All.Count; i++)
        {
            if (SupplyModes.All[i] == workshop.Supply)
            {
                SupplyModeBox.SelectedIndex = i;
            }
        }
    }

    /// <summary>应用作坊的原料供给方式与每日采购上限。</summary>
    private void SupplyApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorkshopGrid.SelectedItem is not WorkshopRow { Workshop: not null } row)
        {
            Log("请先在本城作坊列表中选中一座作坊。");
            return;
        }

        if (!TryReadSupplyLimit(out int limit))
        {
            return;
        }

        SupplyMode mode = SupplyModes.All[Math.Max(0, SupplyModeBox.SelectedIndex)];
        bool ok = _game.SetWorkshopSupply(row.Workshop, mode, limit, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    private bool TryReadSupplyLimit(out int limit)
    {
        limit = 0;
        if (!int.TryParse(SupplyLimitBox.Text.Trim(), out int value) || value < 0)
        {
            Log("每日采购上限请填写不小于 0 的整数。");
            return false;
        }

        limit = value;
        return true;
    }

    /// <summary>卸货：把左侧舱内选中的货物按数量存入本城仓库。</summary>
    private void StoreToWarehouseButton_Click(object sender, RoutedEventArgs e)
    {
        if (CargoGrid.SelectedItem is not CargoRow { Good: not null } row)
        {
            Log("请先在左侧舱内货物中选择要卸下的货物。");
            return;
        }

        if (!TryReadQuantity(out int quantity))
        {
            return;
        }

        bool ok = _game.StoreToWarehouse(row.Good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>取货：把仓库库存里选中的货物按数量装进舱内。</summary>
    private void TakeFromWarehouseButton_Click(object sender, RoutedEventArgs e)
    {
        if (WarehouseGrid.SelectedItem is not WarehouseRow { Good: not null } row)
        {
            Log("请先在仓库库存中选择要取出的货物。");
            return;
        }

        if (!TryReadQuantity(out int quantity))
        {
            return;
        }

        bool ok = _game.TakeFromWarehouse(row.Good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>全部卸货：舱内每种货物按本城仓库剩余容量尽可能存入。</summary>
    private void StoreAllToWarehouseButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.StoreAllToWarehouse(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>全部取货：本城仓库里的货物按剩余载重尽可能装进船舱。</summary>
    private void TakeAllFromWarehouseButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.TakeAllFromWarehouse(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>购置或升级本城房产（家园）。</summary>
    private void EstateButton_Click(object sender, RoutedEventArgs e)
    {
        bool ok = _game.BuyEstate(out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>存入房产：把家园页左侧舱内选中的货物按数量存进本城房产。</summary>
    private void StoreToEstateButton_Click(object sender, RoutedEventArgs e)
    {
        if (EstateCargoGrid.SelectedItem is not CargoRow { Good: not null } row)
        {
            Log("请先在舱内货物中选择要存放的货物。");
            return;
        }

        if (!TryReadEstateQuantity(out int quantity))
        {
            return;
        }

        bool ok = _game.StoreToEstate(row.Good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>取出房产：把房产库存里选中的货物按数量装进船舱。</summary>
    private void TakeFromEstateButton_Click(object sender, RoutedEventArgs e)
    {
        if (EstateGrid.SelectedItem is not WarehouseRow { Good: not null } row)
        {
            Log("请先在房产库存中选择要取出的货物。");
            return;
        }

        if (!TryReadEstateQuantity(out int quantity))
        {
            return;
        }

        bool ok = _game.TakeFromEstate(row.Good, quantity, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>读取家园页的数量输入框；非法时提示并返回 false。</summary>
    private bool TryReadEstateQuantity(out int quantity)
    {
        if (!int.TryParse(EstateQuantityBox.Text.Trim(), out quantity) || quantity <= 0)
        {
            Log("请输入大于 0 的整数数量。");
            return false;
        }

        return true;
    }

    /// <summary>打开只读的产业与仓库总览窗口（跨城查看作坊、仓库与库存明细）。</summary>
    private void IndustryOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        new IndustryOverviewWindow(_game) { Owner = this }.ShowDialog();
    }

    /// <summary>应用本城仓库的自动卖出节奏（不自动卖出 / 每天 / 每 7 天）。</summary>
    private void AutoSellApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (AutoSellBox.SelectedIndex < 0)
        {
            return;
        }

        AutoSellMode mode = AutoSellModes.All[AutoSellBox.SelectedIndex];
        bool ok = _game.SetAutoSell(mode, out string message);
        Log(message);
        if (ok)
        {
            Refresh();
        }
    }

    /// <summary>下一次自动卖出发生的日子：按当前天数向上取整到下一个周期倍数。</summary>
    private int NextAutoSellDay(int cycle) => (_game.Day / cycle + 1) * cycle;

    /// <summary>打开作弊台修改金币；关闭后刷新主界面（余额、按钮可用状态都会变）。</summary>
    private void CheatButton_Click(object sender, RoutedEventArgs e)
    {
        int before = _game.Gold;
        new CheatWindow(_game) { Owner = this }.ShowDialog();

        if (_game.Gold != before)
        {
            Log($"作弊台：金币 {before} → {_game.Gold}");
        }

        Refresh();
    }

    /// <summary>把一条消息插到消息栏最上方，只保留最近 200 条。</summary>
    private void Log(string message)
    {
        LogList.Items.Insert(0, message);
        while (LogList.Items.Count > 200)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }
}
