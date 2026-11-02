using TradeGame.Entities;

namespace TradeGame.Services;

/// <summary>行会任务类型。</summary>
public enum GuildMissionType
{
    /// <summary>收购任务：本城缺少的货物，玩家从别的城市采购后运来交付。</summary>
    Import,

    /// <summary>运货任务：在本城买入货物，运到指定城市交付。</summary>
    Transport,

    /// <summary>国家任务：本国城市挂牌的收购委托，把货物送到该国首都交付，完成后涨国家好感。</summary>
    Nation,
}

/// <summary>
/// 一条任务：在截止日期前交付指定数量的货物。
/// 行会任务的收购任务在发布城市交付；运货任务要送到目的地城市交付，完成后涨发布城市声望。
/// 国家任务统一为收购：把货物送到本国首都交付，货源不限国家，完成后涨发布国家的好感。
/// </summary>
public sealed class GuildMission
{
    public GuildMission(int id, GuildMissionType type, City origin, City destination,
                        Good good, int quantity, int rewardGold, int rewardReputation, int deadlineDay,
                        Nation? nation = null, int rewardFavor = 0, string title = "", int tier = 0)
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
        Nation = nation;
        RewardFavor = rewardFavor;
        Title = title;
        Tier = tier;
    }

    public int Id { get; }
    public GuildMissionType Type { get; }

    /// <summary>发布任务、也是声望归属的城市。</summary>
    public City Origin { get; }

    /// <summary>运货任务的目的地；收购任务与国家任务与 Origin 相同。</summary>
    public City Destination { get; }

    /// <summary>实际需要到达并交付的城市。</summary>
    public City TargetCity => Type == GuildMissionType.Transport ? Destination : Origin;

    /// <summary>需要交付的货物。</summary>
    public Good Good { get; }

    public int Quantity { get; }
    public int RewardGold { get; }

    /// <summary>交付后获得的发布城市声望（国家任务为 0）。</summary>
    public int RewardReputation { get; }

    /// <summary>截止的游戏天数，超过即失败。</summary>
    public int DeadlineDay { get; }

    /// <summary>国家任务所属国家；行会任务为 null。</summary>
    public Nation? Nation { get; }

    /// <summary>国家任务完成后获得的国家好感（行会任务为 0）。</summary>
    public int RewardFavor { get; }

    /// <summary>国家任务的类别（统一为“收购”）；行会任务为空串。</summary>
    public string Title { get; }

    /// <summary>国家任务的档位 1~3（对应好感等级）；行会任务为 0。</summary>
    public int Tier { get; }

    /// <summary>界面用的一行描述，例如“运货 伦敦 → 巴黎”“收购（伦敦 本地）”“国家·收购（巴黎）”。</summary>
    public string RouteText => Type switch
    {
        GuildMissionType.Transport => $"运货 {Origin.Name} → {Destination.Name}",
        GuildMissionType.Nation => $"国家·{Title}（{Origin.Name}）",
        _ => $"收购（{Origin.Name} 本地）",
    };
}
