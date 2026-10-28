using DooDesch.Localization;
using Il2CppScheduleOne.Interaction;
using RVRepairVan.Managers;

namespace RVRepairVan.Base
{
    /// <summary>
    /// Small props that nod at a certain TV show about a chemistry teacher and his RV. All cosmetic and local to
    /// each machine, so co-op needs nothing.
    ///
    /// Under <c>rv/Main</c> of the intact model (x along the RV, front +x; y across, door side +y; z up), so they
    /// travel with the RV and the wreck has none: a book on the dashboard, the house number 308 beside the door,
    /// patched bullet holes beside the door, and, once the interior is gutted, a hazmat suit and a respirator on
    /// hooks. The pizza lies on a garage roof in town, in world space.
    ///
    /// Everything is built from primitives and textures drawn here, except the suit and the respirator: those are
    /// the game's own avatar accessories, loaded from Resources and drawn as static meshes.
    /// </summary>
    internal static class RvEasterEggs
    {
        private const string RootName = "RVRepairVan_EasterEggs";
        private const string PizzaName = "RVRepairVan_RoofPizza";

        private sealed class Spot
        {
            public string Name;
            public Vector3 Pos, Out, Up;
            public Spot(string name, Vector3 pos, Vector3 @out, Vector3 up) { Name = name; Pos = pos; Out = @out; Up = up; }
        }

        // rv/Main space. Each prop is built facing its own +z with +y as its top; Out and Up turn those into the RV.
        // The book's height comes from a ray onto the dashboard; z here is only the fallback.
        // The door-side wall: its inner face is at y 1.575 (the save intercom shares it), the ribbed outer skin at 1.665.
        private static readonly Spot Book = new Spot("Book", new Vector3(5.2f, 0.75f, 0.0f), new Vector3(0f, 0f, 1f), new Vector3(0.34f, 0.94f, 0f));
        private static readonly Spot Number = new Spot("HouseNumber", new Vector3(1.2f, 1.665f, 0.12f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
        private static readonly Spot Holes = new Spot("BulletHoles", new Vector3(1.05f, 1.665f, -0.4f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f));
        private static readonly Spot Gear = new Spot("CookGear", new Vector3(-2.0f, 1.563f, 0.85f), new Vector3(0f, -1f, 0f), new Vector3(0f, 0f, 1f));

        // The avatar accessories are modelled z-up; this stands them upright with their front out of the wall.
        private static Vector3 _meshEuler = new Vector3(-90f, 0f, 0f);

        // The suit is modelled in a T-pose. Its arms are swung down by this many degrees from the horizontal; a bone
        // counts as an arm bone when it sits further out from the middle than this share of the suit's height and
        // in its upper part.
        private static float _armDrop = 84f;
        private static float _armShare = 0.08f;

        // The empty suit: squeezed front to back (1 = as worn), its height in metres, how far its collar top sits
        // below the peg (negative: above it, so the peg goes into the collar and the suit hangs on it), how much
        // higher than the old spot it hangs so the hem swings clear of the floor, and its lean in degrees, top out on
        // the hook, hem back against the wall.
        private static float _suitFlat = 0.3f;
        private static float _suitSize = 1.35f;
        private static float _suitDrop = -0.015f;
        private static float _suitLift = 0.15f;
        private static float _suitTilt = 3f;

        // Nobody stands inside it: the legs swing in at the hip until this share of the gap between the feet is
        // closed, one sleeve hangs this many degrees steeper than the other, and the suit rolls this many degrees
        // sideways on its hook.
        private static float _legPull = 0.7f;
        private static float _armSkew = 5f;
        private static float _suitRoll = 2f;

        // World space: the garage roof beside the Wilkinson house. y is found by a ray from above, so only x/z and the turn matter.
        private static Vector3 _pizzaAt = new Vector3(65.5f, 0f, -126.8f);
        private static float _pizzaYaw = 35f;

        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private static Mesh _disc;

        /// <summary>Scene reset: materials and meshes went with the scene.</summary>
        internal static void Reset() { _mats.Clear(); _disc = null; }

        /// <summary>Build the props once per model; later calls only switch the gear to match the gutted state.</summary>
        internal static void Ensure()
        {
            try
            {
                Transform model = RVManager.Model;
                if (model == null) { Core.LogDebug("[Eggs] RV model not found yet - will retry on the next apply."); return; }
                Transform main = model.Find("rv/Main");
                if (main == null) { Core.Log.Warning("[Eggs] 'rv/Main' not found under the RV model - no props."); return; }

                Transform root = main.Find(RootName);
                if (root == null)
                {
                    Material template = Template(main);
                    if (template == null) { Core.Log.Warning("[Eggs] no RV material to copy - no props."); return; }
                    var go = new GameObject(RootName);
                    go.layer = main.gameObject.layer;
                    go.transform.SetParent(main, false);
                    root = go.transform;
                    int built = 0;
                    if (BuildBook(root, template)) built++;
                    if (BuildNumber(root, template)) built++;
                    if (BuildHoles(root, template)) built++;
                    if (BuildGear(root, template)) built++;
                    Core.Log.Msg("[Eggs] " + built + " of 4 prop group(s) placed in the RV.");
                }
                SyncGear(root);
                EnsurePizza(main);
            }
            catch (Exception e) { Core.Log.Error("[Eggs] placing the props failed: " + e); }
        }

        /// <summary>Is this piece part of the cook gear (suit, respirator, their hooks)? It hangs inside, on the wall.</summary>
        internal static bool IsGear(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (t.name == Gear.Name) return true;
                if (t.name == RootName) return false;
            }
            return false;
        }

        private static void SyncGear(Transform root)
        {
            Transform gear = root.Find(Gear.Name);
            if (gear == null) return;
            // The gear sits on floor that a fitted part would cover; it only shows on free floor, and not after the
            // player took it out at the bodyshop.
            bool show = !RvInterior.GearRemoved && !RvInterior.InFittedPart(gear.position, 0.15f);
            if (gear.gameObject.activeSelf == show) return;
            gear.gameObject.SetActive(show);
            Core.LogDebug("[Eggs] cook gear " + (show ? "shown" : "hidden") + " (taken out=" + RvInterior.GearRemoved + ").");
        }

        // --- the props -------------------------------------------------------------------------------------

        private static Transform Group(Transform root, Spot spot)
        {
            var go = new GameObject(spot.Name);
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            Place(go.transform, spot);
            return go.transform;
        }

        private static void Place(Transform t, Spot spot)
        {
            t.localPosition = spot.Pos;
            t.localRotation = Quaternion.LookRotation(spot.Out, spot.Up);
        }

        // A closed book: dark green cover, cream pages showing on three sides. The hover line names it.
        private static bool BuildBook(Transform root, Material template)
        {
            Transform g = Group(root, Book);
            OntoDash(root.parent, g);
            Box(g, "Cover", new Vector3(0f, 0f, 0.016f), new Vector3(0.155f, 0.215f, 0.004f), Paint(template, "cover", new Color(0.16f, 0.36f, 0.22f)));
            Box(g, "Back", new Vector3(0f, 0f, 0.002f), new Vector3(0.155f, 0.215f, 0.004f), Paint(template, "cover", new Color(0.09f, 0.20f, 0.13f)));
            Box(g, "Spine", new Vector3(-0.0755f, 0f, 0.009f), new Vector3(0.004f, 0.215f, 0.018f), Paint(template, "cover", new Color(0.09f, 0.20f, 0.13f)));
            Box(g, "Pages", new Vector3(0.002f, 0f, 0.009f), new Vector3(0.147f, 0.207f, 0.011f), Paint(template, "pages", new Color(0.93f, 0.89f, 0.78f)));
            Box(g, "Band", new Vector3(0f, 0.06f, 0.0182f), new Vector3(0.155f, 0.012f, 0.0005f), Paint(template, "gold", new Color(0.78f, 0.62f, 0.25f)));

            var hit = new GameObject("Hover");
            hit.layer = RvMobile.InteractionLayer();
            hit.transform.SetParent(g, false);
            hit.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            var col = hit.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.2f, 0.26f, 0.06f);
            var io = hit.AddComponent<InteractableObject>();
            io.SetMessage(L10n.T("Leaves of Grass. Signed 'W.W.'"));
            io.SetInteractableState(InteractableObject.EInteractableState.Label);
            io.MaxInteractionRange = 2.5f;
            return true;
        }

