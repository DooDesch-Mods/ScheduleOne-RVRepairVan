using System.Collections;
using DooDesch.Localization;
using MelonLoader;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Vehicles;
using RVRepairVan.Managers;
using RVRepairVan.Net;
using RVRepairVan.Persistence;

namespace RVRepairVan.Base
{
    /// <summary>
    /// The Engine upgrade: the RV can be driven. Vanilla has no drivable RV - the property is scene-placed without
    /// physics - so a stock vehicle is spawned as a hidden chassis and the RV model, its placed items, the property
    /// bounds and the map icon are parented onto it. The property root itself never moves, so its NavMesh, idle
    /// points and loading dock stay home; <see cref="RvAway"/> makes the crew and deliveries wait for the return.
    ///
    /// The chassis is a networked vehicle: whoever sits at the wheel owns and simulates it, everyone else sees it
    /// through its NetworkTransform. The host spawns and despawns it and tells the clients which one it is
    /// (<see cref="RvOp.MobileSync"/>); each side parents its own copy of the model onto it.
    ///
    /// Two states. Parked: the RV's own colliders are on, the chassis body is off and frozen, so the RV is walkable
    /// wherever it stands. Driving: the RV colliders are off (parented under the chassis they would join its
    /// Rigidbody and drag on the ground) and the chassis body plus a hull box carry the collisions.
    /// </summary>
    internal static class RvMobile
    {
        private const string ChassisCode = "veeper";
        // Leaving the wheel this close to the spot, roughly aligned, puts the RV back home.
        private const float HomeRadius = 8f, HomeMaxAngle = 35f, HomeMaxSpeedKmh = 3f;
        // The host waits this long after a spawn or after the last occupant before it snaps home, so a client that
        // asked for the chassis has time to get in, and the driver who just left has been placed inside.
        private const float SpawnGrace = 8f, ExitGrace = 1.5f;

        private static LandVehicle _chassis;
        private static int _chassisId = -1;   // NetworkObject.ObjectId - a handle within this server session only
        private static InteractableObject _wheel;
        private static Transform _model;
        private static Vector3 _homePos;
        private static Quaternion _homeRot = Quaternion.identity;
        private static bool _homeKnown;

        private static bool _driving, _localWasIn, _pendingEnter, _restored;
        private static float _pendingSince, _spawnedAt, _lastOccupiedAt, _lastSaveAt, _lastDockAt;
        private static int _attachFrame;
        private static object _loop;

        /// <summary>True while the RV is on its chassis, wherever that is - including parked right at home.</summary>
        internal static bool Away => _chassis != null;
        internal static LandVehicle Chassis => _chassis;
        internal static BoxCollider Hull => _hull;

        // --- upgrade hook ---------------------------------------------------------------------------------

        /// <summary>Engine owned: put the drive point at the wheel and, on the host after a load, restore a parked pose.</summary>
        internal static void Apply()
        {
            Transform root = RVManager.Root;
            if (root == null) { Core.Log.Warning("[Mobile] apply: RV not found."); return; }
            if (RVManager.IsDestroyed()) { Core.Log.Msg("[Mobile] apply: the RV is wrecked - no engine to start."); return; }
            Transform model = RVManager.Model;
            if (model == null) { Core.Log.Warning("[Mobile] apply: RV model not found."); return; }
            _model = model;

            if (_wheel == null) _wheel = MakeWheel(model);
            if (_loop == null) _loop = MelonCoroutines.Start(Loop());

            if (!_restored && _chassis == null && (!NetworkBus.Online || NetworkBus.IsServer))
            {
                _restored = true;
                if (RepairStateStore.TryGetAwayPose(out Vector3 pos, out Quaternion rot))
                {
                    Core.Log.Msg("[Mobile] the RV was parked away at " + pos + " - restoring it there.");
                    HostSpawn(pos, rot);
                }
            }
        }

        internal static void Reset()
        {
            if (_loop != null) { MelonCoroutines.Stop(_loop); _loop = null; }
            _chassis = null;
            _chassisId = -1;
            _wheel = null;
            _model = null;
            _homeKnown = false;
            _driving = _localWasIn = _pendingEnter = _restored = false;
            Carried.Clear();
            _muted.Clear();
            _chassisColliders.Clear();
            _static.Clear();
            if (_draw != null) { MelonCoroutines.Stop(_draw); _draw = null; }
            _hull = null;
            RvLights.Reset();
            RvAway.Reset();
        }

        // --- the wheel ------------------------------------------------------------------------------------

        // Model-local fallback when the cab has no object named like a steering wheel.
        private static readonly Vector3 WheelFallbackLocal = new Vector3(-4.74f, 1.92f, -0.84f);

        private static InteractableObject MakeWheel(Transform model)
        {
            try
            {
                Vector3 local = WheelFallbackLocal;
                Transform found = null;
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "SteeringWheel") { found = t; break; }
                    if (found == null && t.name.IndexOf("Steering", StringComparison.OrdinalIgnoreCase) >= 0) found = t;
                }
                if (found != null) local = model.InverseTransformPoint(found.position);
                Core.Log.Msg("[Mobile] drive point at model-local " + local + (found != null ? " (" + found.name + ")" : " (fallback)") + ".");

