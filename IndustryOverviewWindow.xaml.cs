using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using TradeGame.Entities;
using TradeGame.Services;

namespace TradeGame;

/// <summary>
/// 产业与仓库总览窗口：跨城查看所有作坊（类型、等级、日产、产地）、
/// 各城仓库等级与占用情况，以及仓库里每种货物的当前库存。
/// 只读，数据在打开时按当时的游戏状态生成。
/// </summary>
public partial class IndustryOverviewWindow : Window
{
    /// <summary>产业总览的一行：一座作坊（或某城还没有作坊时的占位行）。</summary>
    private sealed class IndustryRow
    {
        public string City { get; init; } = "";
        public string Size { get; init; } = "";

        /// <summary>该城作坊位占用，例如“3 / 4 座”。</summary>
        public string Slots { get; init; } = "";

        /// <summary>产业类型，即生产的货物名称。</summary>
        public string TypeName { get; init; } = "";

        /// <summary>产业链分类：原材料 / 加工品 / 成品。</summary>
        public string Category { get; init; } = "";

        /// <summary>配方文本，例如“生皮×2 → 1”；原料作坊为“—”。</summary>
        public string Recipe { get; init; } = "";

        /// <summary>原料供给方式与每日采购上限；原料作坊为“—”。</summary>
        public string Supply { get; init; } = "";

        public string Tier { get; init; } = "";
        public string DailyOutput { get; init; } = "";

        /// <summary>该货物的产地城市（多个用顿号连接）。</summary>
        public string Origins { get; init; } = "";

        /// <summary>本城仓库状态，例如“Lv2 · 3/400”。</summary>
        public string WarehouseInfo { get; init; } = "";
    }

    /// <summary>仓库总览的一行：一座城市的仓库。</summary>
    private sealed class WarehouseSummaryRow
    {
        public string City { get; init; } = "";
        public string Level { get; init; } = "";
        public string Capacity { get; init; } = "";
        public string Used { get; init; } = "";
        public string Free { get; init; } = "";

        /// <summary>库存概览，例如“咸鱼 ×30、木材 ×12”；空仓时为“空仓库”。</summary>
        public string StockSummary { get; init; } = "";

        /// <summary>占用率，例如“12%”。</summary>
        public string UsageRate { get; init; } = "";

        /// <summary>自动卖出节奏的显示名。</summary>
        public string AutoSell { get; init; } = "";

        /// <summary>本城房产状态，例如“Lv2 · 120/350”；未购置时为“未购置”。</summary>
        public string EstateInfo { get; init; } = "";

        /// <summary>该行对应的城市与节奏，用于在本窗口修改设置。</summary>
        public City? CityRef { get; init; }
        public AutoSellMode Mode { get; init; }
        public bool WarehouseBuilt { get; init; }
    }

    /// <summary>库存明细的一行：某城仓库里的某种货物。</summary>
    private sealed class StockRow
    {
        public string City { get; init; } = "";
        public string Name { get; init; } = "";

        /// <summary>该货物的产地城市（多个用顿号连接）。</summary>
        public string Origins { get; init; } = "";
        public int Quantity { get; init; }
    }

    private readonly TradeGameEngine _game;

    private readonly ObservableCollection<IndustryRow> _industry = new();
    private readonly ObservableCollection<WarehouseSummaryRow> _warehouses = new();
    private readonly ObservableCollection<StockRow> _stock = new();

    public IndustryOverviewWindow(TradeGameEngine game)
    {
        InitializeComponent();

        _game = game;
        IndustryGrid.ItemsSource = _industry;
        WarehouseGrid.ItemsSource = _warehouses;
        StockGrid.ItemsSource = _stock;

        // 自动卖出节奏下拉框：固定三档，顺序与 AutoSellModes.All 一致
        foreach (var mode in AutoSellModes.All)
        {
            AutoSellBox.Items.Add(AutoSellModes.Name(mode));
        }

        AutoSellBox.SelectedIndex = 0;
        Refresh();
    }

