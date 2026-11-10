using System;
using System.Runtime.InteropServices;

/// <summary>
/// 读取 Windows 提供的电池电量与交流电源状态。
/// </summary>
public static class BatteryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static int Percent
    {
        get
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status) || status.BatteryLifePercent == 255)
                return -1;
            return status.BatteryLifePercent;
        }
    }

    public static bool OnAcPower
    {
        get
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status))
                return false;
            return status.AcLineStatus == 1;
        }
    }
}
