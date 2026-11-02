namespace TradeGame.Events;

/// <summary>
/// 新的一天：每日的库存回归与价格结算完成后发布，订阅方看到的是当天的行情。
/// 旅行途中每过一天发布一次，玩家所在城市要等抵达后才切换，需要位置时请直接读取游戏状态。
/// </summary>
public sealed class NewDayEvent : IGameEvent
{
    public NewDayEvent(int day)
    {
        Day = day;
    }

    /// <summary>当前天数（从 1 开始）。</summary>
    public int Day { get; }
}
