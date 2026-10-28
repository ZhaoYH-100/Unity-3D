using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityInspection
{
    public sealed class IssueApiClient : MonoBehaviour
    {
        private const int RequestTimeoutSeconds = 10;

        [SerializeField] private string baseUrl = "http://127.0.0.1:8080";

        public string BaseUrl
        {
            get => baseUrl;
            set => baseUrl = NormalizeBaseUrl(value);
        }

        public void Configure(string apiBaseUrl)
        {
            BaseUrl = apiBaseUrl;
        }

        public IEnumerator CreateIssue(
            string idempotencyKey,
            CreateIssueRequest payload,
            Action<ApiOperationResult> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (!Guid.TryParse(idempotencyKey, out _))
            {
                completed(ApiOperationResult.Failure("提交键无效，请取消草稿后重试。", 0));
                yield break;
            }

            if (payload == null || payload.position == null)
            {
                completed(ApiOperationResult.Failure("问题数据不完整，无法提交。", 0));
                yield break;
            }

            bool completionSent = false;
            void Complete(ApiOperationResult result)
            {
                completionSent = true;
                completed(result);
            }

            string endpoint = BuildIssuesEndpoint(baseUrl);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                completed(ApiOperationResult.Failure(
                    "后端地址无效，草稿已保留。请检查 API Base URL 后重试。",
                    0));
                yield break;
            }

            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(uri.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = RequestTimeoutSeconds
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Idempotency-Key", idempotencyKey);

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    yield return null;
                }

                long statusCode = request.responseCode;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string responseBody = request.downloadHandler != null ? request.downloadHandler.text : null;
                    if (IsTimeoutError(request.error))
                    {
                        Complete(ApiOperationResult.Failure(
                            "请求超时（10 秒）。草稿已保留，请优先重试以避免重复创建。",
                            statusCode));
                        yield break;
                    }

                    string localizedError = ExtractLocalizedServerError(responseBody);
                    string message = string.IsNullOrWhiteSpace(localizedError)
                        ? "提交失败，请确认后端服务可访问后重试。"
                        : $"提交失败：{localizedError}";
                    Complete(ApiOperationResult.Failure(message, statusCode));
                    yield break;
                }

                if (!IsSuccessfulCreateStatus(statusCode))
                {
                    Complete(ApiOperationResult.Failure($"提交失败：HTTP {statusCode}。", statusCode));
                    yield break;
                }

                IssueRecord issue;
                try
                {
                    issue = JsonUtility.FromJson<IssueRecord>(request.downloadHandler.text);
                }
                catch (Exception)
                {
                    Complete(ApiOperationResult.Failure("后端响应无法解析，草稿已保留。", statusCode));
                    yield break;
                }

                if (issue == null || string.IsNullOrWhiteSpace(issue.id) || issue.position == null)
                {
                    Complete(ApiOperationResult.Failure("后端响应缺少必要字段，草稿已保留。", statusCode));
                    yield break;
                }

                Complete(ApiOperationResult.Success(issue, statusCode));
            }
            finally
            {
                request?.Dispose();
            }
        }

        public IEnumerator ListIssues(Action<IssueListOperationResult> completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            string endpoint = BuildIssuesEndpoint(baseUrl);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                completed(IssueListOperationResult.Failure(
                    "后端地址无效，无法恢复现场标记。",
                    0));
                yield break;
            }

            using UnityWebRequest request = UnityWebRequest.Get(uri.AbsoluteUri);
            request.timeout = RequestTimeoutSeconds;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                yield return null;
            }

            long statusCode = request.responseCode;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string localizedError = ExtractLocalizedServerError(
                    request.downloadHandler != null ? request.downloadHandler.text : null);
                string message = IsTimeoutError(request.error)
                    ? "同步超时（10 秒），标记仍可继续查看和上报。"
                    : string.IsNullOrWhiteSpace(localizedError)
                        ? "无法同步后端问题，请检查服务连接。"
                        : $"同步失败：{localizedError}";
                completed(IssueListOperationResult.Failure(message, statusCode));
                yield break;
            }

            if (!IsSuccessfulListStatus(statusCode))
            {
                completed(IssueListOperationResult.Failure(
                    $"同步失败：HTTP {statusCode}。",
                    statusCode));
                yield break;
            }

            IssueListResponse response;
            try
            {
                response = JsonUtility.FromJson<IssueListResponse>(request.downloadHandler.text);
            }
            catch (Exception)
            {
                completed(IssueListOperationResult.Failure(
                    "后端问题列表无法解析。",
                    statusCode));
                yield break;
            }

            completed(IssueListOperationResult.Success(response?.items, statusCode));
        }

        public static bool IsSuccessfulCreateStatus(long statusCode)
        {
            return statusCode == 200 || statusCode == 201;
        }

        public static bool IsSuccessfulListStatus(long statusCode)
        {
            return statusCode == 200;
        }

        public static bool IsTimeoutError(string error)
        {
            return !string.IsNullOrWhiteSpace(error) &&
                   (error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string ExtractServerError(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            try
            {
                ApiErrorEnvelope envelope = JsonUtility.FromJson<ApiErrorEnvelope>(responseBody);
                return envelope?.error == null ? string.Empty : envelope.error.message;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string ExtractLocalizedServerError(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            try
            {
                ApiErrorEnvelope envelope = JsonUtility.FromJson<ApiErrorEnvelope>(responseBody);
                string detail = envelope?.error?.message ?? string.Empty;
                switch (envelope?.error?.code)
                {
                    case "INVALID_ARGUMENT":
                        if (detail.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "问题标题不符合要求，请检查后重试。";
                        }
                        if (detail.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "问题位置数据不符合要求，请重新选择场景位置。";
                        }
                        return "提交内容不符合要求，请检查后重试。";
                    case "IDEMPOTENCY_CONFLICT":
                        return "提交内容与上次请求不一致，请取消草稿后重新创建。";
                    case "NOT_FOUND":
                        return "未找到对应的问题记录。";
                    case "UNSUPPORTED_MEDIA_TYPE":
                        return "请求格式不受支持。";
                    case "PAYLOAD_TOO_LARGE":
                        return "提交内容过大，请精简后重试。";
                    case "INTERNAL":
                        return "服务器内部错误，请稍后重试。";
                    default:
                        return string.IsNullOrWhiteSpace(envelope?.error?.message)
                            ? string.Empty
                            : "服务器未接受本次提交，请检查后重试。";
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string BuildIssuesEndpoint(string apiBaseUrl)
        {
            return NormalizeBaseUrl(apiBaseUrl) + "/api/issues";
        }

        private static string NormalizeBaseUrl(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "http://127.0.0.1:8080"
                : value.Trim().TrimEnd('/');
        }
    }
}
