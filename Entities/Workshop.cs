namespace TradeGame.Entities;

/// <summary>
/// 产业作坊：玩家在某座城市建造的生产建筑，只生产该城本地盛产的货物。
/// 日产量由货物等级决定（一级 5 件、二级 3 件、三级 1 件），产出全部进入本城仓库。
/// 加工品/成品作坊每天要先取得配方原料：默认从本城仓库自供，也可改为按市场价自动采购。
/// 同一种货物可以重复建多座作坊，数量上限由城市规模决定（见 <see cref="Services.IndustryService"/>）。
/// </summary>
public sealed class Workshop
{
    /// <summary>默认每日原料采购上限（金币）。</summary>
    public const int DefaultPurchaseLimit = 200;

    public Workshop(Good good, int builtDay)
    {
        Good = good;
        BuiltDay = builtDay;
    }

    /// <summary>生产的货物。</summary>
    public Good Good { get; }

    /// <summary>建造时的游戏天数。</summary>
    public int BuiltDay { get; }

    /// <summary>原料供给方式，只对加工品/成品有意义；原料作坊始终无输入。</summary>
    public SupplyMode Supply { get; set; } = SupplyMode.SelfSupply;

    /// <summary>市场采购模式下每天最多花多少金币买原料。</summary>
    public int DailyPurchaseLimit { get; set; } = DefaultPurchaseLimit;

    /// <summary>每天产出的件数：一级 5、二级 3、三级 1。</summary>
    public int DailyOutput => DailyOutputFor(Good);

    /// <summary>按货物等级换算日产量。</summary>
    public static int DailyOutputFor(Good good) => good.Tier switch
    {
        1 => 5,
        2 => 3,
        _ => 1,
    };
}
