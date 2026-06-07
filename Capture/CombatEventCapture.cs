// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using LuminaAction = Lumina.Excel.Sheets.Action;
using Vector3 = System.Numerics.Vector3;

namespace ChronoLog.Capture;

/// <summary>
/// The precise layer. Hooks the game's action-effect handler (resolved through the
/// FFXIVClientStructs-maintained address, so it survives patches better than a hand-kept
/// signature) and records the most recent action that targeted each object. When a player
/// dies, that last action is almost always the lethal blow, which gives the death a cause.
///
/// Only the stable Header fields and the parallel target-id array are read; no fixed-buffer
/// effect parsing, which keeps the hook simple and less likely to break on patches. If the
/// hook can't be set up the plugin degrades cleanly: deaths are still detected by HP, just
/// without a named cause.
/// </summary>
public sealed unsafe class CombatEventCapture : IDisposable
{
    private Hook<ActionEffectHandler.Delegates.Receive>? hook;

    private readonly Dictionary<ulong, (uint actionId, DateTime utc)> lastHit = new();

    public bool Active => hook?.IsEnabled ?? false;

    /// <summary>Fired for every action processed, with its action id. Used for phase detection.</summary>
    public event Action<uint>? ActionObserved;

    public void Enable()
    {
        try
        {
            var address = (nint)ActionEffectHandler.Addresses.Receive.Value;
            if (address == 0)
            {
                Plugin.Log.Warning("ActionEffect address unresolved; death-cause capture disabled.");
                return;
            }

            hook = Plugin.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(address, Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not hook ActionEffect; death-cause capture disabled.");
        }
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
        lastHit.Clear();
    }

    /// <summary>Drop the per-target history at the start of a fresh pull.</summary>
    public void Clear() => lastHit.Clear();

    private void Detour(
        uint casterEntityId,
        Character* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        try
        {
            if (header != null)
            {
                var actionId = header->ActionId;
                ActionObserved?.Invoke(actionId);

                if (targetEntityIds != null)
                {
                    int count = header->NumTargets;
                    if (count > 0 && count <= 64)
                    {
                        var now = DateTime.UtcNow;
                        for (int i = 0; i < count; i++)
                        {
                            ulong tid = targetEntityIds[i].Id;
                            if (tid != 0 && tid != 0xE0000000)
                                lastHit[tid] = (actionId, now);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ActionEffect detour parse threw");
        }

        hook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    /// <summary>
    /// Name of the most recent action that hit the given object within the last few seconds,
    /// or null if nothing recent is on record (or the hook is inactive).
    /// </summary>
    public string? GetCause(ulong objectId)
    {
        if (!lastHit.TryGetValue(objectId, out var rec))
            return null;
        if ((DateTime.UtcNow - rec.utc).TotalSeconds > 8)
            return null;
        return ResolveActionName(rec.actionId);
    }

    private static string? ResolveActionName(uint actionId)
    {
        if (actionId == 0)
            return null;
        try
        {
            var sheet = Plugin.DataManager.GetExcelSheet<LuminaAction>();
            var name = sheet?.GetRowOrDefault(actionId)?.Name.ToString();
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