        // Drops the book onto whatever the dashboard has under its spot; without a collider it keeps the stored height.
        private static void OntoDash(Transform main, Transform book)
        {
            Vector3 from = main.TransformPoint(Book.Pos + new Vector3(0f, 0f, 1.2f));
            Vector3 down = main.TransformDirection(Vector3.back);
            foreach (RaycastHit h in Physics.RaycastAll(from, down, 2.5f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider.transform.IsChildOf(book)) continue;
                if (h.collider.name.IndexOf("Chair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (h.collider.name.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                float lz = main.InverseTransformPoint(h.point).z;
                if (lz > 0.2f || lz < -0.4f) continue;   // the front collision box and the floor are not the dashboard
                book.position = h.point + main.TransformDirection(Vector3.forward) * 0.002f;
                Core.LogDebug("[Eggs] book rests on '" + h.collider.name + "' at local " + main.InverseTransformPoint(h.point).ToString("F2") + ".");
                return;
            }
            Core.Log.Warning("[Eggs] nothing under the book's spot on the dashboard - it keeps the stored height.");
        }

        // A brass house number on a dark plate, outside beside the door.
        private static bool BuildNumber(Transform root, Material template)
        {
            Transform g = Group(root, Number);
            Box(g, "Plate", new Vector3(0f, 0f, 0.003f), new Vector3(0.24f, 0.11f, 0.006f), Paint(template, "plate", new Color(0.08f, 0.08f, 0.09f)));
            Material digits = Paint(template, "308", Color.white, NumberTexture());
            Face(g, "Digits", new Vector3(0f, 0f, 0.0062f), new Vector2(0.225f, 0.098f), Quad(), digits);
            return true;
        }

        // Four holes in the door skin; two of them taped over with stickers, the way you hide them from a cop.
        private static bool BuildHoles(Transform root, Material template)
        {
            Transform g = Group(root, Holes);
            Material rim = Paint(template, "rim", new Color(0.42f, 0.42f, 0.42f));
            Material hole = Paint(template, "hole", new Color(0.02f, 0.02f, 0.02f));
            Vector2[] at = { new Vector2(-0.08f, 0.06f), new Vector2(0.09f, -0.03f), new Vector2(0.015f, -0.165f), new Vector2(0.165f, 0.135f) };
            for (int i = 0; i < at.Length; i++)
            {
                Face(g, "Rim" + i, new Vector3(at[i].x, at[i].y, 0.001f), new Vector2(0.05f, 0.05f), Disc(), rim);
                Face(g, "Hole" + i, new Vector3(at[i].x, at[i].y, 0.0016f), new Vector2(0.03f, 0.03f), Disc(), hole);
            }
            Material smiley = Paint(template, "smiley", Color.white, SmileyTexture());
            Face(g, "Sticker0", new Vector3(0.09f, -0.03f, 0.0024f), new Vector2(0.11f, 0.11f), Disc(), smiley);
            Material stripe = Paint(template, "stripe", Color.white, StripeTexture());
            var s1 = Face(g, "Sticker1", new Vector3(0.015f, -0.165f, 0.0024f), new Vector2(0.13f, 0.07f), Quad(), stripe);
            s1.localRotation *= Quaternion.Euler(0f, 0f, -12f);
            return true;
        }

        // Two hooks on the wall: the hazmat suit on one, the respirator on the other, clear of the suit's arms.
        private static bool BuildGear(Transform root, Material template)
        {
            Transform g = Group(root, Gear);
            Material steel = Paint(template, "steel", new Color(0.55f, 0.56f, 0.58f));
            Box(g, "HookPlateA", new Vector3(0f, _suitLift, 0.004f), new Vector3(0.12f, 0.05f, 0.008f), steel);
            Box(g, "PegA", new Vector3(0f, _suitLift, 0.03f), new Vector3(0.012f, 0.012f, 0.05f), steel);
            Box(g, "HookPlateB", new Vector3(0.7f, -0.25f, 0.004f), new Vector3(0.12f, 0.05f, 0.008f), steel);
            Box(g, "PegB", new Vector3(0.7f, -0.25f, 0.03f), new Vector3(0.012f, 0.012f, 0.05f), steel);

            int hung = 0;
            if (Hang(g, "HazmatSuit", "avatar/accessories/waist/hazmatsuit/HazmatSuit", new Vector3(0f, _suitLift - _suitDrop, 0.08f), _suitSize, true)) hung++;
            else Core.Log.Warning("[Eggs] no suit, so no wire hook either.");
            if (_suitDrop > 0.005f) Box(g, "WireHook", new Vector3(0f, _suitLift - 0.5f * _suitDrop, 0.05f), new Vector3(0.006f, _suitDrop + 0.01f, 0.006f), steel);
            if (Hang(g, "Respirator", "avatar/accessories/head/respirator/Respirator", new Vector3(0.7f, -0.26f, 0.07f), 0.24f, false)) hung++;
            Core.Log.Msg("[Eggs] cook gear: " + hung + " of 2 accessories hung.");
            return hung > 0;
        }

        // The accessory is a skinned mesh bound to the avatar; drawn static, it keeps its bind pose. Its top sits on
        // the peg, its front faces into the room, and it is scaled so its tallest side is `size` metres.
        // The mesh is turned upright first, and the offset that puts its top on the peg is taken after that turn.
        private static bool Hang(Transform parent, string name, string resource, Vector3 peg, float size, bool lowerArms)
        {
            UnityEngine.Object asset = Resources.Load(resource);
            GameObject prefab = asset != null ? asset.TryCast<GameObject>() : null;
            if (prefab == null) { Core.Log.Warning("[Eggs] '" + resource + "' not in Resources - no " + name + " on the hook."); return false; }
            SkinnedMeshRenderer pick = null;
            foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                if (pick == null || smr.name.EndsWith("LOD0")) pick = smr;
                if (smr.name.EndsWith("LOD0")) break;
            }
            if (pick == null) { Core.Log.Warning("[Eggs] '" + resource + "' has no skinned mesh - no " + name + " on the hook."); return false; }

            Mesh mesh = pick.sharedMesh;
            Bounds b = mesh.bounds;
            float tall = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float scale = tall > 0.0001f ? size / tall : 1f;

            // The hook group lies in the wall plane (+z out of the wall, +y up the wall). The avatar mesh is y-up and
            // faces +z, which is out of the wall here once it is turned upright.
            var holder = new GameObject(name);
            holder.layer = parent.gameObject.layer;
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = peg;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one * scale;

            var go = new GameObject(name + "_Mesh");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(holder.transform, false);
            Quaternion turn = Quaternion.Euler(_meshEuler);
            go.transform.localRotation = turn;
            float top = float.MinValue;
            Vector3 mid = turn * b.center;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                top = Mathf.Max(top, (turn * corner).y);
            }
            go.transform.localPosition = -new Vector3(mid.x, top, mid.z);
            if (lowerArms)
            {
                Flatten(holder.transform, go.transform, b, turn, peg.z);
                if (Mathf.Abs(_suitTilt) > 0.01f)
                {
                    Vector3 hp = holder.transform.localPosition;
                    holder.transform.localPosition = new Vector3(hp.x, hp.y, hp.z + size * Mathf.Sin(_suitTilt * Mathf.Deg2Rad));
                }
                holder.transform.localRotation = Quaternion.Euler(_suitTilt, 0f, _suitRoll);
            }
            if (!(lowerArms && PoseArms(go, pick)))
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = pick.sharedMaterials;
            }
            Core.Log.Msg("[Eggs] " + name + " from '" + pick.name + "': mesh " + mesh.name + " size " + b.size + ", scale " + scale.ToString("0.###") + ".");
            return true;
        }

