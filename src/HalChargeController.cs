using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

/// <summary>
/// 通过荣耀电脑管家自带的 HardwareHal.dll 读写充电阈值。
///
/// 反汇编得到的调用关系：
///   HalInit(8, 0)
///   HalQueryInformation(8, 0, 12, out[2], 2, &amp;status, 0)   // BIOS WMI 0x1103
///   HalSetInformation(8, 0, 12, in[2],  2, &amp;status, 0)    // BIOS WMI 0x1003
///
/// 两个字节分别对应 EC 的 0x80/0x81（恢复下限/停止上限）。不同 BIOS 版本可能
/// 返回 [下限, 上限] 或 [上限, 下限]，因此这里根据当前值自动判断顺序，并在
/// 写入后回读校验。
/// </summary>
public sealed class HalChargeController : IDisposable
{
    private const int BatteryCategory = 8;
    private const int GetSubCommand = 12;
    private const int SetSubCommand = 12;

    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte HalInitDelegate(int category, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte HalQueryDelegate(int category, int argument, int subCommand,
        IntPtr output, int outputSize, IntPtr status, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte HalSetDelegate(int category, int argument, int subCommand,
        IntPtr input, int inputSize, IntPtr status, int flags);

    private IntPtr module;
    private HalQueryDelegate query;
    private HalSetDelegate set;

    private HalChargeController(IntPtr module, HalQueryDelegate query, HalSetDelegate set)
    {
        this.module = module;
        this.query = query;
        this.set = set;
    }

    public string DriverName
    {
        get { return "HONOR HardwareHal (BIOS WMI)"; }
    }

    public static HalChargeController Open(out string error)
    {
        error = null;

        string directory;
        if (!FindHalDirectory(out directory))
        {
            error = "未找到荣耀电脑管家的 HardwareHal.dll。请先安装荣耀电脑管家，" +
                    "或确认安装路径为 C:\\Program Files\\HONOR\\PCManager。";
            return null;
        }

        string dllPath = Path.Combine(directory, "HardwareHal.dll");
        IntPtr module = LoadLibraryEx(dllPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (module == IntPtr.Zero)
        {
            error = "加载 HardwareHal.dll 失败，错误 " + Marshal.GetLastWin32Error() +
                    "。请确认以管理员身份运行。";
            return null;
        }

        IntPtr initAddress = GetProcAddress(module, "HalInit");
        IntPtr queryAddress = GetProcAddress(module, "HalQueryInformation");
        IntPtr setAddress = GetProcAddress(module, "HalSetInformation");

        if (initAddress == IntPtr.Zero || queryAddress == IntPtr.Zero || setAddress == IntPtr.Zero)
        {
            FreeLibrary(module);
            error = "HardwareHal.dll 缺少必要的导出函数（HalInit/HalQueryInformation/HalSetInformation）。";
            return null;
        }

        HalInitDelegate init = (HalInitDelegate)Marshal.GetDelegateForFunctionPointer(
            initAddress, typeof(HalInitDelegate));
        HalQueryDelegate query = (HalQueryDelegate)Marshal.GetDelegateForFunctionPointer(
            queryAddress, typeof(HalQueryDelegate));
        HalSetDelegate set = (HalSetDelegate)Marshal.GetDelegateForFunctionPointer(
            setAddress, typeof(HalSetDelegate));

        if (init(BatteryCategory, IntPtr.Zero) == 0)
        {
            FreeLibrary(module);
            error = "荣耀 HAL 初始化失败（HalInit 返回 0）。请确认以管理员身份运行。";
            return null;
        }

        HalChargeController controller = new HalChargeController(module, query, set);

        int upper;
        int lower;
        if (!controller.TryRead(out upper, out lower))
        {
            controller.Dispose();
            error = "无法通过荣耀 HAL 读取充电阈值。请确认以管理员身份运行，" +
                    "并且 BIOS/电脑管家支持该接口。";
            return null;
        }

        return controller;
    }

    public bool TryRead(out int upper, out int lower)
    {
        upper = 0;
        lower = 0;

        byte first;
        byte second;
        if (!TryReadRaw(out first, out second))
            return false;

        if (first > 100 || second > 100)
            return false;

        upper = Math.Max(first, second);
        lower = Math.Min(first, second);
        return true;
    }

    public void SetThreshold(int upper, int lower)
    {
        if (upper < 50 || upper > 100)
            throw new ArgumentOutOfRangeException("upper", "充电上限必须在 50% 到 100% 之间。");

        if (lower < 0 || lower > 95)
            throw new ArgumentOutOfRangeException("lower", "恢复下限必须在 0% 到 95% 之间。");

        if (lower >= upper)
            throw new ArgumentException("恢复下限必须小于停止上限。");

        if (upper < 100 && lower < 40)
            throw new ArgumentException("停止上限低于 100% 时，恢复下限不能低于 40%。");

        byte currentFirst;
        byte currentSecond;
        bool haveCurrent = TryReadRaw(out currentFirst, out currentSecond);

        // 当前返回顺序是 [下限, 上限] 时，第一个字节更小。
        bool lowerFirst = haveCurrent && currentFirst < currentSecond;

        if (TrySetOrder(upper, lower, lowerFirst))
            return;

        if (TrySetOrder(upper, lower, !lowerFirst))
            return;

        throw new Exception("写入充电阈值失败：HAL 拒绝了两种字节顺序。" +
            "请确认已以管理员身份运行，且 BIOS 支持充电阈值设置。");
    }

    private bool TrySetOrder(int upper, int lower, bool lowerFirst)
    {
        byte first = lowerFirst ? (byte)lower : (byte)upper;
        byte second = lowerFirst ? (byte)upper : (byte)lower;

        if (!TryWriteRaw(first, second))
            return false;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Thread.Sleep(100);

            int actualUpper;
            int actualLower;
            if (TryRead(out actualUpper, out actualLower) &&
                actualUpper == upper && actualLower == lower)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryReadRaw(out byte first, out byte second)
    {
        first = 0;
        second = 0;

        IntPtr output = Marshal.AllocHGlobal(8);
        IntPtr status = Marshal.AllocHGlobal(4);
        try
        {
            Zero(output, 8);
            Zero(status, 4);

            byte ok = query(BatteryCategory, 0, GetSubCommand, output, 2, status, 0);
            if (ok == 0)
                return false;

            first = Marshal.ReadByte(output, 0);
            second = Marshal.ReadByte(output, 1);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(output);
            Marshal.FreeHGlobal(status);
        }
    }

    private bool TryWriteRaw(byte first, byte second)
    {
        IntPtr input = Marshal.AllocHGlobal(8);
        IntPtr status = Marshal.AllocHGlobal(4);
        try
        {
            Zero(input, 8);
            Zero(status, 4);
            Marshal.WriteByte(input, 0, first);
            Marshal.WriteByte(input, 1, second);

            return set(BatteryCategory, 0, SetSubCommand, input, 2, status, 0) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            Marshal.FreeHGlobal(status);
        }
    }

    private static bool FindHalDirectory(out string directory)
    {
        string[] candidates = new string[]
        {
            ReadInstallLocation(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "HONOR", "PCManager"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Honor", "PCManager"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "HONOR", "PCManager"),
            AppDomain.CurrentDomain.BaseDirectory
        };

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            try
            {
                if (File.Exists(Path.Combine(candidate, "HardwareHal.dll")))
                {
                    directory = candidate;
                    return true;
                }
            }
            catch
            {
            }
        }

        directory = null;
        return false;
    }

    private static string ReadInstallLocation()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Honor\PCManager"))
            {
                if (key != null)
                {
                    object value = key.GetValue("InstallLocation");
                    if (value != null)
                        return value.ToString();
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static void Zero(IntPtr pointer, int size)
    {
        byte[] zero = new byte[size];
        Marshal.Copy(zero, 0, pointer, size);
    }

    public void Dispose()
    {
        if (module != IntPtr.Zero)
        {
            FreeLibrary(module);
            module = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
}

