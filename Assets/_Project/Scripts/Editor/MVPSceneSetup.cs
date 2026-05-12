using System.Collections.Generic;
using System.IO;
using Project.CameraRig;
using Project.Core;
using Project.DebugUI;
using Project.Health;
using Project.Items;
using Project.PlayerInput;
using Project.Skills;
using Project.Units;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Project.EditorTools
{
    /// One-shot builder for the MVP scene + Unit / OrderMarker prefabs.
    /// Re-runnable: existing assets are overwritten and the scene is rebuilt
    /// from scratch each time. Saves to Assets/_Project/Scenes/MVP.unity.
    public static class MVPSceneSetup
    {
        const string ProjectRoot = "Assets/_Project";
        const string PrefabsDir = ProjectRoot + "/Prefabs";
        const string ScenesDir = ProjectRoot + "/Scenes";
        const string SODir = ProjectRoot + "/ScriptableObjects";
        const string BodyPartsDir = SODir + "/BodyParts";
        const string SkillsSODir = SODir + "/Skills";
        const string ItemsSODir = SODir + "/Items";
        const string ScenePath = ScenesDir + "/MVP.unity";
        const string UnitPrefabPath = PrefabsDir + "/Unit.prefab";
        const string MarkerPrefabPath = PrefabsDir + "/OrderMarker.prefab";
        const string QueuedMarkerPrefabPath = PrefabsDir + "/OrderMarker_Queued.prefab";
        const string WorldItemGenericPath = PrefabsDir + "/WorldItem_Generic.prefab";
        const string XPCurvePath = SkillsSODir + "/DefaultXPCurve.asset";
        const string ItemDatabasePath = SODir + "/ItemDatabase.asset";

        [MenuItem("Tools/RTS MVP/Build Scene And Prefabs")]
        public static void Build()
        {
            EnsureFolder(PrefabsDir);
            EnsureFolder(ScenesDir);
            EnsureFolder(BodyPartsDir);
            EnsureFolder(SkillsSODir);
            EnsureFolder(ItemsSODir);

            // ScriptableObject data first — prefabs reference these.
            var bodyParts = CreateOrUpdateBodyPartDefinitions();
            var xpCurve = CreateOrUpdateXPCurve();
            var items = CreateOrUpdateItemData();
            var itemDatabase = CreateOrUpdateItemDatabase(items);

            var unitPrefab = BuildUnitPrefab(bodyParts, xpCurve);
            var markerPrefab = BuildOrderMarkerPrefab();
            var queuedMarkerPrefab = BuildQueuedOrderMarkerPrefab();
            var worldItemGenericPrefab = BuildWorldItemGenericPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var ground = BuildGround();
            BuildObstacles();
            var cam = BuildCamera();

            // Bake before instantiating the unit so its NavMeshAgent has a
            // surface to snap to on first enable.
            BakeNavMesh(ground);

            var unitInstance = (GameObject)PrefabUtility.InstantiatePrefab(unitPrefab);
            unitInstance.transform.position = Vector3.zero;
            unitInstance.name = "Unit";

            BuildGameSystems(unitInstance.GetComponent<Unit>(), cam, markerPrefab, queuedMarkerPrefab, worldItemGenericPrefab, itemDatabase);

            // Sprinkle a few loot piles for pickup testing. Done in edit
            // mode without going through WorldItem.Spawn (which relies on
            // WorldItemService's static, populated only at Play time).
            SpawnInitialWorldItems(itemDatabase, worldItemGenericPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Make the scene the active one in build settings so File>Build Settings is easy.
            var scenes = EditorBuildSettings.scenes;
            bool already = false;
            foreach (var s in scenes) if (s.path == ScenePath) { already = true; break; }
            if (!already)
            {
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
                EditorBuildSettings.scenes = list.ToArray();
            }

            Debug.Log($"[MVPSceneSetup] Built scene at {ScenePath}, prefabs at {PrefabsDir}/. NavMesh baked.");
        }

        // ---- Prefab builders ----

        static GameObject BuildUnitPrefab(List<BodyPartDefinition> bodyParts, XPCurve xpCurve)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Unit";
            // Drop the default collider — NavMeshAgent handles avoidance.
            Object.DestroyImmediate(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_Unit.mat",
                new Color(0.25f, 0.55f, 1f));

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 2f;
            // The default Capsule primitive is centered on its origin: visual
            // extends from y=-1 to y=+1. baseOffset=1 pushes the GameObject up
            // so the capsule's feet sit on the navmesh instead of being
            // half-buried in the ground.
            agent.baseOffset = 1f;
            agent.speed = 4.5f;
            agent.angularSpeed = 720f;
            agent.acceleration = 24f;
            agent.stoppingDistance = 0.05f;
            agent.autoBraking = true;

            go.AddComponent<OrderQueue>();
            go.AddComponent<Unit>();

            // Waypoint path line: connects the unit to its current and pending
            // targeted orders. Uses Sprites/Default for cheap unlit rendering
            // with vertex-color support (URP-compatible).
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = 0.08f;
            line.endWidth = 0.08f;
            line.numCornerVertices = 2;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = 0;
            var lineColor = new Color(1f, 0.85f, 0.1f, 0.85f);
            line.startColor = lineColor;
            line.endColor = lineColor;
            line.sharedMaterial = CreateOrUpdateLineMaterial(
                ProjectRoot + "/Art/Mat_PathLine.mat",
                lineColor);

            go.AddComponent<OrderPathRenderer>();

            // Health: 7 body parts assigned via SerializedObject so the
            // private partDefinitions list is populated.
            var health = go.AddComponent<HealthSystem>();
            var healthSO = new SerializedObject(health);
            var partsProp = healthSO.FindProperty("partDefinitions");
            partsProp.arraySize = bodyParts.Count;
            for (int i = 0; i < bodyParts.Count; i++)
            {
                partsProp.GetArrayElementAtIndex(i).objectReferenceValue = bodyParts[i];
            }
            healthSO.ApplyModifiedPropertiesWithoutUndo();

            // Skills: XPCurve + the 5 SkillData entries pre-populated.
            var skills = go.AddComponent<SkillSystem>();
            var skillsSO = new SerializedObject(skills);
            skillsSO.FindProperty("curve").objectReferenceValue = xpCurve;
            var skillsListProp = skillsSO.FindProperty("skills");
            var skillTypes = System.Enum.GetValues(typeof(SkillType));
            skillsListProp.arraySize = skillTypes.Length;
            for (int i = 0; i < skillTypes.Length; i++)
            {
                var entry = skillsListProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Type").enumValueIndex = i;
                entry.FindPropertyRelative("Level").floatValue = 1f;
                entry.FindPropertyRelative("XPCurrent").floatValue = 0f;
            }
            skillsSO.ApplyModifiedPropertiesWithoutUndo();

            // Inventory: weight-based, default 30 kg capacity. Strength bonus
            // is pushed in by the bridge (Inventory has no ref to SkillSystem).
            var inventory = go.AddComponent<Inventory>();
            var invSO = new SerializedObject(inventory);
            invSO.FindProperty("baseMaxWeight").floatValue = 30f;
            invSO.ApplyModifiedPropertiesWithoutUndo();

            // Bridge between Health, Skills and Inventory (must come after all of them).
            go.AddComponent<SkillModifiersBridge>();

            // Passive XP hooks (Speed from movement, Strength from overweight
            // movement). Split out of the bridge for separation of concerns.
            go.AddComponent<PassiveXPHooks>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, UnitPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildOrderMarkerPrefab()
        {
            // Flat disc made from a cylinder — visible top-down without
            // pulling vertical geometry into the gameplay view.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "OrderMarker";
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_OrderMarker.mat",
                new Color(1f, 0.85f, 0.1f),
                emissive: true);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, MarkerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildQueuedOrderMarkerPrefab()
        {
            // Visually distinct from the primary marker: smaller and orange
            // instead of the primary yellow. Communicates "this is a queued
            // waypoint, not the active order" at a glance.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "OrderMarker_Queued";
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            go.transform.localPosition = new Vector3(0f, 0.02f, 0f);

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_OrderMarker_Queued.mat",
                new Color(1f, 0.55f, 0.1f),
                emissive: true);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, QueuedMarkerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildWorldItemGenericPrefab()
        {
            // Tiny cube used as the fallback world body for items without a
            // bespoke WorldPrefab. The WorldItem component is on the root so
            // Inventory.DropStack and PickupOrder can find it via raycast hit
            // GetComponentInParent.
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "WorldItem_Generic";
            go.transform.localScale = Vector3.one * 0.3f;

            // Box collider is auto-added by CreatePrimitive — make sure it's
            // sized to the new scale and NOT a trigger (raycast must hit it).
            var col = go.GetComponent<BoxCollider>();
            if (col != null) col.isTrigger = false;

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_WorldItem_Generic.mat",
                new Color(0.7f, 0.7f, 0.7f));

            go.AddComponent<WorldItem>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, WorldItemGenericPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ---- Scene content ----

        static void BuildLighting()
        {
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.55f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.35f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.15f, 0.15f);
        }

        static GameObject BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f); // 50×50

            // Static for lighting/navigation.
            GameObjectUtility.SetStaticEditorFlags(ground,
                StaticEditorFlags.ContributeGI |
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.NavigationStatic);

            ground.GetComponent<Renderer>().sharedMaterial = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_Ground.mat",
                new Color(0.32f, 0.36f, 0.30f));

            return ground;
        }

        static void BuildObstacles()
        {
            var parent = new GameObject("Obstacles");
            Vector3[] positions =
            {
                new(  6f, 0.5f,  2f),
                new( -5f, 0.5f,  4f),
                new(  3f, 0.5f, -6f),
                new( -7f, 0.5f, -3f),
                new( 10f, 0.5f, -8f),
                new(-10f, 1.0f,  9f),
            };
            float[] scales = { 1.5f, 2f, 1.2f, 2.5f, 1.8f, 3f };

            var mat = CreateOrUpdateMaterial(
                ProjectRoot + "/Art/Mat_Obstacle.mat",
                new Color(0.5f, 0.5f, 0.55f));

            for (int i = 0; i < positions.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Obstacle_{i}";
                cube.transform.SetParent(parent.transform);
                cube.transform.position = positions[i];
                cube.transform.localScale = Vector3.one * scales[i];
                cube.GetComponent<Renderer>().sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(cube,
                    StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.BatchingStatic |
                    StaticEditorFlags.NavigationStatic);
            }
        }

        static Camera BuildCamera()
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            // Solid color is intentional: an EmptyScene has no skybox material
            // assigned, and a Skybox clear with no material renders to a hard
            // black void. A slate background makes it obvious when the camera
            // drifts off the play area instead of looking like a render fail.
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.5f, 0.55f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;

            camGO.AddComponent<AudioListener>();
            camGO.AddComponent<RTSCameraController>();

            // Top-down tilt at ~60° centered on origin. Closer than a pure
            // overhead so the unit is readable at startup.
            camGO.transform.position = new Vector3(0f, 14f, -9f);
            camGO.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            return cam;
        }

        static GameObject BuildGameSystems(Unit unit, Camera cam, GameObject markerPrefab, GameObject queuedMarkerPrefab, GameObject worldItemGenericPrefab, ItemDatabase itemDatabase)
        {
            var go = new GameObject("GameSystems");
            go.AddComponent<GameTimeService>();

            // Expose the generic WorldItem prefab to WorldItem.Spawn (static).
            var worldItemService = go.AddComponent<WorldItemService>();
            var wisSO = new SerializedObject(worldItemService);
            wisSO.FindProperty("genericPrefab").objectReferenceValue = worldItemGenericPrefab;
            wisSO.ApplyModifiedPropertiesWithoutUndo();

            var debugUI = go.AddComponent<GameTimeDebugUI>();
            var debugSO = new SerializedObject(debugUI);
            debugSO.FindProperty("watchedUnit").objectReferenceValue = unit;
            debugSO.ApplyModifiedPropertiesWithoutUndo();

            var input = go.AddComponent<PlayerInputController>();
            var inputSO = new SerializedObject(input);
            inputSO.FindProperty("unit").objectReferenceValue = unit;
            inputSO.FindProperty("worldCamera").objectReferenceValue = cam;
            inputSO.FindProperty("moveMarkerPrefab").objectReferenceValue = markerPrefab;
            inputSO.FindProperty("queuedMoveMarkerPrefab").objectReferenceValue = queuedMarkerPrefab;
            inputSO.ApplyModifiedPropertiesWithoutUndo();

            var healthDebug = go.AddComponent<HealthSkillsDebugPanel>();
            var healthDebugSO = new SerializedObject(healthDebug);
            healthDebugSO.FindProperty("watchedUnit").objectReferenceValue = unit;
            healthDebugSO.FindProperty("itemDatabase").objectReferenceValue = itemDatabase;
            healthDebugSO.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }

        // ---- Initial world items ----

        static void SpawnInitialWorldItems(ItemDatabase db, GameObject genericPrefab)
        {
            if (db == null || genericPrefab == null) return;

            // (def id, qty, position) — fixed positions known clear of obstacles.
            var spawns = new (string id, int qty, Vector3 pos)[]
            {
                ("branch",          3, new Vector3( 3f,  0.15f,  5f)),
                ("branch",          5, new Vector3(-2f,  0.15f,  6f)),
                ("stone",           4, new Vector3( 4f,  0.15f, -2f)),
                ("stone",           2, new Vector3(-2f,  0.15f, -1f)),
                ("test_rock_10kg",  1, new Vector3( 0f,  0.15f,  4f)),
            };

            var parent = new GameObject("WorldItems");

            foreach (var s in spawns)
            {
                var def = db.GetById(s.id);
                if (def == null)
                {
                    Debug.LogWarning($"[MVPSceneSetup] Missing item id '{s.id}' in ItemDatabase — skipping spawn.");
                    continue;
                }

                var sourcePrefab = def.WorldPrefab != null ? def.WorldPrefab : genericPrefab;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
                go.transform.position = s.pos;
                go.name = $"WorldItem_{def.Id}_x{s.qty}";
                go.transform.SetParent(parent.transform, true);

                var wi = go.GetComponent<WorldItem>();
                if (wi == null) wi = go.AddComponent<WorldItem>();
                wi.Def = def;
                wi.Quantity = s.qty;

                // No editor-time tint anymore: WorldItem.Awake re-applies the
                // FallbackColor via MaterialPropertyBlock at Play start, so the
                // scene file stays clean (no per-instance embedded material).
                // Edit-mode preview shows the generic grey cube — acceptable
                // trade-off for a cleaner asset graph.
            }
        }

        static void BakeNavMesh(GameObject ground)
        {
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        // ---- Asset utilities ----

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Material CreateOrUpdateMaterial(string path, Color color, bool emissive = false)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (emissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 0.6f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CreateOrUpdateLineMaterial(string path, Color color)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            // Sprites/Default supports vertex colors (so LineRenderer's
            // start/end Color works) and renders correctly under URP.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---- ScriptableObject seeders ----

        /// Returns the 7 BodyPartDefinitions in canonical BodyPartId order.
        /// Creates the assets on first run, refreshes default values on
        /// subsequent runs (without touching the Id field of existing ones).
        static List<BodyPartDefinition> CreateOrUpdateBodyPartDefinitions()
        {
            EnsureFolder(BodyPartsDir);

            var defaults = new[]
            {
                new BodyPartSeed(BodyPartId.Head,     "Head",      vital: true,  severable: false, hp: 60f,  brokenPenalty: 0f,   severedPenalty: 0f),
                new BodyPartSeed(BodyPartId.Torso,    "Torso",     vital: true,  severable: false, hp: 100f, brokenPenalty: 0f,   severedPenalty: 0f),
                new BodyPartSeed(BodyPartId.Abdomen,  "Abdomen",   vital: false, severable: false, hp: 80f,  brokenPenalty: 0f,   severedPenalty: 0f),
                new BodyPartSeed(BodyPartId.ArmLeft,  "Left Arm",  vital: false, severable: true,  hp: 50f,  brokenPenalty: 0f,   severedPenalty: 0f),
                new BodyPartSeed(BodyPartId.ArmRight, "Right Arm", vital: false, severable: true,  hp: 50f,  brokenPenalty: 0f,   severedPenalty: 0f),
                new BodyPartSeed(BodyPartId.LegLeft,  "Left Leg",  vital: false, severable: true,  hp: 70f,  brokenPenalty: 0.3f, severedPenalty: 0.7f),
                new BodyPartSeed(BodyPartId.LegRight, "Right Leg", vital: false, severable: true,  hp: 70f,  brokenPenalty: 0.3f, severedPenalty: 0.7f),
            };

            var list = new List<BodyPartDefinition>(defaults.Length);
            foreach (var seed in defaults)
            {
                string path = $"{BodyPartsDir}/BodyPart_{seed.Id}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<BodyPartDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<BodyPartDefinition>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.Id = seed.Id;
                asset.DisplayName = seed.Name;
                asset.IsVital = seed.Vital;
                asset.CanBeSevered = seed.Severable;
                asset.BaseMaxHP = seed.HP;
                asset.WoundedThreshold = 0.7f;
                asset.BrokenThreshold = 0.25f;
                asset.BleedingHPThreshold = 0.5f;
                asset.BleedRateWounded = 0.5f;
                asset.BleedRateBroken = 1.5f;
                asset.BleedRateSevered = seed.Severable ? 3f : 0f;
                asset.MoveSpeedPenaltyIfBroken = seed.BrokenPenalty;
                asset.MoveSpeedPenaltyIfSevered = seed.SeveredPenalty;

                EditorUtility.SetDirty(asset);
                list.Add(asset);
            }

            return list;
        }

        readonly struct BodyPartSeed
        {
            public readonly BodyPartId Id;
            public readonly string Name;
            public readonly bool Vital;
            public readonly bool Severable;
            public readonly float HP;
            public readonly float BrokenPenalty;
            public readonly float SeveredPenalty;
            public BodyPartSeed(BodyPartId id, string name, bool vital, bool severable, float hp, float brokenPenalty, float severedPenalty)
            {
                Id = id; Name = name; Vital = vital; Severable = severable;
                HP = hp; BrokenPenalty = brokenPenalty; SeveredPenalty = severedPenalty;
            }
        }

        static XPCurve CreateOrUpdateXPCurve()
        {
            EnsureFolder(SkillsSODir);

            var curve = AssetDatabase.LoadAssetAtPath<XPCurve>(XPCurvePath);
            if (curve == null)
            {
                curve = ScriptableObject.CreateInstance<XPCurve>();
                AssetDatabase.CreateAsset(curve, XPCurvePath);
            }

            // XP required to advance from level L → L+1. Hand-tuned keys so
            // L1 ≈ 50, L25 ≈ 1000, L50 ≈ 5000, L75 ≈ 15000, L100 ≈ 50000.
            curve.XPRequiredPerLevel = new AnimationCurve(
                new Keyframe(1f,    50f),
                new Keyframe(10f,   200f),
                new Keyframe(25f,   1000f),
                new Keyframe(50f,   5000f),
                new Keyframe(75f,   15000f),
                new Keyframe(100f,  50000f)
            );
            SmoothCurveTangents(curve.XPRequiredPerLevel);

            // Gain multiplier diminishes past L60 to slow the late-game grind
            // without hard-capping. Smoothed tangents for clean interpolation.
            curve.GainMultiplierByLevel = new AnimationCurve(
                new Keyframe(1f,   1.0f),
                new Keyframe(20f,  1.0f),
                new Keyframe(50f,  0.6f),
                new Keyframe(70f,  0.3f),
                new Keyframe(95f,  0.15f),
                new Keyframe(100f, 0.1f)
            );
            SmoothCurveTangents(curve.GainMultiplierByLevel);

            EditorUtility.SetDirty(curve);
            return curve;
        }

        static void SmoothCurveTangents(AnimationCurve c)
        {
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
        }

        // ---- Items & database ----

        readonly struct ItemSeed
        {
            public readonly string Id;
            public readonly string Name;
            public readonly ItemType Type;
            public readonly float Weight;
            public readonly bool Stackable;
            public readonly int MaxStack;
            public readonly Color Color;
            public ItemSeed(string id, string name, ItemType type, float weight, bool stackable, int maxStack, Color color)
            {
                Id = id; Name = name; Type = type; Weight = weight;
                Stackable = stackable; MaxStack = maxStack; Color = color;
            }
        }

        static List<ItemData> CreateOrUpdateItemData()
        {
            EnsureFolder(ItemsSODir);

            var seeds = new[]
            {
                new ItemSeed("branch",            "Branche",            ItemType.Resource,   0.5f, true,  50, new Color(0.45f, 0.30f, 0.15f)),
                new ItemSeed("stone",             "Caillou",            ItemType.Resource,   1.5f, true,  30, new Color(0.55f, 0.55f, 0.55f)),
                new ItemSeed("wood_log",          "Bûche",              ItemType.Resource,   2.5f, true,  20, new Color(0.30f, 0.20f, 0.10f)),
                new ItemSeed("stone_chunk",       "Pierre brute",       ItemType.Resource,   3.0f, true,  15, new Color(0.30f, 0.30f, 0.30f)),
                new ItemSeed("fish_small",        "Petit poisson",      ItemType.Resource,   0.8f, true,  20, new Color(0.85f, 0.85f, 0.90f)),
                new ItemSeed("stone_axe",         "Hache en pierre",    ItemType.Tool,       3.0f, false, 1,  new Color(0.75f, 0.62f, 0.42f)),
                new ItemSeed("stone_pickaxe",     "Pioche en pierre",   ItemType.Tool,       3.5f, false, 1,  new Color(0.70f, 0.58f, 0.40f)),
                new ItemSeed("fishing_rod",       "Canne à pêche",      ItemType.Tool,       1.0f, false, 1,  new Color(0.55f, 0.35f, 0.20f)),
                new ItemSeed("test_rock_10kg",   "Gros caillou (test)", ItemType.Misc,      10.0f, false, 1,  new Color(0.80f, 0.20f, 0.20f)),
                new ItemSeed("test_boulder_50kg","Bloc lourd (test)",   ItemType.Misc,      50.0f, false, 1,  new Color(0.60f, 0.10f, 0.10f)),
            };

            var list = new List<ItemData>(seeds.Length);
            foreach (var seed in seeds)
            {
                string path = $"{ItemsSODir}/Item_{seed.Id}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<ItemData>();
                    AssetDatabase.CreateAsset(asset, path);
                }

                asset.Id = seed.Id;
                asset.DisplayName = seed.Name;
                asset.Type = seed.Type;
                asset.Weight = seed.Weight;
                asset.Stackable = seed.Stackable;
                asset.MaxStackSize = seed.Stackable ? Mathf.Max(1, seed.MaxStack) : 1;
                asset.FallbackColor = seed.Color;
                // WorldPrefab left null — generic prefab handles all MVP items.

                EditorUtility.SetDirty(asset);
                list.Add(asset);
            }
            return list;
        }

        static ItemDatabase CreateOrUpdateItemDatabase(List<ItemData> items)
        {
            EnsureFolder(SODir);

            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(ItemDatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<ItemDatabase>();
                AssetDatabase.CreateAsset(db, ItemDatabasePath);
            }

            db.EditorReplaceAll(items);
            EditorUtility.SetDirty(db);
            return db;
        }
    }
}