                var go = new GameObject("RVRepairVan_SteeringWheel");
                go.transform.SetParent(model, false);
                go.transform.localPosition = local;
                go.layer = InteractionLayer();
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.7f, 0.6f, 0.7f);
                var io = go.AddComponent<InteractableObject>();
                io.SetMessage(L10n.T("Drive"));
                io.MaxInteractionRange = 2.5f;
                io.RequiresUniqueClick = true;
                io.onHovered.AddListener((UnityEngine.Events.UnityAction)OnWheelHovered);
                io.onInteractStart.AddListener((UnityEngine.Events.UnityAction)OnWheel);
                return io;
            }
            catch (Exception e) { Core.Log.Error("[Mobile] drive point failed: " + e); return null; }
        }

        private static int InteractionLayer()
        {
            var im = Singleton<InteractionManager>.Instance;
            int mask = im != null ? im.Interaction_SearchMask.value : 1;
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) return i;
            return 0;
        }

        private static void OnWheelHovered()
        {
            if (_wheel == null) return;
            bool full = _chassis != null && _chassis.GetFirstFreeSeat() == null;
            _wheel.SetMessage(full ? L10n.T("Seats taken") : L10n.T("Drive"));
            _wheel.SetInteractableState(full ? InteractableObject.EInteractableState.Invalid : InteractableObject.EInteractableState.Default);
        }

        private static void OnWheel()
        {
            try
            {
                if (!RvUpgrades.Owned(RvUpgrade.Engine)) { Core.Log.Warning("[Mobile] wheel used without the engine upgrade."); return; }
                if (_chassis != null) { EnterChassis(); return; }
                _pendingEnter = true;
                _pendingSince = Time.time;
                if (NetworkBus.Online && !NetworkBus.IsServer)
                {
                    NetworkBus.SendToHost(RvOp.DriveRequest);
                    Core.Log.Msg("[Mobile] asked the host for the chassis.");
                    return;
                }
                if (!HostStartAtHome()) _pendingEnter = false;
            }
            catch (Exception e) { Core.Log.Error("[Mobile] wheel failed: " + e); }
        }

        /// <summary>Host: a client wants to drive the RV from its spot.</summary>
        internal static void HostOnDriveRequest()
        {
            if (!RvUpgrades.Owned(RvUpgrade.Engine)) { Core.Log.Warning("[Mobile] drive request refused - the engine upgrade is not owned."); return; }
            if (_chassis != null) { Core.Log.Msg("[Mobile] drive request - chassis already out, re-sending it."); Broadcast(); return; }
            HostStartAtHome();
        }

        private static bool HostStartAtHome()
        {
            Transform root = RVManager.Root;
            if (root == null || _model == null) { Core.Log.Warning("[Mobile] start: RV not ready."); return false; }
            if (RVManager.IsDestroyed()) { Core.Log.Warning("[Mobile] start: the RV is wrecked."); return false; }
            ComputeHome(root, _model);
            return HostSpawn(_homePos, _homeRot);
        }

        private static void EnterChassis()
        {
            if (_chassis == null) { Core.Log.Warning("[Mobile] enter: no chassis."); return; }
            if (_chassis.LocalPlayerIsInVehicle) { Core.Log.Msg("[Mobile] enter: already inside."); return; }
            if (_chassis.GetFirstFreeSeat() == null) { Core.Log.Msg("[Mobile] enter: every seat is taken."); return; }
            _chassis.EnterVehicle();
            MelonCoroutines.Start(ResetReverseCamera(_chassis));
            Core.Log.Msg("[Mobile] entering the RV.");
        }

        // The spawn teleport reads as a big negative speed for a few frames, and VehicleCamera then flips to the
        // reverse view and stays there until the vehicle goes forward faster than 2 km/h.
        private static IEnumerator ResetReverseCamera(LandVehicle chassis)
        {
            int resets = 0;
            for (int i = 0; i < 10 && chassis != null; i++)
            {
                yield return null;
                VehicleCamera vc = chassis != null ? chassis.GetComponentInChildren<VehicleCamera>(true) : null;
                if (vc != null && vc.cameraReversed) { vc.cameraReversed = false; resets++; }
            }
            if (resets > 0) Core.LogDebug("[Mobile] camera reverse view reset " + resets + "x after entering.");
        }

        // --- spawn, attach, detach ------------------------------------------------------------------------

        // The chassis sits under the model's middle, turned to face the cab: the RV's front is -root.forward.
        private static void ComputeHome(Transform root, Transform model)
        {
            if (_homeKnown) return;
            Bounds b = BodyBounds(model);
            _homePos = new Vector3(b.center.x, root.position.y + 0.3f, b.center.z);
            _homeRot = root.rotation * Quaternion.Euler(0f, 180f, 0f);
            _homeKnown = true;
            Core.Log.Msg("[Mobile] home chassis pose " + _homePos + " fwd=" + (_homeRot * Vector3.forward) + ".");
        }

        private static bool HostSpawn(Vector3 pos, Quaternion rot)
        {
            try
            {
                VehicleManager vm = NetworkSingleton<VehicleManager>.Instance;
                if (vm == null) { Core.Log.Error("[Mobile] no VehicleManager - cannot spawn the chassis."); return false; }
                // Not player-owned: that keeps it out of the vehicle save, the net worth, the map, Marco's
                // recovery list and the trunk prompt. Entering goes through our wheel, which needs none of that.
                LandVehicle chassis = vm.SpawnAndReturnVehicle(ChassisCode, pos, rot, false);
                if (chassis == null) { Core.Log.Error("[Mobile] spawn of '" + ChassisCode + "' returned null."); return false; }
                if (vm.PlayerOwnedVehicles.Contains(chassis)) vm.PlayerOwnedVehicles.Remove(chassis);
                _spawnedAt = Time.time;
                if (!Attach(chassis)) { chassis.DestroyVehicle(); return false; }
                Broadcast();
                return true;
            }
            catch (Exception e) { Core.Log.Error("[Mobile] spawn failed: " + e); return false; }
        }

        /// <summary>A transform that rides on the chassis, with where it belongs at home.</summary>
        private sealed class CarriedPart
        {
            public Transform T, Parent;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        private static readonly List<CarriedPart> Carried = new List<CarriedPart>();

        private static bool Attach(LandVehicle chassis)
        {
            Transform root = RVManager.Root;
            if (root == null || _model == null) { Core.Log.Error("[Mobile] attach: RV not ready."); return false; }
            ComputeHome(root, _model);

            _chassis = chassis;
            _chassisId = chassis.NetworkObject != null ? chassis.NetworkObject.ObjectId : -1;
            _attachFrame = Time.frameCount;
            _lastOccupiedAt = Time.time;

            // The stock vehicle's own parts, before the RV is parented on and would be counted with them.
            int hidden = 0;
            foreach (Renderer r in chassis.GetComponentsInChildren<Renderer>(true)) { r.forceRenderingOff = true; hidden++; }
            HideChassisExtras(chassis);
            RvLights.MuteChassis(chassis);
            CollectChassisColliders(chassis);

            // Captured at home, before anything moves: the static-batch redraw measures the drive offset from here.
            _modelHomeInverse = _model.worldToLocalMatrix;
            Bounds homeBounds = BodyBounds(_model);

            Carried.Clear();
            Carry(_model);
            RV rv = root.GetComponent<RV>();
            if (rv != null && rv.Container != null) Carry(rv.Container.transform);
            else Core.Log.Warning("[Mobile] no contents container - placed items stay home.");
            Transform bounds = root.Find("BoundingBox");
            if (bounds != null) Carry(bounds); else Core.Log.Warning("[Mobile] no BoundingBox - the RV counts as a property only at home.");
            Transform poi = root.Find("PoI");
            if (poi != null) Carry(poi);

            // Place each part relative to the chassis exactly as it sits relative to the home chassis pose. On the
            // host at spawn that changes nothing; on a client or after a load the chassis is elsewhere already.
            Quaternion invHome = Quaternion.Inverse(_homeRot);
            foreach (CarriedPart p in Carried)
            {
                Vector3 worldPos = p.T.position;
                Quaternion worldRot = p.T.rotation;
                p.T.SetParent(chassis.transform, false);
                p.T.localPosition = invHome * (worldPos - _homePos);
                p.T.localRotation = invHome * worldRot;
            }

            RaiseCamera(chassis);
            BuildHull(chassis, homeBounds);
            int baked = TakeStaticParts(_model);
            RvLights.Bind(_model);

            _driving = true;   // force the first SetDriving to run in full
            SetDriving(chassis.CurrentPlayerOccupancy > 0);

            Property prop = root.GetComponent<Property>();
            RvAway.Begin(prop, chassis);
            Core.Log.Msg("[Mobile] RV on the chassis (id " + _chassisId + "): " + Carried.Count + " part(s) carried, "
                + hidden + " chassis renderer(s) hidden, " + baked + " static part(s) redrawn, " + _chassisColliders.Count
                + " chassis collider(s).");
            return true;
        }

        private static void Carry(Transform t)
        {
            Carried.Add(new CarriedPart { T = t, Parent = t.parent, LocalPos = t.localPosition, LocalRot = t.localRotation });
        }

        /// <summary>Put everything back on its spot. Host: then despawn the chassis.</summary>
        private static void Detach()
        {
            if (_chassis == null) return;
            LandVehicle chassis = _chassis;
            Transform root = RVManager.Root;

            // Where the local player stands in the RV, so a snap home takes them along instead of leaving them in the air.
            bool carryPlayer = false;
            Vector3 playerInModel = Vector3.zero;
            Transform player = LocalPlayerTransform();
            if (player != null && _model != null && !chassis.LocalPlayerIsInVehicle && InsideRv(player.position))
            {
                carryPlayer = true;
                playerInModel = _model.InverseTransformPoint(player.position);
            }

            SetDriving(false);
            Rigidbody rb = chassis.Rb;
            if (rb != null) rb.constraints = RigidbodyConstraints.None;
            foreach (CarriedPart p in Carried)
            {
                if (p.T == null) { Core.Log.Warning("[Mobile] a carried part is gone before the return."); continue; }
                p.T.SetParent(p.Parent, false);
                p.T.localPosition = p.LocalPos;
                p.T.localRotation = p.LocalRot;
            }
            Carried.Clear();
            RvLights.Unbind();
            ReleaseStaticParts();
            if (_hull != null) { UnityEngine.Object.Destroy(_hull.gameObject); _hull = null; }
            _chassisColliders.Clear();
            _chassis = null;
            _chassisId = -1;

            if (carryPlayer && _model != null) TeleportLocalPlayer(_model.TransformPoint(playerInModel), "home with the RV");
            RvAway.End(root != null ? root.GetComponent<Property>() : null);
            RepairStateStore.ClearAwayPose();
            Core.Log.Msg("[Mobile] RV back on its spot.");

            if (!NetworkBus.Online || NetworkBus.IsServer)
            {
                Broadcast();
                MelonCoroutines.Start(DespawnLater(chassis));
            }
        }

        // The clients must have put the model back before the chassis despawns: on a client the model is a child of
        // its copy of the chassis, and a despawn would take the whole RV with it.
        // The weather manager works through a copy of its entity list spread over half a second, and a vehicle
        // destroyed inside that window throws there. So the chassis leaves the list first and is destroyed only
        // after any copy that still holds it is done.
        private static IEnumerator DespawnLater(LandVehicle chassis)
        {
            yield return new WaitForSeconds(2f);
            try
            {
                if (chassis == null) { Core.Log.Warning("[Mobile] chassis already gone before the despawn."); yield break; }
                Il2CppScheduleOne.Core.Weather.EnvironmentHandler.UnregisterWeatherEntity(
                    new Il2CppScheduleOne.Core.Weather.IWeatherEntity(chassis.Pointer));
            }
            catch (Exception e) { Core.Log.Warning("[Mobile] could not take the chassis off the weather list: " + e.Message); }
            yield return new WaitForSeconds(1f);
            try
            {
                if (chassis == null) { Core.Log.Warning("[Mobile] chassis already gone before the despawn."); yield break; }
                chassis.DestroyVehicle();
                Core.Log.Msg("[Mobile] chassis despawned.");
            }
            catch (Exception e) { Core.Log.Error("[Mobile] chassis despawn failed: " + e.Message); }
        }

        // --- network --------------------------------------------------------------------------------------

        private static void Broadcast()
        {
            if (!NetworkBus.Online || !NetworkBus.IsServer) return;
            NetworkBus.BroadcastToAll(RvOp.MobileSync, _chassis != null ? _chassisId : -1);
        }

        /// <summary>Host: part of the join snapshot, after the upgrades.</summary>
        internal static void HostSnapshot() => Broadcast();

        /// <summary>Client: the host names the chassis the RV rides on, or -1 when it is home.</summary>
        internal static void ApplySync(int id)
        {
            if (NetworkBus.IsServer) return;
            if (id < 0)
            {
                if (_chassis != null) { Core.Log.Msg("[Mobile] host: the RV is home."); Detach(); }
                _pendingEnter = false;
                return;
            }
            if (_chassis != null && _chassisId == id) return;
            if (_chassis != null) { Core.Log.Warning("[Mobile] host names chassis " + id + ", we ride on " + _chassisId + " - switching."); Detach(); }
            MelonCoroutines.Start(FindAndAttach(id));
        }

        // The sync can arrive before FishNet has spawned the vehicle on this client.
        private static IEnumerator FindAndAttach(int id)
        {
            for (int i = 0; i < 40; i++)
            {
                LandVehicle v = FindVehicle(id);
                if (v != null)
                {
                    if (_chassis != null) yield break;
                    if (_model == null) Apply();
                    if (_model == null) { Core.Log.Warning("[Mobile] chassis " + id + " found, but the RV model is not ready."); yield break; }
                    if (Attach(v)) Core.Log.Msg("[Mobile] riding on the host's chassis " + id + ".");
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            Core.Log.Error("[Mobile] chassis " + id + " never appeared on this client - the RV stays at its spot here.");
        }

        private static LandVehicle FindVehicle(int id)
        {
            VehicleManager vm = NetworkSingleton<VehicleManager>.Instance;
            if (vm == null || vm.AllVehicles == null) return null;
            for (int i = 0; i < vm.AllVehicles.Count; i++)
            {
                LandVehicle v = vm.AllVehicles[i];
                if (v != null && v.NetworkObject != null && v.NetworkObject.ObjectId == id) return v;
            }
            return null;
        }

        // --- the watch loop -------------------------------------------------------------------------------

        private static IEnumerator Loop()
        {
            int failures = 0;
            while (true)
            {
                yield return new WaitForSeconds(0.1f);
                try { Tick(); failures = 0; }
                catch (Exception e)
                {
                    if (failures++ == 0) Core.Log.Error("[Mobile] watch failed: " + e);
                }
            }
        }

        private static void Tick()
        {
            if (_pendingEnter)
            {
                if (_chassis != null && Time.frameCount - _attachFrame >= 2) { _pendingEnter = false; EnterChassis(); }
                else if (Time.time - _pendingSince > 10f) { _pendingEnter = false; Core.Log.Warning("[Mobile] no chassis 10 s after the wheel was used - gave up."); }
            }
            if (_chassis == null) return;

            bool alive;
            try { alive = _chassis.transform != null; } catch (Exception e) { Core.Log.Warning("[Mobile] chassis check: " + e.Message); alive = false; }
            if (!alive)
            {
                Core.Log.Error("[Mobile] the chassis vanished under the RV - putting the RV back on its spot.");
                _chassis = null;
                return;
            }

            bool localIn = _chassis.LocalPlayerIsInVehicle;
            bool occupied = localIn || _chassis.CurrentPlayerOccupancy > 0;
            if (occupied) _lastOccupiedAt = Time.time;
            if (occupied != _driving)
            {
                SetDriving(occupied);
                if (occupied && !localIn) EvictLocalPlayer();
            }
            if (_localWasIn && !localIn) MelonCoroutines.Start(PlaceInCabin());
            _localWasIn = localIn;
            RvLights.Update();
            // Against a wall the body still falls asleep with a zero threshold, and the driven wheels spin up unloaded
            // under it. Kept awake, they stay in contact with the road and cannot grip all at once later.
            if (_driving && _chassis.LocalPlayerIsDriver)
            {
                Rigidbody rb = _chassis.Rb;
                if (rb != null && rb.IsSleeping()) rb.WakeUp();
            }

            if (NetworkBus.Online && !NetworkBus.IsServer) return;

            if (Time.time - _lastSaveAt > 1f)
            {
                _lastSaveAt = Time.time;
                RepairStateStore.SetAwayPose(_chassis.transform.position, _chassis.transform.rotation);
            }
            if (Time.time - _lastDockAt > 5f)
            {
                _lastDockAt = Time.time;
                Transform root = RVManager.Root;
                if (root != null) RvAway.KeepDockHeld(root.GetComponent<Property>(), _chassis);
            }
            if (!occupied && !_pendingEnter && Time.time - _spawnedAt > SpawnGrace && Time.time - _lastOccupiedAt > ExitGrace && AtHome())
            {
                Core.Log.Msg("[Mobile] parked on its spot - the RV goes home.");
                Detach();
            }
        }

        private static bool AtHome()
        {
            if (!_homeKnown || _chassis == null) return false;
            float d = Vector3.Distance(_chassis.transform.position, _homePos);
            float a = Vector3.Angle(_chassis.transform.forward, _homeRot * Vector3.forward);
            return d <= HomeRadius && a <= HomeMaxAngle && Mathf.Abs(_chassis.Speed_Kmh) < HomeMaxSpeedKmh;
        }

        // --- parked / driving -----------------------------------------------------------------------------

        private static readonly List<Collider> _muted = new List<Collider>();
        private static readonly List<Collider> _chassisColliders = new List<Collider>();
        private static BoxCollider _hull;
        private const float HullHalfWidth = 1.5f, HullClearance = 1.0f, HullTop = 3.4f;

        private static float _sleepThreshold = -1f;   // the chassis' own, read before driving overrides it

        private static void SetDriving(bool driving)
        {
            if (_chassis == null || driving == _driving) return;
            _driving = driving;
            Rigidbody rb = _chassis.Rb;
            if (driving)
            {
                // RV colliders off first, then free the body - the other order lets the floor drag on the ground.
                int muted = 0;
                foreach (CarriedPart p in Carried) if (p.T != null) muted += MuteColliders(p.T);
                SetChassisColliders(true);
                // A body that was frozen falls asleep, and wheel torque does not wake it: the wheels spin up with no
                // load, then grip all at once when something nudges the body, and the RV flips. So it never sleeps
                // while someone drives.
                if (rb != null)
                {
                    if (_sleepThreshold < 0f) _sleepThreshold = rb.sleepThreshold;
                    rb.sleepThreshold = 0f;
                    rb.constraints = RigidbodyConstraints.None;
                    rb.WakeUp();
                }
                Core.Log.Msg("[Mobile] driving: " + muted + " RV collider(s) off, chassis body on.");
            }
            else
            {
                if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                if (rb != null)
                {
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                    if (_sleepThreshold >= 0f) rb.sleepThreshold = _sleepThreshold;
                }
                SetChassisColliders(false);
                int back = RestoreColliders();
                Core.Log.Msg("[Mobile] parked: " + back + " RV collider(s) on, chassis body off and frozen.");
            }
        }

        private static int MuteColliders(Transform t)
        {
            int n = 0;
            foreach (Collider c in t.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                c.enabled = false;
                _muted.Add(c);
                n++;
            }
            return n;
        }

        private static int RestoreColliders()
        {
            int back = 0;
            foreach (Collider c in _muted) if (c != null) { c.enabled = true; back++; }
            if (back != _muted.Count) Core.Log.Warning("[Mobile] " + (_muted.Count - back) + " RV collider(s) gone before restore.");
            _muted.Clear();
            return back;
        }

        // The veeper's body and its bounding box sit inside the RV; parked, they are the invisible wall in the cabin.
        // Only the colliders the vehicle itself has switched on: the wheel meshes carry disabled colliders that reach
        // the road, and with them on the body rests on four blocks instead of its suspension.
        private static void CollectChassisColliders(LandVehicle chassis)
        {
            _chassisColliders.Clear();
            int skipped = 0;
            foreach (Collider c in chassis.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.TryCast<WheelCollider>() != null) continue;
                if (!c.enabled || !c.gameObject.activeInHierarchy) { skipped++; continue; }
                _chassisColliders.Add(c);
            }
            Core.Log.Msg("[Mobile] chassis colliders: " + _chassisColliders.Count + " solid, " + skipped + " switched off by the vehicle and left off.");
        }

        private static void SetChassisColliders(bool on)
        {
            foreach (Collider c in _chassisColliders) if (c != null) c.enabled = on;
            if (_hull != null) _hull.enabled = on;
        }

        // The veeper is half the RV's size. While driving, a box the size of the RV body (lifted off the ground so it
        // does not drag) keeps the RV's front and sides out of walls and other cars.
        private static void BuildHull(LandVehicle chassis, Bounds homeBounds)
        {
            try
            {
                Quaternion invHome = Quaternion.Inverse(_homeRot);
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = homeBounds.center + Vector3.Scale(homeBounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = invHome * (corner - _homePos);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
                // Keep the box well off the road. The RV is four times as long as the chassis, so a bottom close to the
                // ground scrapes at the slightest pitch or body roll: it brakes the RV and, in a turn, levers it over.
                float ground = WheelGroundY(chassis);
                min.y = Mathf.Max(min.y + 0.7f, ground + HullClearance);
                // The roof with its AC unit stands 4.4 m tall, and the town's traffic light arms hang at about 4 m; a full
                // height box stops dead under every crossing. The box ends below them, so the roof clips the arm instead.
                max.y = Mathf.Min(max.y, ground + HullTop);
                // The side steps and the awning reach past the body; the box keeps to the body's 3 m.
                float cx = (min.x + max.x) * 0.5f;
                min.x = Mathf.Max(min.x, cx - HullHalfWidth);
                max.x = Mathf.Min(max.x, cx + HullHalfWidth);
                var go = new GameObject("RVRepairVan_Hull");
                go.layer = chassis.gameObject.layer;
                go.transform.SetParent(chassis.transform, false);
                _hull = go.AddComponent<BoxCollider>();
                _hull.center = (min + max) * 0.5f;
                _hull.size = max - min;
                _hull.enabled = false;
                // Sound zones carry solid colliders on TransparentFX at about 3.5 m, out of reach of a normal car but
                // not of the RV's roof. They would stop it dead in the open.
                int fx = LayerMask.NameToLayer("TransparentFX");
                if (fx >= 0) _hull.excludeLayers = 1 << fx;
                else Core.Log.Warning("[Mobile] no TransparentFX layer - the RV may snag on sound zones.");
                Core.Log.Msg("[Mobile] hull " + _hull.size + " at " + _hull.center + ", " + (min.y - ground).ToString("0.00")
                    + " to " + (max.y - ground).ToString("0.00") + " m above the road.");
            }
            catch (Exception e) { Core.Log.Warning("[Mobile] hull failed - the RV body can clip while driving: " + e.Message); }
        }

        // Chassis-local height of the road under the wheels at rest.
        private static float WheelGroundY(LandVehicle chassis)
        {
            float ground = float.MaxValue;
            foreach (WheelCollider w in chassis.GetComponentsInChildren<WheelCollider>(true))
            {
                float y = chassis.transform.InverseTransformPoint(w.transform.TransformPoint(w.center)).y
                    - w.suspensionDistance * w.suspensionSpring.targetPosition - w.radius;
                ground = Mathf.Min(ground, y);
            }
            if (ground == float.MaxValue) { Core.Log.Warning("[Mobile] chassis has no wheels - hull height taken from the RV body only."); return 0f; }
            return ground;
        }

        private static void HideChassisExtras(LandVehicle chassis)
        {
            try
            {
                if (chassis.intObj != null) chassis.intObj.gameObject.SetActive(false);
                else Core.Log.Warning("[Mobile] chassis has no enter prompt to hide.");
                if (chassis.HumanoidColliderContainer != null) chassis.HumanoidColliderContainer.gameObject.SetActive(false);
            }
            catch (Exception e) { Core.Log.Warning("[Mobile] hiding the chassis prompt failed: " + e.Message); }
        }

        // --- the local player -----------------------------------------------------------------------------

        private static Transform LocalPlayerTransform()
        {
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            return pm != null ? pm.transform : null;
        }

        private static bool InsideRv(Vector3 world)
        {
            if (_chassis == null || _hull == null) return false;
            Vector3 local = _chassis.transform.InverseTransformPoint(world);
            Vector3 c = _hull.center, h = _hull.size * 0.5f;
            return Mathf.Abs(local.x - c.x) <= h.x + 0.3f && Mathf.Abs(local.z - c.z) <= h.z + 0.3f
                && local.y >= c.y - h.y - 1.2f && local.y <= c.y + h.y;
        }

        // Model-local spots: the cab behind the wheel, and the ground outside the side door (the door is on +z).
        private static readonly Vector3 CabStandLocal = new Vector3(-3.7f, 1.8f, -0.3f);
        private static readonly Vector3 DoorOutsideLocal = new Vector3(-2.2f, 1.5f, 3.0f);

        // Someone else just took the wheel. The floor is about to stop being solid, so step out by the door.
        private static void EvictLocalPlayer()
        {
            Transform player = LocalPlayerTransform();
            if (player == null || _model == null || !InsideRv(player.position)) return;
            Vector3 at = Ground(_model.TransformPoint(DoorOutsideLocal));
            TeleportLocalPlayer(at, "out of the RV, someone drives off");
        }

        // Vanilla drops the driver at the stock vehicle's door, which is inside the RV's wall. Stand them up in the
        // cab instead, behind the wheel, once the RV floor is solid again.
        private static IEnumerator PlaceInCabin()
        {
            yield return null;
            yield return null;
            if (_chassis == null || _model == null) yield break;
            Vector3 at = Ground(_model.TransformPoint(CabStandLocal));
            TeleportLocalPlayer(at, "into the cab");
        }

        private static Vector3 Ground(Vector3 from)
        {
            int mask = ~(1 << LayerMask.NameToLayer("Player"));
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            Core.Log.Warning("[Mobile] no floor under " + from + " - placing the player there anyway.");
            return from;
        }

        private static void TeleportLocalPlayer(Vector3 at, string why)
        {
            var pm = PlayerSingleton<PlayerMovement>.Instance;
            if (pm == null) { Core.Log.Warning("[Mobile] no PlayerMovement - cannot move the player " + why + "."); return; }
            pm.Teleport(at, true);
            Core.Log.Msg("[Mobile] player moved " + why + " (" + at + ").");
        }

        // --- camera ---------------------------------------------------------------------------------------

        // The vehicle camera orbits the stock van's cameraOrigin and pulls in wherever a ray from there hits a wall,
        // which inside the RV body is right away. Lift the origin above the roof and back the camera off.
        private const float CameraLift = 3.5f, CameraLateral = 11f, CameraAboveOrigin = 3f;

        private static void RaiseCamera(LandVehicle chassis)
        {
            try
            {
                VehicleCamera cam = chassis.GetComponentInChildren<VehicleCamera>(true);
                if (cam == null) { Core.Log.Warning("[Mobile] chassis has no VehicleCamera - the camera stays inside the RV."); return; }
                if (cam.cameraOrigin != null) cam.cameraOrigin.localPosition += Vector3.up * CameraLift;
                // The camera sits at -forward of the VehicleCamera's own transform, which can face the other way than
                // the chassis. Flip the offset so the camera always trails the cab.
                cam.lateralOffset = Vector3.Dot(cam.transform.forward, chassis.transform.forward) >= 0f ? CameraLateral : -CameraLateral;
                // The camera must sit above the point it looks at. Looking up, its pitch reads as 350 degrees, and the
                // right-click orbit clamps that to 89 - straight above the roof.
                float originAbove = cam.cameraOrigin != null ? cam.cameraOrigin.position.y - cam.transform.position.y : 0f;
                cam.verticalOffset = Mathf.Max(0f, originAbove) + CameraAboveOrigin;
                Core.Log.Msg("[Mobile] camera " + Mathf.Abs(cam.lateralOffset) + " m behind, " + CameraAboveOrigin
                    + " m above its look point (origin " + originAbove.ToString("0.00") + " m over the camera mount).");
            }
            catch (Exception e) { Core.Log.Warning("[Mobile] camera adjust failed: " + e.Message); }
        }

        // --- static-batched parts -------------------------------------------------------------------------

        // 77 of the RV's renderers (wheels, windows, side panels) are static-batched: they draw from one combined
        // mesh baked in world space at the home spot, so moving their transform moves nothing. While the RV is on
        // the chassis, the originals are hidden and their slice of the combined mesh is drawn each frame with the
        // offset from home.
        private sealed class StaticPart
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public Material[] Materials;
            public int FirstSubMesh, Layer;
            public Matrix4x4 HomeBase;
        }

        private static readonly List<StaticPart> _static = new List<StaticPart>();
        private static Matrix4x4 _modelHomeInverse;
        private static object _draw;

        private static int TakeStaticParts(Transform model)
        {
            _static.Clear();
            int skipped = 0;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r.forceRenderingOff || !r.isPartOfStaticBatch) continue;
                MeshRenderer mr = r.TryCast<MeshRenderer>();
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mr == null || mesh == null) { skipped++; continue; }
                Transform batchRoot = r.staticBatchRootTransform;
                var mats = r.sharedMaterials;
                var part = new StaticPart
                {
                    Renderer = r,
                    Mesh = mesh,
                    Materials = new Material[mats.Length],
                    FirstSubMesh = mr.subMeshStartIndex,
                    Layer = r.gameObject.layer,
                    HomeBase = batchRoot != null ? batchRoot.localToWorldMatrix : Matrix4x4.identity,
                };
                for (int i = 0; i < mats.Length; i++) part.Materials[i] = mats[i];
                r.forceRenderingOff = true;
                _static.Add(part);
            }
            if (skipped > 0) Core.Log.Warning("[Mobile] " + skipped + " static part(s) without a MeshRenderer or mesh stay at home.");
            if (_draw != null) MelonCoroutines.Stop(_draw);
            _draw = _static.Count > 0 ? MelonCoroutines.Start(DrawStaticParts()) : null;
            return _static.Count;
        }

        private static IEnumerator DrawStaticParts()
        {
            int failures = 0;
            while (_static.Count > 0 && _model != null)
            {
                Matrix4x4 delta = _model.localToWorldMatrix * _modelHomeInverse;
                foreach (StaticPart p in _static)
                {
                    Matrix4x4 m = delta * p.HomeBase;
                    for (int i = 0; i < p.Materials.Length; i++)
                    {
                        int sub = p.FirstSubMesh + i;
                        if (p.Materials[i] == null || sub >= p.Mesh.subMeshCount) continue;
                        try { Graphics.DrawMesh(p.Mesh, m, p.Materials[i], p.Layer, null, sub); }
                        catch (Exception e)
                        {
                            if (failures++ == 0) Core.Log.Error("[Mobile] drawing a static part failed: " + e.Message);
                        }
                    }
                }
                yield return null;
            }
            _draw = null;
        }

        /// <summary>A redrawn static part picks up the materials its renderer has now (a lamp switched on or off).</summary>
        internal static void RefreshStatic(Renderer r)
        {
            if (r == null) return;
            foreach (StaticPart p in _static)
            {
                if (p.Renderer == null || p.Renderer.Pointer != r.Pointer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < p.Materials.Length && i < mats.Length; i++) p.Materials[i] = mats[i];
                return;
            }
        }

        private static void ReleaseStaticParts()
        {
            if (_draw != null) { MelonCoroutines.Stop(_draw); _draw = null; }
            int back = 0;
            foreach (StaticPart p in _static) if (p.Renderer != null) { p.Renderer.forceRenderingOff = false; back++; }
            if (back != _static.Count) Core.Log.Warning("[Mobile] " + (_static.Count - back) + " static part(s) gone before restore.");
            _static.Clear();
        }

        // The body from its solid colliders: the renderer bounds take in light volumes and LOD helpers and come out
        // twice the size of the RV.
        private static Bounds BodyBounds(Transform model)
        {
            bool any = false;
            Bounds b = new Bounds(model.position, Vector3.zero);
            Transform body = model.Find("rv") ?? model;
            foreach (Collider c in body.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any) Core.Log.Warning("[Mobile] RV model has no solid collider - using its pivot.");
            return b;
        }

