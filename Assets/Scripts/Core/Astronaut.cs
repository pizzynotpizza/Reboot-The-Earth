using UnityEngine;

namespace RebootEarth
{
    /// <summary>
    /// Turns a character prefab into a future citizen: glass helmet bubble + oxygen tank with a
    /// gauge on the back. Optional: arms follow the tracked player (live joints, or pose presets
    /// in keyboard mode).
    /// </summary>
    public class Astronaut : MonoBehaviour
    {
        public int playerIndex = -1;          // -1 = no arm follow
        public bool followArms = true;

        [SerializeField, HideInInspector] Transform lArm, lFore, rArm, rFore, lHand, rHand;   // screen-left / screen-right arms
        Vector3 sLeftUpper, sLeftFore, sRightUpper, sRightFore; // smoothed directions (world)
        float weight;

        public static Astronaut Create(GameObject prefab, Transform parent, Vector3 pos, float height,
                                       int playerIndex, bool faceCamera = true)
        {
            GameObject go;
            if (prefab != null)
            {
#if UNITY_EDITOR
                // edit mode: keep the prefab link so the character can still be edited as a prefab
                go = Application.isPlaying ? Instantiate(prefab, parent)
                   : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#else
                go = Instantiate(prefab, parent);
#endif
            }
            else
            {
                go = new GameObject("Citizen");
                go.transform.SetParent(parent, false);
                var body = Art.Prim(PrimitiveType.Capsule, go.transform, "Body", new Vector3(0, 0.9f, 0),
                                    new Vector3(0.6f, 0.75f, 0.45f), Art.Lit(Palette.Suit));
                Art.Prim(PrimitiveType.Sphere, go.transform, "Head", new Vector3(0, 1.55f, 0),
                         Vector3.one * 0.35f, Art.Lit(Palette.Hex("E2B48C")));
                body.name = "Body";
            }
            go.name = "Citizen_P" + (playerIndex + 1);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, faceCamera ? 180f : 0f, 0);
            go.transform.localScale = Vector3.one * (height / 1.9f);
            var a = go.AddComponent<Astronaut>();
            a.playerIndex = playerIndex;
            a.Dress();
            return a;
        }

        Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>())
                if (t.name == n) return t;
            return null;
        }

        void Dress()
        {
            var head = Find("Head");
            var spine = Find("Spine1") ?? Find("Spine");
            float s = 1f;
            var glass = Art.Transparent(new Color(0.75f, 0.9f, 1f, 0.28f));
            var rim = Art.Lit(new Color(0.95f, 0.95f, 0.92f), 0.6f);
            var tankMat = Art.Lit(Palette.Hex("B9B3A8"), 0.3f);
            var dark = Art.Lit(Palette.Hex("3A3029"), 0.1f);
            var red = Art.Lit(Palette.Hex("C8402E"), 0.2f);

            if (head != null)
            {
                var helmet = Art.Prim(PrimitiveType.Sphere, head, "Helmet", Vector3.zero, Vector3.one, glass);
                helmet.transform.position = head.position + transform.up * 0.1f * transform.lossyScale.y;
                helmet.transform.localScale = Vector3.one * 0.46f / Mathf.Max(head.lossyScale.x, 0.0001f) * transform.lossyScale.x;
                var ring = Art.Prim(PrimitiveType.Cylinder, head, "HelmetRing", Vector3.zero, Vector3.one, rim);
                ring.transform.position = head.position - transform.up * 0.13f * transform.lossyScale.y;
                ring.transform.localScale = new Vector3(0.36f, 0.025f, 0.36f) / Mathf.Max(head.lossyScale.x, 0.0001f) * transform.lossyScale.x;
            }
            if (spine != null)
            {
                float k = transform.lossyScale.x / Mathf.Max(spine.lossyScale.x, 0.0001f);
                var tank = new GameObject("OxygenTank").transform;
                tank.SetParent(spine, false);
                tank.position = spine.position - transform.forward * 0.2f * transform.lossyScale.z
                                + transform.up * 0.05f * transform.lossyScale.y;
                tank.rotation = transform.rotation;
                tank.localScale = Vector3.one * k;
                Art.Prim(PrimitiveType.Cube, tank, "Pack", Vector3.zero, new Vector3(0.38f, 0.52f, 0.2f), tankMat);
                var dial = Art.Prim(PrimitiveType.Cylinder, tank, "Dial", new Vector3(0, 0.08f, -0.105f),
                                    new Vector3(0.2f, 0.01f, 0.2f), rim);
                dial.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Art.Prim(PrimitiveType.Cube, tank, "Needle", new Vector3(-0.03f, 0.1f, -0.115f),
                         new Vector3(0.08f, 0.012f, 0.01f), dark).transform.localRotation = Quaternion.Euler(0, 0, 25);
                Art.Prim(PrimitiveType.Cube, tank, "Mark", new Vector3(0, -0.13f, -0.105f),
                         new Vector3(0.07f, 0.05f, 0.01f), red);
                var hose = Art.Prim(PrimitiveType.Cylinder, tank, "Hose", new Vector3(0.12f, 0.33f, 0.02f),
                                    new Vector3(0.04f, 0.12f, 0.04f), dark);
                hose.transform.localRotation = Quaternion.Euler(20, 0, -15);
            }

            // facing the camera: character's anatomical RIGHT arm is on the screen LEFT
            lArm = Find("RightArm"); lFore = Find("RightForeArm"); lHand = Find("RightHand");
            rArm = Find("LeftArm"); rFore = Find("LeftForeArm"); rHand = Find("LeftHand");
        }

        void LateUpdate()
        {
            if (!followArms || playerIndex < 0 || lArm == null || rArm == null || lFore == null || rFore == null) return;
            var p = TrackingReceiver.P(playerIndex);
            bool has = TryTargets(p, out var lu, out var lf, out var ru, out var rf);
            weight = Mathf.MoveTowards(weight, has ? 1f : 0f, Time.deltaTime * 3f);
            if (weight <= 0.001f) return;
            float k = 1f - Mathf.Pow(0.001f, Time.deltaTime);
            if (has)
            {
                sLeftUpper = Vector3.Slerp(sLeftUpper == Vector3.zero ? lu : sLeftUpper, lu, k);
                sLeftFore = Vector3.Slerp(sLeftFore == Vector3.zero ? lf : sLeftFore, lf, k);
                sRightUpper = Vector3.Slerp(sRightUpper == Vector3.zero ? ru : sRightUpper, ru, k);
                sRightFore = Vector3.Slerp(sRightFore == Vector3.zero ? rf : sRightFore, rf, k);
            }
            Aim(lArm, lFore, sLeftUpper);
            Aim(lFore, lHand, sLeftFore);
            Aim(rArm, rFore, sRightUpper);
            Aim(rFore, rHand, sRightFore);
        }

        void Aim(Transform bone, Transform child, Vector3 dir)
        {
            if (child == null || dir.sqrMagnitude < 1e-4f) return;
            Vector3 cur = (child.position - bone.position).normalized;
            var full = Quaternion.FromToRotation(cur, dir.normalized) * bone.rotation;
            bone.rotation = Quaternion.Slerp(bone.rotation, full, weight);
        }

        static Vector3 D(float x, float y, float z = 0f) => new Vector3(x, y, z).normalized;

        bool TryTargets(PlayerControl p, out Vector3 lu, out Vector3 lf, out Vector3 ru, out Vector3 rf)
        {
            lu = lf = ru = rf = Vector3.zero;
            if (p == null || !p.Present) return false;
            if (p.ArmsValid)
            {
                float ar = p.Aspect;
                Vector3 V(Vector2 a, Vector2 b)
                {
                    var v = new Vector3((b.x - a.x) * ar, b.y - a.y, 0f);
                    // short 2D arm = pointing at the camera
                    float len = v.magnitude;
                    if (len < 0.06f) v += new Vector3(0, 0, -0.08f);
                    return v.normalized;
                }
                lu = V(p.LSh, p.LEl); lf = V(p.LEl, p.LWr);
                ru = V(p.RSh, p.REl); rf = V(p.REl, p.RWr);
                return true;
            }
            switch (p.Pose)
            {
                case "hands_up":
                    lu = D(-0.15f, 1); lf = D(-0.05f, 1); ru = D(0.15f, 1); rf = D(0.05f, 1); return true;
                case "v_pose":
                    lu = D(-0.75f, 0.65f); lf = lu; ru = D(0.75f, 0.65f); rf = ru; return true;
                case "triangle":
                    lu = D(-0.55f, 0.85f); lf = D(0.65f, 0.75f); ru = D(0.55f, 0.85f); rf = D(-0.65f, 0.75f); return true;
                case "push":
                    lu = D(-0.15f, -0.05f, -1f); lf = lu; ru = D(0.15f, -0.05f, -1f); rf = ru; return true;
            }
            return false;
        }
    }
}
