using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 声望升级：某城累计声望跨过等级阈值后发布。
/// 订阅方可以提示新解锁的商品、把新货加入运货任务池，或播放升级演出。
/// </summary>
public sealed class ReputationLevelUpEvent : IGameEvent
{
    public ReputationLevelUpEvent(City city, int oldLevel, int newLevel, int reputation, int day)
    {
        City = city;
        OldLevel = oldLevel;
        NewLevel = newLevel;
        Reputation = reputation;
        Day = day;
    }

    public City City { get; }

    public int OldLevel { get; }

    public int NewLevel { get; }

    /// <summary>升级后的累计声望点数。</summary>
    public int Reputation { get; }

    /// <summary>升级发生的天数。</summary>
    public int Day { get; }
}
