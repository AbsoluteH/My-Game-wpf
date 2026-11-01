using TradeGame.Entities;

namespace TradeGame.Services;

/// <summary>行会任务类型。</summary>
public enum GuildMissionType
{
    /// <summary>收购任务：本城缺少的货物，玩家从别的城市采购后运来交付。</summary>
    Import,

    /// <summary>运货任务：在本城买入货物，运到指定城市交付。</summary>
    Transport,
}

/// <summary>
/// 行会发布的一条任务：在截止日期前交付指定数量的货物。
/// 收购任务在发布城市交付；运货任务要送到目的地城市交付。
/// 完成后获得金币与发布城市的声望，声望等级会解锁声望型货物。
/// </summary>
public sealed class GuildMission
{
    public GuildMission(int id, GuildMissionType type, City origin, City destination,
                        Good good, int quantity, int rewardGold, int rewardReputation, int deadlineDay)
    {
        Id = id;
        Type = type;
        Origin = origin;
        Destination = destination;
        Good = good;
        Quantity = quantity;
        RewardGold = rewardGold;
        RewardReputation = rewardReputation;
        DeadlineDay = deadlineDay;
    }

    public int Id { get; }
    public GuildMissionType Type { get; }

    /// <summary>发布任务、也是声望归属的城市。</summary>
    public City Origin { get; }

    /// <summary>运货任务的目的地；收购任务与 Origin 相同。</summary>
    public City Destination { get; }

    /// <summary>实际需要到达并交付的城市。</summary>
    public City TargetCity => Type == GuildMissionType.Transport ? Destination : Origin;

    /// <summary>需要交付的货物。</summary>
    public Good Good { get; }

    public int Quantity { get; }
    public int RewardGold { get; }

    /// <summary>交付后获得的发布城市声望。</summary>
    public int RewardReputation { get; }

    /// <summary>截止的游戏天数，超过即失败。</summary>
    public int DeadlineDay { get; }

    /// <summary>界面用的一行描述，例如“运货 伦敦 → 巴黎”或“收购（本地）”。</summary>
    public string RouteText => Type == GuildMissionType.Transport
        ? $"运货 {Origin.Name} → {Destination.Name}"
        : $"收购（{Origin.Name} 本地）";
}