        // An empty suit hangs flat against the wall: the mesh is squeezed along its front-to-back axis by _suitFlat,
        // and the holder moves so the squeezed back sits just off the wall.
        private static void Flatten(Transform holder, Transform go, Bounds b, Quaternion turn, float pegZ)
        {
            if (_suitFlat >= 0.999f) return;
            bool latX = b.size.x >= b.size.y;
            Vector3 squeeze = latX ? new Vector3(1f, _suitFlat, 1f) : new Vector3(_suitFlat, 1f, 1f);
            go.localScale = squeeze;
            Vector3 c = turn * Vector3.Scale(b.center, squeeze);
            Vector3 lp = go.localPosition;
            go.localPosition = new Vector3(lp.x, lp.y, -c.z);
            float half = Mathf.Abs((turn * Vector3.Scale(b.extents, squeeze)).z) * holder.localScale.z;
            Vector3 hp = holder.localPosition;
            holder.localPosition = new Vector3(hp.x, hp.y, Mathf.Min(pegZ, half + 0.02f));
            Core.Log.Msg("[Eggs] suit flattened to " + _suitFlat.ToString("0.00") + ", " + (2f * half).ToString("0.000") + " m deep.");
        }

        // The suit is modelled in a T-pose and its mesh cannot be read back, so it is skinned live: one bone per bind
        // pose under the mesh object, each in its bind pose, except the arm bones, which are swung down about their
        // shoulder. False when the mesh has no bind poses or no arm bones on both sides; the caller then draws it
        // static in its T-pose.
        private static bool PoseArms(GameObject go, SkinnedMeshRenderer pick)
        {
            Mesh mesh = pick.sharedMesh;
            var binds = mesh.bindposes;
            int n = binds.Length;
            if (n == 0) { Core.Log.Warning("[Eggs] '" + mesh.name + "' has no bind poses - the suit keeps its T-pose."); return false; }

            // z is up in the mesh; the arms lie along the wider of x and y.
            Bounds b = mesh.bounds;
            Vector3 lat = b.size.x >= b.size.y ? Vector3.right : Vector3.up;
            Vector3 up = Vector3.forward;
            float mid = Vector3.Dot(b.center, lat), reach = _armShare * b.size.z, high = b.min.z + 0.6f * b.size.z;

            var bind = new Matrix4x4[n];
            var side = new int[n];   // 0 = not an arm, 1 = +lat side, 2 = -lat side
            var shoulder = new Vector3[3];
            var nearest = new[] { 0f, float.MaxValue, float.MaxValue };
            string arms = "";
            for (int i = 0; i < n; i++)
            {
                bind[i] = binds[i].inverse;
                Vector3 at = bind[i].GetColumn(3);
                float off = Vector3.Dot(at, lat) - mid;
                if (Mathf.Abs(off) < reach || at.z < high) continue;
                side[i] = off > 0f ? 1 : 2;
                arms += " " + i + ":" + (off / b.size.z).ToString("0.00");
                if (Mathf.Abs(off) < nearest[side[i]]) { nearest[side[i]] = Mathf.Abs(off); shoulder[side[i]] = at; }
            }
            if (nearest[1] == float.MaxValue || nearest[2] == float.MaxValue)
            {
                Core.Log.Warning("[Eggs] no arm bones on both sides of '" + mesh.name + "' (" + n + " bones, arms:" + arms + ") - the suit keeps its T-pose.");
                return false;
            }

            var swing = new Matrix4x4[5];   // by side, and 3/4 for the +lat/-lat leg
            swing[0] = Matrix4x4.identity;
            for (int sd = 1; sd <= 2; sd++)
            {
                float drop = (_armDrop + (sd == 2 ? _armSkew : 0f)) * Mathf.Deg2Rad;
                Vector3 outward = sd == 1 ? lat : -lat;
                Quaternion turn = Quaternion.FromToRotation(outward, (outward * Mathf.Cos(drop) - up * Mathf.Sin(drop)).normalized);
                swing[sd] = Matrix4x4.Translate(shoulder[sd]) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-shoulder[sd]);
            }

