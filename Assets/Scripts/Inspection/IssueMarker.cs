using UnityEngine;
using UnityEngine.UI;

namespace UnityInspection
{
    public sealed class IssueMarker : MonoBehaviour
    {
        private static readonly string[] PreferredFonts =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "SimHei",
            "DengXian",
            "Arial Unicode MS"
        };

        private static Font cachedFont;

        [SerializeField] private Renderer markerRenderer;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject markerLabelRoot;
        [SerializeField] private CanvasGroup markerLabelGroup;
        [SerializeField] private Text markerTitle;
        [SerializeField] private Text markerMeta;

        private float baseScale = 1f;
        private bool highlighted;
        private bool labelVisible;
        private float labelAlpha;
        private Material runtimeMaterial;

        public ApiPosition ReportedPosition { get; private set; }
        public IssueRecord Issue { get; private set; }
        public bool IsDraft => Issue == null;
        public bool IsLabelVisible => labelVisible;
        public string MarkerTitle => markerTitle != null ? markerTitle.text : string.Empty;
        public string MarkerMeta => markerMeta != null ? markerMeta.text : string.Empty;
        public int Sequence { get; private set; }

        public void Configure(Vector3 reportedPosition)
        {
            ReportedPosition = new ApiPosition(reportedPosition);
            Issue = null;
            Sequence = 0;
            ApplyDraftStyle();
        }

        public void ConfigureIssue(IssueRecord issue, int sequence)
        {
            if (issue == null || issue.position == null)
            {
                return;
            }

            Issue = issue;
            Sequence = sequence;
            ReportedPosition = issue.position;
            transform.position = issue.position.ToVector3();
            ApplyIssueStyle();
        }

        public void SetHighlighted(bool value)
        {
            if (highlighted == value)
            {
                return;
            }

            highlighted = value;
            ApplyScale(1f);
            SetLabelVisible(value && Issue != null);
        }

        private void Awake()
        {
            EnsureReferences();
            SetLabelVisible(false, true);
        }

