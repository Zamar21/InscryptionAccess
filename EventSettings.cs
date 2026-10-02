// EventSettings.cs
using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace IKMA
{
    /// <summary>
    /// The kinds of announcement IKMA makes WITHOUT a keypress, each of which
    /// the player can switch. (Session 37, MASTER_PLAN M9.)
    ///
    /// Zamar, Session 37: "Match their implementation and choices instead of
    /// asking me for my opinion. I defer all of our starting design decisions
    /// to their implementation." - Say the Spire 2. So this is their
    /// Settings/EventRegistry.cs, carried over: every event has "Announce"
    /// and "Add to buffer" (here "Add to history", because IKMA's buffer is
    /// its review history), and events that can come from either side of the
    /// table carry a Sources sub-list ("Current Player", "Enemies") offering
    /// only the sides the game actually shows. A source switched off drops
    /// the line entirely - not spoken and not kept - exactly as their
    /// EventDispatcher.Flush does.
    ///
    /// Their event list, mapped onto Inscryption (their word first):
    ///   Death -> Death;  HP Changes -> HP Changes (card damage, the scales);
    ///   Enemy Moves -> Enemy Moves;  Turns -> Turns;  Powers -> Powers
    ///   (sigils);  Card Drawn, Card Played, Card Obtained -> the same;
    ///   Gold -> Teeth;  Orbs -> Bones;  Potion Obtained / Used -> Item
    ///   Obtained / Used (the game's own words win);  Dialogue -> Dialogue;
    ///   Room Entered -> Node Entered.
    /// Inscryption lines with no event of theirs keep IKMA's own work under
    /// the same two switches: Attacks, Your Turn, Challenges, Bosses,
    /// Node Results, Saving. Those labels are PROVISIONAL.
    ///
    /// Their defaults: everything announced and kept, except Dialogue is not
    /// kept, and the current player's own Card Played and Potion Used are
    /// off (the source toggle, so those lines are neither spoken nor kept).
    ///
    /// Lines that are not tagged (reads the player asked for, prompts, idle
    /// prompts, mod messages) are not events and behave exactly as before.
    /// How a call site tags a line: Speech.Event(...) around the Speech call.
    /// </summary>
    internal enum EventKind
    {
        None,
        // Combat
        Death, HpChanges, EnemyMoves, Turns, Powers,
        Attacks, TurnStartReads, Challenges, Bosses,
        // Cards
        CardDrawn, CardPlayed, CardObtained,
        // Resources
        Teeth, Bones, ItemObtained, ItemUsed,
        // Other
        Dialogue, NodeEntered, NodeResults, Saving,
    }

    /// <summary>Who caused (or suffered) the event. Any = not filtered.</summary>
    internal enum EventSource { Any, CurrentPlayer, Enemies }

    internal sealed class EventTag
    {
        internal readonly EventKind Kind;
        internal readonly EventSource Source;
        internal EventTag(EventKind kind, EventSource source) { Kind = kind; Source = source; }

        internal static EventSource Side(bool isPlayer)
            => isPlayer ? EventSource.CurrentPlayer : EventSource.Enemies;

        /// <summary>The side a card is on, read now. Any when it cannot be read.</summary>
        internal static EventSource Side(DiskCardGame.PlayableCard card)
        {
            try { return card == null ? EventSource.Any : Side(!card.OpponentCard); }
            catch { return EventSource.Any; }
        }

        /// <summary>
        /// A damage line that may fold in the card's death (DamageDeathMerger):
        /// a Death once it has died, HP Changes otherwise, decided when the
        /// line is composed.
        /// </summary>
        internal static Func<EventTag> DamageOrDeath(DamageRecord record, DiskCardGame.PlayableCard card)
        {
            var side = Side(card);
            return () => new EventTag(record != null && record.Died ? EventKind.Death : EventKind.HpChanges, side);
        }
    }

    internal sealed class EventDef
    {
        internal EventKind Kind;
        internal string Key;        // config key, never spoken
        internal string Group;      // Combat / Cards / Resources / Other
        internal string Label;
        internal bool DefaultAnnounce = true;
        internal bool DefaultHistory = true;

        // Sources, as theirs: only the sides the game shows.
        internal bool AllowCurrentPlayer;
        internal bool AllowEnemies;
        internal bool DefaultCurrentPlayer = true;
        internal bool DefaultEnemies = true;

        // Turns: their Player Turn Start / Enemy Turn Start are direct
        // toggles in the event's own list, not a Sources sub-list. Same
        // mechanism underneath, different labels and placement.
        internal bool SourcesAreDirect;
        internal string CurrentPlayerLabel;
        internal string EnemiesLabel;

        internal ConfigEntry<bool> Announce;
        internal ConfigEntry<bool> History;
        internal ConfigEntry<bool> CurrentPlayer;
        internal ConfigEntry<bool> Enemies;

        internal bool HasSources => AllowCurrentPlayer || AllowEnemies;
    }

    internal static class EventSettings
    {
        internal static readonly List<EventDef> All = new List<EventDef>
        {
            // Combat
            new EventDef { Kind = EventKind.Death,      Key = "Death",      Group = "Combat", Label = "Death",       AllowCurrentPlayer = true, AllowEnemies = true },
            new EventDef { Kind = EventKind.HpChanges,  Key = "HPChanges",  Group = "Combat", Label = "HP Changes",  AllowCurrentPlayer = true, AllowEnemies = true },
            new EventDef { Kind = EventKind.EnemyMoves, Key = "EnemyMoves", Group = "Combat", Label = "Enemy Moves", AllowEnemies = true },
            new EventDef { Kind = EventKind.Turns,      Key = "Turns",      Group = "Combat", Label = "Turns",
                           AllowCurrentPlayer = true, AllowEnemies = true, SourcesAreDirect = true,
                           CurrentPlayerLabel = "Player Turn Start", EnemiesLabel = "Enemy Turn Start" },
            new EventDef { Kind = EventKind.Powers,     Key = "Powers",     Group = "Combat", Label = "Powers",      AllowCurrentPlayer = true, AllowEnemies = true },
            // IKMA's own (PROVISIONAL labels)
            new EventDef { Kind = EventKind.Attacks,        Key = "Attacks",        Group = "Combat", Label = "Attacks", AllowCurrentPlayer = true, AllowEnemies = true },
            new EventDef { Kind = EventKind.TurnStartReads, Key = "TurnStartReads", Group = "Combat", Label = "Your Turn" },
            new EventDef { Kind = EventKind.Challenges,     Key = "Challenges",     Group = "Combat", Label = "Challenges and Totems" },
            new EventDef { Kind = EventKind.Bosses,         Key = "Bosses",         Group = "Combat", Label = "Bosses" },

            // Cards
            new EventDef { Kind = EventKind.CardDrawn,    Key = "CardDrawn",    Group = "Cards", Label = "Card Drawn" },
            new EventDef { Kind = EventKind.CardPlayed,   Key = "CardPlayed",   Group = "Cards", Label = "Card Played",   AllowCurrentPlayer = true, DefaultCurrentPlayer = false },
            new EventDef { Kind = EventKind.CardObtained, Key = "CardObtained", Group = "Cards", Label = "Card Obtained", AllowCurrentPlayer = true },

            // Resources
            new EventDef { Kind = EventKind.Teeth,        Key = "Teeth",        Group = "Resources", Label = "Teeth",         AllowCurrentPlayer = true },
            new EventDef { Kind = EventKind.Bones,        Key = "Bones",        Group = "Resources", Label = "Bones",         AllowCurrentPlayer = true },
            new EventDef { Kind = EventKind.ItemObtained, Key = "ItemObtained", Group = "Resources", Label = "Item Obtained", AllowCurrentPlayer = true },
            new EventDef { Kind = EventKind.ItemUsed,     Key = "ItemUsed",     Group = "Resources", Label = "Item Used",     AllowCurrentPlayer = true, DefaultCurrentPlayer = false },

            // Other
            new EventDef { Kind = EventKind.Dialogue,    Key = "Dialogue",    Group = "Other", Label = "Dialogue" },   // Session 38, Zamar: dialogue kept by default (theirs: not kept)
            new EventDef { Kind = EventKind.NodeEntered, Key = "NodeEntered", Group = "Other", Label = "Node Entered" },
            new EventDef { Kind = EventKind.NodeResults, Key = "NodeResults", Group = "Other", Label = "Node Results" },
            new EventDef { Kind = EventKind.Saving,      Key = "Saving",      Group = "Other", Label = "Saving" },
        };

        private static readonly Dictionary<EventKind, EventDef> _byKind = new Dictionary<EventKind, EventDef>();

        internal static void BindConfig(ConfigFile config)
        {
            foreach (var d in All)
            {
                string section = "Events." + d.Key;
                d.Announce = config.Bind(section, "Announce", d.DefaultAnnounce, "Speak this event when it happens.");
                d.History  = config.Bind(section, "AddToHistory", d.DefaultHistory, "Keep this event in the review history.");
                if (d.AllowCurrentPlayer)
                    d.CurrentPlayer = config.Bind(section, "CurrentPlayer", d.DefaultCurrentPlayer,
                        d.SourcesAreDirect ? "Player Turn Start." : "Events caused by (or happening to) you.");
                if (d.AllowEnemies)
                    d.Enemies = config.Bind(section, "Enemies", d.DefaultEnemies,
                        d.SourcesAreDirect ? "Enemy Turn Start." : "Events caused by (or happening to) the enemy.");
                _byKind[d.Kind] = d;
            }
        }

        internal static EventDef Get(EventKind kind)
        {
            EventDef d;
            return _byKind.TryGetValue(kind, out d) ? d : null;
        }

        /// <summary>
        /// What happens to a tagged line: dropped entirely (a source switched
        /// off), spoken or not, kept in history or not. An untagged line, or
        /// any failure, answers "as before": spoken, and kept when
        /// <paramref name="keptBefore"/>.
        /// </summary>
        internal static void Decide(EventTag tag, bool keptBefore, out bool drop, out bool speak, out bool keep)
        {
            drop = false; speak = true; keep = keptBefore;
            if (tag == null || tag.Kind == EventKind.None) return;
            var d = Get(tag.Kind);
            if (d == null || d.Announce == null) return;

            if (tag.Source == EventSource.CurrentPlayer)
            {
                if (!d.AllowCurrentPlayer || (d.CurrentPlayer != null && !d.CurrentPlayer.Value)) { drop = true; speak = false; keep = false; return; }
            }
            else if (tag.Source == EventSource.Enemies)
            {
                if (!d.AllowEnemies || (d.Enemies != null && !d.Enemies.Value)) { drop = true; speak = false; keep = false; return; }
            }

            speak = d.Announce.Value;
            keep  = d.History.Value;
        }

        internal static string Describe(EventTag tag)
        {
            if (tag == null) return "untagged";
            var d = Get(tag.Kind);
            return (d?.Label ?? tag.Kind.ToString()) + (tag.Source == EventSource.Any ? "" : " / " + tag.Source);
        }
    }
}
// EventSettings.cs