    /// <summary>按当前游戏状态重建三张表与顶部汇总。</summary>
    private void Refresh()
    {
        string? selectedCity = (WarehouseGrid.SelectedItem as WarehouseSummaryRow)?.City;
        _industry.Clear();
        _warehouses.Clear();
        _stock.Clear();

        int workshopCount = 0;
        int warehouseCount = 0;
        int estateCount = 0;
        int stockUnits = 0;

        foreach (var city in _game.Cities)
        {
            var workshops = _game.Industry.Workshops(city);
            var warehouse = _game.Industry.WarehouseOf(city);
            var estate = _game.Home.EstateOf(city);
            int slots = IndustryService.MaxWorkshops(city);

            workshopCount += workshops.Count;
            if (warehouse.IsBuilt)
            {
                warehouseCount++;
            }

            if (estate.IsOwned)
            {
                estateCount++;
            }

            stockUnits += warehouse.Used;

            // 产业表：每座作坊一行；没有作坊的城市给一行占位，避免漏看
            if (workshops.Count == 0)
            {
                _industry.Add(new IndustryRow
                {
                    City = city.Name,
                    Size = $"Lv{city.Level}",
                    Slots = $"0 / {slots} 座",
                    TypeName = "（暂无作坊）",
                    Category = "",
                    Recipe = "",
                    Supply = "",
                    Tier = "",
                    DailyOutput = "",
                    Origins = "",
                    WarehouseInfo = warehouse.IsBuilt
                        ? $"Lv{warehouse.Level} · {warehouse.Used}/{warehouse.Capacity}"
                        : "未建",
                });
            }
            else
            {
                foreach (var workshop in workshops)
                {
                    _industry.Add(new IndustryRow
                    {
                        City = city.Name,
                        Size = $"Lv{city.Level}",
                        Slots = $"{workshops.Count} / {slots} 座",
                        TypeName = workshop.Good.Name,
                        Category = GoodCategories.Name(workshop.Good.Category),
                        Recipe = RecipeText(workshop.Good),
                        Supply = workshop.Good.Inputs.Count == 0
                            ? "—"
                            : SupplyModes.Name(workshop.Supply) + (workshop.Supply == SupplyMode.MarketPurchase
                                ? $" · {workshop.DailyPurchaseLimit}/天"
                                : ""),
                        Tier = $"Lv{workshop.Good.Tier}",
                        DailyOutput = $"{workshop.DailyOutput} 件/天",
                        Origins = string.Join("、", workshop.Good.Origins),
                        WarehouseInfo = $"Lv{warehouse.Level} · {warehouse.Used}/{warehouse.Capacity}",
                    });
                }
            }

            // 仓库表：每城一行，未建造的也列出来，方便看还差哪座城
            string stockSummary = warehouse.IsBuilt && warehouse.Stock.Count > 0
                ? string.Join("、", _game.Goods
                    .Where(g => warehouse.QuantityOf(g.Id) > 0)
                    .Select(g => $"{g.Name} ×{warehouse.QuantityOf(g.Id)}"))
                : warehouse.IsBuilt ? "空仓库" : "—";

            _warehouses.Add(new WarehouseSummaryRow
            {
                City = city.Name,
                CityRef = city,
                Mode = warehouse.AutoSell,
                WarehouseBuilt = warehouse.IsBuilt,
                AutoSell = warehouse.IsBuilt ? AutoSellModes.Name(warehouse.AutoSell) : "未建",
                Level = warehouse.IsBuilt ? $"Lv{warehouse.Level}" : "未建",
                Capacity = warehouse.IsBuilt ? warehouse.Capacity.ToString() : "—",
                Used = warehouse.IsBuilt ? warehouse.Used.ToString() : "—",
                Free = warehouse.IsBuilt ? warehouse.Free.ToString() : "—",
                StockSummary = stockSummary,
                EstateInfo = estate.IsOwned
                    ? $"Lv{estate.Level} · {estate.Used}/{estate.Capacity}"
                    : "未购置",
                UsageRate = warehouse.IsBuilt && warehouse.Capacity > 0
                    ? $"{warehouse.Used * 100.0 / warehouse.Capacity:0}%"
                    : "—",
            });

            // 库存明细：每城每种有库存的货物一行
            if (!warehouse.IsBuilt)
            {
                continue;
            }

            foreach (var good in _game.Goods)
            {
                int quantity = warehouse.QuantityOf(good.Id);
                if (quantity <= 0)
                {
                    continue;
                }

                _stock.Add(new StockRow
                {
                    City = city.Name,
                    Name = good.Name,
                    Origins = string.Join("、", good.Origins),
                    Quantity = quantity,
                });
            }
        }

        SummaryText.Text = $"共 {workshopCount} 座作坊、{warehouseCount} 座仓库、{estateCount} 处房产，"
                         + $"仓库合计存放 {stockUnits} 单位货物。"
                         + "作坊只能生产本城盛产的货物，日产一级 5 / 二级 3 / 三级 1 件；"
                         + "仓库满时作坊暂停生产。房产容量独立于仓库，房产里的货物不参与自动卖出。";

        // 恢复刷新前的选中城市，并让下拉框显示该城的当前节奏
        if (selectedCity != null)
        {
            WarehouseGrid.SelectedItem = _warehouses.FirstOrDefault(r => r.City == selectedCity);
        }

        if (WarehouseGrid.SelectedItem == null && _warehouses.Count > 0)
        {
            WarehouseGrid.SelectedIndex = 0;
        }

        SyncAutoSellBox();
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

    private string GoodName(string goodId) =>
        _game.Goods.FirstOrDefault(g => g.Id == goodId)?.Name ?? goodId;

    /// <summary>让自动卖出下拉框跟随仓库总览里选中的城市。</summary>
    private void SyncAutoSellBox()
    {
        if (WarehouseGrid.SelectedItem is not WarehouseSummaryRow row)
        {
            AutoSellBox.IsEnabled = false;
            AutoSellApplyButton.IsEnabled = false;
            return;
        }

        for (int i = 0; i < AutoSellModes.All.Count; i++)
        {
            if (AutoSellModes.All[i] == row.Mode)
            {
                AutoSellBox.SelectedIndex = i;
            }
        }

        AutoSellBox.IsEnabled = row.WarehouseBuilt;
        AutoSellApplyButton.IsEnabled = row.WarehouseBuilt;
        StatusText.Text = row.WarehouseBuilt
            ? $"{row.City}：当前 {AutoSellModes.Name(row.Mode)}"
            : $"{row.City} 还没有仓库，建好仓库后才能设置自动卖出";
    }

    private void WarehouseGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        SyncAutoSellBox();

    /// <summary>把下拉框选择的节奏应用到仓库总览里选中的城市。</summary>
    private void AutoSellApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (WarehouseGrid.SelectedItem is not WarehouseSummaryRow row || row.CityRef == null)
        {
            return;
        }

        AutoSellMode mode = AutoSellModes.All[AutoSellBox.SelectedIndex];
        bool ok = _game.SetAutoSell(row.CityRef, mode, out string message);
        StatusText.Text = message;

        if (ok)
        {
            Refresh();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
