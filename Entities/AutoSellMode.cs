using System.Collections.Generic;

namespace TradeGame.Entities;

/// <summary>
/// 仓库自动卖出节奏：每天结算产出后自动卖出本地货物，或每 7 天卖一次，或关闭。
/// </summary>
public enum AutoSellMode
{
    /// <summary>不自动卖出（默认）。</summary>
    None,

    /// <summary>每天结算产出后卖出。</summary>
    Daily,

    /// <summary>每 7 天卖出一次（第 7、14、21… 天）。</summary>
    Every7Days,
}

/// <summary>自动卖出模式的显示名与可选列表，供界面下拉框统一使用。</summary>
public static class AutoSellModes
{
    public static string Name(AutoSellMode mode) => mode switch
    {
        AutoSellMode.Daily => "每天",
        AutoSellMode.Every7Days => "每 7 天",
        _ => "不自动卖出",
    };

    public static IReadOnlyList<AutoSellMode> All { get; } = new[]
    {
        AutoSellMode.None,
        AutoSellMode.Daily,
        AutoSellMode.Every7Days,
    };
}
