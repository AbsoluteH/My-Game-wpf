using System;
using System.Collections.Generic;

namespace TradeGame.Entities;

/// <summary>
/// 货物在产业链中的分类，决定作坊门槛与能否建造作坊。
/// 原材料可以无中生有地产出；加工品与成品必须消耗配方里的输入；
/// 舶来品只能从口岸城市买入转售，不能建作坊。
/// </summary>
public enum GoodCategory
{
    /// <summary>原材料：无输入，城市规模 Lv1 即可建作坊。</summary>
    Raw,

    /// <summary>加工品：消耗 1~2 种原料，需城市规模 Lv2。</summary>
    Processed,

    /// <summary>成品：消耗加工品或舶来品，需城市规模 Lv3。</summary>
    Finished,

    /// <summary>舶来品：只产于口岸城市，禁止建作坊。</summary>
    Imported,
}

/// <summary>配方的一项输入：某货物的数量。</summary>
public sealed class GoodInput
{
    public GoodInput(string goodId, int quantity)
    {
        GoodId = goodId;
        Quantity = quantity;
    }

    /// <summary>输入货物的 id。</summary>
    public string GoodId { get; }

    /// <summary>每产出一件成品需要消耗的输入数量。</summary>
    public int Quantity { get; }
}

/// <summary>产业链分类的显示名、作坊门槛与解析工具，供界面与数据层统一使用。</summary>
public static class GoodCategories
{
    public static string Name(GoodCategory category) => category switch
    {
        GoodCategory.Raw => "原材料",
        GoodCategory.Processed => "加工品",
        GoodCategory.Finished => "成品",
        _ => "舶来品",
    };

    /// <summary>该分类建作坊所需的最低城市规模；舶来品不可建造，返回 0（由调用方先拦截）。</summary>
    public static int WorkshopFloor(GoodCategory category) => category switch
    {
        GoodCategory.Raw => 1,
        GoodCategory.Processed => 2,
        GoodCategory.Finished => 3,
        _ => 0,
    };

    /// <summary>把 JSON 里的分类字符串转换为枚举；无法识别时抛出异常并带上货物名。</summary>
    public static GoodCategory Parse(string? value, string goodName)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "raw":
                return GoodCategory.Raw;
            case "processed":
                return GoodCategory.Processed;
            case "finished":
                return GoodCategory.Finished;
            case "imported":
                return GoodCategory.Imported;
            default:
                throw new ArgumentException($"商品「{goodName}」的 category「{value}」无效（应为 raw/processed/finished/imported）");
        }
    }
}

/// <summary>
/// 商品定义（静态数据）。价格并非固定，而是以 BasePrice 为基准，
/// 再乘以城市产销倍率、供需因子和随机波动得到实际买价。
/// Origins 记录产地城市：在产地买卖该货物价格更便宜。
/// Tier 表示解锁该货物所需的城市等级：城市繁荣到该等级后市场上才会出现它。
/// ReputationTier 表示解锁该货物所需的玩家在本城声望等级：声望不够时即使城市等级够也买不到。
/// 两个条件同时满足才能买入（不需要的条件填 0 或 1）。
/// Category 决定它属于产业链的哪一段，Inputs 是建造作坊时的每日配方。
/// </summary>
public sealed class Good
{
    private readonly HashSet<string> _origins;

    public Good(string id, string name, int basePrice, int tier, int reputationTier,
                GoodCategory category, IReadOnlyList<GoodInput> inputs, params string[] origins)
    {
        Id = id;               // 内部标识，用作货物/库存字典的 key
        Name = name;           // 界面显示名称
        BasePrice = basePrice; // 基础价，所有城市共用这一基准
        Tier = tier;           // 解锁所需城市等级（1-3）
        ReputationTier = reputationTier; // 解锁所需本城声望等级（0-3）
        Category = category;   // 产业链分类
        Inputs = inputs;       // 配方输入；原材料与舶来品为空
        _origins = new HashSet<string>(origins); // 产地城市名，可多个
    }

    public string Id { get; }
    public string Name { get; }
    public int BasePrice { get; }

    /// <summary>解锁该货物所需的城市等级。</summary>
    public int Tier { get; }

    /// <summary>解锁该货物所需的玩家在本城声望等级。</summary>
    public int ReputationTier { get; }

    /// <summary>产业链分类（原材料 / 加工品 / 成品 / 舶来品）。</summary>
    public GoodCategory Category { get; }

    /// <summary>建造本货作坊时每天每件产出消耗的输入；原材料与舶来品为空。</summary>
    public IReadOnlyList<GoodInput> Inputs { get; }

    /// <summary>产地城市名集合，用于界面展示与产地折扣判断。</summary>
    public IReadOnlyCollection<string> Origins => _origins;

    /// <summary>是否允许建造作坊：舶来品只能进口转售，不能自产。</summary>
    public bool CanBuildWorkshop => Category != GoodCategory.Imported;

    /// <summary>建作坊所需的城市规模：货物等级与分类下限取较大者（原料 Lv1 / 加工 Lv2 / 成品 Lv3）。</summary>
    public int WorkshopCityLevel => Math.Max(Tier, GoodCategories.WorkshopFloor(Category));

    /// <summary>该城市是否为此货物的产地。</summary>
    public bool IsProducedIn(string cityName) => _origins.Contains(cityName);
}
