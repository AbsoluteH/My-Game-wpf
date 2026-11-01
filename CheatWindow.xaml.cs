using System.Windows;
using System.Windows.Controls;

namespace TradeGame;

/// <summary>
/// 作弊台：调试用的金币修改面板。可以设为指定金额、按差额增减，或一键加固定数额。
/// 所有改动立即写入游戏状态并显示在窗口顶部；关闭后主界面由调用方刷新。
/// </summary>
public partial class CheatWindow : Window
{
    private readonly TradeGameEngine _game;

    public CheatWindow(TradeGameEngine game)
    {
        InitializeComponent();

        _game = game;
        AmountBox.Text = _game.Gold.ToString();
        RefreshGoldText();
    }

    /// <summary>刷新顶部的当前金币与输入框内容。</summary>
    private void RefreshGoldText()
    {
        GoldText.Text = $"当前金币：{_game.Gold}";
        AmountBox.Text = _game.Gold.ToString();
    }

    /// <summary>读取输入框里的金额；非法输入给出提示并返回 false。</summary>
    private bool TryReadAmount(out int amount)
    {
        amount = 0;

        if (!int.TryParse(AmountBox.Text.Trim(), out int parsed))
        {
            StatusText.Text = "请输入整数金额。";
            return false;
        }

        if (parsed < 0)
        {
            StatusText.Text = "金额不能为负数。";
            return false;
        }

        amount = parsed;
        return true;
    }

    /// <summary>直接把金币设为输入框里的金额。</summary>
    private void SetGoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadAmount(out int amount))
        {
            return;
        }

        int before = _game.Gold;
        int after = _game.SetGold(amount);
        StatusText.Text = $"金币 {before} → {after}";
        RefreshGoldText();
    }

    /// <summary>在现有金币上增加输入框里的金额。</summary>
    private void AddGoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadAmount(out int amount))
        {
            return;
        }

        int before = _game.Gold;
        int after = _game.SetGold(before + amount);
        StatusText.Text = $"金币 {before} → {after}";
        RefreshGoldText();
    }

    /// <summary>从现有金币里扣除输入框里的金额（不会变成负数）。</summary>
    private void SubtractGoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadAmount(out int amount))
        {
            return;
        }

        int before = _game.Gold;
        int after = _game.SetGold(before - amount);
        StatusText.Text = $"金币 {before} → {after}";
        RefreshGoldText();
    }

    /// <summary>快捷加钱：金额取按钮的 Tag（+10000 / +1000），不依赖输入框。</summary>
    private void QuickAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out int amount))
        {
            return;
        }

        int before = _game.Gold;
        int after = _game.SetGold(before + amount);
        StatusText.Text = $"金币 {before} → {after}";
        RefreshGoldText();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
