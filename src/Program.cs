using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InspectionWidget
{
    internal static class Program
    {
        private const string MutexName = "InspectionWidget.SingleInstance";

        // DPI 兜底声明（本任务内联；Native 模块在 Task 3 才建立）
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [STAThread]
        private static void Main()
        {
            // 单实例互斥（Task 0 审查 Minor-3 兜底）：首个实例异常退出后，
            // 命名互斥被废弃，下一个实例在构造 Mutex 时会抛
            // AbandonedMutexException——该异常的语义即「本线程已接管所有权」，
            // 捕获后视为首次启动继续，不当作已有实例在运行而误退。
            Mutex mutex = null;
            bool createdNew;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
                // 构造未完成的 Mutex 对象不能依赖：另开一个同名句柄把命名
                // 互斥保活到本进程结束，保证后续实例仍能探到它已存在；
                // 开句柄失败也只降级为无保活句柄，不阻断启动。
                try
                {
                    mutex = new Mutex(false, MutexName);
                }
                catch
                {
                    mutex = null;
                }
            }

            if (!createdNew)
            {
                if (mutex != null)
                    mutex.Dispose();
                return;
            }

            if (mutex != null)
            {
                using (mutex)
                {
                    RunApplication();
                }
            }
            else
            {
                RunApplication();
            }
        }

        private static void RunApplication()
        {
            try
            {
                // PerMonitorV2 = -4；失败忽略（旧系统无此 API 时靠 manifest 声明）
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                // 忽略 DPI 设置失败
            }

            // 全局异常兜底（Task 8）：UI 线程可恢复异常记日志并尝试让主面板
            // 回到待命、保留待保存图，不退出；非 UI 线程未处理异常只尽力记
            // 日志，IsTerminating 时不拦截、由系统终止。两处处理本身都不得
            // 再抛异常（ErrorLog.Write 内部已全吞，恢复调用另有 try/catch）。
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // R9 口径：显式设 CatchException，使非终止性异常必定走
            // ThreadException 友好处理（记日志 + 恢复待命 + 不退出），
            // 不受 app.config 影响、不弹系统崩溃框；终止性异常
            // （AppDomain.UnhandledException 且 IsTerminating=true）
            // 维持仅记日志、由系统终止，不在此拦截。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ErrorLog.Write("UI线程", e.Exception);
            try
            {
                MainForm mainForm = FindMainForm();
                if (mainForm != null)
                    mainForm.RecoverToIdle();
            }
            catch (Exception recoverEx)
            {
                // 恢复失败只记日志：处理完不退出，面板保持现状等用户处理。
                ErrorLog.Write("UI线程恢复待命", recoverEx);
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex == null)
            {
                ex = new Exception("非UI线程未处理异常（原始对象："
                    + (e.ExceptionObject == null ? "null" : e.ExceptionObject.ToString()) + "）");
            }
            ErrorLog.Write("非UI线程", ex);
            // IsTerminating 时进程即将被系统终止，这里不再拦截、不承诺恢复。
        }

        private static MainForm FindMainForm()
        {
            foreach (Form form in Application.OpenForms)
            {
                MainForm mainForm = form as MainForm;
                if (mainForm != null)
                    return mainForm;
            }
            return null;
        }
    }
}
