using System;
using UnityEngine;
using UnityEngine.UI;

namespace UnityInspection
{
    public sealed class InspectionHud : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private Image crosshair;
        [SerializeField] private Text issueSummaryText;
        [SerializeField] private Text syncStatusText;

        public void Configure(Text statusText, Image crosshair)
        {
            this.statusText = statusText;
            this.crosshair = crosshair;
        }

        public void Configure(
            Text statusText,
            Image crosshair,
            Text issueSummaryText,
            Text syncStatusText)
        {
            Configure(statusText, crosshair);
            this.issueSummaryText = issueSummaryText;
            this.syncStatusText = syncStatusText;
        }

        public void SetStatus(string message, bool isError = false)
        {
            if (statusText == null)
            {
                return;
            }
            statusText.text = message;
            statusText.color = isError
                ? new Color(1f, 0.45f, 0.4f)
                : new Color(0.8f, 1f, 0.82f);
        }

        public void ClearStatus()
        {
            if (statusText != null)
            {
                statusText.text = string.Empty;
            }
        }

        public void SetCrosshairVisible(bool visible)
        {
            if (crosshair != null)
            {
                crosshair.enabled = visible;
            }
        }

        public void SetIssueSummary(int total, int open, int inProgress, int resolved)
        {
            if (issueSummaryText == null)
            {
                return;
            }

            issueSummaryText.text =
                $"全部 {total:00}   待处理 {open:00}   处理中 {inProgress:00}   已完成 {resolved:00}";
        }

        public void SetSyncStatus(string message, bool isError = false)
        {
            if (syncStatusText == null)
            {
                return;
            }

            syncStatusText.text = message;
            syncStatusText.color = isError
                ? new Color(1f, 0.5f, 0.42f)
                : new Color(0.55f, 0.76f, 0.8f);
        }
    }

    [ExecuteAlways]
    public sealed class InspectionChineseFont : MonoBehaviour
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

        private void Awake()
        {
            ApplyFont();
        }

        private void OnEnable()
        {
            ApplyFont();
        }

        private void OnValidate()
        {
            ApplyFont();
        }

        private void ApplyFont()
        {
            Font font = GetChineseFont();
            if (font == null)
            {
                return;
            }

            foreach (Text text in GetComponentsInChildren<Text>(true))
            {
                text.font = font;
            }
        }

        private static Font GetChineseFont()
        {
            if (cachedFont != null)
            {
                return cachedFont;
            }

            string[] installedFonts = Font.GetOSInstalledFontNames();
            foreach (string preferredFont in PreferredFonts)
            {
                foreach (string installedFont in installedFonts)
                {
                    if (!string.Equals(preferredFont, installedFont, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    cachedFont = Font.CreateDynamicFontFromOSFont(installedFont, 24);
                    if (cachedFont != null)
                    {
                        return cachedFont;
                    }
                }
            }

            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return cachedFont;
        }
    }

    public sealed class WorldLabelBillboard : MonoBehaviour
    {
        private Camera targetCamera;

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                return;
            }

            Vector3 direction = transform.position - targetCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }
}
