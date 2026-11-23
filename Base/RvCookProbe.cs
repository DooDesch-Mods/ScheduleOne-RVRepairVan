#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerTasks;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI.Stations;
using Il2CppScheduleOne.DevUtilities;
using MelonLoader;
using RVRepairVan.Managers;
using UnityEngine;
using Task = Il2CppScheduleOne.PlayerTasks.Task;
using Ray = UnityEngine.Ray;

namespace RVRepairVan.Base
{
    /// <summary>
    /// rvcook: a cook at the RV's chemistry station through the game's own task, with the player's click and tilt keys
    /// played in by the probe instead of the mouse. 'rvcook start' fills the station and begins the task as its Begin
    /// button does, 'rvcook tilt [dir] [seconds]' grabs the first pourable of the task and holds the tilt key, and logs
    /// how far it tips, every collider it touches and every change of its pose outside the physics step.
    /// </summary>
    internal static class RvCookProbe
    {
        private static ChemistryStation _station;
        private static Draggable _target;
        private static PourableModule _pour;
        private static DraggableConstraint _constraint;

        // Played-in input.
        internal static bool Holding;
        private static Vector3 _aimLocal;
        private static float _dir, _until, _nextSample, _start;

        // Measures of one tilt.
        private static float _maxTilt, _maxSpout, _minTheta = 180f, _maxTheta;
        private static int _steps, _pouring, _writes;
        private static float _maxWrite, _sumWrite;
        private static Quaternion _post;
        private static bool _havePost;
        // How far each physics step turned the item against its spin times the step, and how often the chassis moved.
        private static Quaternion _raw;
        private static float _rawOmega, _sumAdv, _sumExpect;
        private static bool _haveRaw, _restoreInterp;
        private static RigidbodyInterpolation _interp;
        private static int _parentMoves, _frames;
        private static Vector3 _lastChassisPos;
        private static Quaternion _lastChassisRot;
        private static readonly Dictionary<IntPtr, string> _touched = new Dictionary<IntPtr, string>();
        private static readonly Dictionary<IntPtr, string> _near = new Dictionary<IntPtr, string>();
        private static readonly List<string> _trace = new List<string>();

        private static Task _task;

        internal static void Start()
        {
            Transform root = RVManager.Root;
            RV rv = root != null ? root.GetComponent<RV>() : null;
            if (rv == null || rv.Container == null) { Core.Log.Warning("[Cook] start: RV contents not found."); return; }
            ChemistryStation st = rv.Container.GetComponentInChildren<ChemistryStation>();
            if (st == null) { Core.Log.Warning("[Cook] start: no chemistry station in the RV - 'rvplace chemistrystation' first."); return; }
            ChemistryStationInterface ui = ChemistryStationInterface.Instance;
            if (ui == null) { Core.Log.Warning("[Cook] start: no chemistry station screen in the scene."); return; }
            StationRecipe recipe = null;
            foreach (StationRecipe r in ui.Recipes)
            {
                if (r == null) continue;
                foreach (StationRecipe.IngredientQuantity q in r.Ingredients)
                {
                    StorableItemDefinition d = q.Items.Count > 0 ? q.Items[0].TryCast<StorableItemDefinition>() : null;
                    if (d != null && d.StationItem != null && d.StationItem.GetComponentInChildren<PourableModule>(true) != null) { recipe = r; break; }
                }
                if (recipe != null) break;
            }
            if (recipe == null) { Core.Log.Warning("[Cook] start: no recipe with a pourable ingredient."); return; }
            if (recipe.Ingredients.Count > st.IngredientSlots.Length) { Core.Log.Warning("[Cook] start: recipe '" + recipe.RecipeTitle + "' needs more slots than the station has."); return; }
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                StationRecipe.IngredientQuantity q = recipe.Ingredients[i];
                ItemDefinition d = q.Items[0];
                st.IngredientSlots[i].SetStoredItem(d.GetDefaultInstance(q.Quantity));
            }
            _station = st;
            ui.Open(st);
            MelonCoroutines.Start(Begin(ui, recipe));
        }

