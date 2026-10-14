using System;
using System.Collections;
using DooDesch.Localization;
using Il2CppInterop.Runtime;
using RVRepairVan.Config;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;
using RVRepairVan.Quests;
using UnityEngine.Events;

namespace RVRepairVan.Dialogue
{
    /// <summary>
    /// Injects a "Repair my RV (&lt;price&gt;)" choice into the mechanic Marco's dialogue.
    /// Marco (Il2CppScheduleOne.NPCs.CharacterClasses.Marco) is found at runtime;
    /// the choice is only shown while the RV is destroyed, and charges the player
    /// the configured price before repairing.
    /// </summary>
    internal static class MarcoRepairDialogue
    {
        private static bool _injected;
        private static DialogueController.DialogueChoice _repairChoice;

        // Marco sells the same build-outs in Simple mode; only the repair itself skips the story.
        private static readonly Base.RvUpgrade[] UpgradeOrder =
        {
            Base.RvUpgrade.GutInterior, Base.RvUpgrade.WorkshopFloor,
            Base.RvUpgrade.CrewQuarters, Base.RvUpgrade.LoadingDock,
        };
        private static readonly DialogueController.DialogueChoice[] _upgradeChoices =
            new DialogueController.DialogueChoice[4];

        internal static void Reset()
        {
            _injected = false;
            _repairChoice = null;
            Array.Clear(_upgradeChoices, 0, _upgradeChoices.Length);
        }

        /// <summary>Update the visible choice labels to the current prices (live, no restart).</summary>
        internal static void RefreshPrice()
        {
            try
            {
                if (_repairChoice != null)
                {
                    _repairChoice.ChoiceText = BuildChoiceText();
                }
                for (int i = 0; i < _upgradeChoices.Length && i < UpgradeOrder.Length; i++)
                    if (_upgradeChoices[i] != null) _upgradeChoices[i].ChoiceText = Base.RvUpgrades.ChoiceText(UpgradeOrder[i]);
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Marco] price refresh failed: " + e.Message);
            }
        }

        /// <summary>
        /// Retry-injects the choice until Marco has spawned (NPCs load late). The choice is
        /// hidden until the RV is destroyed, so injecting early on a fresh game is harmless;
        /// the generous window ensures it is in place before the RV can ever be wrecked.
        /// </summary>
        internal static IEnumerator SetupCoroutine()
        {
            for (int attempt = 0; attempt < 20 && !_injected; attempt++)
            {
                yield return new WaitForSeconds(2f);
                TryInject();
            }

            if (!_injected)
            {
                Core.Log.Warning("[Marco] Could not inject RV repair dialogue (Marco not found within ~40s).");
            }
        }

        private static void TryInject()
        {
            try
            {
                Marco marco = FindMarco();
                if (marco == null)
                {
                    return;
                }

                DialogueController dc = FindController(marco);
                if (dc == null)
                {
                    return;
                }

                DialogueController.DialogueChoice choice = new DialogueController.DialogueChoice
                {
                    Enabled = true,
                    ChoiceText = BuildChoiceText(),
                    Conversation = null,
                    Priority = 100
                };

                // Only show the choice while the RV is actually destroyed.
                Func<bool, bool> shouldShow = (enabled) => enabled && RVManager.IsDestroyed();
                choice.shouldShowCheck =
                    DelegateSupport.ConvertDelegate<DialogueController.DialogueChoice.ShouldShowCheck>(shouldShow);

                choice.onChoosen = new UnityEvent();
                choice.onChoosen.AddListener((Action)OnRepairChosen);

                dc.AddDialogueChoice(choice, 100);
                _repairChoice = choice;

                // The build-outs are sold in Simple mode too - only the REPAIR is simple here, not the RV.
                for (int u = 0; u < UpgradeOrder.Length; u++)
                {
                    Base.RvUpgrade up = UpgradeOrder[u];
                    var uc = new DialogueController.DialogueChoice
                    {
                        Enabled = true,
                        ChoiceText = Base.RvUpgrades.ChoiceText(up),
                        Conversation = null,
                        Priority = 93 - u
                    };
                    Func<bool, bool> show = (_) => { try { return Base.RvUpgrades.Available(up); } catch (Exception e) { Core.Swallowed("MarcoRepairDialogue.UpgradeShowCheck", e); return false; } };
                    uc.shouldShowCheck = DelegateSupport.ConvertDelegate<DialogueController.DialogueChoice.ShouldShowCheck>(show);
                    uc.onChoosen = new UnityEvent();
                    uc.onChoosen.AddListener((Action)(() => Base.RvUpgrades.Buy(up)));
                    dc.AddDialogueChoice(uc, 93 - u);
                    _upgradeChoices[u] = uc;
                }

                _injected = true;
                Core.Log.Msg("[Marco] RV repair dialogue injected.");
            }
            catch (Exception e)
            {
                Core.Log.Warning("[Marco] inject attempt failed: " + e.Message);
            }
        }

