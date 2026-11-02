using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 城市升级：繁荣度跨过阈值后发布。
/// 订阅方据此刷新行会任务、解锁商品提示、增加作坊位或做表现层演出。
/// </summary>
public sealed class CityLevelUpEvent : IGameEvent
{
    public CityLevelUpEvent(City city, int oldLevel, int newLevel, int day)
    {
        City = city;
        OldLevel = oldLevel;
        NewLevel = newLevel;
        Day = day;
    }

    public City City { get; }

    public int OldLevel { get; }

    public int NewLevel { get; }

    /// <summary>升级发生的天数。</summary>
    public int Day { get; }
}
