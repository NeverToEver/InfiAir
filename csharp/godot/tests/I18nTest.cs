using System.Threading.Tasks;
using Godot;

namespace InfiAir.Tests;

/// <summary>
/// i18n 测试：locale 切换生效、14 个 key 中英对照、profile 往返、HUD 英文刷新、缺 key 回退。
/// </summary>
public partial class I18nTest : Node
{
    private int _failures;

    private void Check(bool cond, string label)
    {
        if (cond)
        {
            GD.Print("[PASS] " + label);
        }
        else
        {
            _failures++;
            GD.PushError("[FAIL] " + label);
        }
    }

    public override void _Ready()
    {
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var gs = GetNode<GameState>("/root/GameState");
            gs.DeleteSave();
            // L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
            var origHighScore = gs.HighScore;
            gs.HighScore = 0;
            gs.SetLocale("zh");

            // 1. 默认中文
            Check(gs.Locale == "zh", "默认语言 zh");
            Check(TranslationServer.Translate("UI_SCORE") == "分数：%d", "zh 列生效");

            // 2. 切换英文
            gs.SetLocale("en");
            Check(gs.Locale == "en", "set_locale 切换 en");
            Check(TranslationServer.Translate("UI_SCORE") == "Score: %d", "en 列生效");

            // 3. 抽查 key 中英对照（均非 key 本身且互不相同；2026-08-04 扩 welcome/排行榜键）
            string[] keys =
            {
                "UI_SCORE",
                "BUFF_POWER_SHOT_NAME",
                "BASE_TITLE",
                "GO_TITLE",
                "PAUSE_TITLE",
                "START_CONTINUE",
                "SET_CONTROLS",
                "ACT_DASH",
                "TUT_S1_TITLE",
                "WARN_BOSS",
                "WELCOME_LOGIN",
                "WELCOME_GUEST_CONFIRM",
                "WELCOME_MSG_BAD_CRED",
                "LEAD_TITLE",
            };
            var allOk = true;
            foreach (var k in keys)
            {
                gs.SetLocale("zh");
                var zhText = TranslationServer.Translate(k);
                gs.SetLocale("en");
                var enText = TranslationServer.Translate(k);
                if (zhText == k || enText == k || zhText == enText)
                {
                    allOk = false;
                    GD.PushError($"  key 异常: {k} zh={zhText} en={enText}");
                }
            }
            Check(allOk, "14 个 key 中英对照齐全");

            // 4. profile 往返
            gs.SetLocale("en");
            gs.Locale = "zh";  // 内存改回，验证读档覆盖
            gs.LoadProfile();
            Check(gs.Locale == "en", "locale 从 profile 恢复 en");
            TranslationServer.SetLocale(gs.Locale);

            // 5. HUD 刷新：en 下分数标签显示 Score
            AddChild(GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate());
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var scoreLabel = GetNode<Label>("Main/HUD/ScoreLabel");
            Check(scoreLabel.Text.StartsWith("Score"), "HUD 分数标签 en 刷新");
            gs.SetLocale("zh");
            Check(scoreLabel.Text.StartsWith("分数"), "HUD 分数标签切回 zh 刷新");

            // 6. 缺失 key 回退 key 名本身
            Check(TranslationServer.Translate("I18N_NO_SUCH_KEY") == "I18N_NO_SUCH_KEY", "缺失 key 回退 key 名");

            // 收尾：恢复 zh 并落盘
            gs.SetLocale("zh");

            // L15：还原用户最高分并落盘（收尾不污染用户 profile）
            gs.HighScore = origHighScore;
            gs.SaveProfile();
        }
        catch (System.Exception e)
        {
            _failures++;
            GD.PushError($"I18N TEST 异常: {e}");
        }
        finally
        {
            GD.Print($"I18N TEST DONE, failures = {_failures}");
            TestExit.Quit(_failures);
        }
    }
}
