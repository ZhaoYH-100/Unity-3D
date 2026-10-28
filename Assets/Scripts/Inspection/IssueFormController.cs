using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace UnityInspection
{
    public sealed class IssueFormController : MonoBehaviour
    {
        private static readonly string[] PriorityValues = { "low", "medium", "high" };

        [SerializeField] private GameObject panel;
        [SerializeField] private InputField titleField;
        [SerializeField] private InputField descriptionField;
        [SerializeField] private Dropdown priorityField;
        [SerializeField] private Text positionText;
        [SerializeField] private Text errorText;
        [SerializeField] private Text submitLabel;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button cancelButton;

        private bool eventsBound;

        public event Action SubmitRequested;
        public event Action CancelRequested;

        public bool IsVisible => panel != null && panel.activeSelf;
        public string CurrentIdempotencyKey { get; private set; }

        public void Configure(
            GameObject panel,
            InputField titleField,
            InputField descriptionField,
            Dropdown priorityField,
            Text positionText,
            Text errorText,
            Text submitLabel,
            Button submitButton,
            Button cancelButton)
        {
            this.panel = panel;
            this.titleField = titleField;
            this.descriptionField = descriptionField;
            this.priorityField = priorityField;
            this.positionText = positionText;
            this.errorText = errorText;
            this.submitLabel = submitLabel;
            this.submitButton = submitButton;
            this.cancelButton = cancelButton;
            BindEvents();
        }

        public void ShowDraft(
            Vector3 reportedPosition,
            string locationLabel,
            string idempotencyKey)
        {
            CurrentIdempotencyKey = idempotencyKey;
            panel.SetActive(true);
            titleField.text = string.Empty;
            descriptionField.text = string.Empty;
            priorityField.value = 1;
            positionText.text = string.Format(
                CultureInfo.InvariantCulture,
                "设备与区域  {0}\n位置  X {1:0.000}  Y {2:0.000}  Z {3:0.000}",
                string.IsNullOrWhiteSpace(locationLabel) ? "未识别" : locationLabel,
                reportedPosition.x,
                reportedPosition.y,
                reportedPosition.z);
            errorText.text = string.Empty;
            SetFieldsEditable(true);
            submitButton.interactable = true;
            cancelButton.interactable = true;
            submitLabel.text = "提交问题";
            titleField.ActivateInputField();
        }

        public void Hide()
        {
            if (panel != null)
            {
                panel.SetActive(false);
            }

            CurrentIdempotencyKey = null;
        }

        public bool TryReadInput(out string title, out string description, out string priority)
        {
            title = titleField.text.Trim();
            description = descriptionField.text;
            priority = PriorityValues[Mathf.Clamp(priorityField.value, 0, PriorityValues.Length - 1)];

            if (string.IsNullOrEmpty(title))
            {
                ShowValidationError("请填写问题标题。");
                titleField.ActivateInputField();
                return false;
            }

            if (title.Length > 200)
            {
                ShowValidationError("问题标题不能超过 200 个字符。");
                titleField.ActivateInputField();
                return false;
            }

            if (description.Length > 4000)
            {
                ShowValidationError("问题描述不能超过 4000 个字符。");
                descriptionField.ActivateInputField();
                return false;
            }

            errorText.text = string.Empty;
            return true;
        }

        public void SetSubmitting()
        {
            SetFieldsEditable(false);
            submitButton.interactable = false;
            cancelButton.interactable = false;
            submitLabel.text = "正在提交…";
            errorText.text = "正在发送到巡检服务器…";
        }

        public void ShowError(string message)
        {
            SetFieldsEditable(false);
            submitButton.interactable = true;
            cancelButton.interactable = true;
            submitLabel.text = "重新提交";
            errorText.text = message;
        }

        private void ShowValidationError(string message)
        {
            errorText.text = message;
        }

        private void SetFieldsEditable(bool editable)
        {
            titleField.interactable = editable;
            descriptionField.interactable = editable;
            priorityField.interactable = editable;
        }

        private void BindEvents()
        {
            if (eventsBound || submitButton == null || cancelButton == null)
            {
                return;
            }

            submitButton.onClick.AddListener(HandleSubmit);
            cancelButton.onClick.AddListener(HandleCancel);
            eventsBound = true;
        }

        private void Awake()
        {
            BindEvents();
        }

        private void OnDestroy()
        {
            if (!eventsBound)
            {
                return;
            }

            submitButton.onClick.RemoveListener(HandleSubmit);
            cancelButton.onClick.RemoveListener(HandleCancel);
        }

        private void HandleSubmit()
        {
            SubmitRequested?.Invoke();
        }

        private void HandleCancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
