#if DEBUG
using Hash.Api;
using System;
using HarmonyLib;
using Il2CppScheduleOne.Product.Packaging;            // PackagingDefinition

namespace RVRepairVan.Patches
{
    /// <summary>
    /// DEBUG-only test helper. Typing <c>rvtest</c> in the dev console drops a set of pre-packaged products at every
    /// quality into the inventory, so the Marco sample / quality-multiplier flow can be tested without manually
    /// equipping + setquality + packageproduct each one (packageproduct only works on the equipped-in-hand item).
    /// Usage: <c>rvtest</c> (jar OG Kush) or <c>rvtest &lt;packaging&gt; &lt;productId&gt;</c>. Compiled out of Release.
    ///
    /// Both Console.SubmitCommand overloads are patched (string + List&lt;string&gt;): the real console UI and
    /// scripted submitters call the string overload, but depending on build/config either overload may be the
    /// one whose managed prefix actually fires, so catching both is the reliable path (mirrors the Litterally /
    /// Siesta / Snitch / Hotline console patches). Dispatch dedupes per frame so a command with side effects
    /// (rvstage / rvtest) is never applied twice when both prefixes fire for a single submission.
    /// </summary>
    internal static class DebugConsolePatch
    {
        private static readonly EQuality[] Qualities =
            { EQuality.Trash, EQuality.Poor, EQuality.Standard, EQuality.Premium, EQuality.Heavenly };

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            string[] parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static int _lastFrame = -1;
        private static string _lastSig = "";

        /// <summary>Returns true if the command was one of ours (and should be swallowed), false to let the game handle it.</summary>
        /// <summary>
        /// Put these words into the game's command list, so a terminal or an autocomplete can offer them.
        ///
        /// The prefix below answers them and nothing registers them, so no list can learn they exist. hash being
        /// absent makes every call a no-op, and the entry never dispatches - the prefix still runs the line.
        /// </summary>
        internal static void DeclareForTools()
        {
            foreach (string[] one in Listing) HashCommands.Add(one[0], one[1], one[2]);
        }

        private static readonly string[][] Listing =
        {
            new[] { "rvtest", "drop pre-packaged products at every dead drop", "rvtest jar ogkush" },
            new[] { "rvstage", "skip errands while testing - run on the host", "rvstage 3" },
            new[] { "rvdrops", "dump every dead drop: name, position, empty", "rvdrops" },
            new[] { "rvclear", "clear the staged test state", "rvclear" },
            new[] { "rvgiveclient", "hand the client its half of a co-op errand", "rvgiveclient" },
            new[] { "rvdiag", "what the questline thinks is going on", "rvdiag" },
            new[] { "rvpick", "fire one dialogue choice by its rvdiag index", "rvpick marco 3" },
            new[] { "rvhold", "equip a hotbar slot, 'talk' locks equipping the way a conversation does", "rvhold 2 talk" },
            new[] { "rvreinject", "rebuild the quest objects after a reload", "rvreinject" },
            new[] { "rvprobe", "poke one step of the questline", "rvprobe" },
            new[] { "rvupg", "the van's upgrades and what they cost", "rvupg" },
            new[] { "rvrepair", "repair the van right now", "rvrepair" },
            new[] { "rvbuy", "buy an upgrade without the money", "rvbuy" },
            new[] { "rvcam", "park the camera at a fixed view of the RV, 'rvcam off' to return", "rvcam side_n" },
            new[] { "rvwreck", "wreck the RV again and restart the questline", "rvwreck" },
            new[] { "rvparts", "list the children of an RV model node with their position", "rvparts rv/Main/Interior" },
            new[] { "rvtoggle", "switch one RV model node on or off", "rvtoggle rv/Main/Interior/Dash on" },
            new[] { "rvplace", "build an item on the RV floor", "rvplace plasticpot 0" },
            new[] { "rvcrew", "every RV employee: position, locker, pay, work issues", "rvcrew" },
            new[] { "rvassign", "give RV botanists a free RV locker and the RV pots", "rvassign" },
            new[] { "rvgrid", "extent of every RV build grid in the RV's frame", "rvgrid" },
            new[] { "rvstock", "fill the first RV storage rack with soil and seeds", "rvstock" },
            new[] { "rvreach", "for every RV pot and rack: is each access point in bounds and reachable by the crew", "rvreach" },
            new[] { "rvpay", "put cash in every RV locker, the way a player pays the crew", "rvpay 500" },
            new[] { "rvpayrepair", "pay Marco for the repair the way his dialogue choice does", "rvpayrepair" },
            new[] { "rvdoor", "list the door scripts on and near the RV with the access a player gets; 'open' presses the handle", "rvdoor" },
            new[] { "rvbed", "list the RV bed's parts, colliders and every Bed script in the scene; 'use' presses it", "rvbed" },
            new[] { "rvsow","drop a seed into the first RV pot and log where it lands; 'elsewhere' uses a pot of another property", "rvsow" },
            new[] { "rvsave","where the RV's save intercom hangs; 'rvsave at x y z' moves it, 'rvsave use' presses it", "rvsave" },
            new[] { "rvdrive", "use the RV's wheel (needs the engine upgrade); 'home' snaps it back, 'info' dumps state, 'gas 1 0' drives", "rvdrive" },
        };

        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            string cmd = parts[0].ToLower();
            if (cmd != "rvtest" && cmd != "rvstage" && cmd != "rvdrops" && cmd != "rvclear"
                && cmd != "rvgiveclient" && cmd != "rvdiag" && cmd != "rvreinject" && cmd != "rvprobe"
                && cmd != "rvupg" && cmd != "rvrepair" && cmd != "rvbuy" && cmd != "rvcam" && cmd != "rvwreck"
                && cmd != "rvparts" && cmd != "rvtoggle" && cmd != "rvplace" && cmd != "rvcrew" && cmd != "rvassign" && cmd != "rvgrid" && cmd != "rvpay" && cmd != "rvstock" && cmd != "rvreach"
                && cmd != "rvpayrepair" && cmd != "rvpick" && cmd != "rvhold" && cmd != "rvdrive" && cmd != "rvsave" && cmd != "rvsow" && cmd != "rvbed" && cmd != "rvdoor")
                return false;   // not ours - let the game handle it

