using UnityEngine;

namespace RebootEarth.Game2
{
    /// <summary>
    /// Grows the team's existing tree model (Assets/Model/Tree.glb) with "growth" (0..1):
    ///   Stage 1 Seedling (0-30%)   small
    ///   Stage 2 Growing  (31-70%)  medium
    ///   Stage 3 Full     (71-100%) full size
    /// The model is scaled smoothly from its base on the soil; nothing is generated.
    /// </summary>
    [ExecuteAlways]
    public class TreeGrowth : MonoBehaviour
    {
        [Range(0, 1)] public float growth;     // target
        public float shown;                    // displayed (smoothed)
        public float sproutT;                  // 0..1 seed -> first sprout (intro animation)

        [Tooltip("Height of the fully grown tree in world units")]
        public float fullHeight = 4.6f;
        [Tooltip("Smallest size (seedling) as a fraction of the full tree")]
        [Range(0.01f, 0.5f)] public float seedlingScale = 0.08f;
        [Tooltip("Edit mode preview of the growth (0..1)")]
        [Range(0, 1)] public float previewGrowth = 1f;

        [SerializeField, HideInInspector] Transform pivot;      // scaled root (sits on the soil)
        [SerializeField, HideInInspector] float modelHeight = 1f;

        public static string StageName(float g) =>
            g <= 0.30f ? "STAGE 1: SEEDLING" : g <= 0.70f ? "STAGE 2: GROWING TREE" : "STAGE 3: FULLY GROWN TREE";

        public static TreeGrowth Create(Transform parent, Vector3 pos, GameObject treeModel)
        {
            var go = new GameObject("LastSeedTree");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TreeGrowth>();
            t.Build(treeModel);
            return t;
        }

        void Build(GameObject treeModel)
        {
            pivot = new GameObject("GrowPivot").transform;
            pivot.SetParent(transform, false);
            if (treeModel == null)
            {
                Debug.LogWarning("[TreeGrowth] tree model is missing (Assets/Model/Tree.glb)");
                return;
            }
            GameObject m;
#if UNITY_EDITOR
            m = Application.isPlaying ? Instantiate(treeModel, pivot)
                : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(treeModel, pivot);
#else
            m = Instantiate(treeModel, pivot);
#endif
            m.name = "TreeModel";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            m.transform.localScale = Vector3.one;
            // put the bottom of the model on the soil, centered on the trunk
            var rs = m.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                Vector3 local = pivot.InverseTransformPoint(b.center);
                float bottom = pivot.InverseTransformPoint(b.min).y;
                m.transform.localPosition = new Vector3(-local.x, -bottom, -local.z);
                modelHeight = Mathf.Max(0.01f, b.size.y / Mathf.Max(0.0001f, pivot.lossyScale.y));
            }
            if (Application.isPlaying) Apply(0f); else { sproutT = 1f; Apply(previewGrowth); }
        }

        void OnEnable()
        {
            if (!Application.isPlaying && pivot != null) { sproutT = 1f; Apply(previewGrowth); }
        }

        void OnValidate() => OnEnable();

        void Start()
        {
            if (Application.isPlaying) { sproutT = 0f; shown = 0f; Apply(0f); }
        }

        void Update()
        {
            if (pivot == null) return;
            if (!Application.isPlaying)
            {
                sproutT = 1f;
                Apply(previewGrowth);
                return;
            }
            shown = Mathf.MoveTowards(shown, growth, Time.deltaTime * 0.12f);
            Apply(shown);
        }

        static float Ease(float x) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(x));

        void Apply(float g)
        {
            if (pivot == null) return;
            // seed -> sprout pops the model in, then growth scales it to full size
            float s = Mathf.Lerp(seedlingScale, 1f, Ease(g)) * Ease(sproutT);
            float k = fullHeight / modelHeight * s;
            pivot.localScale = Vector3.one * Mathf.Max(0.0001f, k);
            float sway = Application.isPlaying ? Mathf.Sin(Time.time * 1.2f) * 1.5f : 0f;
            pivot.localRotation = Quaternion.Euler(0, 0, sway);
        }
    }
}
