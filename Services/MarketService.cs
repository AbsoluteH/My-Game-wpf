using System;
using System.Collections.Generic;
using System.Linq;
using TradeGame.Entities;
using TradeGame.Strategies;

namespace TradeGame.Services;

/// <summary>
/// 交易服务：在玩家当前城市执行买入/卖出。
/// 买入只限本城经营且已解锁的货物；卖出不限，任何货物城市都会收购（不经营的货物按保守价格收）。
/// 成功后更新玩家舱单和城市库存、重算该城价格；卖出时还按货值给城市累积繁荣度（可能触发升级解锁新商品）。
/// </summary>
public sealed class MarketService
{
    /// <summary>「全部买入」时预留的现金比例：避免一键买空连路费都不剩。</summary>
    private const double CashReserveRate = 0.1;

    /// <summary>每笔卖出货值中有多少比例转化为该城繁荣度（防止原地反复买卖刷繁荣）。</summary>
    private const double ProsperityRate = 0.03;

    private readonly Player _player;
    private readonly IPricingStrategy _pricing;
    private readonly IReadOnlyList<Good> _goods;

    public MarketService(Player player, IPricingStrategy pricing, IReadOnlyList<Good> goods)
    {
        _player = player;
        _pricing = pricing;
        _goods = goods;
    }

    /// <summary>当前城市的买入单价。</summary>
    public int BuyPrice(Good good) => _pricing.BuyPrice(_player.CurrentCity, good);

    /// <summary>当前城市的卖出单价。</summary>
    public int SellPrice(Good good) => _pricing.SellPrice(_player.CurrentCity, good);

    /// <summary>
    /// 按金币、载重、城市库存三者取最小值，得到当前最多能买多少。
    /// 因为买入有滑点（越买越贵），这里用二分按实付能力算，和实际成交保持一致。
    /// </summary>
    public int MaxBuyable(Good good)
    {
        City city = _player.CurrentCity;
        if (!city.CanTrade(good, _player.ReputationLevel(city)))
        {
            return 0;
        }

        int upper = Math.Min(city.Stock[good.Id], _player.FreeCapacity);
        return upper <= 0
            ? 0
            : AffordableQuantity(good, upper, _player.Gold * (1 - CashReserveRate));
    }

    /// <summary>当前最多能卖多少（即舱内该货物数量）。</summary>
    public int MaxSellable(Good good) => _player.Cargo.GetValueOrDefault(good.Id);

    /// <summary>
    /// 买入指定数量的货物。
    /// </summary>
    /// <param name="message">返回给界面显示的提示文本。</param>
    public bool Buy(Good good, int quantity, out string message)
    {
        City city = _player.CurrentCity;

        if (!city.SellsGood(good.Id))
        {
            message = $"{city.Name} 暂不经营 {good.Name}";
            return false;
        }

        if (city.Level < good.Tier)
        {
            message = $"{city.Name} 城市等级不足：买入 {good.Name} 需城市 Lv{good.Tier}（当前 Lv{city.Level}）";
            return false;
        }

        int reputationLevel = _player.ReputationLevel(city);
        if (reputationLevel < good.ReputationTier)
        {
            message = $"{city.Name} 声望不足：买入 {good.Name} 需声望 Lv{good.ReputationTier}（当前 Lv{reputationLevel}）";
            return false;
        }

        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        // 城市库存不足，钱再多也买不到
        if (quantity > city.Stock[good.Id])
        {
            message = $"{city.Name} 的 {good.Name} 库存不足（剩余 {city.Stock[good.Id]}）";
            return false;
        }

        // 载重上限：所有货物共享同一个载重额度
        if (quantity > _player.FreeCapacity)
        {
            message = $"载重不足，还可装载 {_player.FreeCapacity} 单位";
            return false;
        }

        // 大额买入会把本地价格推高，成交总价按逐件递增核算（滑点）：
        // 数量过多时按实付能力缩减，避免“显示价便宜、实际买不起”
        int requested = quantity;
        quantity = AffordableQuantity(good, quantity, _player.Gold);

        if (quantity <= 0)
        {
            message = $"金币不足：按滑点后的价格连 1 件 {good.Name} 都买不起（现有 {_player.Gold}）";
            return false;
        }

        int cost = _pricing.TotalBuyCost(city, good, quantity);
        int unitPrice = (int)Math.Round(cost / (double)quantity);

        _player.Spend(cost);
        _player.AddCargo(good.Id, quantity, unitPrice); // 记录成交均价，用于计算持仓平均成本
        city.Stock[good.Id] -= quantity; // 买走库存，当地供给变紧
        _pricing.UpdatePrice(city, good); // 只重算这一种货，其他货物价格保持不动

        message = $"买入 {good.Name} ×{quantity}，花费 {cost} 金币，均价 {unitPrice}";
        if (quantity < requested)
        {
            message += $"（买入会推高价格，数量从 {requested} 减到 {quantity}）";
        }

        return true;
    }

