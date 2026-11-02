using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>卖出渠道：玩家手动卖出，还是仓库按节奏自动卖出。</summary>
public enum SaleSource
{
    Player,
    WarehouseAutoSell,
}

/// <summary>
/// 卖出完成：金币已入账、库存与价格已更新、城市繁荣度已累积后发布。
/// 与 PurchaseEvent 对称，供任务计数、统计与表现层使用。
/// </summary>
public sealed class SaleCompletedEvent : IGameEvent
{
    public SaleCompletedEvent(City city, Good good, int quantity, int income, int remainingGold,
                              int day, SaleSource source)
    {
        City = city;
        Good = good;
        Quantity = quantity;
        Income = income;
        RemainingGold = remainingGold;
        Day = day;
        Source = source;
    }

    public City City { get; }

    public Good Good { get; }

    public int Quantity { get; }

    /// <summary>本次卖出的总收入（含滑点）。</summary>
    public int Income { get; }

    /// <summary>卖出后玩家剩余金币。</summary>
    public int RemainingGold { get; }

    public int Day { get; }

    /// <summary>卖出渠道。</summary>
    public SaleSource Source { get; }
}
