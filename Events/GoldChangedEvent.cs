namespace TradeGame.Events;

/// <summary>金币变化的原因，订阅方可以按来源过滤（统计、任务、破产判定等）。</summary>
public enum GoldChangeReason
{
    /// <summary>玩家在市场上买入货物。</summary>
    Purchase,

    /// <summary>玩家卖出货物，含仓库自动卖出。</summary>
    Sale,

    /// <summary>作坊在市场采购原料。</summary>
    IndustryPurchase,

    /// <summary>支付旅行路费。</summary>
    TravelFee,

    /// <summary>在酒馆打听行情消息。</summary>
    Tavern,

    /// <summary>投资城市建设。</summary>
    CityInvestment,

    /// <summary>扩容商队载重。</summary>
    CaravanExpansion,

    /// <summary>向行会捐赠换声望。</summary>
    GuildDonation,

    /// <summary>交付行会任务的报酬。</summary>
    GuildReward,

    /// <summary>交付国家任务的报酬。</summary>
    NationReward,

    /// <summary>建造作坊或升级仓库。</summary>
    IndustryInvestment,

    /// <summary>购置或升级城市房产（家园）。</summary>
    PropertyInvestment,

    /// <summary>作弊台直接改钱。</summary>
    Debug,
}

/// <summary>
/// 玩家金币发生变化：在金币已写入后发布。
/// 这是所有经济系统的统一入口，任何扣钱/进账都必须带原因发布，避免各服务各自维护通知。
/// </summary>
public sealed class GoldChangedEvent : IGameEvent
{
    public GoldChangedEvent(int delta, int goldBefore, int goldAfter, GoldChangeReason reason, int day)
    {
        Delta = delta;
        GoldBefore = goldBefore;
        GoldAfter = goldAfter;
        Reason = reason;
        Day = day;
    }

    /// <summary>本次变化量：收入为正，支出为负。</summary>
    public int Delta { get; }

    /// <summary>变化前金币。</summary>
    public int GoldBefore { get; }

    /// <summary>变化后金币。</summary>
    public int GoldAfter { get; }

    /// <summary>变化原因。</summary>
    public GoldChangeReason Reason { get; }

    /// <summary>发生时的天数。</summary>
    public int Day { get; }
}