        // The screen sorts its recipes when the slots change; Begin reads the selected one.
        private static IEnumerator Begin(ChemistryStationInterface ui, StationRecipe recipe)
        {
            yield return null;
            if (ui.selectedRecipe == null || ui.selectedRecipe.Recipe == null) { Core.Log.Warning("[Cook] start: the screen selected no recipe - task not started."); yield break; }
            Transform ic = _station.ItemContainer;
            Core.Log.Msg("[Cook] start: physics syncs transforms on its own " + Physics.autoSyncTransforms + ", item container body "
                + (ic.GetComponentInParent<Rigidbody>() is Rigidbody icb ? "'" + icb.name + "' kinematic " + icb.isKinematic : "none") + ".");
            for (int i = 0; i < _station.IngredientTransforms.Length; i++)
                Core.Log.Msg("[Cook] start: spawn point " + i + " '" + _station.IngredientTransforms[i].name + "' at " + ic.InverseTransformPoint(_station.IngredientTransforms[i].position).ToString("F3") + ".");
            ui.BeginTask();
            Task t0 = TaskManager.Instance != null ? TaskManager.Instance.CurrentTask : null;
            UseChemistryStationTask task0 = t0 != null ? t0.TryCast<UseChemistryStationTask>() : null;
            if (task0 != null)
            {
                Spawned(task0, "set by the task");
                _watch = task0; _watchLeft = 24; _watchSteps = 0;
            }
            else Core.Log.Warning("[Cook] start: no chemistry task right after the screen began it - spawn not watched.");
            yield return null;
            Task t = TaskManager.Instance != null ? TaskManager.Instance.CurrentTask : null;
            Core.Log.Msg("[Cook] start: '" + ui.selectedRecipe.Recipe.RecipeTitle + "' begun on '" + _station.name + "', task "
                + (t != null ? t.GetIl2CppType().Name : "none") + ", chassis " + (RvMobile.Chassis != null ? "out" : "home") + ".");
            UseChemistryStationTask task = t != null ? t.TryCast<UseChemistryStationTask>() : null;
            if (task == null) yield break;
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) yield return new WaitForSeconds(0.5f);
                if (task.WasCollected || _station == null) yield break;
                Transform c = _station.ItemContainer;
                foreach (StationItem it in task.items)
                {
                    if (it == null) continue;
                    Rigidbody rb = it.GetComponentInChildren<Rigidbody>();
                    Core.Log.Msg("[Cook] start: item '" + it.name + "'" + (pass == 0 ? " spawned" : " after 0.5 s") + " at "
                        + (rb != null ? c.InverseTransformPoint(rb.worldCenterOfMass).ToString("F3") + ", speed " + rb.velocity.magnitude.ToString("F2") + ", spin " + rb.angularVelocity.magnitude.ToString("F2")
                            + ", up " + Vector3.Angle(rb.transform.up, c.up).ToString("F1") + " deg off" : c.InverseTransformPoint(it.transform.position).ToString("F3") + " (no body)") + ".");
                }
            }
        }

        // Each item's transform and its body's pose, in the station's item container.
        private static void Spawned(UseChemistryStationTask task, string when)
        {
            Transform c = _station.ItemContainer;
            foreach (StationItem it in task.items)
            {
                if (it == null) continue;
                Rigidbody rb = it.GetComponentInChildren<Rigidbody>();
                // Only transforms and settings: reading the body's pose would push the pending transform write into physics.
                Core.Log.Msg("[Cook] start: item '" + it.name + "' " + when + " (frame " + Time.frameCount + "): transform " + c.InverseTransformPoint(it.transform.position).ToString("F3")
                    + (rb != null ? ", body '" + rb.name + "' transform " + c.InverseTransformPoint(rb.transform.position).ToString("F3")
                        + " (kinematic " + rb.isKinematic + ", interpolation " + rb.interpolation + ")" : ", no body") + ".");
            }
        }

        // The first frames and physics steps after the task spawned its items: where their transforms are.
        private static UseChemistryStationTask _watch;
        private static int _watchLeft, _watchSteps;

        private static void Watch(string when)
        {
            if (_watch == null) return;
            if (_watch.WasCollected || _station == null) { _watch = null; Core.Log.Warning("[Cook] watch: the task went away."); return; }
            Transform c = _station.ItemContainer;
            string s = "";
            foreach (StationItem it in _watch.items)
            {
                if (it == null) continue;
                Rigidbody rb = it.GetComponentInChildren<Rigidbody>();
                s += " '" + it.name + "' " + c.InverseTransformPoint((rb != null ? rb.transform : it.transform).position).ToString("F3") + (rb != null ? " " + rb.interpolation : "");
            }
            Core.Log.Msg("[Cook] watch " + when + " frame " + Time.frameCount + ", " + _watchSteps + " step(s) since the spawn:" + s + ".");
            if (--_watchLeft <= 0) _watch = null;
        }

        /// <summary>Called first in each physics step, before the RV's cargo scan.</summary>
        internal static void BeforeSteps()
        {
            if (_watch == null) return;
            Watch("before step");
            _watchSteps++;
        }

        internal static void Tilt(float dir, float seconds, bool noInterp = false)
        {
            if (_station == null) { Core.Log.Warning("[Cook] tilt: no cook started - 'rvcook start' first."); return; }
            UseChemistryStationTask task = TaskManager.Instance != null && TaskManager.Instance.CurrentTask != null
                ? TaskManager.Instance.CurrentTask.TryCast<UseChemistryStationTask>() : null;
            if (task == null) { Core.Log.Warning("[Cook] tilt: no chemistry task runs."); return; }
            _target = null;
            foreach (StationItem it in task.items)
            {
                PourableModule p = it != null ? it.GetComponentInChildren<PourableModule>() : null;
                if (p == null) continue;
                Draggable d = it.GetComponentInChildren<Draggable>();
                if (d == null || d.Rb == null) continue;
                _target = d; _pour = p; _constraint = d.GetComponent<DraggableConstraint>();
                break;
            }
            if (_target == null) { Core.Log.Warning("[Cook] tilt: the task has no pourable item."); return; }
            Transform c = _constraint != null && _constraint.Container != null ? _constraint.Container : _station.ItemContainer;
            if (!Grab(task)) return;
            // Hold the pointer 15 cm above the grab point, the way a player lifts the bottle off the table.
            _aimLocal = c.InverseTransformPoint(_target.originalHitPoint) + c.InverseTransformDirection(c.up) * 0.15f;
            _task = task;
            _dir = Mathf.Sign(dir == 0f ? 1f : dir);
            _start = Time.time;
            _until = Time.time + seconds;
            _nextSample = Time.time;
            _maxTilt = 0f; _maxSpout = 0f; _minTheta = 180f; _maxTheta = 0f;
            _steps = _pouring = _writes = 0; _maxWrite = _sumWrite = 0f; _havePost = false;
            _touched.Clear(); _near.Clear(); _trace.Clear();
            _haveRaw = false; _sumAdv = _sumExpect = 0f; _parentMoves = _frames = 0;
            _interp = _target.Rb.interpolation;
            if (noInterp) { _target.Rb.interpolation = RigidbodyInterpolation.None; Core.Log.Msg("[Cook] tilt: interpolation of '" + _target.name + "' off for this tilt (was " + _interp + ")."); }
            _restoreInterp = noInterp;
            Rigidbody cb = RvMobile.Chassis != null ? RvMobile.Chassis.GetComponent<Rigidbody>() : null;
            Core.Log.Msg("[Cook] tilt: item body interpolation " + _target.Rb.interpolation + ", kinematic " + _target.Rb.isKinematic + ", parent body "
                + (_target.Rb.transform.parent != null && _target.Rb.transform.parent.GetComponentInParent<Rigidbody>() is Rigidbody pb ? pb.name + " (interpolation " + pb.interpolation + ", kinematic " + pb.isKinematic + ", constraints " + pb.constraints + ")" : "none")
                + ", chassis body " + (cb != null ? cb.name + " (interpolation " + cb.interpolation + ", kinematic " + cb.isKinematic + ", sleeping " + cb.IsSleeping() + ")" : "none") + ".");
            Holding = true;
            Core.Log.Msg("[Cook] tilt: '" + _target.name + "' (pour angle " + _pour.AngleFromUpToPour.ToString("F1") + ", torque " + _target.TorqueMultiplier.ToString("F0")
                + ", axis " + _target.RotationAxis + ") held for " + seconds.ToString("F1") + " s, key " + (_dir > 0 ? "right" : "left") + ", chassis " + (RvMobile.Chassis != null ? "out" : "home") + ".");
            MelonCoroutines.Start(AfterSteps());
        }

        // The click of Task.Update and Task.GetClickable: a ray from the camera onto the bottle, the hit point and grab
        // offset the task keeps, StartClick, and the task's held item. The item goes into the task's forced clickables, so
        // the task keeps it held although no mouse button is down.
        private static bool Grab(Task task)
        {
            Transform cam = PlayerSingleton<PlayerCamera>.Instance.Camera.transform;
            Vector3 to = _target.Rb.worldCenterOfMass - cam.position;
            RaycastHit hit = default;
            bool found = false;
            foreach (RaycastHit h in Physics.RaycastAll(cam.position, to.normalized, 3f, task.clickablesLayerMask, QueryTriggerInteraction.Collide))
            {
                Draggable d = h.collider != null ? h.collider.GetComponentInParent<Draggable>() : null;
                if (d == null || d.Pointer != _target.Pointer) continue;
                if (!found || h.distance < hit.distance) { hit = h; found = true; }
            }
            if (!found) { Core.Log.Warning("[Cook] tilt: the camera ray does not reach '" + _target.name + "' on the task layers - not grabbed."); return false; }
            if (!_target.ClickableEnabled) { Core.Log.Warning("[Cook] tilt: '" + _target.name + "' is not clickable in this step - not grabbed."); return false; }
            _target.SetOriginalHitPoint(hit.point);
            Vector3 offset = Vector3.zero;
            if (_target.AutoCalculateOffset)
            {
                offset = _target.transform.InverseTransformPoint(hit.point);
                if (_target.FlattenZOffset) offset.z = 0f;
            }
            task.relativeHitOffset = offset;
            task.forcedClickables.Add(_target);
            task.clickable = _target;
            _target.StartClick(hit);
            task.draggable = _target;
            task.constraint = _constraint;
            Core.Log.Msg("[Cook] tilt: grabbed '" + _target.name + "' at " + hit.point.ToString("F3") + " (" + hit.collider.name + "), held " + _target.IsHeld + ".");
            Rigidbody rb = _target.Rb;
            Transform c = _constraint != null && _constraint.Container != null ? _constraint.Container : _station.ItemContainer;
            string joints = "";
            foreach (Joint j in rb.GetComponents<Joint>()) joints += (joints.Length > 0 ? ", " : "") + j.GetIl2CppType().Name + " to " + (j.connectedBody != null ? j.connectedBody.name : "world");
            foreach (Joint j in UnityEngine.Object.FindObjectsOfType<Joint>()) if (j.connectedBody != null && j.connectedBody.Pointer == rb.Pointer) joints += (joints.Length > 0 ? ", " : "") + "from " + j.name + " (" + j.GetIl2CppType().Name + ")";
            Core.Log.Msg("[Cook] tilt: item '" + Path(rb.transform) + "' at " + c.InverseTransformPoint(rb.worldCenterOfMass).ToString("F3") + " in the station, up "
                + Vector3.Angle(rb.transform.up, c.up).ToString("F1") + " deg off, speed " + rb.velocity.magnitude.ToString("F2") + ", spin " + rb.angularVelocity.ToString("F2")
                + ", mass " + rb.mass.ToString("F2") + ", inertia " + rb.inertiaTensor.ToString("F4") + ", drag " + rb.drag.ToString("F1") + "/" + rb.angularDrag.ToString("F1")
                + ", constraints " + rb.constraints + ", joints " + (joints.Length > 0 ? joints : "none") + ", gravity " + rb.useGravity
                + ", align " + (_constraint != null && _constraint.AlignUpToContainerPlane) + ", clamp " + (_constraint != null && _constraint.ClampUpDirection) + ".");
            return true;
        }

        private static void Release()
        {
            Task task = _task;
            _task = null;
            if (_restoreInterp && _target != null && !_target.WasCollected) { _target.Rb.interpolation = _interp; _restoreInterp = false; Core.Log.Msg("[Cook] tilt: interpolation of '" + _target.name + "' back to " + _interp + "."); }
            if (task == null || task.WasCollected || _target == null || _target.WasCollected) { Core.Log.Msg("[Cook] tilt: nothing to let go."); return; }
            task.ForceEndClick(_target);
            task.clickable = null;
            task.draggable = null;
            Core.Log.Msg("[Cook] tilt: let go of '" + _target.name + "'.");
        }

        // The task pulls the held item toward the pointer: (point on the drag plane under the pointer - grab point) * 10
        // * DragForceMultiplier (Task.UpdateDraggablePhysics). The real mouse is not the probe's, so this adds the
        // difference between the point under the probe's pointer and the point under the real one. The sum is the pull
        // the task gives with the pointer on the probe's spot.
        private static void Steer(Transform c)
        {
            Camera cam = PlayerSingleton<PlayerCamera>.Instance.Camera;
            Vector3 normal = _constraint != null && _constraint.ProportionalZClamp ? _constraint.Container.forward
                : Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Plane plane = new Plane(normal, _target.originalHitPoint);
            Ray want = cam.ScreenPointToRay(cam.WorldToScreenPoint(c.TransformPoint(_aimLocal)));
            Ray real = cam.ScreenPointToRay(GameInput.GetPointerPosition());
            plane.Raycast(want, out float a);
            plane.Raycast(real, out float b);
            Vector3 force = (want.GetPoint(a) - real.GetPoint(b)) * 10f * _target.DragForceMultiplier;
            if (_target.DragForceOrigin != null) _target.Rb.AddForceAtPosition(force, _target.DragForceOrigin.position, ForceMode.Acceleration);
            else _target.Rb.AddForce(force, ForceMode.Acceleration);
        }

        // Each frame: the tilt key stays down.
        internal static void Frame()
        {
            if (_watch != null) Watch("late");
            if (!Holding) return;
            GameInput.MotionAxis = new Vector2(_dir, 0f);
            if (RvMobile.Chassis == null) return;
            Transform ct = RvMobile.Chassis.transform;
            if (_frames > 0 && ((ct.position - _lastChassisPos).sqrMagnitude > 1e-10f || Quaternion.Angle(ct.rotation, _lastChassisRot) > 1e-4f)) _parentMoves++;
            _frames++;
            _lastChassisPos = ct.position; _lastChassisRot = ct.rotation;
        }

        // After each physics step: the pose the simulation left, to catch writes before the next step.
        private static IEnumerator AfterSteps()
        {
            while (Holding)
            {
                yield return new WaitForFixedUpdate();
                if (_target == null || _target.WasCollected) yield break;
                _post = _target.Rb.rotation;
                _havePost = true;
                if (_haveRaw)
                {
                    _sumAdv += Quaternion.Angle(_raw, _post);
                    _sumExpect += _rawOmega * Time.fixedDeltaTime * Mathf.Rad2Deg;
                    _haveRaw = false;
                }
            }
        }

        internal static void FixedStep()
        {
            if (!Holding) return;
            if (_target == null || _target.WasCollected || _station == null) { Holding = false; Core.Log.Warning("[Cook] tilt: the item or station went away mid-tilt."); return; }
            GameInput.MotionAxis = new Vector2(_dir, 0f);
            Rigidbody rb = _target.Rb;
            Transform c = _constraint != null && _constraint.Container != null ? _constraint.Container : _station.ItemContainer;
            Steer(c);
            if (_havePost)
            {
                float w = Quaternion.Angle(_post, rb.rotation);
                if (w > 0.05f) { _writes++; _sumWrite += w; _maxWrite = Mathf.Max(_maxWrite, w); }
            }
            float tilt = Vector3.SignedAngle(c.up, Vector3.ProjectOnPlane(_target.transform.up, c.forward), c.forward);
            float spout = _pour.PourPoint != null ? Vector3.Angle(Vector3.up, _pour.PourPoint.forward) : -1f;
            Camera cam = PlayerSingleton<PlayerCamera>.Instance.Camera;
            Vector3 flat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            float theta = Vector3.Angle(flat, c.forward);
            theta = Mathf.Min(theta, 180f - theta);
            _maxTilt = Mathf.Max(_maxTilt, Mathf.Abs(tilt));
            _maxSpout = Mathf.Max(_maxSpout, spout);
            _minTheta = Mathf.Min(_minTheta, theta); _maxTheta = Mathf.Max(_maxTheta, theta);
            _steps++;
            if (_pour.IsPouring) _pouring++;
            int contacts = Touch(rb, tilt);
            _raw = rb.rotation; _rawOmega = rb.angularVelocity.magnitude; _haveRaw = true;
            if (Time.time >= _nextSample)
            {
                _nextSample = Time.time + 0.25f;
                _trace.Add((Time.time - _start).ToString("F2") + "s tilt " + tilt.ToString("F1") + " spout " + spout.ToString("F1") + " spin "
                    + rb.angularVelocity.magnitude.ToString("F2") + " about " + c.InverseTransformDirection(rb.angularVelocity).ToString("F2") + " (station axes) contacts " + contacts + (TaskHolds() ? "" : " NOT HELD"));
            }
            if (Time.time < _until) return;
            Holding = false;
            GameInput.MotionAxis = Vector2.zero;
            Report();
            Release();
        }

        private static bool TaskHolds()
        {
            Task t = TaskManager.Instance != null ? TaskManager.Instance.CurrentTask : null;
            return t != null && _target.IsHeld;
        }

        // Every solid collider within 2 cm of the item that may collide with it, first seen at which tilt.
        private static int Touch(Rigidbody rb, float tilt)
        {
            int n = 0;
            foreach (Collider mine in rb.GetComponentsInChildren<Collider>())
            {
                if (mine == null || !mine.enabled || mine.isTrigger || mine.attachedRigidbody == null || mine.attachedRigidbody.Pointer != rb.Pointer) continue;
                Bounds b = mine.bounds;
                foreach (Collider other in Physics.OverlapBox(b.center, b.extents + Vector3.one * 0.02f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (other == null || (other.attachedRigidbody != null && other.attachedRigidbody.Pointer == rb.Pointer)) continue;
                    if (Physics.GetIgnoreCollision(mine, other) || Physics.GetIgnoreLayerCollision(mine.gameObject.layer, other.gameObject.layer)) continue;
                    bool pen = Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth);
                    if (!pen)
                    {
                        // PhysX already pushes within the contact offset, before the shapes overlap.
                        Vector3 q = other.ClosestPoint(b.center);
                        float gap = Vector3.Distance(mine.ClosestPoint(q), q);
                        if (gap < 0.01f && !_near.ContainsKey(other.Pointer))
                            _near[other.Pointer] = Path(other.transform) + " [" + other.GetIl2CppType().Name + ", layer " + LayerMask.LayerToName(other.gameObject.layer)
                                + ", body " + (other.attachedRigidbody != null ? other.attachedRigidbody.name : "none") + "] gap " + (gap * 1000f).ToString("F0") + " mm at tilt " + tilt.ToString("F1") + ", by " + mine.name;
                        continue;
                    }
                    n++;
                    if (_touched.ContainsKey(other.Pointer)) continue;
                    _touched[other.Pointer] = Path(other.transform) + " [" + other.GetIl2CppType().Name + ", layer " + LayerMask.LayerToName(other.gameObject.layer)
                        + ", body " + (other.attachedRigidbody != null ? other.attachedRigidbody.name + (other.attachedRigidbody.isKinematic ? " kinematic" : " dynamic") : "none")
                        + "] first at tilt " + tilt.ToString("F1") + ", depth " + (depth * 1000f).ToString("F0") + " mm, by " + mine.name;
                }
            }
            return n;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 4 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        private static void Report()
        {
            foreach (string s in _trace) Core.Log.Msg("[Cook] trace " + s);
            foreach (string s in _touched.Values) Core.Log.Msg("[Cook] touched " + s);
            foreach (string s in _near.Values) Core.Log.Msg("[Cook] near " + s);
            Core.Log.Msg("[Cook] steps: the item turned " + _sumAdv.ToString("F0") + " deg in all steps, its spin gave " + _sumExpect.ToString("F0")
                + " deg (" + (_sumExpect > 0f ? (100f * _sumAdv / _sumExpect).ToString("F0") : "-") + "%). Chassis transform moved in " + _parentMoves + " of " + _frames + " frame(s).");
            Core.Log.Msg("[Cook] tilt result: " + (_pouring > 0 ? "POURED" : "NO POUR") + " in " + _pouring + " of " + _steps + " step(s). Tilt up to "
                + _maxTilt.ToString("F1") + " deg against the station, spout up to " + _maxSpout.ToString("F1") + " deg from up, pour angle "
                + _pour.AngleFromUpToPour.ToString("F1") + ". " + _touched.Count + " collider(s) touched. Pose changed outside the step in " + _writes
                + " step(s), " + (_writes > 0 ? (_sumWrite / _writes).ToString("F2") : "0") + " deg avg, " + _maxWrite.ToString("F2") + " max. Camera yaw off the tip axis "
                + _minTheta.ToString("F1") + ".." + _maxTheta.ToString("F1") + " deg. Held at the end " + TaskHolds() + ", chassis " + (RvMobile.Chassis != null ? "out" : "home") + ".");
        }

        internal static void Stop()
        {
            Holding = false;
            GameInput.MotionAxis = Vector2.zero;
            if (_task != null) Release();
            Task t = TaskManager.Instance != null ? TaskManager.Instance.CurrentTask : null;
            if (t == null) { Core.Log.Msg("[Cook] stop: no task runs."); return; }
            t.StopTask();
            Core.Log.Msg("[Cook] stop: task stopped.");
            MelonCoroutines.Start(CloseScreen());
        }

        // A stopped task opens the station screen again, as after a cook the player broke off.
        private static IEnumerator CloseScreen()
        {
            yield return null;
            ChemistryStationInterface ui = ChemistryStationInterface.Instance;
            if (ui == null || ui.ChemistryStation == null) { Core.Log.Msg("[Cook] stop: the station screen is closed."); yield break; }
            ui.Close();
            Core.Log.Msg("[Cook] stop: the station screen closed.");
        }
    }
}
#endif
