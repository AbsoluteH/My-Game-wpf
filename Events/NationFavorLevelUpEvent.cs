using TradeGame.Entities;

namespace TradeGame.Events;

/// <summary>
/// 国家好感升级：某国累计好感跨过等级阈值后发布。
/// 订阅方可以刷新该国的国家任务档位、提示新解锁任务或播放演出。
/// </summary>
public sealed class NationFavorLevelUpEvent : IGameEvent
{
    public NationFavorLevelUpEvent(Nation nation, int oldLevel, int newLevel, int favor, int day)
    {
        Nation = nation;
        OldLevel = oldLevel;
        NewLevel = newLevel;
        Favor = favor;
        Day = day;
    }

    public Nation Nation { get; }

    public int OldLevel { get; }

    public int NewLevel { get; }

    /// <summary>升级后的累计好感点数。</summary>
    public int Favor { get; }

    /// <summary>升级发生的天数。</summary>
    public int Day { get; }
}
