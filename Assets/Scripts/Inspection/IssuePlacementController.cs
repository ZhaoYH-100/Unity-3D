using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityInspection
{
    public sealed class IssuePlacementController : MonoBehaviour
    {
        public const float MarkerSurfaceOffset = 0.02f;

        [SerializeField] private Camera sceneCamera;
        [SerializeField] private LayerMask inspectableMask;
        [SerializeField] private GameObject markerPrefab;
        [SerializeField] private IssueFormController form;
        [SerializeField] private IssueDetailController detail;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private InspectionHud hud;
        [SerializeField] private IssueApiClient apiClient;
        [SerializeField] private Transform markerRoot;
        [SerializeField] private float maximumRayDistance = 100f;
        [SerializeField] private float refreshIntervalSeconds = 8f;

        private readonly Dictionary<string, IssueMarker> restoredMarkers =
            new Dictionary<string, IssueMarker>();
        private GameObject draftMarker;
        private CreateIssueRequest frozenRequest;
        private bool requestFrozen;
        private bool listRequestInFlight;
        private InspectionLocation draftLocation;

        public DraftState State { get; private set; } = DraftState.Idle;
        public Vector3 CurrentDraftPoint { get; private set; }
        public GameObject DraftMarker => draftMarker;
        public string DraftIdempotencyKey { get; private set; }
        public CreateIssueRequest FrozenRequest => frozenRequest;
        public int RestoredMarkerCount => restoredMarkers.Count;
        public bool IsIssueDetailVisible => detail != null && detail.IsVisible;

        public void Configure(
            Camera sceneCamera,
            LayerMask inspectableMask,
            GameObject markerPrefab,
            IssueFormController form,
            IssueDetailController detail,
            FirstPersonController player,
            InspectionHud hud,
            IssueApiClient apiClient,
            Transform markerRoot)
        {
            this.sceneCamera = sceneCamera;
            this.inspectableMask = inspectableMask;
            this.markerPrefab = markerPrefab;
            this.form = form;
            this.detail = detail;
            this.player = player;
            this.hud = hud;
            this.apiClient = apiClient;
            this.markerRoot = markerRoot;
        }

        public bool TryBeginDraftAtPoint(Vector3 reportedPoint, Vector3 surfaceNormal)
        {
            if (State != DraftState.Idle)
            {
                return false;
            }

            if (form == null || player == null || hud == null)
            {
                Debug.LogError("Issue placement dependencies are not configured.");
                return false;
            }

            HideIssueDetails();
            draftLocation = ResolveLocationAtPoint(reportedPoint);
            Vector3 visualPosition = reportedPoint + surfaceNormal.normalized * MarkerSurfaceOffset;
            draftMarker = CreateMarker(visualPosition, reportedPoint, true);
            CurrentDraftPoint = reportedPoint;
            DraftIdempotencyKey = Guid.NewGuid().ToString();
            frozenRequest = null;
            requestFrozen = false;

            State = DraftState.Drafting;
            player.SetInputLocked(true);
            form.ShowDraft(
                reportedPoint,
                draftLocation != null ? draftLocation.DisplayName : "SITE-01 · 公共区域",
                DraftIdempotencyKey);
            hud.SetCrosshairVisible(false);
            hud.SetStatus("已定位现场设备，请填写问题信息。");
            return true;
        }

        public bool TryPlaceAtScreenPosition(Vector3 screenPosition)
        {
            if (State != DraftState.Idle || sceneCamera == null)
            {
                return false;
            }

            Ray ray = sceneCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    maximumRayDistance,
                    inspectableMask,
                    QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return TryBeginDraftAtPoint(hit.point, hit.normal);
        }

        public bool TryActivateWorldClick(Vector3 screenPosition)
        {
            if (State != DraftState.Idle || sceneCamera == null)
            {
                return false;
            }

            Ray ray = sceneCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    maximumRayDistance,
                    ~0,
                    QueryTriggerInteraction.Collide))
            {
                return false;
            }

            IssueMarker marker = hit.collider.GetComponentInParent<IssueMarker>();
            if (marker != null && !marker.IsDraft)
            {
                ShowIssueDetails(marker.Issue);
                return true;
            }

            if ((inspectableMask.value & (1 << hit.collider.gameObject.layer)) == 0)
            {
                return false;
            }

            return TryBeginDraftAtPoint(hit.point, hit.normal);
        }

        public void ShowIssueDetails(IssueRecord issue)
        {
            if (issue == null || detail == null)
            {
                return;
            }

            detail.Show(issue);
            hud.SetCrosshairVisible(false);
            hud.SetStatus($"查看问题：{issue.title}");
            player.SetInputLocked(true);
        }

        public void HideIssueDetails()
        {
            if (detail == null || !detail.IsVisible)
            {
                return;
            }

            detail.Hide();
            if (State == DraftState.Idle)
            {
                hud.SetCrosshairVisible(true);
                hud.ClearStatus();
                player.SetInputLocked(false);
            }
        }

        public void SubmitDraft()
        {
            if (State != DraftState.Drafting && State != DraftState.Error)
            {
                return;
            }

            if (!requestFrozen)
            {
                if (!form.TryReadInput(out string title, out string description, out string priority))
                {
                    hud.SetStatus("请先填写必填内容。", true);
                    return;
                }

                frozenRequest = new CreateIssueRequest
                {
                    title = title,
                    description = description,
                    priority = priority,
                    deviceCode = draftLocation != null ? draftLocation.DeviceCode : "SITE-01",
                    zone = draftLocation != null ? draftLocation.Zone : "公共区域",
                    position = new ApiPosition(CurrentDraftPoint)
                };
                requestFrozen = true;
            }

            State = DraftState.Submitting;
            form.SetSubmitting();
            hud.SetStatus("正在提交问题…");
            StartCoroutine(apiClient.CreateIssue(DraftIdempotencyKey, frozenRequest, HandleCreateCompleted));
        }

        public void CancelDraft()
        {
            if (State != DraftState.Drafting && State != DraftState.Error)
            {
                return;
            }

            ClearDraft();
            hud.SetStatus("已取消问题草稿。");
        }

        public void RefreshIssues()
        {
            if (apiClient == null || listRequestInFlight || !isActiveAndEnabled)
            {
                return;
            }

            listRequestInFlight = true;
            hud.SetSyncStatus("正在同步现场标记…");
            StartCoroutine(apiClient.ListIssues(HandleListCompleted));
        }

        public void ApplyIssueSnapshot(IssueRecord[] issues)
        {
            EnsureMarkerRoot();
            HashSet<string> received = new HashSet<string>();
            IssueRecord[] safeIssues = issues ?? Array.Empty<IssueRecord>();

            for (int index = 0; index < safeIssues.Length; index++)
            {
                IssueRecord issue = safeIssues[index];
                if (issue == null ||
                    string.IsNullOrWhiteSpace(issue.id) ||
                    issue.position == null ||
                    !received.Add(issue.id))
                {
                    continue;
                }

                if (restoredMarkers.TryGetValue(issue.id, out IssueMarker marker) && marker != null)
                {
                    marker.ConfigureIssue(issue, index + 1);
                    continue;
                }

                GameObject markerObject = CreateMarker(issue.position.ToVector3(), issue.position.ToVector3(), false);
                markerObject.name = $"Issue Marker {issue.id}";
                markerObject.transform.SetParent(markerRoot, true);
                marker = markerObject.GetComponent<IssueMarker>();
                marker.ConfigureIssue(issue, index + 1);
                restoredMarkers[issue.id] = marker;
            }

            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, IssueMarker> pair in restoredMarkers)
            {
                if (!received.Contains(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }
            foreach (string id in removed)
            {
                if (restoredMarkers.TryGetValue(id, out IssueMarker marker) && marker != null)
                {
                    if (detail != null && detail.CurrentIssue != null && detail.CurrentIssue.id == id)
                    {
                        HideIssueDetails();
                    }
                    Destroy(marker.gameObject);
                }
                restoredMarkers.Remove(id);
            }

            UpdateSummaryFromMarkers();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F5))
            {
                RefreshIssues();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (IsIssueDetailVisible)
                {
                    HideIssueDetails();
                    return;
                }
                if (State == DraftState.Drafting || State == DraftState.Error)
                {
                    CancelDraft();
                    return;
                }
            }

            UpdateMarkerHighlight();

            if (Input.GetMouseButtonDown(0) == false || IsPointerOverUi())
            {
                return;
            }

            TryActivateWorldClick(Input.mousePosition);
        }

        private void Start()
        {
            if (form == null)
            {
                Debug.LogError("Issue form is not configured.");
                enabled = false;
                return;
            }

            EnsureMarkerRoot();
            form.SubmitRequested += SubmitDraft;
            form.CancelRequested += CancelDraft;
            if (detail != null)
            {
                detail.CloseRequested += HideIssueDetails;
            }

            RefreshIssues();
            StartCoroutine(RefreshLoop());
        }

        private void OnDestroy()
        {
            if (form == null)
            {
                return;
            }

            form.SubmitRequested -= SubmitDraft;
            form.CancelRequested -= CancelDraft;
            if (detail != null)
            {
                detail.CloseRequested -= HideIssueDetails;
            }
            DestroyMarker();
        }

        private IEnumerator RefreshLoop()
        {
            while (isActiveAndEnabled)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(2f, refreshIntervalSeconds));
                if (State == DraftState.Idle)
                {
                    RefreshIssues();
                }
            }
        }

        private void HandleListCompleted(IssueListOperationResult result)
        {
            listRequestInFlight = false;
            if (result == null)
            {
                hud.SetSyncStatus("同步未返回结果 · F5 重试", true);
                return;
            }

            if (!result.Succeeded)
            {
                hud.SetSyncStatus($"{result.Message}  F5 重试", true);
                return;
            }

            ApplyIssueSnapshot(result.Issues);
            hud.SetSyncStatus($"已同步 · {DateTime.Now:HH:mm:ss}  /  F5 刷新");
        }

        private void HandleCreateCompleted(ApiOperationResult result)
        {
            if (result.Succeeded)
            {
                IssueRecord issue = result.Issue;
                string issueID = issue.id;
                ClearDraft();
                if (!restoredMarkers.ContainsKey(issue.id))
                {
                    GameObject markerObject = CreateMarker(
                        issue.position.ToVector3(),
                        issue.position.ToVector3(),
                        false);
                    markerObject.name = $"Issue Marker {issue.id}";
                    markerObject.transform.SetParent(markerRoot, true);
                    IssueMarker marker = markerObject.GetComponent<IssueMarker>();
                    restoredMarkers[issue.id] = marker;
                }
                restoredMarkers[issue.id].ConfigureIssue(issue, restoredMarkers.Count);
                UpdateSummaryFromMarkers();
                hud.SetSyncStatus($"已保存 · {DateTime.Now:HH:mm:ss}");
                hud.SetStatus($"问题已保存：{issueID}");
                return;
            }

            State = DraftState.Error;
            form.ShowError(result.Message);
            hud.SetStatus(result.Message, true);
        }

        private GameObject CreateMarker(
            Vector3 visualPosition,
            Vector3 reportedPosition,
            bool isDraft)
        {
            GameObject marker;
            if (markerPrefab != null)
            {
                marker = Instantiate(markerPrefab, visualPosition, Quaternion.identity);
            }
            else
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.position = visualPosition;
                marker.transform.localScale = Vector3.one * 0.25f;
            }

            marker.name = isDraft ? "Issue Draft Marker" : "Issue Marker";
            IssueMarker markerComponent = marker.GetComponent<IssueMarker>();
            if (markerComponent == null)
            {
                markerComponent = marker.AddComponent<IssueMarker>();
            }

            markerComponent.Configure(reportedPosition);
            return marker;
        }

        private void ClearDraft()
        {
            DestroyMarker();
            form.Hide();
            player.SetInputLocked(false);
            hud.SetCrosshairVisible(true);
            State = DraftState.Idle;
            DraftIdempotencyKey = null;
            frozenRequest = null;
            requestFrozen = false;
            CurrentDraftPoint = Vector3.zero;
            draftLocation = null;
        }

        private void DestroyMarker()
        {
            if (draftMarker != null)
            {
                Destroy(draftMarker);
                draftMarker = null;
            }
        }

        private void EnsureMarkerRoot()
        {
            if (markerRoot != null)
            {
                return;
            }

            GameObject root = new GameObject("Restored Issue Markers");
            markerRoot = root.transform;
        }

        private void UpdateSummaryFromMarkers()
        {
            int open = 0;
            int inProgress = 0;
            int resolved = 0;
            foreach (IssueMarker marker in restoredMarkers.Values)
            {
                if (marker == null || marker.Issue == null)
                {
                    continue;
                }
                switch (marker.Issue.status)
                {
                    case "resolved":
                        resolved++;
                        break;
                    case "in_progress":
                        inProgress++;
                        break;
                    default:
                        open++;
                        break;
                }
            }
            hud.SetIssueSummary(
                restoredMarkers.Count,
                open,
                inProgress,
                resolved);
        }

        private void UpdateMarkerHighlight()
        {
            if (State != DraftState.Idle || sceneCamera == null)
            {
                return;
            }

            bool overUi = IsPointerOverUi();
            Ray ray = sceneCamera.ScreenPointToRay(Input.mousePosition);
            Physics.Raycast(
                ray,
                out RaycastHit hit,
                maximumRayDistance,
                ~0,
                QueryTriggerInteraction.Collide);
            IssueMarker hovered = overUi ? null : hit.collider?.GetComponentInParent<IssueMarker>();

            foreach (IssueMarker marker in restoredMarkers.Values)
            {
                if (marker != null)
                {
                    marker.SetHighlighted(marker == hovered && !marker.IsDraft);
                }
            }
        }

        private InspectionLocation ResolveLocationAtPoint(Vector3 point)
        {
            Collider[] colliders = Physics.OverlapSphere(
                point + Vector3.up * 0.015f,
                0.12f,
                inspectableMask,
                QueryTriggerInteraction.Ignore);
            foreach (Collider collider in colliders)
            {
                InspectionLocation location = collider.GetComponentInParent<InspectionLocation>();
                if (location != null)
                {
                    return location;
                }
            }

            return null;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