        private void Update()
        {
            if (Issue != null && Issue.status == "in_progress")
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 3.4f) * 0.07f;
                ApplyScale(pulse);
            }

            AnimateLabel();
        }

        private void LateUpdate()
        {
            if (markerLabelRoot == null ||
                !markerLabelRoot.activeSelf ||
                Camera.main == null)
            {
                return;
            }

            Vector3 direction =
                markerLabelRoot.transform.position - Camera.main.transform.position;
            if (direction.sqrMagnitude > 0.001f)
            {
                markerLabelRoot.transform.rotation =
                    Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }

        private void EnsureReferences()
        {
            if (markerRenderer == null)
            {
                markerRenderer = GetComponentInChildren<Renderer>();
            }
            if (visualRoot == null)
            {
                visualRoot = markerRenderer != null ? markerRenderer.transform : transform;
            }
            if (markerLabelGroup == null)
            {
                markerLabelGroup = GetComponentInChildren<CanvasGroup>(true);
            }
            if (markerLabelRoot == null && markerLabelGroup != null)
            {
                markerLabelRoot = markerLabelGroup.gameObject;
            }
            if (markerTitle == null || markerMeta == null)
            {
                Text[] labels = GetComponentsInChildren<Text>(true);
                if (labels.Length > 0)
                {
                    markerTitle = labels[0];
                }
                if (labels.Length > 1)
                {
                    markerMeta = labels[1];
                }
            }
            ApplyChineseFont(markerTitle);
            ApplyChineseFont(markerMeta);
        }

        private static void ApplyChineseFont(Text label)
        {
            if (label == null)
            {
                return;
            }

            if (cachedFont == null)
            {
                string[] installedFonts = Font.GetOSInstalledFontNames();
                foreach (string preferredFont in PreferredFonts)
                {
                    foreach (string installedFont in installedFonts)
                    {
                        if (!string.Equals(
                                preferredFont,
                                installedFont,
                                System.StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        cachedFont = Font.CreateDynamicFontFromOSFont(installedFont, 24);
                        if (cachedFont != null)
                        {
                            break;
                        }
                    }
                    if (cachedFont != null)
                    {
                        break;
                    }
                }
            }

            if (cachedFont != null)
            {
                label.font = cachedFont;
            }
        }

        private void ApplyDraftStyle()
        {
            EnsureReferences();
            ApplyColor(new Color(1f, 0.38f, 0.12f), 1.45f);
            baseScale = 1.08f;
            ApplyScale(1f);
            if (markerTitle != null)
            {
                markerTitle.text = "问题草稿";
            }
            if (markerMeta != null)
            {
                markerMeta.text = "填写完成后提交";
            }
            SetLabelVisible(false, true);
        }

        private void ApplyIssueStyle()
        {
            EnsureReferences();
            Color color = PriorityColor(Issue.priority);
            if (Issue.status == "resolved")
            {
                color = Color.Lerp(color, new Color(0.55f, 0.66f, 0.68f), 0.72f);
            }

            float emission = Issue.status == "in_progress" ? 2.2f : 1.35f;
            if (Issue.status == "resolved")
            {
                emission = 0.25f;
            }
            ApplyColor(color, emission);

            float priorityScale = Issue.priority == "high" ? 1.2f : Issue.priority == "medium" ? 1f : 0.84f;
            baseScale = Issue.status == "resolved" ? priorityScale * 0.76f : priorityScale;
            ApplyScale(1f);

            if (markerTitle != null)
            {
                markerTitle.text = Issue.title;
            }
            if (markerMeta != null)
            {
                string deviceCode = string.IsNullOrWhiteSpace(Issue.deviceCode)
                    ? "未关联设备"
                    : Issue.deviceCode;
                string zone = string.IsNullOrWhiteSpace(Issue.zone)
                    ? "未划分区域"
                    : Issue.zone;
                markerMeta.text =
                    $"{deviceCode} · {zone}    {PriorityLabel(Issue.priority)} / {StatusLabel(Issue.status)}";
            }
            SetLabelVisible(false, true);
        }

        private void SetLabelVisible(bool visible, bool immediate = false)
        {
            labelVisible = visible;
            if (markerLabelRoot == null || markerLabelGroup == null)
            {
                return;
            }

            if (!markerLabelRoot.activeSelf)
            {
                markerLabelRoot.SetActive(true);
            }

            if (immediate)
            {
                labelAlpha = visible ? 1f : 0f;
                markerLabelGroup.alpha = labelAlpha;
                markerLabelRoot.SetActive(visible);
            }
        }

        private void AnimateLabel()
        {
            if (markerLabelRoot == null || markerLabelGroup == null)
            {
                return;
            }

            float target = labelVisible ? 1f : 0f;
            if (Mathf.Approximately(labelAlpha, target))
            {
                return;
            }

            if (!markerLabelRoot.activeSelf)
            {
                markerLabelRoot.SetActive(true);
            }
            labelAlpha = Mathf.MoveTowards(labelAlpha, target, Time.unscaledDeltaTime * 8f);
            markerLabelGroup.alpha = labelAlpha;
            if (!labelVisible && labelAlpha <= 0.001f)
            {
                markerLabelGroup.alpha = 0f;
                markerLabelRoot.SetActive(false);
            }
        }

        private void ApplyColor(Color color, float emission)
        {
            if (markerRenderer == null)
            {
                return;
            }

            if (runtimeMaterial == null)
            {
                runtimeMaterial = markerRenderer.material;
            }
            runtimeMaterial.color = color;
            runtimeMaterial.EnableKeyword("_EMISSION");
            runtimeMaterial.SetColor("_EmissionColor", color * emission);
        }

        private void ApplyScale(float pulse)
        {
            float highlightScale = highlighted ? 1.18f : 1f;
            transform.localScale = Vector3.one * baseScale * pulse * highlightScale;
        }

        private static string PriorityLabel(string priority)
        {
            switch (priority)
            {
                case "high":
                    return "高优先级";
                case "low":
                    return "低优先级";
                default:
                    return "中优先级";
            }
        }

        private static string StatusLabel(string status)
        {
            switch (status)
            {
                case "resolved":
                    return "已完成";
                case "in_progress":
                    return "处理中";
                default:
                    return "待处理";
            }
        }

        private static Color PriorityColor(string priority)
        {
            switch (priority)
            {
                case "low":
                    return new Color(0.2f, 0.86f, 0.58f);
                case "high":
                    return new Color(1f, 0.28f, 0.18f);
                default:
                    return new Color(1f, 0.68f, 0.16f);
            }
        }
    }
}
