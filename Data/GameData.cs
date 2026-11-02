using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradeGame.Entities;

namespace TradeGame.Data;

/// <summary>
/// 游戏数据加载器：商品表、城市表与平衡参数都存在 Data/game-data.json 中，
/// 改数值只需编辑该 JSON，不必重新编译。
/// 加载时会做完整性校验（id 唯一、等级区间不重叠、产地必须是已定义的城市且盛产），
/// 校验失败直接抛出异常并说明是哪一项有问题，避免带着坏数据进入游戏。
/// </summary>
public static class GameData
{
    /// <summary>数据文件相对路径（相对于程序运行目录）。</summary>
    private const string DataRelativePath = "Data/game-data.json";

    private static readonly Lazy<GameDataFile> Data = new(LoadAndValidate);

    /// <summary>从 JSON 创建商品表。</summary>
    public static List<Good> CreateGoods() =>
        Data.Value.Goods
            .Select(g => new Good(
                g.Id, g.Name, g.BasePrice, g.Tier, g.ReputationTier,
                GoodCategories.Parse(g.Category, g.Name),
                g.Inputs.Select(i => new GoodInput(i.Id, i.Quantity)).ToList(),
                g.Origins.ToArray()))
            .ToList();

    /// <summary>从 JSON 创建国家表。</summary>
    public static List<Nation> CreateNations() =>
        Data.Value.Nations
            .Select(n => new Nation(n.Name, n.FavorThresholds))
            .ToList();

    /// <summary>从 JSON 创建城市表，并把城市绑定到已创建的国家（同时配置城市升级门槛）。</summary>
    public static List<City> CreateCities(IReadOnlyList<Nation> nations)
    {
        City.ConfigureProsperityThresholds(Data.Value.Balance.CityLevelThresholds);
        var nationsByName = nations.ToDictionary(n => n.Name);

        return Data.Value.Cities
            .Select(c => new City(c.Name, c.X, c.Y, c.IsPort, nationsByName[c.Nation],
                                  new Dictionary<string, double>(c.Multipliers)))
            .ToList();
    }

    /// <summary>
    /// 初始化各城库存与平衡库存：越盛产（倍率越大）库存规模越大，下限 200。
    /// 初始库存等于平衡库存，此时供需因子为 1。
    /// 库存规模要远大于商队载重（100），否则一笔交易就会把本地价格推高 / 砸低太多，
    /// 既不符合真实市场深度，也会让跨城贸易的利润被滑点吃掉。
    /// 不经营的货物也初始化一份库存：城市虽然不卖它，但玩家拿来的任何货物它都收购，
    /// 缺货倍率（0.75）下库存规模约 450，倾销量过大时同样会把价格压低。
    /// </summary>
    public static void InitializeMarkets(IReadOnlyList<City> cities, IReadOnlyList<Good> goods)
    {
        foreach (var city in cities)
        {
            foreach (var good in goods)
            {
                int equilibrium = Math.Max(200, (int)Math.Round(600 * city.MultiplierFor(good.Id)));
                city.Equilibrium[good.Id] = equilibrium;
                city.Stock[good.Id] = equilibrium;
            }
        }
    }

