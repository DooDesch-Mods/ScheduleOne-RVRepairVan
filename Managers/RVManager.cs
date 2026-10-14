using System;

namespace RVRepairVan.Managers
{
    /// <summary>
    /// Locates the RV in the scene and performs the actual repair (state + visual swap).
    /// Static, backend-agnostic logic - no MonoBehaviour injection required.
    ///
    /// Scene layout (verified against decompiled v0.4.5f2):
    ///   @Properties/RV            -> root, carries the RV (Property) component
    ///     RV                      -> intact model
    ///     Destroyed RV            -> wreck model (active while destroyed)
    ///       CartelNote            -> note left on the wreck
    /// </summary>
    internal static class RVManager
    {
        private static Transform _root;        // @Properties/RV
        private static RV _rv;                  // RV component on the root
        private static Transform _model;        // child "RV"
        private static Transform _destroyed;    // child "Destroyed RV"
        private static Transform _cartelNote;   // Destroyed RV/CartelNote

        internal static bool IsReady => _root != null && _rv != null;

        /// <summary>The RV root transform (@Properties/RV), or null. Locates on demand.</summary>
        internal static Transform Root => TryLocate() ? _root : null;

        /// <summary>
        /// The intact model (child "RV"), wherever it is. Driven off its spot it rides on the RvMobile chassis and is
        /// no longer under the root, so a root.Find("RV") misses it.
        /// </summary>
        internal static Transform Model => TryLocate() ? (_model != null ? _model : _root.Find("RV")) : null;

        /// <summary>World position of the RV (used as a quest-marker fallback). Never throws.</summary>
        internal static bool TryGetPosition(out Vector3 position)
        {
            position = Vector3.zero;
            try
            {
                if (!TryLocate())
                {
                    return false;
                }
                position = _root.position;
                return true;
            }
            catch (Exception e)
            {
                Core.Swallowed("RVManager.TryGetPosition", e);
                return false;
            }
        }

        internal static void Reset()
        {
            _root = null;
            _rv = null;
            _model = null;
            _destroyed = null;
            _cartelNote = null;
        }

        /// <summary>Diagnostic: dump EVERY RV component (path + destroyed flags + children) so we can
        /// see which RV is the wrecked one and where it lives.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        internal static void LogState()
        {
            try
            {
                var rvs = UnityEngine.Object.FindObjectsOfType<RV>(true);
                int n = rvs == null ? 0 : rvs.Length;
                Core.Log.Msg($"[RVManager] DIAG: FindObjectsOfType<RV>(true) -> {n} RV component(s)");
                for (int i = 0; i < n; i++)
                {
                    RV rv = rvs[i];
                    if (rv == null) continue;
                    Transform t = ((Component)rv).transform;
                    bool dest = false, exp = false;
                    try { dest = rv.IsDestroyed; } catch (Exception e) { Core.Swallowed("RVManager.LogState.IsDestroyed", e); }
                    try { exp = rv._exploded; } catch (Exception e) { Core.Swallowed("RVManager.LogState._exploded", e); }
                    string children = "";
                    for (int c = 0; c < t.childCount; c++)
                    {
                        Transform ch = t.GetChild(c);
                        children += ch.name + "[" + (ch.gameObject.activeSelf ? "ON" : "off") + "] ";
                    }
                    Core.Log.Msg($"[RVManager] DIAG: RV#{i} path='{FullPath(t)}' activeInHierarchy={t.gameObject.activeInHierarchy} IsDestroyed={dest} _exploded={exp}");
                    Core.Log.Msg($"[RVManager] DIAG: RV#{i} children -> {children}");
                }

                GameObject props = GameObject.Find("@Properties");
                if (props != null)
                {
                    string kids = "";
                    for (int i = 0; i < props.transform.childCount; i++)
                    {
                        Transform ch = props.transform.GetChild(i);
                        kids += ch.name + "[" + (ch.gameObject.activeSelf ? "ON" : "off") + "] ";
                    }
                    Core.Log.Msg($"[RVManager] DIAG: @Properties children -> {kids}");
                }
            }
            catch (Exception e) { Core.Log.Warning("[RVManager] DIAG failed: " + e.Message); }
        }

        private static string FullPath(Transform t)
        {
            string p = t.name;
            Transform cur = t.parent;
            int guard = 0;
            while (cur != null && guard++ < 12) { p = cur.name + "/" + p; cur = cur.parent; }
            return p;
        }

