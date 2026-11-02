using System;
using System.Collections.Generic;

namespace TradeGame.Events;

/// <summary>
/// 游戏事件标记接口：所有可发布给外部订阅的事件都实现它。
/// 事件层不依赖界面，便于未来迁到 Godot 时按同名事件逐一接线。
/// </summary>
public interface IGameEvent
{
}

/// <summary>
/// 轻量同步事件总线：发布方只管广播，订阅方自行决定要做什么。
/// 全部操作都在游戏逻辑线程上完成，不做线程安全处理。
/// 派发时遍历处理器快照，允许订阅方在回调里退订而不打乱本次派发。
/// </summary>
public sealed class GameEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    /// <summary>订阅某类事件；同一处理器重复订阅会被忽略。</summary>
    public void Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        List<Delegate> list = GetOrCreateList<T>();
        if (!list.Contains(handler))
        {
            list.Add(handler);
        }
    }

    /// <summary>取消订阅；未订阅过时什么也不做。</summary>
    public void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_handlers.TryGetValue(typeof(T), out List<Delegate>? list))
        {
            list.Remove(handler);
        }
    }

    /// <summary>广播事件；没有任何订阅者时直接返回。</summary>
    public void Publish<T>(T gameEvent) where T : IGameEvent
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list) || list.Count == 0)
        {
            return;
        }

        // 快照派发：回调里 Subscribe/Unsubscribe 不会修改正在遍历的集合
        Delegate[] snapshot = list.ToArray();
        foreach (Delegate handler in snapshot)
        {
            ((Action<T>)handler)(gameEvent);
        }
    }

    private List<Delegate> GetOrCreateList<T>() where T : IGameEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out List<Delegate>? list))
        {
            list = new List<Delegate>();
            _handlers[typeof(T)] = list;
        }

        return list;
    }
}
