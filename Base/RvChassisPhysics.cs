using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The moment the chassis turns from kinematic to dynamic on this peer: on a client, when that client takes the wheel.
    /// The game runs this check on every vehicle each physics step, so anything but the chassis returns on a pointer
    /// compare.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(LandVehicle), nameof(LandVehicle.UpdatePhysicallySimulated))]
    internal static class RvChassisPhysicsPatch
    {
        private static void Prefix(LandVehicle __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (__instance.Pointer != RvMobile.ChassisPtr || RvMobile.ChassisPtr == IntPtr.Zero) return;
                Rigidbody rb = __instance.Rb;
                __state = rb != null && rb.isKinematic;
            }
            catch (Exception e) { Core.Log.Error("[Mobile] reading the chassis' physics state failed: " + e); }
        }

        private static void Postfix(LandVehicle __instance, bool __state)
        {
            if (!__state) return;
            try { RvMobile.OnTurnedDynamic(__instance); }
            catch (Exception e) { Core.Log.Error("[Mobile] putting the chassis at rest failed: " + e); }
        }
    }
}
