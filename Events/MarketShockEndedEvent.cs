using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 行情暴涨/大跌结束：价格已恢复常态后发布。
/// 订阅方据此清理提示、恢复地图标记，或结算依赖该行情的逻辑。
/// </summary>
public sealed class MarketShockEndedEvent : IGameEvent
{
    public MarketShockEndedEvent(int eventId, City city, Good good, MarketEventKind kind,
                                 double delta, int startDay, int endDay, int day)
    {
        EventId = eventId;
        City = city;
        Good = good;
        Kind = kind;
        Delta = delta;
        StartDay = startDay;
        EndDay = endDay;
        Day = day;
    }

    /// <summary>行情事件 id，与开始事件一致。</summary>
    public int EventId { get; }

    public City City { get; }

    public Good Good { get; }

    public MarketEventKind Kind { get; }

    /// <summary>波动幅度（0.35 表示 35%）。</summary>
    public double Delta { get; }

    public int StartDay { get; }

    public int EndDay { get; }

    /// <summary>发布时的天数。</summary>
    public int Day { get; }
}
