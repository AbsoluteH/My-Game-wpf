namespace TradeGame.Entities;

/// <summary>行情事件的方向：暴涨（价格上浮）或大跌（价格下调）。</summary>
public enum MarketEventKind
{
    Surge,
    Slump,
}

/// <summary>
/// 随机行情事件：某座城市的某种货物在一段时间内买卖价格大幅上浮或下调。
/// 事件对买价与卖价同时生效（价格整体乘以 <see cref="Multiplier"/>），
/// 事件到期后价格恢复常态。<see cref="Discovered"/> 表示玩家是否已在酒馆打听到它。
/// </summary>
public sealed class MarketEvent
{
    public MarketEvent(int id, City city, Good good, MarketEventKind kind, double delta,
                       int startDay, int endDay)
    {
        Id = id;
        City = city;
        Good = good;
        Kind = kind;
        Delta = Math.Clamp(delta, 0.05, 0.6);
        StartDay = startDay;
        EndDay = endDay;
    }

    public int Id { get; }
    public City City { get; }
    public Good Good { get; }
    public MarketEventKind Kind { get; }

    /// <summary>波动幅度（0.35 表示 35%）。</summary>
    public double Delta { get; }

    public int StartDay { get; }
    public int EndDay { get; }

    /// <summary>玩家是否已在酒馆打听到这条消息。</summary>
    public bool Discovered { get; private set; }

    /// <summary>作用在价格上的倍率：暴涨为 1+幅度，大跌为 1-幅度。</summary>
    public double Multiplier => Kind == MarketEventKind.Surge ? 1 + Delta : 1 - Delta;

    public bool IsActiveOn(int day) => day >= StartDay && day <= EndDay;

    public void MarkDiscovered() => Discovered = true;

    /// <summary>给玩家看的消息文本，会标注剩余天数或已结束。</summary>
    public string Describe(int day)
    {
        string trend = Kind == MarketEventKind.Surge ? "暴涨" : "大跌";
        string span = IsActiveOn(day)
            ? (EndDay > day ? $"第 {StartDay}~{EndDay} 天（还剩 {EndDay - day} 天）" : $"第 {StartDay}~{EndDay} 天（今天结束）")
            : $"第 {StartDay}~{EndDay} 天（已结束）";
        return $"{City.Name}的{Good.Name}{trend}，幅度约 {Delta:P0}，{span}";
    }
}