#if DEBUG
        // --- console --------------------------------------------------------------------------------------

        /// <summary>rvdrive: the same as using the wheel.</summary>
        internal static void DebugDrive() => OnWheel();

        /// <summary>rvdrive home: put the RV back on its spot now, wherever it is. Host only.</summary>
        internal static void DebugHome()
        {
            if (NetworkBus.Online && !NetworkBus.IsServer) { Core.Log.Warning("[Mobile] rvdrive home: host only."); return; }
            if (_chassis == null) { Core.Log.Msg("[Mobile] rvdrive home: the RV is home."); return; }
            if (_chassis.LocalPlayerIsInVehicle) _chassis.ExitVehicle();
            Detach();
        }

        internal static string DebugState() =>
            "away=" + Away + " id=" + _chassisId + " driving=" + _driving + " homeKnown=" + _homeKnown
            + (_chassis != null ? " pos=" + _chassis.transform.position + " fromHome=" + Vector3.Distance(_chassis.transform.position, _homePos).ToString("0.0")
                + " occupants=" + _chassis.CurrentPlayerOccupancy + " speed=" + _chassis.Speed_Kmh.ToString("0.0")
                + " constraints=" + (_chassis.Rb != null ? _chassis.Rb.constraints.ToString() : "no rb") : "")
            + (_wheel != null ? " wheel=" + _wheel.transform.position : " no wheel");
#endif
    }
}
