// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using ChronoLog.Model;

namespace ChronoLog.Windows;

/// <summary>
/// Compact floating button, styled to blend with the FFXIV native UI.
/// Only visible when the player is inside an instanced duty.
/// Drag it to reposition; ImGui persists the position automatically.
/// </summary>
public sealed class MarkOverlayWindow : Window
{
    // FFXIV-palette colors: dark near-black bg, muted blue-grey border, warm gold when active.
    private static readonly Vector4 BgColor        = new(0.04f, 0.04f, 0.08f, 0.82f);
    private static readonly Vector4 BorderColor    = new(0.28f, 0.28f, 0.45f, 0.75f);
    private static readonly Vector4 BtnColor       = new(0.14f, 0.14f, 0.22f, 1.00f);
    private static readonly Vector4 BtnHover       = new(0.24f, 0.24f, 0.38f, 1.00f);
    private static readonly Vector4 BtnActive      = new(0.10f, 0.10f, 0.18f, 1.00f);
    private static readonly Vector4 BtnMarked      = new(0.50f, 0.38f, 0.06f, 1.00f);
    private static readonly Vector4 BtnMarkedHover = new(0.62f, 0.48f, 0.10f, 1.00f);
    private static readonly Vector4 BtnMarkedAct   = new(0.38f, 0.28f, 0.04f, 1.00f);

    private readonly Plugin plugin;

    public MarkOverlayWindow(Plugin plugin)
        : base("##cl_mark_overlay",
               ImGuiWindowFlags.NoTitleBar
               | ImGuiWindowFlags.AlwaysAutoResize
               | ImGuiWindowFlags.NoScrollbar
               | ImGuiWindowFlags.NoScrollWithMouse
               | ImGuiWindowFlags.NoNav
               | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
    }

    public override bool DrawConditions()
        => plugin.Config.ShowMarkOverlay
        && Plugin.Condition[ConditionFlag.BoundByDuty];

    public override void PreDraw()
    {
        // Default to the lower-right corner (near the native server-info clock) on first use.
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(
            new Vector2(vp.WorkPos.X + vp.WorkSize.X - 140f,
                        vp.WorkPos.Y + vp.WorkSize.Y - 54f),
            ImGuiCond.FirstUseEver);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,  new Vector2(8f, 5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,  1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,   new Vector2(6f, 3f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, BgColor);
        ImGui.PushStyleColor(ImGuiCol.Border,   BorderColor);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(4);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        var session = plugin.DutyTracker.Session;
        PullEntry? target = session?.Current;
        if (target == null && session?.Pulls.Count > 0)
            target = session.Pulls[^1];

        if (target == null)
        {
            ImGui.TextDisabled("ChronoLog");
            return;
        }

        var isActive = session?.Current != null;
        var isMarked = target.IsMarked;

        // Fixed-width label so the button doesn't jump when toggling.
        var label = isMarked ? "★ Unmark" : "★ Mark  ";

        if (isMarked)
        {
            ImGui.PushStyleColor(ImGuiCol.Button,        BtnMarked);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnMarkedHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  BtnMarkedAct);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button,        BtnColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BtnHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive,  BtnActive);
        }

        if (ImGui.Button(label, new Vector2(88f, 0f)))
            plugin.MarkCurrentOrLast();

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            var tip = isActive
                ? (isMarked ? "Remove mark from current pull" : "Mark the current pull")
                : (isMarked ? "Remove mark from last pull" : "Mark the last pull");
            ImGui.SetTooltip(tip);
        }
    }
}
