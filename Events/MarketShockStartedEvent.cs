using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 行情暴涨/大跌开始：价格倍率与下限已写入后发布。
/// 订阅方可以做提示、地图标记、音效，或让自动交易系统开始关注。
/// </summary>
public sealed class MarketShockStartedEvent : IGameEvent
{
    public MarketShockStartedEvent(int eventId, City city, Good good, MarketEventKind kind,
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

    /// <summary>行情事件 id，用于和结束事件配对。</summary>
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
