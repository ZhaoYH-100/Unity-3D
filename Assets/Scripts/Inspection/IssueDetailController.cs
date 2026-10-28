using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace UnityInspection
{
    public sealed class IssueDetailController : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text titleText;
        [SerializeField] private Text metaText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text locationText;
        [SerializeField] private Image priorityBar;
        [SerializeField] private Button closeButton;

        private bool eventsBound;

        public event Action CloseRequested;

        public bool IsVisible => panel != null && panel.activeSelf;
        public IssueRecord CurrentIssue { get; private set; }

        public void Configure(
            GameObject panel,
            Text titleText,
            Text metaText,
            Text descriptionText,
            Text locationText,
            Image priorityBar,
            Button closeButton)
        {
            this.panel = panel;
            this.titleText = titleText;
            this.metaText = metaText;
            this.descriptionText = descriptionText;
            this.locationText = locationText;
            this.priorityBar = priorityBar;
            this.closeButton = closeButton;
            BindEvents();
        }

        public void Show(IssueRecord issue)
        {
            if (issue == null || panel == null)
            {
                return;
            }

            CurrentIssue = issue;
            titleText.text = issue.title;
            descriptionText.text = string.IsNullOrWhiteSpace(issue.description)
                ? "未填写补充描述。"
                : issue.description;
            locationText.text = string.Format(
                CultureInfo.InvariantCulture,
                "设备  {0}\n区域  {1}\n坐标  X {2:0.00}   Y {3:0.00}   Z {4:0.00}",
                string.IsNullOrWhiteSpace(issue.deviceCode) ? "未关联" : issue.deviceCode,
                string.IsNullOrWhiteSpace(issue.zone) ? "未划分" : issue.zone,
                issue.position != null ? issue.position.x : 0f,
                issue.position != null ? issue.position.y : 0f,
                issue.position != null ? issue.position.z : 0f);
            metaText.text = $"{StatusLabel(issue.status)}  /  {PriorityLabel(issue.priority)}优先级  /  ID {ShortID(issue.id)}";
            priorityBar.color = PriorityColor(issue.priority);
            panel.SetActive(true);
        }

        public void Hide()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }
            CurrentIssue = null;
        }

        private void BindEvents()
        {
            if (eventsBound || closeButton == null)
            {
                return;
            }
            closeButton.onClick.AddListener(HandleClose);
            eventsBound = true;
        }

        private void Awake()
        {
            BindEvents();
        }

        private void OnDestroy()
        {
            if (eventsBound && closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleClose);
            }
        }

        private void HandleClose()
        {
            CloseRequested?.Invoke();
        }

        private static string ShortID(string id)
        {
            return string.IsNullOrWhiteSpace(id) || id.Length < 8
                ? id
                : id.Substring(0, 8).ToUpperInvariant();
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

        private static string PriorityLabel(string priority)
        {
            switch (priority)
            {
                case "high":
                    return "高";
                case "low":
                    return "低";
                default:
                    return "中";
            }
        }

        private static Color PriorityColor(string priority)
        {
            switch (priority)
            {
                case "high":
                    return new Color(1f, 0.3f, 0.22f);
                case "low":
                    return new Color(0.25f, 0.82f, 0.58f);
                default:
                    return new Color(1f, 0.67f, 0.2f);
            }
        }
    }
}
