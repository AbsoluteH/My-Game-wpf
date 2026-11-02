using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 抵达城市：旅行天数推进完、所在地切换后发布。
/// 订阅方可以刷新行会挂牌、打开当地市场/酒馆界面，或触发到站类剧情。
/// </summary>
public sealed class ArrivedAtCityEvent : IGameEvent
{
    public ArrivedAtCityEvent(City fromCity, City toCity, int days, int day)
    {
        FromCity = fromCity;
        ToCity = toCity;
        Days = days;
        Day = day;
    }

    public City FromCity { get; }

    public City ToCity { get; }

    /// <summary>本次旅行消耗的天数。</summary>
    public int Days { get; }

    /// <summary>抵达时的天数。</summary>
    public int Day { get; }
}
