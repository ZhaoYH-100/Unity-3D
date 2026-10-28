using UnityEngine;

namespace UnityInspection
{
    public sealed class InspectionLocation : MonoBehaviour
    {
        [SerializeField] private string deviceCode = "SITE-01";
        [SerializeField] private string zone = "公共区域";

        public string DeviceCode => string.IsNullOrWhiteSpace(deviceCode) ? "SITE-01" : deviceCode;
        public string Zone => string.IsNullOrWhiteSpace(zone) ? "公共区域" : zone;
        public string DisplayName => $"{DeviceCode} · {Zone}";

        public void Configure(string newDeviceCode, string newZone)
        {
            deviceCode = string.IsNullOrWhiteSpace(newDeviceCode) ? "SITE-01" : newDeviceCode.Trim();
            zone = string.IsNullOrWhiteSpace(newZone) ? "公共区域" : newZone.Trim();
        }
    }
}