        private static Marco FindMarco()
        {
            var marcos = UnityEngine.Object.FindObjectsOfType<Marco>();
            if (marcos == null || marcos.Length == 0)
            {
                return null;
            }
            return marcos[0];
        }

        private static DialogueController FindController(Marco marco)
        {
            try
            {
                DialogueHandler dh = ((NPC)marco).DialogueHandler;
                if (dh != null)
                {
                    DialogueController dc = ((Component)dh).GetComponentInChildren<DialogueController>();
                    if (dc != null)
                    {
                        return dc;
                    }
                }
            }
            catch (Exception e) { Core.Swallowed("MarcoRepairDialogue.FindController.direct", e); /* fall through */ }

            try
            {
                return ((Component)marco).GetComponentInChildren<DialogueController>();
            }
            catch (Exception e)
            {
                Core.Swallowed("MarcoRepairDialogue.FindController.children", e);
                return null;
            }
        }

        private static string BuildChoiceText()
        {
            return L10n.T("Repair my RV ({0})", MoneyManager.FormatAmount(RVRepairVanPreferences.RepairPrice));
        }

        // Floating worldspace bubble at Marco (his completion line after the repair cinematic).
        private static void WorldSay(NPC npc, string line)
        {
            try
            {
                if (npc != null) npc.SendWorldSpaceDialogue(line, 5f);
            }
            catch (Exception e) { Core.Swallowed("MarcoRepairDialogue.WorldSay", e); /* worldspace lines are nice-to-have */ }
        }

        private static void OnRepairChosen()
        {
            try
            {
                if (!RVRepairVanPreferences.Enabled)
                {
                    return;
                }

                if (!RVManager.IsDestroyed())
                {
                    Core.Log.Msg("[Marco] RV is not destroyed - nothing to repair.");
                    return;
                }

                float price = RVRepairVanPreferences.RepairPrice;

                // Money via S1API (backend-agnostic wrapper) instead of direct interop.
                if (S1API.Money.Money.GetCashBalance() < price)
                {
                    Core.Log.Msg("[Marco] Not enough cash for repair (" + MoneyManager.FormatAmount(price) + ").");
                    return;
                }

                Marco marco = FindMarco();

                // Co-op CLIENT: the repair (RV state + persistence) is host-authoritative; the money is this player's
                // own cash, taken here and refunded if the host refuses. The host's RepairApplied swaps the RV.
                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    int ticket = ClientPayment.Charge((int)price, "repair");
                    if (ticket == 0) return;
                    NetworkBus.SendToHost(RvOp.PayRepair, 0, ticket, (int)price);
                    RVRepairVan.Effects.RepairCinematic.Play(
                        null,
                        () => WorldSay(marco, L10n.T("There she is - back from the dead. Try not to total her again.")),
                        () => Questline.GruntNpc(marco));
                    return;
                }

                // Co-op HOST: authoritative repair WITH the cinematic (the host player is acting).
                if (NetworkBus.IsServer) { Questline.HostPayRepair(true); return; }

                // Offline single-player: unchanged. Commit payment synchronously (so an interrupted cinematic can
                // never double-charge), then play the repair cinematic - parity with the questline path.
                S1API.Money.Money.ChangeCashBalance(-price, true, true);
                float paid = price;
                RVRepairVan.Effects.RepairCinematic.Play(
                    () =>   // at the darkest point, with the swap hidden
                    {
                        if (RVManager.Repair())
                        {
                            RepairStateStore.SetRepaired(true);
                            RepairQuest.CompleteIfActive();
                            Core.Log.Msg("[Marco] RV repaired for " + MoneyManager.FormatAmount(paid) + ".");
                        }
                    },
                    () => WorldSay(marco, L10n.T("There she is - back from the dead. Try not to total her again.")),
                    () => Questline.GruntNpc(marco));   // mid-repair: Marco hurts himself
            }
            catch (Exception e)
            {
                Core.Log.Error("[Marco] repair failed: " + e);
            }
        }
    }
}
