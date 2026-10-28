using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UnityInspection.Tests
{
    public sealed class InspectionPlayModeTests
    {
        private const string SceneName = "Inspection";

        [UnityTest]
        public IEnumerator Scene_HasInspectableGeometryPlayerAndForm()
        {
            yield return LoadInspectionScene();

            Camera camera = Object.FindFirstObjectByType<Camera>();
            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            IssueFormController form = Object.FindFirstObjectByType<IssueFormController>(FindObjectsInactive.Include);
            IssueDetailController detail = Object.FindFirstObjectByType<IssueDetailController>(FindObjectsInactive.Include);

            Assert.That(camera, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(placement, Is.Not.Null);
            Assert.That(form, Is.Not.Null);
            Assert.That(detail, Is.Not.Null);
            Assert.That(placement.State, Is.EqualTo(DraftState.Idle));
            Assert.That(form.IsVisible, Is.False);

            int inspectableCount = 0;
            Scene activeScene = SceneManager.GetActiveScene();
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (item.gameObject.layer == LayerMask.NameToLayer("Inspectable"))
                    {
                        inspectableCount++;
                    }
                }
            }

            Assert.That(inspectableCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(camera.transform.parent, Is.EqualTo(player.transform));
        }

        [UnityTest]
        public IEnumerator Placement_UsesExactHitPointAndKeepsSingleDraft()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            Assert.That(placement, Is.Not.Null);
            Assert.That(player, Is.Not.Null);

            Vector3 hitPoint = new Vector3(1.25f, 0.8f, 2.4f);
            Vector3 hitNormal = Vector3.up;
            Assert.That(placement.TryBeginDraftAtPoint(hitPoint, hitNormal), Is.True);

            Assert.That(placement.State, Is.EqualTo(DraftState.Drafting));
            Assert.That(placement.CurrentDraftPoint, Is.EqualTo(hitPoint));
            Assert.That(player.InputLocked, Is.True);
            Assert.That(placement.DraftMarker, Is.Not.Null);

            IssueMarker marker = placement.DraftMarker.GetComponent<IssueMarker>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.ReportedPosition.ToVector3(), Is.EqualTo(hitPoint));
            Assert.That(
                placement.DraftMarker.transform.position,
                Is.EqualTo(hitPoint + hitNormal * IssuePlacementController.MarkerSurfaceOffset));

            GameObject firstMarker = placement.DraftMarker;
            Assert.That(placement.TryBeginDraftAtPoint(Vector3.one, Vector3.up), Is.False);
            Assert.That(placement.DraftMarker, Is.SameAs(firstMarker));

            placement.CancelDraft();
            Assert.That(placement.State, Is.EqualTo(DraftState.Idle));
            Assert.That(player.InputLocked, Is.False);
            Assert.That(placement.DraftMarker, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_CenterScreenRayHitsInspectableSurface()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            Assert.That(placement, Is.Not.Null);

            Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            Assert.That(placement.TryPlaceAtScreenPosition(center), Is.True);
            Assert.That(placement.State, Is.EqualTo(DraftState.Drafting));
            Assert.That(placement.CurrentDraftPoint.z, Is.EqualTo(6.2f).Within(0.02f));
            Assert.That(placement.DraftMarker, Is.Not.Null);
            placement.CancelDraft();
        }

        [Test]
        public void ApiContractHelpers_OnlyAcceptCreateSuccessAndRejectOtherStatuses()
        {
            Assert.That(IssueApiClient.IsSuccessfulCreateStatus(200), Is.True);
            Assert.That(IssueApiClient.IsSuccessfulCreateStatus(201), Is.True);
            Assert.That(IssueApiClient.IsSuccessfulCreateStatus(204), Is.False);
            Assert.That(IssueApiClient.IsSuccessfulCreateStatus(409), Is.False);
            Assert.That(IssueApiClient.IsSuccessfulCreateStatus(500), Is.False);
            Assert.That(IssueApiClient.IsSuccessfulListStatus(200), Is.True);
            Assert.That(IssueApiClient.IsSuccessfulListStatus(204), Is.False);
            Assert.That(IssueApiClient.IsTimeoutError("Request timeout"), Is.True);
            Assert.That(IssueApiClient.IsTimeoutError("Connection timed out"), Is.True);
            Assert.That(IssueApiClient.IsTimeoutError("Could not resolve host"), Is.False);

            const string errorBody = "{\"error\":{\"code\":\"INVALID_ARGUMENT\",\"message\":\"title is required\"}}";
            Assert.That(
                IssueApiClient.ExtractServerError(errorBody),
                Is.EqualTo("title is required"));
            Assert.That(IssueApiClient.ExtractServerError("not json"), Is.Empty);
            Assert.That(
                IssueApiClient.BuildIssuesEndpoint("http://127.0.0.1:8080/"),
                Is.EqualTo("http://127.0.0.1:8080/api/issues"));
        }

        [UnityTest]
        public IEnumerator ApiClient_CreateListAndReplayShareOneIssueID()
        {
            string baseUrl = Environment.GetEnvironmentVariable("UNITY_INSPECTION_API_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Assert.Ignore("UNITY_INSPECTION_API_URL is not set; integration test was not run.");
            }

            yield return LoadInspectionScene();
            IssueApiClient apiClient = Object.FindFirstObjectByType<IssueApiClient>();
            Assert.That(apiClient, Is.Not.Null);
            apiClient.Configure(baseUrl);

            string key = Guid.NewGuid().ToString();
            CreateIssueRequest request = new CreateIssueRequest
            {
                title = "Unity integration check",
                description = "Created from the Unity PlayMode test.",
                priority = "high",
                deviceCode = "CAB-A",
                zone = "A区 · 配电区",
                position = new ApiPosition(new Vector3(1.125f, 0.75f, 2.375f))
            };

            ApiOperationResult first = null;
            yield return apiClient.StartCoroutine(apiClient.CreateIssue(key, request, result => first = result));
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Succeeded, Is.True, first?.Message);
            Assert.That(first.StatusCode, Is.EqualTo(201));
            Assert.That(first.Issue.id, Is.Not.Empty);

            ApiOperationResult replay = null;
            yield return apiClient.StartCoroutine(apiClient.CreateIssue(key, request, result => replay = result));
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.Succeeded, Is.True, replay?.Message);
            Assert.That(replay.StatusCode, Is.EqualTo(200));
            Assert.That(replay.Issue.id, Is.EqualTo(first.Issue.id));

            using UnityWebRequest listRequest = UnityWebRequest.Get(IssueApiClient.BuildIssuesEndpoint(baseUrl));
            yield return listRequest.SendWebRequest();
            Assert.That(listRequest.result, Is.EqualTo(UnityWebRequest.Result.Success), listRequest.error);

            IssueListResponse list = JsonUtility.FromJson<IssueListResponse>(listRequest.downloadHandler.text);
            Assert.That(list, Is.Not.Null);
            IssueRecord[] matches = Array.FindAll(list.items, item => item.id == first.Issue.id);
            Assert.That(matches, Has.Length.EqualTo(1));
            Assert.That(matches[0].position.x, Is.EqualTo(request.position.x).Within(0.000001f));
            Assert.That(matches[0].position.y, Is.EqualTo(request.position.y).Within(0.000001f));
            Assert.That(matches[0].position.z, Is.EqualTo(request.position.z).Within(0.000001f));
            Assert.That(matches[0].status, Is.EqualTo("open"));
            Assert.That(matches[0].deviceCode, Is.EqualTo("CAB-A"));
            Assert.That(matches[0].zone, Is.EqualTo("A区 · 配电区"));
        }

        [UnityTest]
        public IEnumerator Markers_RestoreSnapshotAndOpenIssueDetails()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            IssueDetailController detail = Object.FindFirstObjectByType<IssueDetailController>(FindObjectsInactive.Include);
            Assert.That(placement, Is.Not.Null);
            Assert.That(detail, Is.Not.Null);

            IssueRecord highPriority = new IssueRecord
            {
                id = "0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8",
                title = "配电柜温度异常",
                description = "右侧温度明显偏高",
                priority = "high",
                status = "open",
                deviceCode = "WALL-N",
                zone = "北侧公共区域",
                position = new ApiPosition(new Vector3(0f, 1.65f, 6.18f))
            };
            IssueRecord resolved = new IssueRecord
            {
                id = "5aa670db-c30d-4cab-8668-c02700992222",
                title = "设备箱标签破损",
                description = string.Empty,
                priority = "low",
                status = "resolved",
                deviceCode = "BOX-01",
                zone = "B区 · 暂存区",
                position = new ApiPosition(new Vector3(-1.75f, 1.0f, -1.95f))
            };

            placement.ApplyIssueSnapshot(new[] { highPriority, resolved });
            yield return null;

            Assert.That(placement.RestoredMarkerCount, Is.GreaterThanOrEqualTo(2));
            IssueMarker[] markers = Array.FindAll(
                Object.FindObjectsByType<IssueMarker>(FindObjectsSortMode.None),
                marker => marker.Issue != null &&
                          (marker.Issue.id == highPriority.id || marker.Issue.id == resolved.id));
            Assert.That(markers, Has.Length.EqualTo(2));
            IssueMarker highMarker = Array.Find(
                markers,
                marker => marker.Issue != null && marker.Issue.id == highPriority.id);
            IssueMarker resolvedMarker = Array.Find(
                markers,
                marker => marker.Issue != null && marker.Issue.id == resolved.id);
            Assert.That(highMarker, Is.Not.Null);
            Assert.That(resolvedMarker, Is.Not.Null);
            Assert.That(highMarker.transform.localScale.x, Is.GreaterThan(resolvedMarker.transform.localScale.x));
            Assert.That(highMarker.IsLabelVisible, Is.False);
            Assert.That(resolvedMarker.IsLabelVisible, Is.False);
            Assert.That(highMarker.MarkerTitle, Is.EqualTo(highPriority.title));

            highMarker.SetHighlighted(true);
            Assert.That(highMarker.IsLabelVisible, Is.True);
            yield return null;
            highMarker.SetHighlighted(false);
            Assert.That(highMarker.IsLabelVisible, Is.False);

            Camera camera = Object.FindFirstObjectByType<Camera>();
            Vector3 screenPoint = camera.WorldToScreenPoint(highPriority.position.ToVector3());
            Assert.That(placement.TryActivateWorldClick(screenPoint), Is.True);
            Assert.That(detail.IsVisible, Is.True);
            Assert.That(detail.CurrentIssue.deviceCode, Is.EqualTo("WALL-N"));
            Assert.That(GameObject.Find("Detail Title").GetComponent<Text>().text, Is.EqualTo(highPriority.title));

            placement.HideIssueDetails();
            Assert.That(detail.IsVisible, Is.False);

            placement.ApplyIssueSnapshot(new[] { resolved });
            yield return null;
            Assert.That(
                Array.Exists(
                    Object.FindObjectsByType<IssueMarker>(FindObjectsSortMode.None),
                    marker => marker.Issue != null && marker.Issue.id == highPriority.id),
                Is.False);
        }

        [UnityTest]
        public IEnumerator ApiClient_ConnectionFailureIsExplicit()
        {
            yield return LoadInspectionScene();
            IssueApiClient apiClient = Object.FindFirstObjectByType<IssueApiClient>();
            apiClient.Configure("http://127.0.0.1:1");

            ApiOperationResult result = null;
            CreateIssueRequest request = new CreateIssueRequest
            {
                title = "Expected failure",
                description = string.Empty,
                priority = "low",
                position = new ApiPosition(Vector3.zero)
            };

            yield return apiClient.StartCoroutine(
                apiClient.CreateIssue(Guid.NewGuid().ToString(), request, value => result = value));

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator ApiClient_ServerValidationAndInvalidUrlFailExplicitly()
        {
            string baseUrl = Environment.GetEnvironmentVariable("UNITY_INSPECTION_API_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Assert.Ignore("UNITY_INSPECTION_API_URL is not set; integration test was not run.");
            }

            yield return LoadInspectionScene();
            IssueApiClient apiClient = Object.FindFirstObjectByType<IssueApiClient>();
            apiClient.Configure(baseUrl);

            CreateIssueRequest invalidRequest = new CreateIssueRequest
            {
                title = string.Empty,
                description = string.Empty,
                priority = "low",
                position = new ApiPosition(Vector3.zero)
            };

            ApiOperationResult validationResult = null;
            yield return apiClient.StartCoroutine(
                apiClient.CreateIssue(Guid.NewGuid().ToString(), invalidRequest, result => validationResult = result));
            Assert.That(validationResult, Is.Not.Null);
            Assert.That(validationResult.Succeeded, Is.False);
            Assert.That(validationResult.StatusCode, Is.EqualTo(400));
            Assert.That(validationResult.Message, Does.Contain("问题标题"));

            apiClient.Configure("not a valid URL");
            ApiOperationResult urlResult = null;
            yield return apiClient.StartCoroutine(
                apiClient.CreateIssue(Guid.NewGuid().ToString(), invalidRequest, result => urlResult = result));
            Assert.That(urlResult, Is.Not.Null);
            Assert.That(urlResult.Succeeded, Is.False);
            Assert.That(urlResult.Message, Is.Not.Empty);
        }

        [UnityTest]
        public IEnumerator Form_EmptyTitleStaysDraftingWithoutNetworkRequest()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            Assert.That(placement, Is.Not.Null);
            Assert.That(placement.TryBeginDraftAtPoint(Vector3.one, Vector3.up), Is.True);

            placement.SubmitDraft();
            yield return null;

            Assert.That(placement.State, Is.EqualTo(DraftState.Drafting));
            Assert.That(GameObject.Find("Title Input").GetComponent<InputField>().interactable, Is.True);
            Assert.That(GameObject.Find("Form Error").GetComponent<Text>().text, Is.Not.Empty);
            placement.CancelDraft();
        }

        [UnityTest]
        public IEnumerator Placement_ErrorRetryKeepsDraftAndCreatesOneIssue()
        {
            string baseUrl = Environment.GetEnvironmentVariable("UNITY_INSPECTION_API_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Assert.Ignore("UNITY_INSPECTION_API_URL is not set; integration test was not run.");
            }

            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            IssueApiClient apiClient = Object.FindFirstObjectByType<IssueApiClient>();
            FirstPersonController player = Object.FindFirstObjectByType<FirstPersonController>();
            Assert.That(placement, Is.Not.Null);
            Assert.That(apiClient, Is.Not.Null);
            Assert.That(player, Is.Not.Null);

            string uniqueTitle = $"Unity form retry {Guid.NewGuid():N}";
            Vector3 hitPoint = new Vector3(2.25f, 1.1f, 1.75f);
            Assert.That(placement.TryBeginDraftAtPoint(hitPoint, Vector3.forward), Is.True);
            string draftKey = placement.DraftIdempotencyKey;
            Assert.That(draftKey, Is.Not.Empty);

            GameObject.Find("Title Input").GetComponent<InputField>().text = uniqueTitle;
            GameObject.Find("Description Input").GetComponent<InputField>().text = "Retry must preserve this description.";
            GameObject.Find("Priority").GetComponent<Dropdown>().value = 2;

            apiClient.Configure("http://127.0.0.1:1");
            placement.SubmitDraft();
            yield return WaitForState(placement, DraftState.Error, 8f);

            Assert.That(placement.State, Is.EqualTo(DraftState.Error));
            Assert.That(player.InputLocked, Is.True);
            Assert.That(placement.DraftMarker, Is.Not.Null);
            Assert.That(placement.DraftIdempotencyKey, Is.EqualTo(draftKey));
            Assert.That(GameObject.Find("Title Input").GetComponent<InputField>().interactable, Is.False);
            Assert.That(GameObject.Find("Description Input").GetComponent<InputField>().interactable, Is.False);
            Assert.That(GameObject.Find("Priority").GetComponent<Dropdown>().interactable, Is.False);

            apiClient.Configure(baseUrl);
            placement.SubmitDraft();
            yield return WaitForState(placement, DraftState.Idle, 12f);

            Assert.That(placement.State, Is.EqualTo(DraftState.Idle));
            Assert.That(player.InputLocked, Is.False);
            Assert.That(placement.DraftMarker, Is.Null);

            using UnityWebRequest listRequest = UnityWebRequest.Get(IssueApiClient.BuildIssuesEndpoint(baseUrl));
            yield return listRequest.SendWebRequest();
            Assert.That(listRequest.result, Is.EqualTo(UnityWebRequest.Result.Success), listRequest.error);
            IssueListResponse list = JsonUtility.FromJson<IssueListResponse>(listRequest.downloadHandler.text);
            IssueRecord[] matches = Array.FindAll(list.items, item => item.title == uniqueTitle);
            Assert.That(matches, Has.Length.EqualTo(1));
            Assert.That(matches[0].description, Is.EqualTo("Retry must preserve this description."));
            Assert.That(matches[0].priority, Is.EqualTo("high"));
            Assert.That(matches[0].position.x, Is.EqualTo(hitPoint.x).Within(0.000001f));
            Assert.That(matches[0].position.y, Is.EqualTo(hitPoint.y).Within(0.000001f));
            Assert.That(matches[0].position.z, Is.EqualTo(hitPoint.z).Within(0.000001f));
        }

        [UnityTest]
        public IEnumerator Form_VisualSurfaceFitsInCanvas()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            Assert.That(placement, Is.Not.Null);
            Assert.That(placement.TryBeginDraftAtPoint(new Vector3(0.2f, 0.3f, 0.4f), Vector3.up), Is.True);
            yield return null;
            yield return null;

            GameObject formPanel = GameObject.Find("Issue Form");
            Assert.That(formPanel, Is.Not.Null);
            Assert.That(formPanel.activeInHierarchy, Is.True);

            RectTransform formRect = formPanel.GetComponent<RectTransform>();
            Vector3[] corners = new Vector3[4];
            formRect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(screenPoint.x, Is.InRange(-1f, Screen.width + 1f));
                Assert.That(screenPoint.y, Is.InRange(-1f, Screen.height + 1f));
            }

            Assert.That(GameObject.Find("Title Input").GetComponent<InputField>().interactable, Is.True);
            Assert.That(GameObject.Find("Description Input").GetComponent<InputField>().interactable, Is.True);
            Assert.That(GameObject.Find("Priority").GetComponent<Dropdown>().interactable, Is.True);
            Assert.That(GameObject.Find("Submit").GetComponent<Button>().interactable, Is.True);
            Assert.That(GameObject.Find("Cancel").GetComponent<Button>().interactable, Is.True);
            placement.CancelDraft();
        }

        [UnityTest]
        public IEnumerator Form_OffscreenPreviewCanBeRendered()
        {
            yield return LoadInspectionScene();

            IssuePlacementController placement = Object.FindFirstObjectByType<IssuePlacementController>();
            Assert.That(placement, Is.Not.Null);
            Assert.That(placement.TryBeginDraftAtPoint(new Vector3(0.25f, 0.35f, 0.45f), Vector3.up), Is.True);
            yield return null;

            Camera camera = Object.FindFirstObjectByType<Camera>();
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);

            RenderMode previousMode = canvas.renderMode;
            Camera previousWorldCamera = canvas.worldCamera;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture capture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);

            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 2f;
                Canvas.ForceUpdateCanvases();
                yield return null;

                camera.targetTexture = capture;
                camera.aspect = 1600f / 900f;
                camera.Render();
                RenderTexture.active = capture;

                Texture2D image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, 1600f, 900f), 0, 0);
                image.Apply();

                string previewPath = Path.GetFullPath(
                    Path.Combine(Application.dataPath, "..", "Logs", "unity-inspection-preview.png"));
                File.WriteAllBytes(previewPath, image.EncodeToPNG());
                Object.Destroy(image);

                Assert.That(new FileInfo(previewPath).Length, Is.GreaterThan(1000));
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = previousTarget;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousWorldCamera;
                capture.Release();
                Object.Destroy(capture);
                placement.CancelDraft();
            }
        }

        private static IEnumerator WaitForState(
            IssuePlacementController placement,
            DraftState expected,
            float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (placement.State != expected && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                placement.State,
                Is.EqualTo(expected),
                $"Timed out waiting for {expected}; current state is {placement.State}.");
        }

        private static IEnumerator LoadInspectionScene()
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null, $"Could not load {SceneName}; verify EditorBuildSettings.");
            while (!operation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;
        }
    }
}