        /// <summary>Find and cache the RV. Returns true once located. Never throws.</summary>
        internal static bool TryLocate()
        {
            if (IsReady)
            {
                return true;
            }

            try
            {
                GameObject props = GameObject.Find("@Properties");
                if (props == null)
                {
                    return false;
                }

                Transform root = props.transform.Find("RV");
                if (root == null)
                {
                    return false;
                }

                RV rv = root.GetComponent<RV>();
                if (rv == null)
                {
                    return false;
                }

                _root = root;
                _rv = rv;
                _model = root.Find("RV");
                _destroyed = root.Find("Destroyed RV");
                _cartelNote = _destroyed != null ? _destroyed.Find("CartelNote") : null;
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning("[RVManager] locate failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// True if the RV is currently in its destroyed/wrecked state.
        /// Never throws - it is called from the dialogue choice predicate, which must
        /// stay exception-free so a fresh game (RV not yet spawned/destroyed) is unaffected.
        /// </summary>
        internal static bool IsDestroyed()
        {
            try
            {
                if (!TryLocate())
                {
                    return false;
                }

                if (_destroyed != null && _destroyed.gameObject.activeSelf)
                {
                    return true;
                }

                return _rv.IsDestroyed;
            }
            catch (Exception e)
            {
                Core.Swallowed("RVManager.IsDestroyed", e);
                return false;
            }
        }

        /// <summary>
        /// Authoritative repair: clear the destroyed/exploded state AND swap visuals.
        /// Writing IsDestroyed is what makes the repair PERSIST (RV.ShouldSave/GetSaveString)
        /// and be correct for late joiners (OnSpawnServer pushes it) - so in co-op this must
        /// run on the HOST only (the host owns the RV instance + the save). Offline it runs
        /// locally exactly as before. Does NOT charge the player (caller handles payment).
        /// </summary>
        internal static bool Repair()
        {
            if (!TryLocate())
            {
                Core.Log.Warning("[RVManager] RV not found - cannot repair.");
                return false;
            }

            // State flags (interop exposes these private game fields as settable properties).
            try { _rv.IsDestroyed = false; }
            catch (Exception e) { Core.Log.Warning("[RVManager] set IsDestroyed failed: " + e.Message); }
            try { _rv._exploded = false; }
            catch (Exception e) { Core.Swallowed("RVManager.Repair._exploded", e); /* field may not exist on every build */ }

            return RepairVisualOnly();
        }

        /// <summary>
        /// Visual-only repair: swap the wreck model back to the intact model + re-enable FX,
        /// WITHOUT touching IsDestroyed/_exploded. Safe to run on any client (a client writing
        /// IsDestroyed neither replicates nor persists, so non-host clients only do the visual).
        /// Idempotent - re-running on an already-intact RV is a no-op.
        /// </summary>
        internal static bool RepairVisualOnly()
        {
            if (!TryLocate())
            {
                Core.Log.Warning("[RVManager] RV not found - cannot repair (visual).");
                return false;
            }

            // Visual swap: intact model on, wreck + note off.
            try
            {
                if (_model != null && !_model.gameObject.activeSelf)
                {
                    _model.gameObject.SetActive(true);
                }
                if (_destroyed != null && _destroyed.gameObject.activeSelf)
                {
                    _destroyed.gameObject.SetActive(false);
                }
                if (_cartelNote != null && _cartelNote.gameObject.activeSelf)
                {
                    _cartelNote.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning("[RVManager] visual swap failed: " + e.Message);
            }

            // Re-enable the FX container if the explosion disabled it.
            try
            {
                if (_rv.FXContainer != null && !_rv.FXContainer.gameObject.activeSelf)
                {
                    _rv.FXContainer.gameObject.SetActive(true);
                }
            }
            catch (Exception e) { Core.Swallowed("RVManager.RepairVisualOnly.FXContainer", e); /* FXContainer optional */ }

            Base.RvLiner.Ensure();
            Core.Log.Msg("[RVManager] RV repaired (visual).");
            return true;
        }

#if DEBUG
        /// <summary>
        /// Debug helper: wreck the RV so the repair flow can be tested without progressing
        /// to the story destruction event. Sets the destroyed state and swaps to the wreck visual.
        /// </summary>
        internal static bool Destroy()
        {
            if (!TryLocate())
            {
                Core.Log.Warning("[RVManager] RV not found - cannot destroy.");
                return false;
            }

            try { _rv.IsDestroyed = true; }
            catch (Exception e) { Core.Log.Warning("[RVManager] set IsDestroyed(true) failed: " + e.Message); }
            try { _rv._exploded = true; }
            catch (Exception e) { Core.Swallowed("RVManager.Destroy._exploded", e); /* field may not exist on every build */ }

            try
            {
                if (_destroyed != null && !_destroyed.gameObject.activeSelf)
                {
                    _destroyed.gameObject.SetActive(true);
                }
                if (_model != null && _model.gameObject.activeSelf)
                {
                    _model.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning("[RVManager] destroy visual swap failed: " + e.Message);
            }

            Core.Log.Msg("[RVManager] RV wrecked (debug).");
            return true;
        }
#endif
    }
}
