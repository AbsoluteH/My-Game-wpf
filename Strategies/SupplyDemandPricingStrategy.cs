using System;
using System.Collections.Generic;
using TradeGame.Entities;

namespace TradeGame.Strategies;

/// <summary>
/// 供需定价策略：价格 = 基础价 ÷ 产销倍率 × 产地折扣 × 供需因子 × 随机波动。
/// 产销倍率越大表示当地越盛产，价格越低；
/// 若当前城市正是该货物的产地，再额外打折，买卖都更便宜。
/// 供需因子 = 平衡库存 / 当前库存（限制在 0.6~1.8）：库存多则便宜，库存少则昂贵。
/// 产销倍率取 0.5 次幂做压缩：既保留城市间的价格排序，又不让产地/销地的价差过大（避免跑一趟就暴富）。
/// </summary>
public sealed class SupplyDemandPricingStrategy : IPricingStrategy
{
    /// <summary>卖出价 = 买价 × 该比例，形成买卖价差，避免同城原地套利。</summary>
    private const double SellRate = 0.85;

    /// <summary>产地折扣：在产地城市买卖该货物时的价格系数。</summary>
    private const double OriginDiscount = 0.9;

    /// <summary>产销倍率的压缩指数：0.5 表示按平方根压缩，产地与销地的价差明显收窄。</summary>
    private const double MultExponent = 0.5;

    private const double MinSupplyFactor = 0.6;
    private const double MaxSupplyFactor = 1.8;

    /// <summary>每日随机波动范围为 [0.95, 1.05)。</summary>
    private const double MinNoise = 0.95;
    private const double NoiseRange = 0.10;

    private readonly Random _rng;

    public SupplyDemandPricingStrategy(Random? rng = null)
    {
        _rng = rng ?? new Random();
    }

    public int BuyPrice(City city, Good good) => city.Prices[good.Id];

    public int SellPrice(City city, Good good) =>
        Math.Max(1, (int)Math.Floor(city.Prices[good.Id] * SellRate));

    /// <summary>买入滑点：逐件按“买走这件之后”的库存算价，因此越买越贵。</summary>
    public int TotalBuyCost(City city, Good good, int quantity)
    {
        double basePrice = city.Prices[good.Id];
        double factor0 = SupplyFactor(city, good, city.Stock[good.Id]);
        double sum = 0;

        for (int i = 0; i < quantity; i++)
        {
            sum += basePrice * SupplyFactor(city, good, city.Stock[good.Id] - i) / factor0;
        }

        return (int)Math.Round(sum);
    }

    /// <summary>卖出滑点：逐件按“卖入这件之后”的库存算价，因此越卖越便宜。</summary>
    public int TotalSellIncome(City city, Good good, int quantity)
    {
        double basePrice = city.Prices[good.Id];
        double factor0 = SupplyFactor(city, good, city.Stock[good.Id]);
        double sum = 0;

        for (int i = 0; i < quantity; i++)
        {
            sum += basePrice * SellRate * SupplyFactor(city, good, city.Stock[good.Id] + i) / factor0;
        }

        return (int)Math.Round(sum);
    }

    /// <summary>给定库存下的供需因子，限制在 0.6~1.8。</summary>
    private static double SupplyFactor(City city, Good good, int stock) =>
        Math.Clamp(city.Equilibrium[good.Id] / (double)Math.Max(1, stock),
                   MinSupplyFactor, MaxSupplyFactor);

    public void UpdatePrices(City city, IReadOnlyList<Good> goods)
    {
        // 换天时所有货物统一重算（含随机波动）
        foreach (var good in goods)
        {
            UpdatePrice(city, good);
        }
    }

    public void UpdatePrice(City city, Good good)
    {
        double supplyFactor = Math.Clamp(
            city.Equilibrium[good.Id] / (double)Math.Max(1, city.Stock[good.Id]),
            MinSupplyFactor, MaxSupplyFactor);

        double noise = MinNoise + _rng.NextDouble() * NoiseRange;

        // 倍率越高越盛产，价格越低，所以放在分母上；
        // 不经营的货物用缺货倍率（小于 1），价格高于基准价——本地没货源，只能等外来供给
        // 倍率做 0.5 次幂压缩：保留“产地便宜、销地贵”的排序，但把产地与销地的价差控制在合理范围
        double abundance = city.SellsGood(good.Id)
            ? Math.Pow(Math.Max(0.1, city.Multipliers[good.Id]), MultExponent)
            : City.UntradedMultiplier;

        // 当前城市就是产地时再打折，本地买卖都更便宜
        double originFactor = good.IsProducedIn(city.Name) ? OriginDiscount : 1.0;

        // 随机行情事件：暴涨（>1）或大跌（<1），买卖价同步调整，事件到期后恢复
        double eventFactor = city.EventMultiplierFor(good.Id);

        double price = good.BasePrice / abundance * originFactor * supplyFactor * noise * eventFactor;
        int value = Math.Max(1, (int)Math.Round(price));

        // 大跌事件期间的硬下限：任何时刻都不得低于产地价（含少量运费缓冲）
        int floor = city.PriceFloorFor(good.Id);
        if (floor > 0)
        {
            value = Math.Max(value, floor);
        }

        city.Prices[good.Id] = value;
    }
}
