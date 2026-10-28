using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnityInspection.EditorTools
{
    public static class InspectionSceneBuilder
    {
        public const int InspectableLayer = 8;
        public const string ScenePath = "Assets/Scenes/Inspection.unity";
        private const string MarkerPrefabPath = "Assets/Prefabs/IssueMarker.prefab";
        private const string MaterialsFolder = "Assets/Materials";
        private static Font cachedFont;

        [MenuItem("Tools/Unity Inspection/Rebuild Inspection Scene")]
        public static void BuildFromMenu()
        {
            Build();
            EditorUtility.DisplayDialog(
                "Unity 巡检",
                "巡检场景、问题标记、材质和界面已重新生成。",
                "确定");
        }

        public static void Build()
        {
            ConfigureProject();
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(MaterialsFolder);

            Material floorMaterial = CreateMaterial(
                "Floor",
                new Color(0.14f, 0.17f, 0.18f),
                0.2f,
                0.28f);
            Material wallMaterial = CreateMaterial(
                "Wall",
                new Color(0.54f, 0.57f, 0.57f),
                0.05f,
                0.22f);
            Material cabinetMaterial = CreateMaterial(
                "Cabinet",
                new Color(0.08f, 0.22f, 0.27f),
                0.72f,
                0.55f);
            Material cabinetDoorMaterial = CreateMaterial(
                "Cabinet Door",
                new Color(0.16f, 0.38f, 0.43f),
                0.68f,
                0.62f);
            Material crateMaterial = CreateMaterial(
                "Crate",
                new Color(0.22f, 0.29f, 0.15f),
                0.58f,
                0.36f);
            Material darkMetalMaterial = CreateMaterial(
                "Metal Trim",
                new Color(0.045f, 0.06f, 0.07f),
                0.85f,
                0.72f);
            Material safetyMaterial = CreateMaterial(
                "Safety Yellow",
                new Color(0.96f, 0.57f, 0.06f),
                0.12f,
                0.4f);
            Material tableMaterial = CreateMaterial(
                "Inspection Table",
                new Color(0.34f, 0.24f, 0.14f),
                0.18f,
                0.38f);
            Material paperMaterial = CreateMaterial(
                "Inspection Paper",
                new Color(0.9f, 0.9f, 0.82f),
                0f,
                0.12f);
            Material lightPanelMaterial = CreateEmissiveMaterial(
                "Light Panel",
                new Color(0.78f, 0.9f, 1f),
                new Color(0.55f, 0.75f, 1f) * 1.8f);
            Material markerMaterial = CreateMarkerMaterial();

            GameObject markerPrefab = BuildMarkerPrefab(markerMaterial);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment(
                floorMaterial,
                wallMaterial,
                cabinetMaterial,
                cabinetDoorMaterial,
                crateMaterial,
                darkMetalMaterial,
                safetyMaterial,
                tableMaterial,
                paperMaterial,
                lightPanelMaterial);
            BuildLighting();
            FirstPersonController player = BuildPlayer(out Camera sceneCamera);
            BuildInterface(player, sceneCamera, markerPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Failed to save scene at {ScenePath}.");
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Built {ScenePath}, {MarkerPrefabPath}, and inspection UI.");
        }

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "Unity Inspection";
            PlayerSettings.productName = "Unity 3D 虚拟现场巡检";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0)
            {
                throw new InvalidOperationException("TagManager.asset could not be loaded.");
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            SerializedProperty inspectable = layers.GetArrayElementAtIndex(InspectableLayer);
            if (inspectable.stringValue != "Inspectable")
            {
                inspectable.stringValue = "Inspectable";
                tagManager.ApplyModifiedPropertiesWithoutUndo();
            }

            if (LayerMask.NameToLayer("Inspectable") != InspectableLayer)
            {
                throw new InvalidOperationException("The Inspectable layer was not configured at index 8.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException($"Invalid asset folder path: {path}");
            }

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static Material CreateMaterial(
            string name,
            Color color,
            float metallic = 0f,
            float smoothness = 0.25f)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                throw new InvalidOperationException("The built-in Standard shader is unavailable.");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = name
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateEmissiveMaterial(string name, Color color, Color emission)
        {
            Material material = CreateMaterial(name, color);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateMarkerMaterial()
        {
            Material material = CreateMaterial("IssueMarker", new Color(1f, 0.25f, 0.08f));
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(1f, 0.12f, 0.02f) * 1.3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildMarkerPrefab(Material markerMaterial)
        {
            GameObject marker = new GameObject("IssueMarker");
            IssueMarker markerComponent = marker.AddComponent<IssueMarker>();

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Marker Orb";
            orb.transform.SetParent(marker.transform, false);
            orb.transform.localScale = Vector3.one * 0.28f;
            orb.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            SphereCollider collider = orb.GetComponent<SphereCollider>();
            collider.radius = 0.62f;

            GameObject labelObject = CreateUiObject("Marker Label", marker.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(360f, 70f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelObject.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            labelObject.transform.localScale = Vector3.one * 0.0031f;

            Canvas labelCanvas = labelObject.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;
            labelCanvas.sortingOrder = 60;
            labelCanvas.overrideSorting = true;

            CanvasGroup labelGroup = labelObject.AddComponent<CanvasGroup>();
            labelGroup.alpha = 0f;
            labelGroup.interactable = false;
            labelGroup.blocksRaycasts = false;

            Image labelBackground = CreateImage(
                "Background",
                labelObject.transform,
                new Color(0.025f, 0.045f, 0.055f, 0.96f));
            Stretch(labelBackground.rectTransform);
            labelBackground.raycastTarget = false;

            Image labelAccent = CreateImage(
                "Accent",
                labelObject.transform,
                new Color(0.2f, 0.82f, 0.88f, 1f));
            RectTransform accentRect = labelAccent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(5f, 0f);
            accentRect.anchoredPosition = Vector2.zero;
            labelAccent.raycastTarget = false;

            Text labelTitle = CreateText(
                "Title",
                labelObject.transform,
                "问题名称",
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft);
            SetTopRect(labelTitle.rectTransform, 7f, 28f, 15f);
            labelTitle.color = new Color(0.97f, 0.98f, 0.98f);

            Text labelMeta = CreateText(
                "Meta",
                labelObject.transform,
                "设备 · 区域  优先级 / 状态",
                14,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(labelMeta.rectTransform, 37f, 24f, 15f);
            labelMeta.color = new Color(0.62f, 0.79f, 0.82f);

            labelObject.SetActive(false);

            SerializedObject serializedMarker = new SerializedObject(markerComponent);
            serializedMarker.FindProperty("markerRenderer").objectReferenceValue =
                orb.GetComponent<Renderer>();
            serializedMarker.FindProperty("visualRoot").objectReferenceValue = orb.transform;
            serializedMarker.FindProperty("markerLabelRoot").objectReferenceValue = labelObject;
            serializedMarker.FindProperty("markerLabelGroup").objectReferenceValue = labelGroup;
            serializedMarker.FindProperty("markerTitle").objectReferenceValue = labelTitle;
            serializedMarker.FindProperty("markerMeta").objectReferenceValue = labelMeta;
            serializedMarker.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(marker, MarkerPrefabPath);
            Object.DestroyImmediate(marker);
            return prefab;
        }

        private static void BuildEnvironment(
            Material floorMaterial,
            Material wallMaterial,
            Material cabinetMaterial,
            Material cabinetDoorMaterial,
            Material crateMaterial,
            Material darkMetalMaterial,
            Material safetyMaterial,
            Material tableMaterial,
            Material paperMaterial,
            Material lightPanelMaterial)
        {
            GameObject environment = new GameObject("Inspection Environment");

            BuildRoomShell(
                environment.transform,
                floorMaterial,
                wallMaterial,
                darkMetalMaterial,
                safetyMaterial,
                lightPanelMaterial);

            BuildCabinet(
                "Cabinet A",
                new Vector3(-3.2f, 1.25f, 2.3f),
                new Vector3(1.5f, 2.5f, 0.9f),
                "配电柜 A",
                "CAB-A",
                "A区 · 配电区",
                environment.transform,
                cabinetMaterial,
                cabinetDoorMaterial,
                darkMetalMaterial,
                safetyMaterial);
            BuildCabinet(
                "Cabinet B",
                new Vector3(3.1f, 0.9f, 1.5f),
                new Vector3(1.2f, 1.8f, 1.1f),
                "配电柜 B",
                "CAB-B",
                "B区 · 配电区",
                environment.transform,
                cabinetMaterial,
                cabinetDoorMaterial,
                darkMetalMaterial,
                safetyMaterial);
            BuildEquipmentCrate(
                new Vector3(-1.75f, 0.45f, -1.45f),
                new Vector3(1.45f, 0.9f, 0.95f),
                "设备箱",
                environment.transform,
                crateMaterial,
                darkMetalMaterial,
                safetyMaterial);
            BuildInspectionTable(
                new Vector3(3.1f, 0f, 4.1f),
                new Vector3(2.3f, 0.82f, 1.1f),
                "巡检台",
                environment.transform,
                tableMaterial,
                darkMetalMaterial,
                safetyMaterial,
                paperMaterial);

            CreateWorldLabel(
                "Cabinet A Label",
                "配电柜 A",
                new Vector3(-3.2f, 3.02f, 1.55f),
                new Color(0.12f, 0.72f, 0.92f));
            CreateWorldLabel(
                "Cabinet B Label",
                "配电柜 B",
                new Vector3(3.1f, 2.28f, 0.7f),
                new Color(0.12f, 0.72f, 0.92f));
            CreateWorldLabel(
                "Equipment Crate Label",
                "设备箱",
                new Vector3(-1.75f, 1.38f, -1.45f),
                new Color(0.88f, 0.64f, 0.14f));
            CreateWorldLabel(
                "Inspection Table Label",
                "巡检台",
                new Vector3(4.75f, 1.92f, 3.35f),
                new Color(0.95f, 0.5f, 0.16f));
        }

        private static void BuildRoomShell(
            Transform parent,
            Material floorMaterial,
            Material wallMaterial,
            Material darkMetalMaterial,
            Material safetyMaterial,
            Material lightPanelMaterial)
        {
            GameObject floor = CreateCube("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(13.4f, 0.2f, 13.4f), floorMaterial, parent);
            floor.AddComponent<InspectionLocation>().Configure("FLOOR-01", "公共通行区");
            GameObject backWall = CreateCube("Back Wall", new Vector3(0f, 2.2f, 6.3f), new Vector3(13.4f, 4.4f, 0.2f), wallMaterial, parent);
            backWall.AddComponent<InspectionLocation>().Configure("WALL-N", "北侧公共区域");
            GameObject leftWall = CreateCube("Left Wall", new Vector3(-6.6f, 2.2f, 0f), new Vector3(0.2f, 4.4f, 12.4f), wallMaterial, parent);
            leftWall.AddComponent<InspectionLocation>().Configure("WALL-W", "西侧公共区域");
            GameObject rightWall = CreateCube("Right Wall", new Vector3(6.6f, 2.2f, 0f), new Vector3(0.2f, 4.4f, 12.4f), wallMaterial, parent);
            rightWall.AddComponent<InspectionLocation>().Configure("WALL-E", "东侧公共区域");

            for (int index = -5; index <= 5; index += 2)
            {
                float offset = index * 1.1f;
                CreateCube(
                    $"Floor Grid X {index}",
                    new Vector3(offset, 0.006f, 0f),
                    new Vector3(0.025f, 0.012f, 12.4f),
                    darkMetalMaterial,
                    parent,
                    false);
                CreateCube(
                    $"Floor Grid Z {index}",
                    new Vector3(0f, 0.006f, offset),
                    new Vector3(12.4f, 0.012f, 0.025f),
                    darkMetalMaterial,
                    parent,
                    false);
            }

            CreateCube("Safety Line Front", new Vector3(0f, 0.018f, -3.75f), new Vector3(7.6f, 0.025f, 0.12f), safetyMaterial, parent, false);
            CreateCube("Safety Line Left", new Vector3(-3.75f, 0.018f, 1.3f), new Vector3(0.12f, 0.025f, 10.1f), safetyMaterial, parent, false);
            CreateCube("Safety Line Right", new Vector3(3.75f, 0.018f, 1.3f), new Vector3(0.12f, 0.025f, 10.1f), safetyMaterial, parent, false);

            CreateCube("Wall Trim Back", new Vector3(0f, 0.18f, 6.17f), new Vector3(13f, 0.28f, 0.06f), darkMetalMaterial, parent, false);
            CreateCube("Wall Trim Left", new Vector3(-6.47f, 0.18f, 0f), new Vector3(0.06f, 0.28f, 12f), darkMetalMaterial, parent, false);
            CreateCube("Wall Trim Right", new Vector3(6.47f, 0.18f, 0f), new Vector3(0.06f, 0.28f, 12f), darkMetalMaterial, parent, false);

            CreateCube("Light Panel A", new Vector3(-2.4f, 3.35f, 6.17f), new Vector3(2.2f, 0.14f, 0.05f), lightPanelMaterial, parent, false);
            CreateCube("Light Panel B", new Vector3(2.4f, 3.35f, 6.17f), new Vector3(2.2f, 0.14f, 0.05f), lightPanelMaterial, parent, false);

            for (int index = -5; index <= 5; index++)
            {
                float x = index * 1.1f;
                CreateCube(
                    $"Back Wall Seam {index}",
                    new Vector3(x, 2.2f, 6.18f),
                    new Vector3(0.035f, 4.1f, 0.035f),
                    darkMetalMaterial,
                    parent,
                    false);
            }
        }

        private static void BuildCabinet(
            string name,
            Vector3 center,
            Vector3 size,
            string label,
            string deviceCode,
            string zone,
            Transform parent,
            Material bodyMaterial,
            Material doorMaterial,
            Material trimMaterial,
            Material safetyMaterial)
        {
            GameObject cabinet = CreateGroup(name, parent, center);
            cabinet.AddComponent<InspectionLocation>().Configure(deviceCode, zone);
            float frontZ = -size.z * 0.5f;

            CreateLocalCube("Cabinet Body", cabinet.transform, Vector3.zero, size, bodyMaterial);
            CreateLocalCube(
                "Door",
                cabinet.transform,
                new Vector3(0f, 0f, frontZ - 0.035f),
                new Vector3(size.x * 0.82f, size.y * 0.9f, 0.055f),
                doorMaterial);
            CreateLocalCube(
                "Door Split",
                cabinet.transform,
                new Vector3(0f, 0f, frontZ - 0.072f),
                new Vector3(0.025f, size.y * 0.84f, 0.02f),
                trimMaterial);
            CreateLocalCube(
                "Top Vent",
                cabinet.transform,
                new Vector3(0f, size.y * 0.31f, frontZ - 0.072f),
                new Vector3(size.x * 0.56f, 0.2f, 0.025f),
                trimMaterial);
            CreateLocalCube(
                "Handle",
                cabinet.transform,
                new Vector3(size.x * 0.31f, -0.04f, frontZ - 0.1f),
                new Vector3(0.055f, Mathf.Min(0.46f, size.y * 0.24f), 0.065f),
                trimMaterial);
            CreateLocalCube(
                "Warning Plate",
                cabinet.transform,
                new Vector3(-size.x * 0.24f, -size.y * 0.33f, frontZ - 0.072f),
                new Vector3(size.x * 0.34f, 0.18f, 0.02f),
                safetyMaterial);

            for (int vent = -2; vent <= 2; vent++)
            {
                CreateLocalCube(
                    $"Vent Slit {vent}",
                    cabinet.transform,
                    new Vector3(vent * 0.08f, size.y * 0.31f, frontZ - 0.09f),
                    new Vector3(0.018f, 0.13f, 0.02f),
                    bodyMaterial);
            }

            CreateLocalCube(
                "Status Light",
                cabinet.transform,
                new Vector3(size.x * 0.31f, size.y * 0.33f, frontZ - 0.1f),
                new Vector3(0.09f, 0.09f, 0.045f),
                safetyMaterial);

            for (int footX = -1; footX <= 1; footX += 2)
            {
                for (int footZ = -1; footZ <= 1; footZ += 2)
                {
                    CreateLocalCube(
                        $"Foot {footX} {footZ}",
                        cabinet.transform,
                        new Vector3(footX * size.x * 0.36f, -size.y * 0.52f, footZ * size.z * 0.3f),
                        new Vector3(0.18f, 0.08f, 0.18f),
                        trimMaterial);
                }
            }

        }

        private static void BuildEquipmentCrate(
            Vector3 center,
            Vector3 size,
            string label,
            Transform parent,
            Material bodyMaterial,
            Material trimMaterial,
            Material safetyMaterial)
        {
            GameObject crate = CreateGroup("Equipment Crate", parent, center);
            crate.AddComponent<InspectionLocation>().Configure("BOX-01", "B区 · 暂存区");
            float frontZ = -size.z * 0.5f;

            CreateLocalCube("Crate Body", crate.transform, Vector3.zero, size, bodyMaterial);
            CreateLocalCube(
                "Reinforced Lid",
                crate.transform,
                new Vector3(0f, size.y * 0.51f, 0f),
                new Vector3(size.x * 1.02f, 0.1f, size.z * 1.03f),
                trimMaterial);
            CreateLocalCube(
                "Top Handle",
                crate.transform,
                new Vector3(0f, size.y * 0.61f, 0f),
                new Vector3(0.52f, 0.07f, 0.1f),
                trimMaterial);

            for (int side = -1; side <= 1; side += 2)
            {
                CreateLocalCube(
                    $"Corner Brace {side}",
                    crate.transform,
                    new Vector3(side * size.x * 0.46f, 0f, frontZ - 0.055f),
                    new Vector3(0.09f, size.y * 0.9f, 0.07f),
                    trimMaterial);
                CreateLocalCube(
                    $"Side Handle {side}",
                    crate.transform,
                    new Vector3(side * (size.x * 0.5f + 0.04f), 0.03f, 0f),
                    new Vector3(0.08f, 0.14f, 0.45f),
                    trimMaterial);
            }

            CreateLocalCube(
                "Hazard Strip",
                crate.transform,
                new Vector3(0f, -size.y * 0.25f, frontZ - 0.062f),
                new Vector3(size.x * 0.76f, 0.18f, 0.025f),
                safetyMaterial);

            for (int stripe = -3; stripe <= 3; stripe++)
            {
                GameObject mark = CreateLocalCube(
                    $"Hazard Mark {stripe}",
                    crate.transform,
                    new Vector3(stripe * 0.19f, -size.y * 0.25f, frontZ - 0.078f),
                    new Vector3(0.07f, 0.16f, 0.018f),
                    trimMaterial);
                mark.transform.localRotation = Quaternion.Euler(0f, 0f, 32f);
            }

        }

        private static void BuildInspectionTable(
            Vector3 center,
            Vector3 size,
            string label,
            Transform parent,
            Material tableMaterial,
            Material trimMaterial,
            Material safetyMaterial,
            Material paperMaterial)
        {
            GameObject table = CreateGroup("Inspection Table", parent, center);
            table.AddComponent<InspectionLocation>().Configure("TBL-01", "C区 · 作业区");
            CreateLocalCube(
                "Tabletop",
                table.transform,
                new Vector3(0f, size.y, 0f),
                new Vector3(size.x, 0.14f, size.z),
                tableMaterial);

            for (int legX = -1; legX <= 1; legX += 2)
            {
                for (int legZ = -1; legZ <= 1; legZ += 2)
                {
                    CreateLocalCube(
                        $"Table Leg {legX} {legZ}",
                        table.transform,
                        new Vector3(legX * (size.x * 0.42f), size.y * 0.5f, legZ * (size.z * 0.34f)),
                        new Vector3(0.09f, size.y, 0.09f),
                        trimMaterial);
                }
            }

            CreateLocalCube(
                "Inspection Sheet",
                table.transform,
                new Vector3(-0.38f, size.y + 0.09f, 0.03f),
                new Vector3(0.58f, 0.025f, 0.38f),
                paperMaterial);
            CreateLocalCube(
                "Tool Case",
                table.transform,
                new Vector3(0.48f, size.y + 0.15f, 0f),
                new Vector3(0.5f, 0.22f, 0.34f),
                safetyMaterial);
            CreateLocalCube(
                "Tool Case Handle",
                table.transform,
                new Vector3(0.48f, size.y + 0.29f, 0f),
                new Vector3(0.22f, 0.05f, 0.06f),
                trimMaterial);

        }

        private static GameObject CreateGroup(string name, Transform parent, Vector3 position)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.position = position;
            return group;
        }

        private static GameObject CreateLocalCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = InspectableLayer;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static void CreateWorldLabel(
            string name,
            string text,
            Vector3 position,
            Color accent)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            labelObject.transform.position = position;
            labelObject.transform.localScale = Vector3.one * 0.0037f;

            Canvas canvas = labelObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(300f, 68f);

            Image panel = labelObject.AddComponent<Image>();
            panel.color = new Color(0.018f, 0.03f, 0.04f, 0.9f);
            panel.raycastTarget = false;

            Image stripe = CreateImage("Accent", labelObject.transform, accent);
            RectTransform stripeRect = stripe.rectTransform;
            stripeRect.anchorMin = new Vector2(0f, 0f);
            stripeRect.anchorMax = new Vector2(0f, 1f);
            stripeRect.pivot = new Vector2(0f, 0.5f);
            stripeRect.offsetMin = Vector2.zero;
            stripeRect.offsetMax = new Vector2(10f, 0f);

            Text label = CreateText(
                "Text",
                labelObject.transform,
                text,
                28,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 2f);
            labelRect.offsetMax = new Vector2(-10f, -2f);
            label.color = new Color(0.95f, 0.97f, 1f);

            Outline outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            labelObject.AddComponent<InspectionChineseFont>();
            labelObject.AddComponent<WorldLabelBillboard>();
        }

        private static GameObject CreateCube(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent,
            bool inspectable = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.layer = inspectable ? InspectableLayer : 0;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static void BuildLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.53f, 0.56f);

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1f, 0.95f, 0.86f);
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            GameObject fillObject = new GameObject("Inspection Fill Light");
            fillObject.transform.position = new Vector3(0f, 3.2f, -1.4f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 11f;
            fill.intensity = 1.35f;
            fill.color = new Color(0.72f, 0.84f, 1f);
            fill.shadows = LightShadows.Soft;
        }

        private static FirstPersonController BuildPlayer(out Camera sceneCamera)
        {
            GameObject playerObject = new GameObject("Player");
            playerObject.transform.position = new Vector3(0f, 0f, -4.8f);

            CharacterController characterController = playerObject.AddComponent<CharacterController>();
            characterController.height = 1.8f;
            characterController.radius = 0.35f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            characterController.stepOffset = 0.3f;

            FirstPersonController player = playerObject.AddComponent<FirstPersonController>();

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(playerObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.nearClipPlane = 0.05f;
            sceneCamera.farClipPlane = 100f;
            sceneCamera.fieldOfView = 65f;
            cameraObject.AddComponent<AudioListener>();
            player.Configure(cameraObject.transform);
            return player;
        }

        private static void BuildInterface(
            FirstPersonController player,
            Camera sceneCamera,
            GameObject markerPrefab)
        {
            GameObject canvasObject = new GameObject("Inspection HUD");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            BuildEventSystem();

            Text title = CreateText(
                "Title",
                canvasObject.transform,
                "虚拟现场巡检",
                24,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(title.rectTransform, 28f, 40f, 40f);
            title.color = new Color(0.9f, 0.95f, 1f);

            Text instructions = CreateText(
                "Instructions",
                canvasObject.transform,
                "WASD 移动  |  右键环视  |  左键：上报 / 查看标记  |  F5 同步  |  Esc 关闭",
                20,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(instructions.rectTransform, 72f, 34f, 64f);
            instructions.color = new Color(0.78f, 0.84f, 0.88f);

            Text status = CreateText(
                "Status",
                canvasObject.transform,
                string.Empty,
                21,
                FontStyle.Bold,
                TextAnchor.UpperCenter);
            SetTopRect(status.rectTransform, 102f, 46f, 80f);
            status.color = new Color(0.8f, 1f, 0.82f);

            Text summary = CreateText(
                "Issue Summary",
                canvasObject.transform,
                "全部 00   待处理 00   处理中 00   已完成 00",
                19,
                FontStyle.Bold,
                TextAnchor.UpperRight);
            SetTopRightRect(summary.rectTransform, -32f, -28f, 680f, 30f);
            summary.color = new Color(0.9f, 0.94f, 0.95f);

            Text syncStatus = CreateText(
                "Sync Status",
                canvasObject.transform,
                "正在同步现场标记…",
                16,
                FontStyle.Normal,
                TextAnchor.UpperRight);
            SetTopRightRect(syncStatus.rectTransform, -32f, -58f, 680f, 26f);
            syncStatus.color = new Color(0.55f, 0.76f, 0.8f);

            Image crosshair = CreateImage("Crosshair", canvasObject.transform, new Color(1f, 1f, 1f, 0.92f));
            RectTransform crosshairRect = crosshair.rectTransform;
            crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
            crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRect.pivot = new Vector2(0.5f, 0.5f);
            crosshairRect.anchoredPosition = Vector2.zero;
            crosshairRect.sizeDelta = new Vector2(7f, 7f);
            crosshair.raycastTarget = false;

            InspectionHud hud = canvasObject.AddComponent<InspectionHud>();
            hud.Configure(status, crosshair, summary, syncStatus);

            GameObject formPanel = CreateImage(
                "Issue Form",
                canvasObject.transform,
                new Color(0.035f, 0.055f, 0.07f, 0.96f)).gameObject;
            RectTransform formRect = formPanel.GetComponent<RectTransform>();
            formRect.anchorMin = new Vector2(0f, 0f);
            formRect.anchorMax = new Vector2(0f, 0f);
            formRect.pivot = new Vector2(0f, 0f);
            formRect.anchoredPosition = new Vector2(32f, 32f);
            formRect.sizeDelta = new Vector2(530f, 630f);

            Text formHeading = CreateText(
                "Form Heading",
                formPanel.transform,
                "上报问题",
                27,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(formHeading.rectTransform, 20f, 36f, 20f);
            formHeading.color = new Color(1f, 0.6f, 0.32f);

            CreateText("Title Label", formPanel.transform, "问题标题（必填）", 18, FontStyle.Bold, TextAnchor.UpperLeft)
                .rectTransform.anchorMin = new Vector2(0f, 1f);
            Text titleLabel = formPanel.transform.Find("Title Label").GetComponent<Text>();
            SetTopRect(titleLabel.rectTransform, 70f, 24f, 24f);

            InputField titleField = BuildInputField("Title Input", formPanel.transform, "例如：1 号机柜温度异常", false, 200);
            SetTopRect(titleField.GetComponent<RectTransform>(), 96f, 44f, 24f);

            Text descriptionLabel = CreateText(
                "Description Label",
                formPanel.transform,
                "问题描述（选填）",
                18,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(descriptionLabel.rectTransform, 154f, 24f, 24f);

            InputField descriptionField = BuildInputField(
                "Description Input",
                formPanel.transform,
                "补充问题现象、影响范围等信息",
                true,
                4000);
            SetTopRect(descriptionField.GetComponent<RectTransform>(), 180f, 98f, 24f);

            Text priorityLabel = CreateText(
                "Priority Label",
                formPanel.transform,
                "优先级",
                18,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(priorityLabel.rectTransform, 292f, 24f, 24f);

            Dropdown priorityField = BuildDropdown(formPanel.transform);
            SetTopRect(priorityField.GetComponent<RectTransform>(), 318f, 40f, 24f);

            Text positionText = CreateText(
                "Position",
                formPanel.transform,
                "问题位置",
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(positionText.rectTransform, 370f, 54f, 24f);
            positionText.color = new Color(0.64f, 0.74f, 0.8f);

            Text errorText = CreateText(
                "Form Error",
                formPanel.transform,
                string.Empty,
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(errorText.rectTransform, 428f, 70f, 24f);
            errorText.color = new Color(1f, 0.48f, 0.42f);

            Button submitButton = BuildButton(
                "Submit",
                formPanel.transform,
                "提交问题",
                new Color(0.92f, 0.36f, 0.12f),
                out Text submitLabel);
            RectTransform submitRect = submitButton.GetComponent<RectTransform>();
            submitRect.anchorMin = new Vector2(0f, 0f);
            submitRect.anchorMax = new Vector2(0f, 0f);
            submitRect.pivot = new Vector2(0f, 0f);
            submitRect.anchoredPosition = new Vector2(24f, 24f);
            submitRect.sizeDelta = new Vector2(250f, 46f);

            Button cancelButton = BuildButton(
                "Cancel",
                formPanel.transform,
                "取消",
                new Color(0.2f, 0.27f, 0.32f),
                out _);
            RectTransform cancelRect = cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(1f, 0f);
            cancelRect.anchorMax = new Vector2(1f, 0f);
            cancelRect.pivot = new Vector2(1f, 0f);
            cancelRect.anchoredPosition = new Vector2(-24f, 24f);
            cancelRect.sizeDelta = new Vector2(180f, 46f);

            IssueFormController form = formPanel.AddComponent<IssueFormController>();
            form.Configure(
                formPanel,
                titleField,
                descriptionField,
                priorityField,
                positionText,
                errorText,
                submitLabel,
                submitButton,
                cancelButton);
            formPanel.SetActive(false);

            GameObject detailPanel = CreateImage(
                "Issue Details",
                canvasObject.transform,
                new Color(0.025f, 0.045f, 0.055f, 0.97f)).gameObject;
            RectTransform detailRect = detailPanel.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(1f, 1f);
            detailRect.anchorMax = new Vector2(1f, 1f);
            detailRect.pivot = new Vector2(1f, 1f);
            detailRect.anchoredPosition = new Vector2(-32f, -126f);
            detailRect.sizeDelta = new Vector2(500f, 555f);

            Image priorityBar = CreateImage(
                "Priority Bar",
                detailPanel.transform,
                new Color(1f, 0.66f, 0.18f));
            RectTransform priorityBarRect = priorityBar.rectTransform;
            priorityBarRect.anchorMin = new Vector2(0f, 0f);
            priorityBarRect.anchorMax = new Vector2(0f, 1f);
            priorityBarRect.pivot = new Vector2(0f, 0.5f);
            priorityBarRect.anchoredPosition = Vector2.zero;
            priorityBarRect.sizeDelta = new Vector2(7f, 0f);

            Text detailEyebrow = CreateText(
                "Detail Eyebrow",
                detailPanel.transform,
                "FIELD NOTE / 现场问题",
                15,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(detailEyebrow.rectTransform, 20f, 24f, 28f);
            detailEyebrow.color = new Color(0.5f, 0.75f, 0.8f);

            Text detailTitle = CreateText(
                "Detail Title",
                detailPanel.transform,
                "问题标题",
                28,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(detailTitle.rectTransform, 58f, 72f, 28f);
            detailTitle.color = new Color(0.96f, 0.97f, 0.97f);

            Text detailMeta = CreateText(
                "Detail Meta",
                detailPanel.transform,
                string.Empty,
                16,
                FontStyle.Bold,
                TextAnchor.UpperLeft);
            SetTopRect(detailMeta.rectTransform, 136f, 24f, 28f);
            detailMeta.color = new Color(0.95f, 0.7f, 0.38f);

            Text detailDescription = CreateText(
                "Detail Description",
                detailPanel.transform,
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(detailDescription.rectTransform, 178f, 132f, 28f);
            detailDescription.color = new Color(0.79f, 0.84f, 0.86f);
            detailDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            detailDescription.verticalOverflow = VerticalWrapMode.Truncate;

            Text detailLocation = CreateText(
                "Detail Location",
                detailPanel.transform,
                string.Empty,
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft);
            SetTopRect(detailLocation.rectTransform, 330f, 100f, 28f);
            detailLocation.color = new Color(0.58f, 0.78f, 0.82f);

            Text detailHint = CreateText(
                "Detail Hint",
                detailPanel.transform,
                "Esc 或关闭按钮返回现场",
                15,
                FontStyle.Normal,
                TextAnchor.LowerLeft);
            SetTopRect(detailHint.rectTransform, 474f, 30f, 28f);
            detailHint.color = new Color(0.48f, 0.56f, 0.6f);

            Button detailCloseButton = BuildButton(
                "Close Issue Detail",
                detailPanel.transform,
                "关闭",
                new Color(0.16f, 0.23f, 0.27f),
                out _);
            RectTransform closeRect = detailCloseButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-20f, -16f);
            closeRect.sizeDelta = new Vector2(82f, 36f);

            IssueDetailController detail = detailPanel.AddComponent<IssueDetailController>();
            detail.Configure(
                detailPanel,
                detailTitle,
                detailMeta,
                detailDescription,
                detailLocation,
                priorityBar,
                detailCloseButton);
            detailPanel.SetActive(false);

            canvasObject.AddComponent<InspectionChineseFont>();

            GameObject systems = new GameObject("Inspection Systems");
            IssueApiClient apiClient = systems.AddComponent<IssueApiClient>();
            apiClient.Configure("http://127.0.0.1:8080");
            GameObject markerRootObject = new GameObject("Restored Issue Markers");
            IssuePlacementController placement = systems.AddComponent<IssuePlacementController>();
            placement.Configure(
                sceneCamera,
                LayerMask.GetMask("Inspectable"),
                markerPrefab,
                form,
                detail,
                player,
                hud,
                apiClient,
                markerRootObject.transform);
        }

        private static void BuildEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        private static InputField BuildInputField(
            string name,
            Transform parent,
            string placeholderValue,
            bool multiline,
            int characterLimit)
        {
            Image background = CreateImage(name, parent, new Color(0.09f, 0.13f, 0.16f, 1f));
            InputField input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.characterLimit = characterLimit;
            input.textComponent = CreateInputText("Text", background.transform, string.Empty, multiline);
            input.placeholder = CreateInputText("Placeholder", background.transform, placeholderValue, multiline);
            return input;
        }

        private static Text CreateInputText(string name, Transform parent, string value, bool multiline)
        {
            Text text = CreateText(
                name,
                parent,
                value,
                18,
                FontStyle.Normal,
                multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 8f);
            rect.offsetMax = new Vector2(-12f, -8f);
            text.supportRichText = false;
            return text;
        }

        private static Dropdown BuildDropdown(Transform parent)
        {
            Image background = CreateImage("Priority", parent, new Color(0.09f, 0.13f, 0.16f, 1f));
            Dropdown dropdown = background.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = background;

            Text caption = CreateText(
                "Caption",
                background.transform,
                "中",
                18,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            RectTransform captionRect = caption.rectTransform;
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = new Vector2(12f, 0f);
            captionRect.offsetMax = new Vector2(-36f, 0f);

            Text arrow = CreateText(
                "Arrow",
                background.transform,
                "v",
                17,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            RectTransform arrowRect = arrow.rectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = new Vector2(1f, 1f);
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-12f, 0f);
            arrowRect.sizeDelta = new Vector2(20f, 0f);

            Image templateImage = CreateImage(
                "Template",
                background.transform,
                new Color(0.055f, 0.08f, 0.1f, 1f));
            RectTransform templateRect = templateImage.rectTransform;
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            templateRect.sizeDelta = new Vector2(0f, 124f);

            ScrollRect scroll = templateImage.gameObject.AddComponent<ScrollRect>();
            Image viewportImage = CreateImage(
                "Viewport",
                templateImage.transform,
                new Color(0f, 0f, 0f, 0f));
            RectTransform viewportRect = viewportImage.rectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(2f, 2f);
            viewportRect.offsetMax = new Vector2(-2f, -2f);
            viewportImage.gameObject.AddComponent<RectMask2D>();

            GameObject content = CreateUiObject("Content", viewportImage.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 120f);

            Image itemImage = CreateImage("Item", content.transform, new Color(0.09f, 0.13f, 0.16f, 1f));
            RectTransform itemRect = itemImage.rectTransform;
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(0.5f, 1f);
            itemRect.anchoredPosition = Vector2.zero;
            itemRect.sizeDelta = new Vector2(0f, 40f);

            Toggle toggle = itemImage.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemImage;
            Image checkmark = CreateImage("Checkmark", itemImage.transform, new Color(1f, 0.5f, 0.2f, 1f));
            RectTransform checkmarkRect = checkmark.rectTransform;
            checkmarkRect.anchorMin = new Vector2(0f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
            checkmarkRect.pivot = new Vector2(0f, 0.5f);
            checkmarkRect.anchoredPosition = new Vector2(12f, 0f);
            checkmarkRect.sizeDelta = new Vector2(12f, 12f);
            toggle.graphic = checkmark;

            Text itemLabel = CreateText(
                "Item Label",
                itemImage.transform,
                "中",
                17,
                FontStyle.Normal,
                TextAnchor.MiddleLeft);
            RectTransform itemLabelRect = itemLabel.rectTransform;
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(34f, 0f);
            itemLabelRect.offsetMax = new Vector2(-8f, 0f);

            scroll.content = contentRect;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            dropdown.template = templateRect;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData("低"),
                new Dropdown.OptionData("中"),
                new Dropdown.OptionData("高")
            };
            dropdown.value = 1;
            dropdown.RefreshShownValue();
            templateImage.gameObject.SetActive(false);
            return dropdown;
        }

        private static Button BuildButton(
            string name,
            Transform parent,
            string label,
            Color backgroundColor,
            out Text labelText)
        {
            Image background = CreateImage(name, parent, backgroundColor);
            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            labelText = CreateText(
                "Label",
                background.transform,
                label,
                18,
                FontStyle.Bold,
                TextAnchor.MiddleCenter);
            RectTransform labelRect = labelText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return button;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject gameObject = CreateUiObject(name, parent);
            Image image = gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            string value,
            int size,
            FontStyle style,
            TextAnchor alignment)
        {
            GameObject gameObject = CreateUiObject(name, parent);
            Text text = gameObject.AddComponent<Text>();
            text.font = GetFont();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.9f, 0.92f, 0.94f);
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static Font GetFont()
        {
            if (cachedFont == null)
            {
                cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (cachedFont == null)
            {
                throw new InvalidOperationException("The built-in UI font could not be loaded.");
            }

            return cachedFont;
        }

        private static void SetTopRect(
            RectTransform rect,
            float top,
            float height,
            float horizontalMargin)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(-(horizontalMargin * 2f), height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetTopRightRect(
            RectTransform rect,
            float right,
            float top,
            float width,
            float height)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(right, top);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
