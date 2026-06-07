// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog

using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace ChronoLog.Capture;

/// <summary>
/// Reads the primary enemy's HP straight from the object table each tick. No hooks.
/// "Primary enemy" is the targetable hostile BattleNpc with the largest max HP, which
/// in raid content is the boss. Tracks the lowest fraction reached for best-pull stats.
/// </summary>
public sealed class BossHpReader
{
    public bool HasBoss { get; private set; }
    public string BossName { get; private set; } = string.Empty;
    public ulong BossObjectId { get; private set; }

    public float CurrentHpFraction { get; private set; } = 1f;
    public float LowestHpFraction { get; private set; } = 1f;

    /// <summary>
    /// Most recent HP reading strictly above zero. Used as a fallback when LowestHpFraction
    /// is 0 on a wipe, which happens when the boss is scripted to 0 HP as part of an enrage
    /// sequence or phase transition (targetable for one tick at 0 HP before despawning).
    /// </summary>
    public float LastNonZeroHpFraction { get; private set; } = 1f;

    public bool IsBossTargetable { get; private set; }
    public bool IsCasting { get; private set; }
    public uint CurrentCastId { get; private set; }

    public void Reset()
    {
        HasBoss = false;
        BossName = string.Empty;
        BossObjectId = 0;
        CurrentHpFraction = 1f;
        LowestHpFraction = 1f;
        LastNonZeroHpFraction = 1f;
        IsBossTargetable = false;
        IsCasting = false;
        CurrentCastId = 0;
    }

    public void Tick()
    {
        IBattleChara? boss = null;
        uint bestMaxHp = 0;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IBattleNpc npc) continue;
            if (npc.BattleNpcKind != BattleNpcSubKind.Combatant) continue;
            if (npc.MaxHp == 0) continue;
            if (!npc.IsTargetable) continue;

            if (npc.MaxHp > bestMaxHp)
            {
                bestMaxHp = npc.MaxHp;
                boss = npc;
            }
        }

        if (boss == null)
        {
            HasBoss = false;
            IsBossTargetable = false;
            IsCasting = false;
            CurrentCastId = 0;
            return;
        }

        HasBoss = true;
        BossName = boss.Name.TextValue;
        BossObjectId = boss.GameObjectId;
        IsBossTargetable = boss.IsTargetable;

        CurrentHpFraction = boss.MaxHp == 0 ? 1f : Math.Clamp((float)boss.CurrentHp / boss.MaxHp, 0f, 1f);
        if (CurrentHpFraction < LowestHpFraction)
            LowestHpFraction = CurrentHpFraction;
        if (CurrentHpFraction > 0f)
            LastNonZeroHpFraction = CurrentHpFraction;

        IsCasting = boss.IsCasting;
        CurrentCastId = boss.IsCasting ? boss.CastActionId : 0;
    }
}
