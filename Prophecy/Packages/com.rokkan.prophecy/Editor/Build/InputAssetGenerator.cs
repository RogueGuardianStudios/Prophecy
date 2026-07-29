using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rokkan.Prophecy.Editor.Build
{
    /// <summary>
    /// Builds <c>Prophecy.inputactions</c> from code.
    ///
    /// <para><b>Why generated.</b> A <c>.inputactions</c> file is JSON full of generated GUIDs
    /// cross-referencing each other — precisely the format the project forbids hand-authoring.
    /// Rather than click it together in the Input Actions window and hope it stays consistent,
    /// the map is declared here and serialised by Unity's own writer. Re-running restates the
    /// whole asset, so the file can never drift from this declaration.</para>
    ///
    /// <para><b>The whole final moveset is bound now</b>, including abilities that ship disabled.
    /// Input plumbing is the most annoying thing to retrofit — an ability that arrives later
    /// should need a flag flip in the sim, not a new action, a new binding, a new capture field
    /// and a prefab rewire. The buttons for parry, dodge and Flame-Arts exist from this build even
    /// though the modules behind three of them are empty.</para>
    ///
    /// <para>Debug lives in its own map so a debug key can never collide with a gameplay binding,
    /// and so the whole map can be disabled in a shipping build by not enabling it.</para>
    ///
    /// <para>This does <b>not</b> delete the stock <c>InputSystem_Actions.inputactions</c> from the
    /// URP template. That asset may still be referenced by template scenes and project settings,
    /// and quietly deleting a referenced asset is not this generator's call to make.</para>
    /// </summary>
    public static class InputAssetGenerator
    {
        public const string AssetPath = "Assets/_Prophecy/Input/Prophecy.inputactions";

        public const string GameplayMap = "Gameplay";
        public const string DebugMap = "Debug";

        [MenuItem("Prophecy/Build/Generate Input Actions", priority = 21)]
        public static void Generate()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            BuildGameplayMap(asset);
            BuildDebugMap(asset);

            var directory = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            Directory.CreateDirectory(directory);

            // ToJson is the Input System's own serialiser — the same writer the editor window
            // uses — so the result is a valid asset rather than something that merely looks like one.
            File.WriteAllText(AssetPath, asset.ToJson());
            Object.DestroyImmediate(asset);

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            Debug.Log($"[Prophecy] Generated {AssetPath}");
        }

        /// <summary><c>-executeMethod</c> target. See <see cref="ProphecyAssetBootstrap"/>.</summary>
        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Prophecy] Input asset generation failed: {e}");
                EditorApplication.Exit(1);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static void BuildGameplayMap(InputActionAsset asset)
        {
            var map = asset.AddActionMap(GameplayMap);

            // Move is a Value action: the sim reads a magnitude, not just a direction, because the
            // analog run model blends top speed off stick deflection.
            var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");
            move.AddBinding("<Gamepad>/dpad");

            Button(map, "Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Button(map, "Attack", "<Keyboard>/j", "<Gamepad>/buttonWest", "<Mouse>/leftButton");
            Button(map, "Block", "<Keyboard>/k", "<Gamepad>/leftShoulder");
            Button(map, "Parry", "<Keyboard>/l", "<Gamepad>/rightShoulder");
            Button(map, "Dodge", "<Keyboard>/leftCtrl", "<Gamepad>/buttonEast");
            Button(map, "FlameArt", "<Keyboard>/f", "<Gamepad>/rightTrigger");
            Button(map, "Interact", "<Keyboard>/e", "<Gamepad>/buttonNorth");

            // Run ships as a toggle (open knob #1), so this is a discrete press, not a held axis.
            Button(map, "RunToggle", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
        }

        private static void BuildDebugMap(InputActionAsset asset)
        {
            var map = asset.AddActionMap(DebugMap);
            Button(map, "ToggleOverlay", "<Keyboard>/f1");
        }

        private static void Button(InputActionMap map, string name, params string[] bindings)
        {
            var action = map.AddAction(name, InputActionType.Button);
            foreach (var binding in bindings)
                action.AddBinding(binding);
        }
    }
}