    /// <summary>在常见位置查找数据文件：优先程序运行目录，其次当前工作目录。</summary>
    private static string ResolveDataPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, DataRelativePath),
            Path.Combine(Directory.GetCurrentDirectory(), DataRelativePath),
        };

        foreach (string path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException(
            $"找不到游戏数据文件 {DataRelativePath}。请确认它已随程序一起发布（查找过：{string.Join("、", candidates)}）");
    }

    private static GameDataFile LoadAndValidate()
    {
        string path = ResolveDataPath();
        string json = File.ReadAllText(path);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var file = JsonSerializer.Deserialize<GameDataFile>(json, options)
                   ?? throw new InvalidDataException($"{path} 内容为空或格式不正确");

        Validate(file, path);
        return file;
    }

    /// <summary>数据完整性校验：把容易出错的地方在启动时就拦住。</summary>
    private static void Validate(GameDataFile file, string path)
    {
        if (file.Goods.Count == 0)
        {
            throw new InvalidDataException($"{path}：goods 不能为空");
        }

        if (file.Cities.Count == 0)
        {
            throw new InvalidDataException($"{path}：cities 不能为空");
        }

        if (file.Nations.Count == 0)
        {
            throw new InvalidDataException($"{path}：nations 不能为空");
        }

        // 国家：名称唯一、好感门槛从 0 开始严格递增
        foreach (var duplicate in file.Nations.GroupBy(n => n.Name).Where(n => n.Count() > 1))
        {
            throw new InvalidDataException($"{path}：国家「{duplicate.Key}」重复");
        }

        foreach (var nation in file.Nations)
        {
            if (string.IsNullOrWhiteSpace(nation.Name))
            {
                throw new InvalidDataException($"{path}：有国家缺少 name");
            }

            if (nation.FavorThresholds.Length < Nation.MaxLevel + 1)
            {
                throw new InvalidDataException(
                    $"{path}：国家「{nation.Name}」的 favorThresholds 至少需要 {Nation.MaxLevel + 1} 个元素（第 0 位固定为 0）");
            }

            var favorThresholds = nation.FavorThresholds;
            if (favorThresholds[0] != 0
                || favorThresholds.Zip(favorThresholds.Skip(1), (a, b) => b > a).Any(increasing => !increasing))
            {
                throw new InvalidDataException($"{path}：国家「{nation.Name}」的 favorThresholds 必须从 0 开始且严格递增");
            }
        }

        var nationNames = file.Nations.Select(n => n.Name).ToHashSet();

        // 商品：id 唯一、价格为正、等级合法
        foreach (var duplicate in file.Goods.GroupBy(g => g.Id).Where(g => g.Count() > 1))
        {
            throw new InvalidDataException($"{path}：商品 id「{duplicate.Key}」重复");
        }

        foreach (var good in file.Goods)
        {
            if (string.IsNullOrWhiteSpace(good.Id) || string.IsNullOrWhiteSpace(good.Name))
            {
                throw new InvalidDataException($"{path}：有商品缺少 id 或名称");
            }

            if (good.BasePrice <= 0)
            {
                throw new InvalidDataException($"{path}：商品「{good.Name}」basePrice 必须大于 0");
            }

            if (good.Tier < 1)
            {
                throw new InvalidDataException($"{path}：商品「{good.Name}」tier 必须 ≥ 1");
            }
        }

        // 城市：名称唯一、坐标必须在 Multipliers 里引用了已有的商品
        foreach (var duplicate in file.Cities.GroupBy(c => c.Name).Where(c => c.Count() > 1))
        {
            throw new InvalidDataException($"{path}：城市「{duplicate.Key}」重复");
        }

        var cityNames = file.Cities.Select(c => c.Name).ToHashSet();
        var goodIds = file.Goods.Select(g => g.Id).ToHashSet();

        foreach (var city in file.Cities)
        {
            if (string.IsNullOrWhiteSpace(city.Nation) || !nationNames.Contains(city.Nation))
            {
                throw new InvalidDataException(
                    $"{path}：城市「{city.Name}」的 nation「{city.Nation}」不是已定义的国家");
            }

            if (city.Multipliers.Count == 0)
            {
                throw new InvalidDataException($"{path}：城市「{city.Name}」没有任何经营货物");
            }

            foreach (string goodId in city.Multipliers.Keys.Where(id => !goodIds.Contains(id)))
            {
                throw new InvalidDataException($"{path}：城市「{city.Name}」引用了不存在的商品「{goodId}」");
            }

            if (city.Multipliers.Values.Any(v => v <= 0))
            {
                throw new InvalidDataException($"{path}：城市「{city.Name}」的产销倍率必须大于 0");
            }
        }

        // 每个国家至少要有 1 座城市，否则国家任务没有挂牌与交付地点
        foreach (var nation in file.Nations)
        {
            if (!file.Cities.Any(c => c.Nation == nation.Name))
            {
                throw new InvalidDataException($"{path}：国家「{nation.Name}」没有任何城市");
            }
        }

        // 产地：必须是已定义的城市，且该城确实经营这种货物（倍率大于 1 才叫盛产）
        foreach (var good in file.Goods)
        {
            foreach (string origin in good.Origins)
            {
                var city = file.Cities.FirstOrDefault(c => c.Name == origin);
                if (city == null)
                {
                    throw new InvalidDataException($"{path}：商品「{good.Name}」的产地「{origin}」不是已定义的城市");
                }

                if (!city.Multipliers.TryGetValue(good.Id, out double multiplier) || multiplier <= 1.0)
                {
                    throw new InvalidDataException(
                        $"{path}：商品「{good.Name}」的产地「{origin}」必须经营该货且倍率 > 1（当前 {multiplier:0.00}）");
                }
            }
        }

        // 产业链：分类合法、配方完整、原料存在，且舶来品只能产自口岸
        var priceById = file.Goods.ToDictionary(g => g.Id, g => g.BasePrice);
        var portNames = file.Cities.Where(c => c.IsPort).Select(c => c.Name).ToHashSet();

        foreach (var good in file.Goods)
        {
            GoodCategory category;
            try
            {
                category = GoodCategories.Parse(good.Category, good.Name);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"{path}：{ex.Message}");
            }

            if (category == GoodCategory.Raw || category == GoodCategory.Imported)
            {
                if (good.Inputs.Count > 0)
                {
                    throw new InvalidDataException(
                        $"{path}：商品「{good.Name}」是{GoodCategories.Name(category)}，不应填写 inputs");
                }
            }
            else if (good.Inputs.Count == 0)
            {
                throw new InvalidDataException(
                    $"{path}：商品「{good.Name}」是{GoodCategories.Name(category)}，必须填写 inputs 配方");
            }

            foreach (var input in good.Inputs)
            {
                if (!goodIds.Contains(input.Id))
                {
                    throw new InvalidDataException($"{path}：商品「{good.Name}」的配方引用了不存在的商品「{input.Id}」");
                }

                if (input.Id == good.Id)
                {
                    throw new InvalidDataException($"{path}：商品「{good.Name}」的配方不能引用自己");
                }

                if (input.Quantity <= 0)
                {
                    throw new InvalidDataException($"{path}：商品「{good.Name}」的配方数量必须大于 0");
                }
            }

            // 同一原料只能出现一次：重复写会让作坊采购分两笔成交、价格与预算判断失真
            foreach (var duplicate in good.Inputs.GroupBy(i => i.Id).Where(g => g.Count() > 1))
            {
                throw new InvalidDataException(
                    $"{path}：商品「{good.Name}」的配方重复引用了「{duplicate.Key}」，请合并成一条数量");
            }

            // 配方经济性：一整套输入按基础价采购的成本必须低于产出基础价，否则作坊注定亏本
            if (good.Inputs.Count > 0)
            {
                int inputCost = good.Inputs.Sum(i => priceById[i.Id] * i.Quantity);
                if (inputCost >= good.BasePrice)
                {
                    throw new InvalidDataException(
                        $"{path}：商品「{good.Name}」的配方成本 {inputCost} 不低于基础价 {good.BasePrice}，作坊会亏本");
                }
            }

            if (category == GoodCategory.Imported)
            {
                foreach (string origin in good.Origins)
                {
                    if (!portNames.Contains(origin))
                    {
                        throw new InvalidDataException(
                            $"{path}：舶来品「{good.Name}」的产地「{origin}」不是口岸城市（舶来品只能产自口岸）");
                    }
                }
            }
        }

        // 等级价格：高一级的最低价必须高于低一级的最高价，保证“升级后新货更贵”
        for (int tier = 1; tier < file.Goods.Max(g => g.Tier); tier++)
        {
            var current = file.Goods.Where(g => g.Tier == tier).ToList();
            var next = file.Goods.Where(g => g.Tier == tier + 1).ToList();
            if (current.Count == 0 || next.Count == 0)
            {
                continue;
            }

            int maxCurrent = current.Max(g => g.BasePrice);
            int minNext = next.Min(g => g.BasePrice);
            if (minNext <= maxCurrent)
            {
                throw new InvalidDataException(
                    $"{path}：Tier {tier} 最高价 {maxCurrent} 不小于 Tier {tier + 1} 最低价 {minNext}，升级后新货不一定更贵");
            }
        }

        // 平衡参数：升级门槛必须从 0 开始且递增
        var thresholds = file.Balance.CityLevelThresholds;
        if (thresholds.Length < City.MaxLevel)
        {
            throw new InvalidDataException(
                $"{path}：balance.cityLevelThresholds 至少需要 {City.MaxLevel} 个元素（第 0 位固定为 0）");
        }

        if (thresholds[0] != 0 || thresholds.Zip(thresholds.Skip(1), (a, b) => b > a).Any(increasing => !increasing))
        {
            throw new InvalidDataException($"{path}：balance.cityLevelThresholds 必须从 0 开始且严格递增");
        }
    }

    /// <summary>与 game-data.json 结构一一对应的数据模型。</summary>
    private sealed class GameDataFile
    {
        [JsonPropertyName("goods")]
        public List<GoodEntry> Goods { get; set; } = new();

        [JsonPropertyName("cities")]
        public List<CityEntry> Cities { get; set; } = new();

        [JsonPropertyName("nations")]
        public List<NationEntry> Nations { get; set; } = new();

        [JsonPropertyName("balance")]
        public BalanceEntry Balance { get; set; } = new();
    }

    private sealed class GoodEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("basePrice")]
        public int BasePrice { get; set; }

        [JsonPropertyName("tier")]
        public int Tier { get; set; }

        [JsonPropertyName("reputationTier")]
        public int ReputationTier { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; } = "raw";

        [JsonPropertyName("inputs")]
        public List<InputEntry> Inputs { get; set; } = new();

        [JsonPropertyName("origins")]
        public List<string> Origins { get; set; } = new();
    }

    private sealed class InputEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }

    private sealed class CityEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("isPort")]
        public bool IsPort { get; set; }

        [JsonPropertyName("nation")]
        public string Nation { get; set; } = "";

        [JsonPropertyName("multipliers")]
        public Dictionary<string, double> Multipliers { get; set; } = new();
    }

    private sealed class NationEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("favorThresholds")]
        public int[] FavorThresholds { get; set; } = { 0, 30, 100, 200 };
    }

    private sealed class BalanceEntry
    {
        [JsonPropertyName("cityLevelThresholds")]
        public int[] CityLevelThresholds { get; set; } = { 0, 6000, 18000 };
    }
}
