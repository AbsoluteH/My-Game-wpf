using TradeGame.Services;

namespace TradeGame.Events;

/// <summary>
/// 行会任务完成：货物已扣除、金币与声望已发放后发布。
/// 订阅方可以接成就、剧情分支或城市关系变化，不需要再读任务是否还在进行中。
/// </summary>
public sealed class GuildMissionCompletedEvent : IGameEvent
{
    public GuildMissionCompletedEvent(GuildMission mission, int rewardGold, int rewardReputation, int day)
    {
        Mission = mission;
        RewardGold = rewardGold;
        RewardReputation = rewardReputation;
        Day = day;
    }

    /// <summary>已完成的任务快照。</summary>
    public GuildMission Mission { get; }

    /// <summary>实际发放的金币报酬。</summary>
    public int RewardGold { get; }

    /// <summary>实际发放的发布城市声望。</summary>
    public int RewardReputation { get; }

    /// <summary>完成时的天数。</summary>
    public int Day { get; }
}
