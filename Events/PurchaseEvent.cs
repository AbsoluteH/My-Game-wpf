using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 玩家买入成功：金币已扣、库存与价格已更新后发布。
/// 携带成交快照，订阅方不需要反查服务，即可做任务计数、音效、飘字等表现。
/// </summary>
public sealed class PurchaseEvent : IGameEvent
{
    public PurchaseEvent(City city, Good good, int quantity, int totalCost, int remainingGold, int day)
    {
        City = city;
        Good = good;
        Quantity = quantity;
        TotalCost = totalCost;
        RemainingGold = remainingGold;
        Day = day;
    }

    /// <summary>成交城市。</summary>
    public City City { get; }

    /// <summary>成交货物。</summary>
    public Good Good { get; }

    /// <summary>实际成交数量（可能因滑点比请求的少）。</summary>
    public int Quantity { get; }

    /// <summary>本次成交总花费（含滑点）。</summary>
    public int TotalCost { get; }

    /// <summary>成交后玩家剩余金币。</summary>
    public int RemainingGold { get; }

    /// <summary>成交时的天数。</summary>
    public int Day { get; }
}
