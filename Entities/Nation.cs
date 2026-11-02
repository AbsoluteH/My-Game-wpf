using System;
using System.Collections.Generic;
using System.Linq;

namespace TradeGame.Entities;

/// <summary>
/// 国家：一组城市的政治归属，管控玩家与该国的「国家好感」。
/// 与城市声望并列：城市声望是本地信誉，国家好感是政治关系，全国共享。
/// 好感等级决定能接哪一档国家任务，门槛由 Data/game-data.json 的
/// nations.favorThresholds 配置，默认 { 0, 30, 100, 200 }。
/// </summary>
public sealed class Nation
{
    /// <summary>国家好感最高等级。</summary>
    public const int MaxLevel = 3;

    /// <summary>升到各等级所需的累计好感：索引 = 等级，所以 Lv1 需 30、Lv2 需 100、Lv3 需 200。</summary>
    private readonly int[] _favorThresholds;

    public Nation(string name, IReadOnlyList<int> favorThresholds)
    {
        Name = name;
        _favorThresholds = favorThresholds.Take(MaxLevel + 1).ToArray();
    }

    public string Name { get; }

    /// <summary>升到指定等级（1~3）所需的累计好感。</summary>
    public int FavorThresholdFor(int level) => _favorThresholds[Math.Clamp(level, 1, MaxLevel)];

    /// <summary>累计好感对应的好感等级 0~3；0 级表示尚未与该国建立关系。</summary>
    public int LevelFor(int favor)
    {
        int level = 0;
        while (level < MaxLevel && favor >= _favorThresholds[level + 1])
        {
            level++;
        }

        return level;
    }
}
