using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>One-time legacy scene migration and editor checks for the adaptive gate.</summary>
public static class ArGatesMaintenance
{
    const string ReportPath = "D:/blender/chatgpt/unity-maintenance-report.txt";
    const string HandleFolder = "Assets/Models/GateInteraction";

    [MenuItem("Tools/AR Gates/Migrate Adaptive Scene")]
    public static void MigrateScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/lastvorotanew.unity" || scene.isDirty)
            throw new InvalidOperationException("Open the saved lastvorotanew scene before migration.");
        var builder = Object.FindFirstObjectByType<GateBuilder>();
        if (!builder || !builder.partsPrefab) throw new InvalidOperationException("Gate parts are missing.");
        var root = builder.gameObject;
        var picker = root.GetComponent<GateColorPicker>();
        var controller = root.GetComponent<GateController>();
        var handle = builder.handleObject ? builder.handleObject : root.transform.Find("ruchka");
        if (!handle || !picker || !controller) throw new InvalidOperationException("Missing handle/menu/controller.");

        if (!root.GetComponent<Animator>() && picker.paletteMaterials.Count > 0)
        {
            Debug.Log("Adaptive scene already migrated; no objects were removed.");
            return;
        }

        var materials = new Dictionary<string, Material>();
        foreach (var m in picker.paletteMaterials.Where(m => m)) materials[Clean(m.name)] = m;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in renderer.sharedMaterials)
                if (m) materials[Clean(m.name)] = m;
        var needed = picker.palette.Select(p => Clean(p.materialName)).Distinct().ToArray();
        var missing = needed.Where(n => !materials.ContainsKey(n)).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("Missing palette materials: " + string.Join(", ", missing));

        // Backup before any scene edits; assets and existing user changes are kept.
        string backup = "D:/blender/chatgpt/lastvorotanew.before-maintenance.unity";
        if (!File.Exists(backup)) File.Copy(scene.path, backup);
        Undo.RegisterFullObjectHierarchyUndo(root, "Migrate adaptive gate");
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(root),
                PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        picker.paletteMaterials = needed.Select(n => materials[n]).ToList();
        picker.builder = builder;
        builder.handleObject = handle;
        controller.builder = builder;
        controller.gateAnimator = null;
        var mover = root.GetComponent<GateMover>();
        if (mover) { mover.gateController = controller; mover.lockPlacementAfterFit = true; }

        if (!AssetDatabase.IsValidFolder(HandleFolder))
            AssetDatabase.CreateFolder("Assets/Models", "GateInteraction");
        int meshIndex = 0;
        foreach (var mf in handle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            string path = HandleFolder + "/HandleMesh" + meshIndex++ + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh)
            {
                mesh = Object.Instantiate(mf.sharedMesh);
                mesh.name = "Gate interaction handle";
                AssetDatabase.CreateAsset(mesh, path);
            }
            var original = mf.sharedMesh;
            mf.sharedMesh = mesh;
            foreach (var collider in handle.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh == original) collider.sharedMesh = mesh;
        }
        var legacyChildren = new List<GameObject>();
        foreach (Transform child in root.transform)
            if (child != handle) legacyChildren.Add(child.gameObject);
        foreach (var child in legacyChildren) Undo.DestroyObjectImmediate(child);
        var animator = root.GetComponent<Animator>();
        if (animator) Undo.DestroyObjectImmediate(animator);
        EditorUtility.SetDirty(builder);
        EditorUtility.SetDirty(picker);
        EditorUtility.SetDirty(controller);
        if (mover) EditorUtility.SetDirty(mover);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);

        var report = $"Migration passed: removed {legacyChildren.Count} legacy root children; " +
            $"preserved {picker.paletteMaterials.Count} palette materials, handle interaction and gate parts.\n" +
            "Legacy GLB remains in Assets as the source of palette materials/textures; no full gate prefab or Animator remains in the scene.\n";
        File.WriteAllText(ReportPath, report);
        Debug.Log(report);
    }

    static string Clean(string name) => (name ?? "").Replace(" (Instance)", "").Trim();

    [MenuItem("Tools/AR Gates/Verify Adaptive Gate In Play Mode")]
    public static void VerifyPlayMode()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var builder = Object.FindFirstObjectByType<GateBuilder>();
        var mover = builder.GetComponent<GateMover>();
        var picker = builder.GetComponent<GateColorPicker>();
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Gate verification failed: " + message);
            checks++;
        }
        Check(builder.Root != null, "Adaptive gate was not created.");
        Check(builder.handleObject && builder.handleObject.IsChildOf(builder.Root), "Handle did not attach.");
        var palette = (IList)typeof(GateColorPicker).GetField("_palette", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(picker);
        Check(palette.Count >= picker.palette.Count, "Palette entries were lost.");
        Check(builder.GetComponent<Animator>() == null, "Legacy animator is still present.");

        float originalWidth = builder.openingWidth, originalHeight = builder.openingHeight;
        bool windows = builder.windowsEnabled, wicket = builder.wicketEnabled;
        var position = builder.transform.position;
        var rotation = builder.transform.rotation;
        var scale = builder.transform.localScale;
        try
        {
            mover.allowScale = true;
            mover.SetPlacementLocked(false);
            Check(mover.CanMove && mover.CanRotate && mover.CanScale, "Free preview should allow gestures before fitting.");
            builder.transform.localScale = Vector3.one * 1.6f;
            Vector3 bottom = builder.transform.TransformPoint(builder.modelOffset);
            builder.Fit(3.5f, 2.65f);
            Check((builder.transform.localScale - Vector3.one).sqrMagnitude < 1e-8f, "DISTO fit did not reset scale.");
            Check(mover.IsPlacementLocked && !mover.CanMove && !mover.CanRotate && !mover.CanScale,
                "Placement gestures are not all locked after fitting.");
            var frozenPosition = builder.transform.position;
            var frozenRotation = builder.transform.rotation;
            var frozenScale = builder.transform.localScale;
            var modeField = typeof(GateMover).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance);
            var apply = typeof(GateMover).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (string mode in new[] { "Move", "Scale" })
            {
                modeField.SetValue(mover, Enum.Parse(modeField.FieldType, mode));
                apply.Invoke(mover, null);
                Check(builder.transform.position == frozenPosition && builder.transform.rotation == frozenRotation &&
                    builder.transform.localScale == frozenScale, "Locked gesture changed the transform.");
            }
            var toggle = typeof(GateColorPicker).GetMethod("TogglePlacementLock", BindingFlags.NonPublic | BindingFlags.Instance);
            var label = (TMPro.TextMeshPro)typeof(GateColorPicker).GetField("_placementText", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(picker);
            Check(label != null, "Placement lock button is absent from the Gate tab.");
            toggle.Invoke(picker, null);
            Check(!mover.IsPlacementLocked && mover.CanMove && mover.CanRotate && mover.CanScale,
                "Menu button did not unlock all gestures.");
            Check(label.text == "Зафиксировать положение", "Unlocked button label is incorrect.");
            Check(modeField.GetValue(mover).ToString() == "None", "Unlock retained a stale gesture.");
            toggle.Invoke(picker, null);
            Check(mover.IsPlacementLocked && label.text == "Разблокировать положение", "Menu button did not relock.");
            Check(builder.transform.position == frozenPosition && builder.transform.rotation == frozenRotation &&
                builder.transform.localScale == frozenScale, "Lock toggle changed placement.");
            Check(Vector3.Distance(bottom, builder.transform.TransformPoint(builder.modelOffset)) < .0001f, "Reset moved the gate bottom.");
            builder.PlaceInOpening(new Vector3(2, 0, 3), Vector3.back);
            Check(Vector3.Distance(builder.transform.TransformPoint(builder.modelOffset), new Vector3(2,0,3)) < .0001f, "Wrong opening origin.");
            builder.FlipSide(); builder.FlipSide();
            Check(Vector3.Distance(builder.transform.TransformPoint(builder.modelOffset), new Vector3(2,0,3)) < .0001f && mover.IsPlacementLocked && !mover.CanMove && !mover.CanRotate && !mover.CanScale, "Flip changed fitting origin/lock.");

            foreach (var size in new[] { new Vector2(3.5f,2.65f), new Vector2(2f,2f), new Vector2(5f,4.5f) })
            {
                builder.SetWicketAngle(0);
                builder.Build(size.x, size.y);
                Check(Mathf.Abs(builder.CurtainWidth - size.x - builder.widthOverlap) < .0001f, "Wrong curtain width.");
                Check(Mathf.Abs(builder.SectionHeights.Sum() - builder.CurtainHeight) < .0001f, "Section height sum mismatch.");
                foreach (float progress in new[] {0f, .5f, 1f})
                {
                    builder.SetProgress(progress);
                    Check(Mathf.Abs(builder.Progress-progress) < .0001f, "Opening progress did not apply.");
                    Check(builder.Root.GetComponentsInChildren<Transform>(true).All(t =>
                        float.IsFinite(t.position.x) && float.IsFinite(t.position.y) && float.IsFinite(t.position.z)), "Non-finite geometry pose.");
                }
            }
            builder.SetProgress(0);
            builder.Build(3.5f,2.65f);
            builder.SetOptions(true,true);
            builder.SetProgress(0);
            builder.SetWicketAngle(45);
            Check(builder.WicketOpen, "Wicket failed to open.");
            builder.SetProgress(.5f);
            Check(builder.Progress == 0, "Gate rose with wicket open.");
            builder.SetWicketAngle(0);
            builder.SetOptions(false,false);
            Check(!builder.HasWicket, "Wicket option did not disable.");
            Check(mover.IsPlacementLocked, "Rebuild lost placement lock.");
            Check(builder.handleObject != null, "Handle lost during rebuild.");
            string report = $"Play Mode verification passed: {checks} checks. Sizes 3500x2650, 2000x2000, 5000x4500; closed/half/open; palette; handle; full placement lock; menu unlock/relock; flip; wicket interlock.\n";
            File.AppendAllText(ReportPath, report);
            Debug.Log(report);
        }
        finally
        {
            builder.SetWicketAngle(0);
            builder.StopAnimation();
            builder.windowsEnabled = windows; builder.wicketEnabled = wicket;
            builder.Build(originalWidth,originalHeight);
            builder.SetProgress(0);
            builder.transform.SetPositionAndRotation(position,rotation);
            builder.transform.localScale = scale;
        }
    }
}
