// Copyright (C) 2026 AemiliusXIV -- https://github.com/AemiliusXIV/ChronoLog
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;

namespace ChronoLog.Phases;

/// <summary>Authored phase data for one fight: the opening phase name and the abilities
/// that mark entry into each later phase.</summary>
public sealed class FightPhases
{
    public required string Phase1Name { get; init; }

    /// <summary>Boss ability id (decimal) -> formal name of the phase it belongs to.</summary>
    public required Dictionary<uint, string> Transitions { get; init; }
}

/// <summary>
/// Per-fight phase tables keyed by territory id. Ability ids are cross-checked against
/// cactbot's raidboss data (Apache-2.0; see NOTICE); the names are the game's own phase
/// names. Detection runs off the ActionEffect hook so instant transitions are caught.
///
/// Coverage target: all Ultimates plus multi-phase Savage. Ability ids need live
/// verification on first run per fight â€” the hook fires on the right event but some
/// trigger types (StartsUsing vs Ability) may differ from what cactbot uses.
/// </summary>
public static class PhaseTables
{
    private static readonly Dictionary<uint, FightPhases> Tables = new()
    {
        // The Unending Coil of Bahamut (Ultimate) - territory 733
        // Trios are scripted in fixed order inside Bahamut Prime; each fires a unique StartsUsing.
        [733] = new FightPhases
        {
            Phase1Name = "Twintania",
            Transitions = new Dictionary<uint, string>
            {
                [9921]  = "Nael deus Darnus",                  // 0x26C1 Dalamud Dive (first Nael cast)
                [9937]  = "Bahamut Prime",                     // 0x26D1 Seventh Umbral Era
                [9954]  = "Bahamut Prime - Quickmarch Trio",  // 0x26E2
                [9955]  = "Bahamut Prime - Blackfire Trio",   // 0x26E3
                [9956]  = "Bahamut Prime - Fellruin Trio",    // 0x26E4
                [9957]  = "Bahamut Prime - Heavensfall Trio", // 0x26E5
                [9958]  = "Bahamut Prime - Tenstrike Trio",   // 0x26E6
                [9959]  = "Bahamut Prime - Grand Octet",      // 0x26E7
                [9961]  = "Golden Bahamut",                    // 0x26E9 Teraflare
            },
        },

        // The Weapon's Refrain (Ultimate) - territory 777
        // Ultima Weapon phase tracked through to sub-mechanics; Primal Roulette has no
        // distinct phase trigger so Suppression remains the last named phase.
        [777] = new FightPhases
        {
            Phase1Name = "Garuda",
            Transitions = new Dictionary<uint, string>
            {
                [11103] = "Ifrit",                                      // 0x2B5F Crimson Cyclone
                [11517] = "Titan",                                      // 0x2CFD Geocrush
                [11147] = "The Ultima Weapon",                          // 0x2B8B Ultima (tank-LB prompt)
                [11126] = "The Ultima Weapon - Ultimate Predation",     // 0x2B76
                [11596] = "The Ultima Weapon - Ultimate Annihilation",  // 0x2D4C
                [11597] = "The Ultima Weapon - Ultimate Suppression",   // 0x2D4D
            },
        },

        // The Epic of Alexander (Ultimate) - territory 887
        // Alexander Prime's Inception and Wormhole sections merged into one phase;
        // Perfect Alexander's two projections likewise merged.
        [887] = new FightPhases
        {
            Phase1Name = "Living Liquid",
            Transitions = new Dictionary<uint, string>
            {
                [18494] = "Cruise Chaser & Brute Justice", // 0x483E Judgment Nisi
                [18543] = "Alexander Prime",               // 0x486F Inception Formation
                [18555] = "Perfect Alexander",             // 0x487B Fate Projection Î±
            },
        },

        // Dragonsong's Reprise (Ultimate) - territory 968
        [968] = new FightPhases
        {
            Phase1Name = "Adelphel, Grinnaux & Charibert",
            Transitions = new Dictionary<uint, string>
            {
                [25544] = "Thordan",                // 0x63C8 Ascalon's Mercy Concealed
                [26376] = "Nidhogg",                // 0x6708 Final Chorus
                [25314] = "Eyes",                   // 0x62E2 Spear of the Fury
                [27526] = "King Thordan",           // 0x6B86 Incarnation
                [26215] = "Hraesvelgr & Nidhogg",  // 0x6667
                [29156] = "Dragon-King Thordan",    // 0x71E4 Shockwave
            },
        },

        // The Omega Protocol (Ultimate) - territory 1122
        // P3/P4/P5-Delta transitions are Omega self-casts with no public ability name;
        // IDs confirmed as cactbot timeline sync anchors.
        [1122] = new FightPhases
        {
            Phase1Name = "Omega",
            Transitions = new Dictionary<uint, string>
            {
                [31552] = "Omega-M & Omega-F",       // 0x7B40 Firewall
                [31507] = "Omega Reconfigured",      // 0x7B13 self-cast sync
                [31559] = "Blue Screen",             // 0x7B47 self-cast sync
                [31612] = "Run: Dynamis (Delta)",    // 0x7B7C self-cast sync
                [32788] = "Run: Dynamis (Sigma)",    // 0x8014
                [32789] = "Run: Dynamis (Omega)",    // 0x8015
                [32626] = "Alpha Omega",             // 0x7F72 Blind Faith
            },
        },

        // Futures Rewritten (Ultimate) - territory 1238
        // Flat five-phase view; P2/P3 sub-sections not tracked.
        // Note: 0x9D36 (Materialization, P4) has a lower id than 0x9D49 (Hell's Judgment, P3)
        // but fires later in the scripted sequence â€” lookup order does not matter here.
        [1238] = new FightPhases
        {
            Phase1Name = "Fatebreaker",
            Transitions = new Dictionary<uint, string>
            {
                [40191] = "Usurper of Frost",                       // 0x9CFF Quadruple Slap
                [40265] = "Oracle of Darkness",                     // 0x9D49 Hell's Judgment
                [40246] = "Usurper of Frost & Oracle of Darkness",  // 0x9D36 Materialization
                [40306] = "Pandora",                                // 0x9D72 Fulgent Blade
            },
        },

        // Dancing Mad (Ultimate) - territory 1363 (patch 7.51)
        // Three-phase fight; trigger data is from early post-release cactbot sets.
        // Verify both ids on first live run.
        [1363] = new FightPhases
        {
            Phase1Name = "Kefka",
            Transitions = new Dictionary<uint, string>
            {
                [49740] = "God Kefka",        // 0xC24C Ultimate Embrace
                [50167] = "Chaos & Exdeath",  // 0xC3F7 Aero III Assault
            },
        },

        // â”€â”€ Multi-phase Savage â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // AAC Light-heavyweight M4S (Wicked Thunder) - territory 1232
        // Cross Tail Switch intermission is not tracked; phase advances directly to the
        // first Sabbath mechanic so the label is useful the moment P2 content begins.
        // Ion Cluster (0x9622) fires slightly before Sunrise Sabbath starts â€” cactbot
        // uses it as the trigger because the debuffs pre-date the Sabbath cast bar.
        [1232] = new FightPhases
        {
            Phase1Name = "Wicked Thunder",
            Transitions = new Dictionary<uint, string>
            {
                [38435] = "Wicked Thunder - Twilight Sabbath",  // 0x9623
                [38434] = "Wicked Thunder - Sunrise Sabbath",   // 0x9622 Ion Cluster
                [39609] = "Wicked Thunder - Midnight Sabbath",  // 0x9AB9
            },
        },

        // AAC Cruiserweight M4S (Howling Blade) - territory 1263
        // 0xA82D is an Ability-type event (no cast bar) that cactbot uses as the P2 gate;
        // ActionEffectHandler catches it correctly since it is an action-effect packet.
        [1263] = new FightPhases
        {
            Phase1Name = "Howling Blade",
            Transitions = new Dictionary<uint, string>
            {
                [43053] = "Howling Blade (Phase 2)",  // 0xA82D Down for the Count
            },
        },

        // AAC Heavyweight M4S (Lindwurm) - territory 1327
        // Two P1 sub-phases tracked; 0xB4D8 Replication (first cast) marks the P2 gate.
        // Note: 0xBEC0 (Curtain Call) has a higher id than 0xB4C6 (Slaughtershed) but
        // fires first in the scripted sequence â€” lookup order is independent of id magnitude.
        [1327] = new FightPhases
        {
            Phase1Name = "Lindwurm",
            Transitions = new Dictionary<uint, string>
            {
                [48832] = "Lindwurm - Curtain Call",  // 0xBEC0 Grotesquerie: Curtain Call
                [46278] = "Lindwurm - Slaughtershed", // 0xB4C6
                [46296] = "Lindwurm (Phase 2)",       // 0xB4D8 Replication (first cast)
            },
        },
    };

    public static FightPhases? For(uint territoryId) =>
        Tables.TryGetValue(territoryId, out var fight) ? fight : null;
}
