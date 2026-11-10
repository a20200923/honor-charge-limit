using System;
using System.ServiceProcess;
using Microsoft.Win32;

/// <summary>
/// 荣耀电脑管家的电池安全管理配置。
/// 注册表值必须是 REG_SZ 字符串，不能写成 DWORD。
///   HKLM\SOFTWARE\PCManager\MBAPowerManager
///     PowerSafeManagerStatus = "1" / "0"
///     PowerSafeManagerMode   = "0" 家用 / "1" 办公 / "2" 差旅
/// MBAMainService 读取该配置后设置 EC 的 0x85/0x87/0x89 模式标志。
/// </summary>
public static class PowerManagerConfig
{
    private const string KeyPath = @"SOFTWARE\PCManager\MBAPowerManager";
    private const string ValueStatus = "PowerSafeManagerStatus";
    private const string ValueMode = "PowerSafeManagerMode";
    private const string ServiceName = "MBAMainService";

    public static bool IsEnabled()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath))
            {
                if (key == null) return false;
                object value = key.GetValue(ValueStatus);
                return value != null && value.ToString().Trim() == "1";
            }
        }
        catch
        {
            return false;
        }
    }

    public static int GetMode()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath))
            {
                if (key == null) return 0;
                object value = key.GetValue(ValueMode);
                int mode;
                if (value != null && int.TryParse(value.ToString().Trim(), out mode) &&
                    mode >= 0 && mode <= 2)
                {
                    return mode;
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    public static void Save(bool enabled, int mode)
    {
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath))
        {
            if (key == null)
                throw new Exception("无法打开注册表项：" + KeyPath);

            key.SetValue(ValueStatus, enabled ? "1" : "0", RegistryValueKind.String);
            key.SetValue(ValueMode, mode.ToString(), RegistryValueKind.String);
        }
    }

    public static bool RestartService(out string error)
    {
        error = null;
        try
        {
            using (ServiceController service = new ServiceController(ServiceName))
            {
                service.Refresh();
                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(15));
                }

                service.Refresh();
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running,
                    TimeSpan.FromSeconds(15));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string GetModeName(int mode)
    {
        switch (mode)
        {
            case 1: return "办公模式 90% / 70%";
            case 2: return "差旅模式 100% / 95%";
            default: return "家用模式 70% / 40%";
        }
    }
}
