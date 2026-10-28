using System;
using UnityEngine;

namespace UnityInspection
{
    public enum DraftState
    {
        Idle,
        Drafting,
        Submitting,
        Error
    }

    [Serializable]
    public sealed class ApiPosition
    {
        public float x;
        public float y;
        public float z;

        public ApiPosition()
        {
        }

        public ApiPosition(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public sealed class CreateIssueRequest
    {
        public string title;
        public string description;
        public string priority;
        public string deviceCode;
        public string zone;
        public ApiPosition position;
    }

    [Serializable]
    public sealed class IssueRecord
    {
        public string id;
        public string title;
        public string description;
        public string priority;
        public string status;
        public string deviceCode;
        public string zone;
        public ApiPosition position;
        public string createdAt;
        public string updatedAt;
    }

    [Serializable]
    public sealed class IssueListResponse
    {
        public IssueRecord[] items = Array.Empty<IssueRecord>();
    }

    public sealed class IssueListOperationResult
    {
        private IssueListOperationResult(
            bool succeeded,
            IssueRecord[] issues,
            string message,
            long statusCode)
        {
            Succeeded = succeeded;
            Issues = issues ?? Array.Empty<IssueRecord>();
            Message = message;
            StatusCode = statusCode;
        }

        public bool Succeeded { get; }
        public IssueRecord[] Issues { get; }
        public string Message { get; }
        public long StatusCode { get; }

        public static IssueListOperationResult Success(IssueRecord[] issues, long statusCode)
        {
            return new IssueListOperationResult(true, issues, string.Empty, statusCode);
        }

        public static IssueListOperationResult Failure(string message, long statusCode)
        {
            return new IssueListOperationResult(false, null, message, statusCode);
        }
    }

    [Serializable]
    public sealed class ApiErrorEnvelope
    {
        public ApiError error;
    }

    [Serializable]
    public sealed class ApiError
    {
        public string code;
        public string message;
    }

    public sealed class ApiOperationResult
    {
        private ApiOperationResult(bool succeeded, IssueRecord issue, string message, long statusCode)
        {
            Succeeded = succeeded;
            Issue = issue;
            Message = message;
            StatusCode = statusCode;
        }

        public bool Succeeded { get; }
        public IssueRecord Issue { get; }
        public string Message { get; }
        public long StatusCode { get; }

        public static ApiOperationResult Success(IssueRecord issue, long statusCode)
        {
            return new ApiOperationResult(true, issue, string.Empty, statusCode);
        }

        public static ApiOperationResult Failure(string message, long statusCode)
        {
            return new ApiOperationResult(false, null, message, statusCode);
        }
    }
}