            // The legs: every bone in the lower half off the middle. Each leg turns about its highest bone (the hip)
            // so its lowest bone (the foot) moves _legPull of the way to the middle.
            float waist = b.min.z + 0.5f * b.size.z, legGap = 0.02f * b.size.z;
            var hip = new Vector3[5];
            var foot = new Vector3[5];
            var found = new bool[5];
            int legs = 0;
            for (int i = 0; i < n; i++)
            {
                if (side[i] != 0) continue;
                Vector3 at = bind[i].GetColumn(3);
                float off = Vector3.Dot(at, lat) - mid;
                if (at.z > waist || Mathf.Abs(off) < legGap) continue;
                int lg = off > 0f ? 3 : 4;
                side[i] = lg;
                legs++;
                if (!found[lg]) { hip[lg] = at; foot[lg] = at; found[lg] = true; }
                if (at.z > hip[lg].z) hip[lg] = at;
                if (at.z < foot[lg].z) foot[lg] = at;
            }
            for (int lg = 3; lg <= 4; lg++)
            {
                swing[lg] = Matrix4x4.identity;
                if (!found[lg] || hip[lg].z - foot[lg].z < 0.01f) continue;
                Vector3 hang = foot[lg] - hip[lg];
                float footOff = Vector3.Dot(foot[lg], lat) - mid;
                Quaternion turn = Quaternion.FromToRotation(hang, hang - lat * footOff * _legPull);
                swing[lg] = Matrix4x4.Translate(hip[lg]) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-hip[lg]);
            }
            if (!found[3] || !found[4]) Core.Log.Warning("[Eggs] no leg bones on both sides of '" + mesh.name + "' - the legs keep their stance.");

            var bones = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                Matrix4x4 m = swing[side[i]] * bind[i];
                var bone = new GameObject("Bone" + i).transform;
                bone.SetParent(go.transform, false);
                bone.localPosition = m.GetColumn(3);
                bone.localRotation = m.rotation;
                bone.localScale = m.lossyScale;
                bones[i] = bone;
            }
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = go.transform;
            smr.sharedMaterials = pick.sharedMaterials;
            smr.updateWhenOffscreen = true;
            Core.Log.Msg("[Eggs] suit arms lowered " + _armDrop.ToString("0") + "/" + (_armDrop + _armSkew).ToString("0") + " degrees, legs pulled in "
                + _legPull.ToString("0.00") + ": " + n + " bones, " + legs + " leg bones, arm bones" + arms + ".");
            return true;
        }

        // --- the pizza ---------------------------------------------------------------------------------------

        private static void EnsurePizza(Transform main)
        {
            if (_pizzaAt == Vector3.zero) return;
            if (GameObject.Find(PizzaName) != null) return;
            Material template = Template(main);
            if (template == null) { Core.Log.Warning("[Eggs] no material for the pizza."); return; }

            Vector3 at = _pizzaAt;
            string under = "nothing";
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(new Vector3(at.x, at.y + 30f, at.z), Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                at = hit.point;
                normal = hit.normal;
                under = "'" + hit.collider.name + "'";
            }
            else Core.Log.Warning("[Eggs] no roof under the pizza spot " + _pizzaAt + " - it floats at the stored height.");

            var go = new GameObject(PizzaName);
            go.transform.position = at + normal * 0.01f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, _pizzaYaw, 0f) * Quaternion.Euler(-90f, 0f, 0f);   // +z along the roof normal
            Material crust = Paint(template, "crust", new Color(0.62f, 0.40f, 0.18f));
            Face(go.transform, "Crust", new Vector3(0f, 0f, 0.008f), new Vector2(0.46f, 0.46f), Disc(), crust);
            Face(go.transform, "Top", new Vector3(0f, 0f, 0.016f), new Vector2(0.44f, 0.44f), Disc(), Paint(template, "pizza", Color.white, PizzaTexture()));
            Face(go.transform, "Under", new Vector3(0f, 0f, 0.001f), new Vector2(0.46f, 0.46f), Disc(), crust).localRotation = Quaternion.identity;
            Core.Log.Msg("[Eggs] pizza placed on " + under + " at " + go.transform.position + ", roof normal " + normal.ToString("F2") + ".");
        }

        // --- building blocks ---------------------------------------------------------------------------------

        private static Material Template(Transform main)
        {
            Transform interior = main.Find("Interior");
            Renderer r = interior != null ? interior.GetComponent<Renderer>() : null;
            if (r == null) r = main.GetComponentInChildren<MeshRenderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        // The RV's own lit material, stripped of its textures, in one colour and optionally one texture of ours.
        private static Material Paint(Material template, string key, Color colour, Texture2D tex = null)
        {
            if (_mats.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(template) { name = "RVRepairVan_Egg_" + key };
            foreach (string t in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                if (m.HasProperty(t)) m.SetTexture(t, null);
            m.DisableKeyword("_NORMALMAP");
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            _mats[key] = m;
            return m;
        }

        private static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = parent.gameObject.layer;
            UnityEngine.Object.Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(parent, false);
            box.transform.localPosition = pos;
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return box.transform;
        }

        // A flat shape whose visible side faces +z of the parent (out of the wall, up from the dash).
        private static Transform Face(Transform parent, string name, Vector3 pos, Vector2 size, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // the meshes face -z, like Unity's quad
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private static Mesh _quad;

        // Unit quad facing -z, like Unity's own quad.
        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            var m = new Mesh { name = "RVRepairVan_EggQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return _quad = m;
        }

        // Unit-diameter disc facing -z, with planar UVs.
        private static Mesh Disc()
        {
            if (_disc != null) return _disc;
            const int N = 32;
            var v = new Vector3[N + 1];
            var uv = new Vector2[N + 1];
            var n = new Vector3[N + 1];
            var tri = new int[N * 3];
            v[0] = Vector3.zero; uv[0] = new Vector2(0.5f, 0.5f); n[0] = Vector3.back;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                v[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                n[i + 1] = Vector3.back;
                tri[i * 3] = 0;
                tri[i * 3 + 1] = 1 + (i + 1) % N;
                tri[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { name = "RVRepairVan_EggDisc" };
            m.vertices = v; m.uv = uv; m.normals = n; m.triangles = tri;
            m.RecalculateBounds();
            return _disc = m;
        }

        // --- textures ----------------------------------------------------------------------------------------

        private static Texture2D NewTex(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "RVRepairVan_Egg_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        private static readonly string[] Glyph3 = { "01110", "10001", "00001", "00110", "00001", "10001", "01110" };
        private static readonly string[] Glyph0 = { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
        private static readonly string[] Glyph8 = { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };

        private static Texture2D NumberTexture()
        {
            const int W = 256, H = 112, Cell = 12;
            Texture2D tex = NewTex(W, H, "308");
            var px = new Color32[W * H];
            var plate = new Color32(22, 22, 25, 255);
            var brass = new Color32(201, 160, 72, 255);
            for (int i = 0; i < px.Length; i++) px[i] = plate;
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
                if (x < 6 || y < 6 || x >= W - 6 || y >= H - 6) px[y * W + x] = brass;
            string[][] glyphs = { Glyph3, Glyph0, Glyph8 };
            int textW = 3 * 5 * Cell + 2 * Cell, x0 = (W - textW) / 2, y0 = (H + 7 * Cell) / 2;
            for (int g = 0; g < 3; g++)
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (glyphs[g][row][col] != '1') continue;
                        int cx = x0 + g * 6 * Cell + col * Cell, cy = y0 - (row + 1) * Cell;
                        for (int dx = 1; dx < Cell - 1; dx++) for (int dy = 1; dy < Cell - 1; dy++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x >= 0 && y >= 0 && x < W && y < H) px[y * W + x] = brass;
                        }
                    }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D SmileyTexture()
        {
            const int S = 128;
            Texture2D tex = NewTex(S, S, "smiley");
            var px = new Color32[S * S];
            var yellow = new Color32(250, 210, 40, 255);
            var ink = new Color32(30, 25, 20, 255);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    bool eye = Sq(u + 0.32f, v - 0.28f) < 0.012f || Sq(u - 0.32f, v - 0.28f) < 0.012f;
                    bool smile = r > 0.5f && r < 0.62f && v < -0.1f;
                    bool edge = r > 0.9f;
                    px[y * S + x] = eye || smile || edge ? ink : yellow;
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D StripeTexture()
        {
            const int W = 128, H = 64;
            Texture2D tex = NewTex(W, H, "stripe");
            var px = new Color32[W * H];
            var red = new Color32(196, 38, 40, 255);
            var white = new Color32(240, 236, 228, 255);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = (y > 20 && y < 44) ? white : red;
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D PizzaTexture()
        {
            const int S = 256;
            Texture2D tex = NewTex(S, S, "pizza");
            var px = new Color32[S * S];
            var crust = new Color32(196, 132, 62, 255);
            var cheese = new Color32(246, 206, 96, 255);
            var sauce = new Color32(186, 56, 30, 255);
            var pep = new Color32(150, 30, 26, 255);
            Vector2[] slices = { new Vector2(0.35f, 0.2f), new Vector2(-0.3f, 0.4f), new Vector2(-0.45f, -0.2f), new Vector2(0.1f, -0.5f),
                new Vector2(0.5f, -0.25f), new Vector2(0.0f, 0.05f), new Vector2(-0.05f, 0.62f), new Vector2(0.58f, 0.42f) };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    Color32 c = r > 0.86f ? crust : cheese;
                    if (r <= 0.86f && Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 7f) > 0.66f) c = sauce;
                    if (r <= 0.82f)
                        foreach (Vector2 p in slices)
                            if (Sq(u - p.x, v - p.y) < 0.012f) { c = pep; break; }
                    px[y * S + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        private static float Sq(float a, float b) => a * a + b * b;

#if DEBUG
        /// <summary>
        /// Debug (rvegg): report the props; 'at &lt;prop&gt; x y z [ox oy oz ux uy uz]' moves one in rv/Main space;
        /// 'pizza x z [yaw]' drops the pizza on whatever roof is under that world point; 'probe' logs the dashboard
        /// and the door in rv/Main space.
        /// </summary>
        internal static void DebugCmd(string[] args)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Transform model = RVManager.Model;
            Transform main = model != null ? model.Find("rv/Main") : null;
            Transform root = main != null ? main.Find(RootName) : null;
            string a = args.Length > 0 ? args[0].ToLower() : "";

            if (a == "at" && args.Length >= 5 && root != null)
            {
                Spot spot = new[] { Book, Number, Holes, Gear }.FirstOrDefault(s => s.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                if (spot == null) { Core.Log.Warning("[Debug] rvegg at: no prop '" + args[1] + "' (Book, HouseNumber, BulletHoles, CookGear)."); return; }
                spot.Pos = new Vector3(float.Parse(args[2], ci), float.Parse(args[3], ci), float.Parse(args[4], ci));
                if (args.Length >= 11)
                {
                    spot.Out = new Vector3(float.Parse(args[5], ci), float.Parse(args[6], ci), float.Parse(args[7], ci));
                    spot.Up = new Vector3(float.Parse(args[8], ci), float.Parse(args[9], ci), float.Parse(args[10], ci));
                }
                Transform t = root.Find(spot.Name);
                if (t != null) Place(t, spot);
            }
            else if (a == "pizza" && args.Length >= 3 && main != null)
            {
                _pizzaAt = new Vector3(float.Parse(args[1], ci), 0f, float.Parse(args[2], ci));
                if (args.Length >= 4) _pizzaYaw = float.Parse(args[3], ci);
                GameObject old = GameObject.Find(PizzaName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                EnsurePizza(main);
            }
            else if (a == "suit" && args.Length >= 2 && main != null)
            {
                // rvegg suit <flat> [size] [drop] [lift] [tilt] [legpull] [armskew] [roll]
                _suitFlat = float.Parse(args[1], ci);
                if (args.Length >= 3) _suitSize = float.Parse(args[2], ci);
                if (args.Length >= 4) _suitDrop = float.Parse(args[3], ci);
                if (args.Length >= 5) _suitLift = float.Parse(args[4], ci);
                if (args.Length >= 6) _suitTilt = float.Parse(args[5], ci);
                if (args.Length >= 7) _legPull = float.Parse(args[6], ci);
                if (args.Length >= 8) _armSkew = float.Parse(args[7], ci);
                if (args.Length >= 9) _suitRoll = float.Parse(args[8], ci);
                if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
                Ensure();
                root = main.Find(RootName);
            }
            else if (a == "arms" && args.Length >= 2 && main != null)
            {
                _armDrop = float.Parse(args[1], ci);
                if (args.Length >= 3) _armShare = float.Parse(args[2], ci);
                if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
                Ensure();
                root = main.Find(RootName);
            }
            else if (a == "houses")
            {
                // Every house driveway, with the roofs around it: angle/distance -> height on 'collider' (normal y).
                int listed = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
                {
                    if (t.name.IndexOf("house parking", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Vector3 o = t.position;
                    string roofs = "";
                    for (int deg = 0; deg < 360; deg += 45)
                        foreach (float d in new[] { 5f, 9f })
                        {
                            Vector3 q = o + Quaternion.Euler(0f, deg, 0f) * Vector3.forward * d;
                            if (!Physics.Raycast(new Vector3(q.x, o.y + 30f, q.z), Vector3.down, out RaycastHit h, 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            if (h.point.y < o.y + 2f) continue;
                            roofs += " " + deg + "/" + d.ToString("0") + "=(" + q.x.ToString("F1") + "," + q.z.ToString("F1") + ")" + h.point.y.ToString("F1") + "'" + h.collider.name + "'" + h.normal.y.ToString("F2");
                        }
                    Core.Log.Msg("[Debug] rvegg house '" + t.name + "' at " + o.ToString("F1") + ":" + (roofs.Length > 0 ? roofs : " no roof around"));
                    listed++;
                }
                Core.Log.Msg("[Debug] rvegg houses: " + listed + " driveway(s).");
                return;
            }
            else if (a == "meshrot" && args.Length >= 4 && main != null)
            {
                _meshEuler = new Vector3(float.Parse(args[1], ci), float.Parse(args[2], ci), float.Parse(args[3], ci));
                if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
                Ensure();
                root = main.Find(RootName);
            }
            else if (a == "rebuild" && main != null)
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
                _mats.Clear();
                Ensure();
                root = main.Find(RootName);
            }
            else if (a == "probe" && main != null)
            {
                foreach (Renderer r in main.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name.ToLower();
                    if (!(n.Contains("dash") || n.Contains("door") || n.Contains("step") || n.Contains("wall") || n.Contains("switch"))) continue;
                    Bounds wb = r.bounds;
                    Vector3 lo = main.InverseTransformPoint(wb.min), hi = main.InverseTransformPoint(wb.max);
                    Core.Log.Msg("[Debug] rvegg probe '" + r.name + "' active=" + r.gameObject.activeInHierarchy + " local min=" + Vector3.Min(lo, hi).ToString("F2") + " max=" + Vector3.Max(lo, hi).ToString("F2"));
                }
            }
            else if (a == "wall" && root != null)
            {
                // How far each wall prop's back sits from the wall behind it; its own +z points out of the wall.
                foreach (Spot spot in new[] { Number, Holes, Gear })
                {
                    Transform t = root.Find(spot.Name);
                    if (t == null) { Core.Log.Warning("[Debug] rvegg wall: no '" + spot.Name + "' under the props."); continue; }
                    RvSavePoint.ProbeWall("egg " + spot.Name + " (active=" + t.gameObject.activeInHierarchy + ")", t, -t.forward);
                }
                return;
            }

            else if (a == "garages")
            {
                // Every garage door in the scene, with what lies 2.5 m to either side of its face - a roof if it is a
                // little above the door's top. The door is thin along its face normal, so the thinner world axis is it.
                int listed = 0;
                foreach (MeshRenderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (!r.name.StartsWith("GarageDoor")) continue;
                    Bounds b = r.bounds;
                    Transform t = r.transform;
                    Vector3 n = b.extents.x < b.extents.z ? Vector3.right : Vector3.forward;
                    string sides = "";
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        Vector3 p = b.center + n * (2.5f * sign);
                        sides += " (" + p.x.ToString("F1") + ", " + p.z.ToString("F1") + "): "
                            + (Physics.Raycast(new Vector3(p.x, b.max.y + 20f, p.z), Vector3.down, out RaycastHit h, 40f, ~0, QueryTriggerInteraction.Ignore)
                                ? h.point.y.ToString("F2") + " on '" + h.collider.name + "'" : "nothing");
                    }
                    Core.Log.Msg("[Debug] rvegg garage '" + r.name + "' parent '" + (t.parent != null ? t.parent.name : "-") + "' center=" + b.center.ToString("F1")
                        + " top=" + b.max.y.ToString("F2") + sides);
                    listed++;
                }
                Core.Log.Msg("[Debug] rvegg garages: " + listed + " door(s).");
                return;
            }
            else if ((a == "view" || a == "vieww") && args.Length >= 7)
            {
                // Free camera: 'view' takes eye and target in rv/Main space, 'vieww' in world space.
                Vector3 eye = new Vector3(float.Parse(args[1], ci), float.Parse(args[2], ci), float.Parse(args[3], ci));
                Vector3 at = new Vector3(float.Parse(args[4], ci), float.Parse(args[5], ci), float.Parse(args[6], ci));
                if (a == "view")
                {
                    if (main == null) { Core.Log.Warning("[Debug] rvegg view: no rv/Main."); return; }
                    eye = main.TransformPoint(eye);
                    at = main.TransformPoint(at);
                }
                PlayerSingleton<PlayerCamera>.Instance.OverrideTransform(eye, Quaternion.LookRotation(at - eye), 0f, false);
                Core.Log.Msg("[Debug] rvegg view: eye=" + eye.ToString("F2") + " at=" + at.ToString("F2"));
                return;
            }
            else if (a == "look" && args.Length >= 2)
            {
                // Camera in front of a prop, along its Out direction; 'rvcam off' gives the camera back.
                Transform t = args[1].ToLower() == "pizza" ? GameObject.Find(PizzaName)?.transform : root != null ? root.Find(args[1]) : null;
                if (t == null) { Core.Log.Warning("[Debug] rvegg look: no prop '" + args[1] + "'."); return; }
                float dist = args.Length >= 3 ? float.Parse(args[2], ci) : 0.8f;
                Vector3 eye = t.position + t.forward * dist + (args[1].ToLower() == "pizza" ? Vector3.right * dist * 0.6f : Vector3.zero);
                PlayerSingleton<PlayerCamera>.Instance.OverrideTransform(eye, Quaternion.LookRotation(t.position - eye), 0f, false);
                Core.Log.Msg("[Debug] rvegg look " + t.name + ": eye=" + eye.ToString("F2"));
            }

            if (root == null) { Core.Log.Warning("[Debug] rvegg: no props in the RV (model " + (model != null) + ")."); return; }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.parent == root)
                    Core.Log.Msg("[Debug] rvegg '" + t.name + "' active=" + t.gameObject.activeInHierarchy + " local=" + t.localPosition.ToString("F2") + " euler=" + t.localEulerAngles.ToString("F0") + " world=" + t.position.ToString("F2"));
            GameObject pizza = GameObject.Find(PizzaName);
            Core.Log.Msg("[Debug] rvegg pizza " + (pizza != null ? "at " + pizza.transform.position.ToString("F2") : "not placed (spot " + _pizzaAt + ")") + ".");
        }
#endif
    }
}
