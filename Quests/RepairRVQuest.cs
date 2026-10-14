using System;
using System.Reflection;
using DooDesch.Localization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RVRepairVan.Managers;
using S1API.Quests;

namespace RVRepairVan.Quests
{
    /// <summary>
    /// The "Back on the Road" quest (auto-started by the questline), built on S1API's Quest
    /// wrapper (handles the game's quest object, journal + HUD, and per-save persistence automatically).
    ///
    /// Lifecycle (driven by S1API): constructor builds the underlying quest object ->
    /// the game's Quest.Start fires -> S1API calls CreateInternal -> OnCreated (entries
    /// added here) -> InitializeQuest -> Begin (if AutoBegin) -> shows in journal/HUD.
    /// </summary>
    public class RepairRVQuest : Quest
    {
        protected override string Title => RepairQuest.Title;

        protected override string Description =>
            L10n.T("Your RV's wrecked. Someone in Hyland Point has to know a guy.");

        protected override bool AutoBegin => true;

        // A wrench icon (a PNG embedded in this DLL) instead of S1API's default phone-contacts icon.
        private static Sprite _icon;
        private static bool _iconTried;
        protected override Sprite QuestIcon
        {
            get
            {
                if (!_iconTried) { _iconTried = true; _icon = LoadIcon(); }
                return _icon;
            }
        }

        private static Sprite LoadIcon()
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RVRepairVan.quest_icon.png"))
                {
                    if (s == null) { Core.Log.Warning("[Quest] icon resource missing"); return null; }
                    byte[] bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                    ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes, false);
                    return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }
            catch (Exception e) { Core.Log.Warning("[Quest] icon load failed: " + e.Message); return null; }
        }

        protected override void OnCreated()
        {
            base.OnCreated();
            try
            {
                // Initial objective = the first real step (NOT the same text as the quest title, or the
                // journal shows it twice). The questline immediately re-syncs this to the current stage.
                QuestEntry entry = AddEntry(L10n.T("Ask Donna at the motel about your RV"));

                // The game's QuestEntry.Start() registers a compass element from PoILocation. S1API leaves
                // PoILocation null, and on this build (0.4.5f2) CompassManager then dereferences null every
                // frame (the "0m flicker"). Fix: give the entry a VALID fixed POI - the RV's own position as
                // a neutral default; the questline's SyncEntry then points it at the right NPC per stage.
                entry.POIPosition = RVManager.TryGetPosition(out Vector3 rvPos) ? rvPos : Vector3.zero;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Quest] OnCreated failed: " + e.Message);
            }
        }
    }
}
