using System;
using Il2CppScheduleOne.Vehicles;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Steering for the RV on its chassis.
    ///
    /// The chassis keeps the stock vehicle's steering: full lock at any speed, reached in a fifth of a second.
    /// Under a body this long that reads as nimble on the road. Full lock stays for manoeuvring; above
    /// <see cref="FullLockKmh"/> the lock narrows to <see cref="HighSpeedLock"/> at <see cref="NarrowKmh"/>, and the
    /// wheel turns at <see cref="Rate"/> degrees a second. The whole curve scales with the steering factor of the
    /// installed tuning parts, so a tuned RV turns wider at every speed.
    ///
    /// Vanilla computes the steer angle on the driver's machine from <c>ActualMaxSteeringAngle</c> and
    /// <c>steerRate</c> and replicates the angle, so this only has to run where the local player drives.
    /// </summary>
    internal static class RvSteering
    {
        private const float FullLock = 35f;       // the stock chassis' maxSteeringAngle
        private const float HighSpeedLock = 15f;
        private const float FullLockKmh = 20f;
        private const float NarrowKmh = 60f;
        private const float Rate = 90f;           // degrees a second; the stock chassis turns at 180

        private static LandVehicle _logged;
        private static float _loggedFactor = float.NaN;
        private static bool _factorFailed;

        internal static void Apply(LandVehicle chassis)
        {
            float factor = TuneFactor(chassis);
            float kmh = Math.Abs(chassis.Speed_Kmh);
            float lockDeg = Mathf.Lerp(FullLock, HighSpeedLock, Mathf.InverseLerp(FullLockKmh, NarrowKmh, kmh)) * factor;
            chassis.OverrideMaxSteerAngle(lockDeg);
            chassis.steerRate = Rate;
            if (!ReferenceEquals(_logged, chassis))
            {
                _logged = chassis;
                _loggedFactor = float.NaN;
                Core.Log.Msg("[Steer] RV steering on: " + FullLock + " deg up to " + FullLockKmh + " km/h, " + HighSpeedLock + " deg from "
                    + NarrowKmh + " km/h, " + Rate + " deg/s.");
            }
            if (!(Math.Abs(factor - _loggedFactor) < 0.0005f))
            {
                _loggedFactor = factor;
                Core.Log.Msg("[Steer] tuning factor x" + factor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ": "
                    + (FullLock * factor).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " deg up to " + FullLockKmh + " km/h, "
                    + (HighSpeedLock * factor).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " deg from " + NarrowKmh + " km/h.");
            }
        }

        /// <summary>
        /// The steering factor of the installed parts: the tuned maxSteeringAngle over the untuned one. Chassis keeps the
        /// untuned value and sets maxSteeringAngle to it times this factor, so asking Chassis gives the ratio without
        /// reading the live field, which a per-frame read would compound. 1 for a stock RV.
        /// </summary>
        private static float TuneFactor(LandVehicle chassis)
        {
            if (_factorFailed) return 1f;
            try
            {
                float f = Chassis.Api.ChassisApi.TuningStatsOf(chassis).Steer;
                if (float.IsNaN(f) || float.IsInfinity(f) || f <= 0.01f)
                {
                    Core.Log.Warning("[Steer] tuning steer factor " + f + " is not usable - the RV steers with its stock lock.");
                    _factorFailed = true;
                    return 1f;
                }
                return f;
            }
            catch (Exception e)
            {
                _factorFailed = true;
                Core.Log.Error("[Steer] reading the tuning steer factor from Chassis failed - the RV steers with its stock lock for this session: " + e);
                return 1f;
            }
        }
    }
}
