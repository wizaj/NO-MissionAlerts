using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NOMissionAlerts
{
    /// <summary>
    /// Reroutes story/mission text to a centre-screen alert overlay.
    ///
    /// Mission text flows through MissionMessages.ShowMessgeLocal (game's own
    /// typo) -> GameplayUI.GameMessage -> the small message feed box. The
    /// killfeed is a separate channel entirely (MessageManager.RpcKillMessage
    /// -> MessageUI.KillFeed), so intercepting here cannot affect it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.nomissionalerts";
        public const string PluginName = "Mission Alerts";
        public const string PluginVersion = "0.3.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> HideFromFeed;
        internal static ConfigEntry<int> FontSize;
        internal static ConfigEntry<float> BaseSeconds;
        internal static ConfigEntry<float> PerCharSeconds;
        internal static ConfigEntry<float> CoalesceSeconds;
        internal static ConfigEntry<float> VerticalAnchor;

        // Both are non-public on MissionMessages, hence reflection.
        private static MethodInfo isLocalFaction;
        private static MethodInfo playSound;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Show mission/story text as a centre-screen alert.");

            HideFromFeed = Config.Bind("General", "HideFromFeed", true,
                "Remove mission text from the small message box (true = move, " +
                "false = show in both places). Kill feed is never affected.");

            FontSize = Config.Bind("Style", "FontSize", 26,
                new ConfigDescription("Alert text size.", new AcceptableValueRange<int>(12, 60)));

            VerticalAnchor = Config.Bind("Style", "VerticalAnchor", 0.32f,
                new ConfigDescription(
                    "Vertical position of the alert as a fraction of screen height " +
                    "(0 = top, 0.5 = dead centre).",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            BaseSeconds = Config.Bind("Timing", "BaseSeconds", 8f,
                new ConfigDescription("Minimum time an alert stays up.",
                    new AcceptableValueRange<float>(1f, 30f)));

            PerCharSeconds = Config.Bind("Timing", "PerCharSeconds", 0.08f,
                new ConfigDescription("Extra display time per character, for longer texts.",
                    new AcceptableValueRange<float>(0f, 0.4f)));

            CoalesceSeconds = Config.Bind("Timing", "CoalesceSeconds", 1.5f,
                new ConfigDescription(
                    "Messages arriving within this many seconds of the previous one are " +
                    "merged into the same alert as extra lines. Mission designers often " +
                    "script a story paragraph as several back-to-back message outcomes; " +
                    "the vanilla feed shows them together, so this restores that look. " +
                    "0 still merges same-instant bursts.",
                    new AcceptableValueRange<float>(0f, 10f)));

            isLocalFaction = AccessTools.Method(typeof(MissionMessages), "IsLocalFaction");
            playSound = AccessTools.Method(typeof(MissionMessages), "PlaySound");

            MethodInfo target = AccessTools.Method(typeof(MissionMessages), "ShowMessgeLocal");
            if (target == null)
            {
                Log.LogError("MissionMessages.ShowMessgeLocal not found — mod inert. " +
                             "The game update likely renamed it (it was a typo, after all).");
                return;
            }

            var harmony = new Harmony(PluginGuid);
            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(Plugin), nameof(ShowMessagePrefix))));

            var host = new GameObject("NOMissionAlerts_Host");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<AlertOverlay>();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Mission text rerouted to centre screen " +
                        $"(HideFromFeed={HideFromFeed.Value}).");
        }

        private static bool ShowMessagePrefix(string message, FactionHQ filterFaction)
        {
            if (!Enabled.Value || string.IsNullOrEmpty(message)) return true;

            try
            {
                // Same gate the original applies first: ignore texts meant for
                // another faction. Letting the original run preserves its
                // behaviour exactly for those.
                if (isLocalFaction != null && !(bool)isLocalFaction.Invoke(null, new object[] { filterFaction }))
                    return true;

                AlertOverlay.Push(message);

                // Sound is owned by the overlay (played whenever the displayed
                // text changes), regardless of the original playsound flag.
                return !HideFromFeed.Value;
            }
            catch (Exception e)
            {
                Log.LogWarning($"Alert reroute failed, deferring to the game: {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// Plays the game's own mission-alert sound. PlaySound is instance-level
        /// and non-public; the singleton and its own same-frame dedupe make this
        /// safe to call once per displayed message.
        /// </summary>
        internal static void PlayAlertSound()
        {
            try
            {
                MissionMessages instance = NetworkSceneSingleton<MissionMessages>.i;
                if (instance != null && playSound != null)
                    playSound.Invoke(instance, null);
            }
            catch (Exception e)
            {
                Log.LogWarning($"Could not play alert sound: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Centre-screen alert renderer. Shows one alert at a time from a queue,
    /// duration scaled by length, fading out over the last half second.
    /// IMGUI richText handles the basic markup (b/i/color/size) that mission
    /// designers use in TMP strings.
    ///
    /// Messages arriving in a burst (within CoalesceSeconds of each other) are
    /// merged into one alert as extra lines. Mission designers script story
    /// paragraphs as several back-to-back ShowMessage outcomes; the vanilla
    /// feed accumulates them so they read as one paragraph, and without
    /// merging the one-at-a-time overlay would stretch that paragraph into
    /// minutes of sequential alerts.
    /// </summary>
    internal class AlertOverlay : MonoBehaviour
    {
        private struct Entry
        {
            public string Text;
            public float Arrived;
        }

        private static readonly Queue<Entry> Pending = new Queue<Entry>();

        private string current;
        private float showUntil;
        private float lastMergeArrival;
        private GUIStyle style;

        public static void Push(string message) =>
            Pending.Enqueue(new Entry { Text = message, Arrived = Time.unscaledTime });

        private void Update()
        {
            if (current != null && Time.unscaledTime >= showUntil)
                current = null;

            if (current == null && Pending.Count > 0)
            {
                Entry first = Pending.Dequeue();
                current = first.Text;
                lastMergeArrival = first.Arrived;
                showUntil = Time.unscaledTime
                            + Plugin.BaseSeconds.Value
                            + first.Text.Length * Plugin.PerCharSeconds.Value;
                Plugin.PlayAlertSound();
            }

            // Fold the rest of a burst into the visible alert. Compares arrival
            // times, not display time, so a burst queued behind a long-running
            // earlier alert still merges once it gets its turn — while a message
            // arriving long after the current alert started shows separately.
            while (current != null && Pending.Count > 0
                   && Pending.Peek().Arrived - lastMergeArrival <= Plugin.CoalesceSeconds.Value)
            {
                Entry next = Pending.Dequeue();
                current += "\n" + next.Text;
                lastMergeArrival = next.Arrived;
                showUntil += next.Text.Length * Plugin.PerCharSeconds.Value;
            }
        }

        private void OnGUI()
        {
            if (current == null) return;

            if (style == null)
            {
                style = new GUIStyle
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = true,
                    richText = true,
                };
            }
            style.fontSize = Plugin.FontSize.Value;

            float alpha = Mathf.Clamp01((showUntil - Time.unscaledTime) / 0.5f);
            float width = Screen.width * 0.6f;
            var rect = new Rect((Screen.width - width) / 2f,
                                Screen.height * Plugin.VerticalAnchor.Value,
                                width, Screen.height * 0.4f);

            style.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), current, style);
            style.normal.textColor = new Color(1f, 0.85f, 0.35f, alpha);
            GUI.Label(rect, current, style);
        }
    }
}
