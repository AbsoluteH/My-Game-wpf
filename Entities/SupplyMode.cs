using System.Collections.Generic;

namespace TradeGame.Entities;

/// <summary>
/// 加工品/成品作坊的原料供给方式：
/// 自供从本城仓库取料（可来自自家原料作坊），市场采购则每天按本城实时买价买入缺额。
/// </summary>
public enum SupplyMode
{
    /// <summary>从本城仓库取原料，库存不足时停产（默认）。</summary>
    SelfSupply,

    /// <summary>每天按本城实时买价自动采购原料，受每日采购上限约束。</summary>
    MarketPurchase,
}

/// <summary>供给模式的显示名与可选列表，供界面下拉框统一使用。</summary>
public static class SupplyModes
{
    public static string Name(SupplyMode mode) => mode switch
    {
        SupplyMode.MarketPurchase => "市场采购",
        _ => "自供（仓库取料）",
    };

    public static IReadOnlyList<SupplyMode> All { get; } = new[]
    {
        SupplyMode.SelfSupply,
        SupplyMode.MarketPurchase,
    };
}