            // Both overloads can fire for a single submission (the string body calls the List body), so run the
            // side effects once per identical command+frame and swallow the duplicate.
            string sig = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && sig == _lastSig) return true;   // already handled this frame
            _lastFrame = frame; _lastSig = sig;

            try
            {
                switch (cmd)
                {
                    case "rvtest":
                        GiveTestProducts(parts.Length > 1 ? parts[1] : "jar", parts.Length > 2 ? parts[2] : "ogkush");
                        break;
                    case "rvstage":   // skip errands while testing - run ON THE HOST (it broadcasts to clients)
                        if (parts.Length > 1 && int.TryParse(parts[1], out int st)) RVRepairVan.Quests.Questline.DebugSetStage(st);
                        else Core.Log.Warning("[Debug] rvstage <n>  (1=Start 5=Referred 6=MarcoMet 7=ReadyToPay 8=Trusted 9=Paid). Run on the HOST.");
                        break;
                    case "rvdrops":   // dump every dead drop: name, position, empty? - to diagnose the pickup drop
                        DumpDrops();
                        break;
                    case "rvclear":   // wipe accumulated test crates/packages from ALL drops + re-reserve a fresh one
                        ClearErrandItems();
                        RVRepairVan.Quests.Questline.DebugResetErrandDrop();
                        break;
                    case "rvdiag":    // dump the quest gate + every injected dialogue choice
                        RVRepairVan.Quests.Questline.DebugDiag();
                        break;
                    case "rvhold":    // equip hotbar slot n; 'rvhold n talk' then locks equipping like a conversation does
                        if (parts.Length > 1 && int.TryParse(parts[1], out int holdSlot)) Hold(holdSlot, parts.Length > 2 && parts[2] == "talk");
                        else Core.Log.Warning("[Debug] rvhold <slot 0-8> [talk]");
                        break;
                    case "rvpick":    // fire one dialogue choice by its rvdiag index: rvpick marco 3
                        if (parts.Length > 2 && int.TryParse(parts[2], out int pick))
                            RVRepairVan.Quests.Questline.DebugPick(parts[1].ToLowerInvariant(), pick);
                        else Core.Log.Warning("[Debug] rvpick <donna|ming|marco> <index>  (index from rvdiag)");
                        break;
                    case "rvprobe":   // measure the RV property: idle points, capacity, docks, grids, NavMesh
                        RvProbe.Run();
                        break;
                    case "rvrepair":  // put the RV back together without paying Marco, so the build-outs are testable
                        if (RVRepairVan.Managers.RVManager.Repair())
                        {
                            RVRepairVan.Persistence.RepairStateStore.SetRepaired(true);
                            RVRepairVan.Persistence.RepairStateStore.SetRepairStartedAt(0);
                            Core.Log.Msg("[Debug] rvrepair: RV repaired and marked as paid for.");
                        }
                        else Core.Log.Warning("[Debug] rvrepair: repair call failed (is the RV loaded?).");
                        break;
                    case "rvbuy":     // exercise the REAL purchase path (money + cinematic), as Marco's choice does
                        if (parts.Length > 1 && int.TryParse(parts[1], out int bit))
                        {
                            float before = S1API.Money.Money.GetCashBalance();
                            RVRepairVan.Base.RvUpgrades.Buy((RVRepairVan.Base.RvUpgrade)bit);
                            Core.Log.Msg("[Debug] rvbuy " + bit + ": cash " + before + " -> " + S1API.Money.Money.GetCashBalance()
                                + ", mask=" + RVRepairVan.Base.RvUpgrades.Mask);
                        }
                        else Core.Log.Warning("[Debug] rvbuy <bit>  (1=GutInterior 2=WorkshopFloor 4=CrewQuarters 8=LoadingDock 16=Engine)");
                        break;
                    case "rvupg":     // 'rvupg' dumps the build-out state; 'rvupg <mask>' grants/revokes (host)
                        if (parts.Length > 1 && int.TryParse(parts[1], out int mask))
                            RVRepairVan.Base.RvUpgrades.DebugSetMask(mask);
                        else
                            RVRepairVan.Base.RvUpgrades.DebugDump();
                        break;
                    case "rvreinject":   // 'rvreinject degraded' exercises the no-container fallback (adds duplicates)
                        RVRepairVan.Quests.Questline.DebugReinject(parts.Length > 1 && parts[1].ToLower() == "degraded");
                        break;
                    case "rvdrive":   // 'rvdrive' = use the wheel, 'home' snaps back, 'info' dumps, 'look', 'exit', 'gas'
                        if (parts.Length > 1 && parts[1].ToLower() == "home") Base.RvMobile.DebugHome();
                        else if (parts.Length > 1 && parts[1].ToLower() == "info") Base.RvDrive.Info();
                        else if (parts.Length > 1 && parts[1].ToLower() == "lights") Base.RvDrive.Lights();
                        else if (parts.Length > 1 && parts[1].ToLower() == "rec") Base.RvDrive.Rec(parts.Length > 2 ? parts[2..] : Array.Empty<string>());
                        else if (parts.Length > 1 && parts[1].ToLower() == "tp") Base.RvDrive.Tp(parts.Length > 2 ? parts[2..] : Array.Empty<string>());
                        else if (parts.Length > 1 && parts[1].ToLower() == "hull") Base.RvDrive.HullToggle(parts.Length > 2 ? parts[2].ToLower() : "");
                        else if (parts.Length > 1 && parts[1].ToLower() == "stat") { Base.RvDrive.Stat(parts.Length > 2 ? parts[2] : "-"); Base.RvWheels.Report(); }
                        else if (parts.Length > 1 && parts[1].ToLower() == "probe") Base.RvDrive.Probe();
                        else if (parts.Length > 1 && parts[1].ToLower() == "look") Base.RvDrive.Look(parts.Length > 2 && parts[2].ToLower() == "off", parts.Length > 2 && parts[2].ToLower() == "rear");
                        else if (parts.Length > 3 && parts[1].ToLower() == "lamps" && parts[2].ToLower() == "inside") Base.RvLights.SwitchInside(parts[3].ToLower() == "on");
                        else if (parts.Length > 2 && parts[1].ToLower() == "lamps") Base.RvLights.Force(parts[2].ToLower());
                        else if (parts.Length > 2 && parts[1].ToLower() == "cam") Base.RvCamera.Debug(parts[2].ToLower());
                        else if (parts.Length > 1 && parts[1].ToLower() == "exit") Base.RvDrive.Exit();
                        else if (parts.Length > 1 && parts[1].ToLower() == "npc") Base.RvDrive.Npc(parts.Length > 2 ? parts[2].ToLower() : "");
                        else if (parts.Length > 2 && parts[1].ToLower() == "steps") Base.RvSteps.SetIn(parts[2].ToLower() == "in");
                        else if (parts.Length > 2 && parts[1].ToLower() == "gas") Base.RvDrive.Gas(parts[2].ToLower(), parts.Length > 3 ? parts[3] : "0");
                        else Base.RvMobile.DebugDrive();
                        break;
                    case "rvsave":    // report the save intercom; 'rvsave at x y z [fx fy fz]' moves it (rv/Main space)
                        Base.RvSavePoint.DebugCmd(parts.Length > 1 ? parts[1..] : Array.Empty<string>());
                        break;
                    case "rvcam":     // fixed camera views around and inside the RV, for screenshot rounds
                        if (parts.Length > 7 && parts[1].ToLower() == "at") RvCamAt(parts);
                        else RvCam(parts.Length > 1 ? parts[1].ToLower() : "");
                        break;
                    case "rvwreck":   // back to a wrecked RV and quest stage Started, to replay the questline
                        // 'rvwreck game' wrecks it the way the tutorial quest does while a save loads, and keeps the
                        // paid repair, so the restore watcher has to bring it back.
                        if (parts.Length > 1 && parts[1].ToLower() == "game")
                        {
                            if (RVRepairVan.Managers.RVManager.Destroy()) Core.Log.Msg("[Debug] rvwreck game: RV wrecked, the paid repair is kept.");
                            else Core.Log.Warning("[Debug] rvwreck game: destroy call failed (is the RV loaded?).");
                            break;
                        }
                        if (RVRepairVan.Managers.RVManager.Destroy())
                        {
                            RVRepairVan.Persistence.RepairStateStore.SetRepaired(false);
                            RVRepairVan.Quests.Questline.DebugSetStage(1);
                            Core.Log.Msg("[Debug] rvwreck: RV wrecked, stage -> Started.");
                        }
                        else Core.Log.Warning("[Debug] rvwreck: destroy call failed (is the RV loaded?).");
                        break;
                    case "rvparts":  RvDevTools.Parts(string.Join(" ", parts, 1, parts.Length - 1)); break;
                    case "rvtoggle":
                        // Node names carry spaces ("Chair (1)"), so the path is every word after the command and only a
                        // trailing on/off is read as the switch.
                        if (parts.Length > 1)
                        {
                            string last = parts[parts.Length - 1].ToLower();
                            bool hasState = parts.Length > 2 && (last == "on" || last == "off");
                            RvDevTools.Toggle(string.Join(" ", parts, 1, parts.Length - (hasState ? 2 : 1)), hasState ? last : "");
                        }
                        else Core.Log.Warning("[Debug] rvtoggle <path under the RV model> [on|off]");
                        break;
                    case "rvplace":
                        if (parts.Length > 1) RvDevTools.Place(parts[1].ToLower(), parts.Length > 2 && int.TryParse(parts[2], out int rot) ? rot : 0);
                        else Core.Log.Warning("[Debug] rvplace <itemId> [rotation]");
                        break;
                    case "rvcrew":   RvDevTools.Crew(); break;
                    case "rvassign": RvDevTools.Assign(); break;
                    case "rvgrid":   RvDevTools.GridExtent(); break;
                    case "rvstock":  RvDevTools.Stock(); break;
                    case "rvdoor":   RvDevTools.DoorInfo(parts.Length > 1 ? parts[1] : ""); break;
                    case "rvbed":    RvDevTools.BedInfo(parts.Length > 1 ? parts[1] : ""); break;
                    case "rvsow":   RvDevTools.Sow(parts.Length > 1 && parts[1] == "elsewhere"); break;
                    case "rvreach":  RvDevTools.Reach(); break;
                    case "rvpayrepair":
                        Core.Log.Msg("[Debug] rvpayrepair: HostPayRepair returned "
                            + RVRepairVan.Quests.Questline.HostPayRepair(true) + ".");
                        break;
                    case "rvpay":    RvDevTools.Pay(parts.Length > 1 && float.TryParse(parts[1], out float amount) ? amount : 500f); break;
                    case "rvgiveclient":   // host: tell every CLIENT to spawn packaged test products (jar OG Kush)
                        RVRepairVan.Net.NetworkBus.BroadcastToAll(RVRepairVan.Net.RvOp.DebugGiveItems);
                        Core.Log.Msg("[Debug] rvgiveclient: broadcast DebugGiveItems to clients (run this on the HOST).");
                        break;
                }
            }
            catch (Exception e) { Core.Log.Warning("[Debug] console cmd failed: " + e.Message); }
            return true;   // ours - swallow it either way
        }

        // Camera offsets from the RV root (world axes; the RV is static and lies along X). Eye height inside is the
        // cabin floor (y 1.08) plus a standing player's eye line.
        // The model sits 0.2 m above the root; the offsets above are from the root.
        private static readonly Vector3 ModelLift = new Vector3(0f, 0.2f, 0f);

        private static readonly (string name, Vector3 eye, Vector3 target)[] CamViews =
        {
            ("side_n",  new Vector3(0f, 2.0f, 7.0f),    new Vector3(0f, 1.4f, 0f)),
            ("side_s",  new Vector3(0f, 2.0f, -7.0f),   new Vector3(0f, 1.4f, 0f)),
            ("end_e",   new Vector3(8.5f, 2.2f, 0.5f),  new Vector3(0f, 1.4f, 0f)),
            ("end_w",   new Vector3(-8.5f, 2.2f, 0.5f), new Vector3(0f, 1.4f, 0f)),
            ("corner",  new Vector3(6.5f, 3.5f, 6.5f),  new Vector3(0f, 1.2f, 0f)),
            ("top",     new Vector3(0.5f, 14f, 0.5f),   new Vector3(0f, 0f, 0f)),
            ("in_e",    new Vector3(2.8f, 2.6f, 0.2f),  new Vector3(-3.0f, 1.6f, -0.6f)),
            ("in_w",    new Vector3(-2.6f, 2.6f, -0.6f), new Vector3(3.0f, 1.6f, 0.4f)),
            ("in_floor", new Vector3(0.2f, 3.4f, -0.4f), new Vector3(0.2f, 1.08f, -0.5f)),
        };

        private static void RvCam(string view)
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null) { Core.Log.Warning("[Debug] rvcam: no PlayerCamera."); return; }
            if (view == "off")
            {
                cam.StopTransformOverride(0f, true, true);
                Core.Log.Msg("[Debug] rvcam: camera back on the player.");
                return;
            }
            Transform root = RVRepairVan.Managers.RVManager.Root;
            if (root == null) { Core.Log.Warning("[Debug] rvcam: RV root not found."); return; }
            foreach (var v in CamViews)
            {
                if (v.name != view) continue;
                // Relative to the model, so the views follow the RV when it is driven off its spot.
                Transform model = RVRepairVan.Managers.RVManager.Model;
                Vector3 eye = model != null ? model.TransformPoint(v.eye - ModelLift) : root.position + v.eye;
                Vector3 at = model != null ? model.TransformPoint(v.target - ModelLift) : root.position + v.target;
                cam.OverrideTransform(eye, Quaternion.LookRotation(at - eye), 0f, false);
                Core.Log.Msg("[Debug] rvcam " + view + ": eye=" + eye + " looking at " + at);
                return;
            }
            string names = "";
            foreach (var v in CamViews) names += " " + v.name;
            Core.Log.Warning("[Debug] rvcam <view|off>, views:" + names);
        }

        // rvcam at ex ey ez tx ty tz - a free view in the same frame as the named views.
        private static void RvCamAt(string[] parts)
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            Transform model = RVRepairVan.Managers.RVManager.Model;
            if (cam == null || model == null) { Core.Log.Warning("[Debug] rvcam at: no camera or RV model."); return; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(int i) => float.Parse(parts[i], ci);
            Vector3 eye = model.TransformPoint(new Vector3(F(2), F(3), F(4)) - ModelLift);
            Vector3 at = model.TransformPoint(new Vector3(F(5), F(6), F(7)) - ModelLift);
            cam.OverrideTransform(eye, Quaternion.LookRotation(at - eye), 0f, false);
            Core.Log.Msg("[Debug] rvcam at: eye=" + eye + " looking at " + at);
        }

        internal static void ClearErrandItems()
        {
            try
            {
                var all = S1API.DeadDrops.DeadDropManager.All;
                if (all == null) { Core.Log.Msg("[Debug] rvclear: no drops."); return; }
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    n += all[i].Storage.RemoveAllOfDefinition("rv_ming_crate");
                    n += all[i].Storage.RemoveAllOfDefinition("rv_marco_package");
                }
                Core.Log.Msg("[Debug] rvclear: removed " + n + " quest item(s) from all drops.");
            }
            catch (Exception e) { Core.Log.Warning("[Debug] rvclear failed: " + e.Message); }
        }

        // Equip a hotbar slot without a key press. 'talk' then disables equipping, which is what the dialogue state
        // does: the hand empties and the slot index moves to PriorEquippedSlotIndex.
        private static void Hold(int slot, bool talk)
        {
            PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null) { Core.Log.Warning("[Debug] rvhold: no PlayerInventory."); return; }
            inv.SetEquippingEnabled(true);
            inv.Equip(inv.IndexAllSlots(slot));
            if (talk) inv.SetEquippingEnabled(false);
            Core.Log.Msg("[Debug] rvhold: equipped=" + inv.EquippedSlotIndex + " prior=" + inv.PriorEquippedSlotIndex
                + " item=" + (inv.IndexAllSlots(slot)?.ItemInstance?.Name ?? "<empty>") + (talk ? " (talking)" : ""));
        }

        internal static void DumpDrops()
        {
            try
            {
                var all = S1API.DeadDrops.DeadDropManager.All;
                if (all == null) { Core.Log.Msg("[Debug] rvdrops: DeadDropManager.All is null."); return; }
                Core.Log.Msg("[Debug] rvdrops: " + all.Length + " dead drops (empty=no items):");
                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    if (d == null) continue;
                    Core.Log.Msg("  [" + i + "] '" + d.Name + "' pos=(" + Mathf.RoundToInt(d.Position.x)
                        + "," + Mathf.RoundToInt(d.Position.z) + ") empty=" + d.IsEmpty);
                }
            }
            catch (Exception e) { Core.Log.Warning("[Debug] rvdrops failed: " + e.Message); }
        }

        internal static void GiveTestProducts(string packagingId, string productId)
        {
            PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
            if (inv == null) { Core.Log.Warning("[Debug] rvtest: no PlayerInventory."); return; }

            ProductDefinition def = Il2CppScheduleOne.Registry.GetItem(productId.ToLower())?.TryCast<ProductDefinition>();
            PackagingDefinition pkg = Il2CppScheduleOne.Registry.GetItem(packagingId.ToLower())?.TryCast<PackagingDefinition>();
            if (def == null) { Core.Log.Warning("[Debug] rvtest: unknown product '" + productId + "'."); return; }
            if (pkg == null) { Core.Log.Warning("[Debug] rvtest: unknown packaging '" + packagingId + "'."); return; }

            int given = 0;
            foreach (EQuality q in Qualities)
            {
                ProductItemInstance inst = def.GetDefaultInstance(1)?.TryCast<ProductItemInstance>();
                if (inst == null) continue;
                inst.Quality = q;
                inst.SetPackaging(pkg);   // the exact call the 'packageproduct' console command uses
                if (!inv.CanItemFitInInventory(inst)) { Core.Log.Warning("[Debug] rvtest: inventory full at " + q + "."); break; }
                inv.AddItemToInventory(inst);
                given++;
            }
            Core.Log.Msg("[Debug] rvtest: gave " + given + " packaged " + def.Name + " (" + pkg.Name + ") - one per quality.");
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(string) })]
    internal static class Rv_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args)
        {
            try { return !DebugConsolePatch.TryHandle(args); } catch (Exception e) { Core.Swallowed("DebugConsolePatch.SubmitCommand(string)", e); return true; }
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Rv_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
        {
            try { return !DebugConsolePatch.TryHandle(args); } catch (Exception e) { Core.Swallowed("DebugConsolePatch.SubmitCommand(List)", e); return true; }
        }
    }
}
#endif
