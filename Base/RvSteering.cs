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
    /// wheel turns at <see cref="Rate"/> degrees a second.
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

        internal static void Apply(LandVehicle chassis)
        {
            float kmh = Math.Abs(chassis.Speed_Kmh);
            float lockDeg = Mathf.Lerp(FullLock, HighSpeedLock, Mathf.InverseLerp(FullLockKmh, NarrowKmh, kmh));
            chassis.OverrideMaxSteerAngle(lockDeg);
            chassis.steerRate = Rate;
            if (!ReferenceEquals(_logged, chassis))
            {
                _logged = chassis;
                Core.Log.Msg("[Steer] RV steering on: " + FullLock + " deg up to " + FullLockKmh + " km/h, " + HighSpeedLock + " deg from "
                    + NarrowKmh + " km/h, " + Rate + " deg/s.");
            }
        }
    }
}
