using System.Collections.Generic;
using TradeGame.Entities;

namespace TradeGame.Strategies;

/// <summary>
/// 定价策略接口：把“价格怎么算”从交易逻辑里抽出来，
/// 以后想换成固定价、季节价等规则只需实现一个新策略。
/// </summary>
public interface IPricingStrategy
{
    /// <summary>重算某城全部商品的价格并写回 city.Prices。</summary>
    void UpdatePrices(City city, IReadOnlyList<Good> goods);

    /// <summary>只重算某城某一种商品的价格（交易后库存变化时使用，避免影响其他货物行情）。</summary>
    void UpdatePrice(City city, Good good);

    /// <summary>该城该商品的买入单价。</summary>
    int BuyPrice(City city, Good good);

    /// <summary>该城该商品的卖出单价（通常低于买价，形成价差）。</summary>
    int SellPrice(City city, Good good);

    /// <summary>
    /// 一次买入指定数量的总花费：数量越大越会推高本地价格，所以按逐件递增的成本累加（滑点）。
    /// </summary>
    int TotalBuyCost(City city, Good good, int quantity);

    /// <summary>
    /// 一次卖出指定数量的总收入：数量越大越会压低本地价格，所以按逐件递减的收入累加（滑点）。
    /// </summary>
    int TotalSellIncome(City city, Good good, int quantity);
}
