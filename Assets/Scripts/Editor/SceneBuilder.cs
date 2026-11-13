#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RebootEarth.EditorTools
{
    /// <summary>Menu: Reboot The Earth / Build Scenes  -> creates the 3 scenes + build settings.</summary>
    public static class SceneBuilder
    {
        const string P1Prefab = "Assets/Model/Characters/P1_Woodcutter.prefab";
        const string P2Prefab = "Assets/Model/Characters/P2_Survivor.prefab";
        const string TreeModel = "Assets/Model/Tree.glb";       // imported by glTFast

        [MenuItem("Reboot The Earth/Build Scenes")]
        public static void BuildAll()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                Build(GameSession.IntroScene, typeof(IntroController)),
                Build(GameSession.Game1Scene, typeof(Game1.WasteGame)),
                Build(GameSession.Game2Scene, typeof(Game2.SeedGame)),
            };
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorSceneManager.OpenScene("Assets/Scenes/" + GameSession.IntroScene + ".unity");
            Debug.Log("[Reboot The Earth] scenes built: 00_Intro, 01_ScrapSweepers, 02_SeedOfTomorrow");
        }

        static EditorBuildSettingsScene Build(string name, System.Type controller)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.4f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.SkyTopDirty;
            camGo.transform.position = new Vector3(0, 0, -30);
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.48f, 0.42f);

            new GameObject("TrackingReceiver").AddComponent<TrackingReceiver>();

            var ctrl = new GameObject(controller.Name);
            var comp = ctrl.AddComponent(controller);
            var so = new SerializedObject(comp);
            var p1 = so.FindProperty("player1Prefab");
            var p2 = so.FindProperty("player2Prefab");
            if (p1 != null) p1.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(P1Prefab);
            if (p2 != null) p2.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(P2Prefab);
            var tree = so.FindProperty("treeModel");
            if (tree != null) tree.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(TreeModel);
            so.ApplyModifiedPropertiesWithoutUndo();

            // build every visual now (edit mode) so it is visible and editable in the scene
            if (comp is IntroController intro) intro.BuildScene();
            else if (comp is Game1.WasteGame g1) g1.BuildScene();
            else if (comp is Game2.SeedGame g2) g2.BuildScene();
            EditorUtility.SetDirty(comp);

            string path = "Assets/Scenes/" + name + ".unity";
            EditorSceneManager.SaveScene(scene, path);
            return new EditorBuildSettingsScene(path, true);
        }
    }
}
#endif
