using BossTemplate;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

[InitializeOnLoad]
internal static class BossDebugToolbar
{
    private const string InvinciblePath = "Boss Arena/Invincible";
    private const string SkipPath = "Boss Arena/Skip Phase";
    private const string InvincibleMenu = "Tools/Boss Arena/무적 전환";
    private const string SkipMenu = "Tools/Boss Arena/페이즈 건너뛰기";

    static BossDebugToolbar()
    {
        EditorApplication.playModeStateChanged += _ =>
        {
            MainToolbar.Refresh(InvinciblePath);
            MainToolbar.Refresh(SkipPath);
        };
    }

    [MainToolbarElement(InvinciblePath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 14)]
    private static MainToolbarElement InvincibleButton()
    {
        var arena = InvincibilityArena();
        string label = arena && arena.Player.Health.DebugInvincible ? "무적 ON" : "무적 OFF";
        return new MainToolbarButton(new MainToolbarContent(label, "플레이어 무적 전환"), ToggleInvincibility)
        {
            enabled = arena != null
        };
    }

    [MainToolbarElement(SkipPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 15)]
    private static MainToolbarElement SkipButton()
    {
        return new MainToolbarButton(new MainToolbarContent("페이즈 건너뛰기", "현재 페이즈 즉시 종료"), SkipPhase)
        {
            enabled = ActiveArena() != null
        };
    }

    private static BossArena ActiveArena() =>
        EditorApplication.isPlaying ? Object.FindAnyObjectByType<BossArena>() : null;

    private static BossArena InvincibilityArena()
    {
        var arena = ActiveArena();
        return arena && !arena.ResearchActive ? arena : null;
    }

    [MenuItem(InvincibleMenu, false, 10)]
    private static void ToggleInvincibility()
    {
        var arena = InvincibilityArena();
        if (!arena) return;

        FighterHealth health = arena.Player.Health;
        health.DebugInvincible = !health.DebugInvincible;
        MainToolbar.Refresh(InvinciblePath);
    }

    [MenuItem(SkipMenu, false, 11)]
    private static void SkipPhase()
    {
        var arena = ActiveArena();
        if (arena) arena.SkipCurrentPhase();
    }

    [MenuItem(InvincibleMenu, true)]
    private static bool CanToggleInvincibility() => InvincibilityArena() != null;

    [MenuItem(SkipMenu, true)]
    private static bool CanSkipPhase() => ActiveArena() != null;
}