    /// <summary>
    /// 在给定预算内能买到的最大件数：买入成本随数量递增，用二分查找。
    /// </summary>
    /// <param name="budget">可用于本次买入的金币上限。</param>
    private int AffordableQuantity(Good good, int wanted, double budget)
    {
        City city = _player.CurrentCity;
        int low = 1;
        int high = wanted;
        int best = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (_pricing.TotalBuyCost(city, good, mid) <= budget)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }

    /// <summary>
    /// 卖出指定数量的货物。
    /// </summary>
    public bool Sell(Good good, int quantity, out string message)
    {
        int owned = _player.Cargo.GetValueOrDefault(good.Id);
        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        if (quantity > owned)
        {
            message = $"货物不足，{good.Name} 仅有 {owned}";
            return false;
        }

        if (!SellToCity(_player.CurrentCity, good, quantity, out message))
        {
            return false;
        }

        _player.RemoveCargo(good.Id, quantity);
        return true;
    }

    /// <summary>
    /// 把一批货直接卖给指定城市的市场，不要求玩家就在该城（仓库自动卖出走这条路径）。
    /// 调用方负责货物的来源与数量校验。
    /// 大额卖出会把本地价格压低，成交总收入按逐件递减核算（滑点），越卖越便宜。
    /// </summary>
    public bool SellToCity(City city, Good good, int quantity, out string message)
    {
        if (quantity <= 0)
        {
            message = "数量必须大于 0";
            return false;
        }

        int income = _pricing.TotalSellIncome(city, good, quantity);
        int unitPrice = (int)Math.Round(income / (double)quantity);

        _player.Earn(income);
        city.Stock[good.Id] += quantity; // 卖入市场，当地供给变松
        _pricing.UpdatePrice(city, good); // 只重算这一种货，其他货物价格保持不动

        message = $"卖出 {good.Name} ×{quantity}，获得 {income} 金币，均价 {unitPrice}";
        GrowCity(city, income, ref message); // 货物进入本地消费，城市因此繁荣
        return true;
    }

    /// <summary>估算在某城买入指定件数的总花费（含滑点），供产业采购做预算判断。</summary>
    public int EstimateBuyCost(City city, Good good, int quantity) =>
        quantity <= 0 ? 0 : _pricing.TotalBuyCost(city, good, quantity);

    /// <summary>
    /// 产业采购：在某城按实时买价买入指定数量的原料，直接送进作坊消耗（不进入船舱）。
    /// 调用方须先用 <see cref="EstimateBuyCost"/> 确认预算与库存；这里再做一次兜底校验。
    /// </summary>
    public bool BuyForIndustry(City city, Good good, int quantity, out int cost, out string message)
    {
        cost = 0;

        if (quantity <= 0)
        {
            message = "采购数量必须大于 0";
            return false;
        }

        if (quantity > city.Stock[good.Id])
        {
            message = $"{city.Name} 的 {good.Name} 库存不足（剩余 {city.Stock[good.Id]}）";
            return false;
        }

        int total = _pricing.TotalBuyCost(city, good, quantity);
        if (_player.Gold < total)
        {
            message = $"金币不足：在 {city.Name} 采购 {good.Name} ×{quantity} 需要 {total}，现有 {_player.Gold}";
            return false;
        }

        _player.Spend(total);
        city.Stock[good.Id] -= quantity; // 原料进入作坊，不再留在本地市场
        _pricing.UpdatePrice(city, good);

        cost = total;
        message = $"{city.Name} 采购 {good.Name} ×{quantity}，花费 {total} 金币";
        return true;
    }

    /// <summary>
    /// 按成交货值给城市增加繁荣度，升级时把新解锁的商品名拼到提示文本后面。
    /// </summary>
    private void GrowCity(City city, int tradeValue, ref string message)
    {
        int gained = Math.Max(1, (int)Math.Round(tradeValue * ProsperityRate));
        int oldLevel = city.Level;
        city.AddProsperity(gained);

        message += $"，{city.Name} 繁荣 +{gained}";

        if (city.Level > oldLevel)
        {
            string unlocks = string.Join("、", _goods
                .Where(g => city.SellsGood(g.Id)
                            && g.Tier > oldLevel
                            && g.Tier <= city.Level
                            && g.ReputationTier <= _player.ReputationLevel(city))
                .Select(g => g.Name));

            message += unlocks.Length > 0
                ? $"（升到 Lv{city.Level}，解锁 {unlocks}）"
                : $"（升到 Lv{city.Level}）";
        }
    }
}
