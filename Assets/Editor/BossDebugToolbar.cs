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
        var arena = ActiveArena();
        string label = arena && arena.Player.Health.DebugInvincible ? "무적 ON" : "무적 OFF";
        return new MainToolbarButton(new MainToolbarContent(label, "플레이어 무적 전환"), ToggleInvincibility)
        {
            enabled = EditorApplication.isPlaying
        };
    }

    [MainToolbarElement(SkipPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 15)]
    private static MainToolbarElement SkipButton()
    {
        return new MainToolbarButton(new MainToolbarContent("페이즈 건너뛰기", "현재 페이즈 즉시 종료"), SkipPhase)
        {
            enabled = EditorApplication.isPlaying
        };
    }

    private static BossArena ActiveArena() =>
        EditorApplication.isPlaying ? Object.FindAnyObjectByType<BossArena>() : null;

    [MenuItem(InvincibleMenu, false, 10)]
    private static void ToggleInvincibility()
    {
        var arena = ActiveArena();
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
    [MenuItem(SkipMenu, true)]
    private static bool CanUseDebugMenu() => ActiveArena() != null;
}
