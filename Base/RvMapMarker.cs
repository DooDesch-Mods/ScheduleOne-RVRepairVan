using DooDesch.Localization;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The RV on the phone map. While someone drives it shows the game's vehicle marker on the chassis; parked it shows
    /// the RV property's own marker, which rides on the chassis away from home. <see cref="RvMobile"/> keeps the chassis
    /// out of the player-owned vehicles, so the game switches the chassis marker off; each tick sets it back. Each peer
    /// runs this for its own map.
    /// </summary>
    internal static class RvMapMarker
    {
        private static POI _property;
        private static IntPtr _ownedIconsOn = IntPtr.Zero;
        private static int _shown = -1;   // -1 unknown, 0 parked marker, 1 vehicle marker
        private static bool _iconsWarned;

        /// <summary>The RV property's map marker, carried with the chassis.</summary>
        internal static void Bind(Transform poi)
        {
            _property = poi != null ? poi.GetComponent<POI>() : null;
            _ownedIconsOn = IntPtr.Zero;
            _shown = -1;
            if (_property == null) Core.Log.Warning("[Map] the RV property has no map marker - parked, the RV shows no marker away from home.");
        }

        internal static void Update(LandVehicle chassis, bool driving)
        {
            POI vehicle = chassis != null ? chassis.POI : null;
            if (vehicle != null)
            {
                if (driving)
                {
                    string text = L10n.T("RV");
                    if (vehicle.MainText != text) vehicle.SetMainText(text);
                }
                if (vehicle.enabled != driving) vehicle.enabled = driving;
            }
            if (_property != null)
            {
                if (_property.enabled == driving) _property.enabled = !driving;
                if (!driving) MarkOwned();
            }

            int shown = driving ? 1 : 0;
            if (shown == _shown) return;
            _shown = shown;
            if (driving && vehicle == null) Core.Log.Warning("[Map] the chassis has no vehicle marker - the RV shows no marker while driving.");
            else Core.Log.Msg("[Map] RV marker: " + (driving ? "vehicle (driving)" : "property (parked)") + ".");
        }

        /// <summary>The RV is back on its spot without a chassis: only the property marker stays, as in the base game.</summary>
        internal static void Release(LandVehicle chassis)
        {
            POI vehicle = chassis != null ? chassis.POI : null;
            if (vehicle != null) vehicle.enabled = false;
            if (_property != null)
            {
                _property.enabled = true;
                MarkOwned();
            }
            _property = null;
            _shown = -1;
            Core.Log.Msg("[Map] RV marker: property at home.");
        }

#if DEBUG
        /// <summary>rvmap: both markers' state in the log; "open" takes the phone out on the map, centred on the RV;
        /// "close" puts the phone away.</summary>
        internal static void Debug(string arg)
        {
            var menu = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.GameplayMenu>.Instance;
            if (arg == "close")
            {
                if (menu == null) { Core.Log.Warning("[Map] rvmap close: no gameplay menu."); return; }
                if (menu.IsOpen) menu.Close();
                Core.Log.Msg("[Map] rvmap close: phone put away.");
                return;
            }
            bool open = arg == "open";
            LandVehicle chassis = RvMobile.Chassis;
            POI vehicle = chassis != null ? chassis.POI : null;
            POI property = _property;
            if (property == null)
            {
                var pm = Il2CppScheduleOne.Property.PropertyManager.Instance;
                var rv = pm != null ? pm.GetProperty("rv") : null;
                property = rv != null ? rv.PoI : null;
            }
            Core.Log.Msg("[Map] rvmap: chassis " + (chassis != null ? "yes" : "none") + ", driving " + RvMobile.Driving
                + " | vehicle marker " + Describe(vehicle) + " | property marker " + Describe(property));
            if (!open) return;
            var map = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.UI.Phone.Map.MapApp>.Instance;
            if (menu == null || map == null) { Core.Log.Warning("[Map] rvmap open: no gameplay menu or map app."); return; }
            if (!menu.IsOpen) menu.Open();
            menu.SetScreen(Il2CppScheduleOne.UI.GameplayMenu.EGameplayScreen.Phone);
            map.SetOpen(true);
            POI shown = vehicle != null && vehicle.enabled ? vehicle : property;
            if (shown != null && shown.UI != null) { shown.UpdatePosition(); map.FocusPosition(shown.UI.anchoredPosition); }
            Core.Log.Msg("[Map] rvmap open: phone on the map" + (shown != null && shown.UI != null ? ", centred on the RV." : "; no RV marker to centre on."));
        }

        private static string Describe(POI poi)
        {
            if (poi == null) return "none";
            string icon = "-";
            if (poi.IconContainer != null)
            {
                Transform owned = poi.IconContainer.Find("Owned");
                if (owned != null) icon = owned.gameObject.activeSelf ? "owned" : "unowned";
            }
            return (poi.enabled && poi.gameObject.activeInHierarchy ? "on" : "off") + " '" + (poi.MainText ?? "").Replace("\n", " ")
                + "' ui=" + (poi.UI != null) + " icon=" + icon + " at " + poi.transform.position.ToString("F0");
        }
#endif

        // A marker switched back on builds its map item fresh from the prefab, with the unowned icon. The property sets
        // the owned icon only once, after the save loads, so it is set again on each new map item.
        private static void MarkOwned()
        {
            if (!_property.gameObject.activeInHierarchy || !_property.UISetup || _property.UI == null || _property.IconContainer == null) return;
            if (_property.UI.Pointer == _ownedIconsOn) return;
            Transform owned = _property.IconContainer.Find("Owned");
            Transform unowned = _property.IconContainer.Find("Unowned");
            if (owned == null || unowned == null)
            {
                if (!_iconsWarned) Core.Log.Warning("[Map] the RV marker has no Owned/Unowned icon - it keeps the prefab's icon.");
                _iconsWarned = true;
                _ownedIconsOn = _property.UI.Pointer;
                return;
            }
            unowned.gameObject.SetActive(false);
            owned.gameObject.SetActive(true);
            _ownedIconsOn = _property.UI.Pointer;
        }
    }
}
