using System.Collections.Generic;
using System.IO;
using Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    /// <summary>
    /// Builds the M2 merge board scene inside Unity, so no scene or material files are hand-written.
    /// Menu: Tools > The Life We Left Behind > Create Merge Board Scene.
    /// Batch mode (used by SetupUnity.cmd): -executeMethod Game.Editor.BoardSceneBuilder.CreateSceneBatch
    /// </summary>
    [InitializeOnLoad]
    public static class BoardSceneBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/MergeBoard.unity";
        public const string MaterialPath = "Assets/Game/Presentation/Placeholder/SpriteUnlit.mat";
        /// <summary>Written by the setup script: open the board scene once when the Editor next starts.</summary>
        public const string OpenOnceFlag = "Library/Game.OpenMergeBoard.flag";
        private const string UnlitShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

        static BoardSceneBuilder()
        {
            if (Application.isBatchMode || !File.Exists(OpenOnceFlag))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                File.Delete(OpenOnceFlag);
                if (File.Exists(ScenePath) && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Debug.Log("[Board] Opened " + ScenePath + ". Set the Game view to a portrait size and press Play.");
                }
            };
        }

        [MenuItem("Tools/The Life We Left Behind/Create Merge Board Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
        }

        /// <summary>Entry point for batch mode: builds the scene without dialogs and exits non-zero on failure.</summary>
        public static void CreateSceneBatch()
        {
            try
            {
                Build();
                if (!File.Exists(ScenePath))
                {
                    Debug.LogError("[Board] Scene was not saved to " + ScenePath);
                    EditorApplication.Exit(2);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Board] Creating the scene failed: " + e);
                EditorApplication.Exit(3);
            }
        }

        private static void Build()
        {
            Material material = GetOrCreateUnlitMaterial();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;

            var board = new GameObject("Merge Board");
            BoardScreen screen = board.AddComponent<BoardScreen>();
            var serialized = new SerializedObject(screen);
            serialized.FindProperty("spriteMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);
            Debug.Log("[Board] Created " + ScenePath + (material == null ? " (WARNING: unlit sprite shader not found; sprites use the default material)" : "") +
                      ". Press Play to try the board.");
        }

        private static Material GetOrCreateUnlitMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(UnlitShader);
            if (shader == null)
            {
                Debug.LogWarning("[Board] Shader '" + UnlitShader + "' not found. Is the project using URP?");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var material = new Material(shader) { name = "SpriteUnlit" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void AddToBuildSettingsFirst(string path)
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path != path)
                {
                    scenes.Add(s);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
