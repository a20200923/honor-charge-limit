using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool screenshot = args.Length > 0 &&
            args[0].Equals("--screenshot", StringComparison.OrdinalIgnoreCase);

        if (args.Length > 0)
        {
            if (!screenshot && !IsAdministrator())
            {
                Log("此操作需要管理员权限。请右键“以管理员身份运行”，" +
                    "或在管理员命令提示符中执行。");
                return 1;
            }

            return RunCommandLine(args);
        }

        if (!IsAdministrator())
        {
            RelaunchElevated();
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCommandLine(string[] args)
    {
        AttachConsole(-1);

        try
        {
            string command = args[0].ToLowerInvariant();

            if (command == "--read")
            {
                int mode = PowerManagerConfig.GetMode();
                bool enabled = PowerManagerConfig.IsEnabled();
                string message = string.Format(
                    "电池安全管理：{0}\r\n模式：{1}\r\n电池：{2}%\r\n电源：{3}",
                    enabled ? "开启" : "关闭",
                    PowerManagerConfig.GetModeName(mode),
                    BatteryInfo.Percent,
                    BatteryInfo.OnAcPower ? "已接电源" : "使用电池");

                string error;
                using (HalChargeController hal = HalChargeController.Open(out error))
                {
                    int upper;
                    int lower;
                    if (hal != null && hal.TryRead(out upper, out lower))
                    {
                        message += string.Format("\r\nBIOS 阈值：停止 {0}% / 恢复 {1}%", upper, lower);
                    }
                    else
                    {
                        message += "\r\nBIOS 阈值：读取失败（" + (error ?? "HAL 不可用") + "）";
                    }
                }

                Log(message);
                return 0;
            }

            if (command == "--mode")
            {
                if (args.Length < 2)
                    throw new Exception("用法：--mode home|office|travel（或 0|1|2）");

                Log(ApplyMode(true, ParseMode(args[1])));
                return 0;
            }

            if (command == "--restore" || command == "--off")
            {
                Log(ApplyMode(false, 0));
                return 0;
            }

            if (command == "--apply")
            {
                throw new Exception(
                    "自定义阈值需要动态监控，请运行 GUI 设置 70-90% 的上限。\r\n" +
                    "命令行只支持 --mode home|office|travel 三种官方模式。");
            }

            if (command == "--screenshot")
            {
                string path = args.Length >= 2
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot.png");

                using (MainForm form = new MainForm(true))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-4000, -4000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    Thread.Sleep(500);

                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap,
                            new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(path, ImageFormat.Png);
                    }

                    form.Close();
                }

                Log("截图已保存：" + path);
                return 0;
            }

            Log("未知参数：" + args[0]);
            return 2;
        }
        catch (Exception ex)
        {
            Log("操作失败：" + ex.Message);
            return 1;
        }
    }

    private static int ParseMode(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "home":
            case "0":
                return 0;
            case "office":
            case "1":
                return 1;
            case "travel":
            case "2":
                return 2;
            default:
                throw new Exception("未知模式：" + text + "（可用 home/office/travel 或 0/1/2）");
        }
    }

    private static string ApplyMode(bool enabled, int mode)
    {
        int upper;
        int lower;

        switch (mode)
        {
            case 1:
                upper = 90;
                lower = 70;
                break;
            case 2:
                upper = 100;
                lower = 95;
                break;
            default:
                upper = 70;
                lower = 40;
                break;
        }

        if (!enabled)
        {
            upper = 100;
            lower = 0;
        }

        string error;
        using (HalChargeController hal = HalChargeController.Open(out error))
        {
            if (hal == null)
                throw new Exception("HAL 不可用：" + error);

            hal.SetThreshold(upper, lower);

            PowerManagerConfig.Save(enabled, mode);

            string serviceError;
            bool serviceOk = PowerManagerConfig.RestartService(out serviceError);
            Thread.Sleep(1500);

            hal.SetThreshold(upper, lower);

            return string.Format(
                "电池安全管理：{0}\r\n模式：{1}\r\n目标阈值：停止 {2}% / 恢复 {3}%\r\n" +
                "MBAMainService：{4}\r\nBIOS 阈值：已写入",
                enabled ? "开启" : "关闭",
                enabled ? PowerManagerConfig.GetModeName(mode) : "已关闭",
                upper, lower,
                serviceOk ? "已重启" : "重启失败（" + serviceError + "）");
        }
    }

    private static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            return new WindowsPrincipal(identity).IsInRole(
                WindowsBuiltInRole.Administrator);
        }
    }

    private static void RelaunchElevated()
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(
                Application.ExecutablePath);
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show("需要管理员权限才能控制充电。\r\n\r\n" + ex.Message,
                "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void Log(string message)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "last_operation.log");
            File.WriteAllText(path, message + Environment.NewLine +
                "时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                Environment.NewLine);
        }
        catch
        {
        }

        try
        {
            Console.WriteLine(message);
        }
        catch
        {
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
