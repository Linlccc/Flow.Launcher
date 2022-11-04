using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

// http://blogs.microsoft.co.il/arik/2010/05/28/wpf-single-instance-application/
// modified to allow single instace restart
namespace Flow.Launcher.Helper
{
    internal enum WM
    {
        NULL = 0x0000,
        CREATE = 0x0001,
        DESTROY = 0x0002,
        MOVE = 0x0003,
        SIZE = 0x0005,
        ACTIVATE = 0x0006,
        SETFOCUS = 0x0007,
        KILLFOCUS = 0x0008,
        ENABLE = 0x000A,
        SETREDRAW = 0x000B,
        SETTEXT = 0x000C,
        GETTEXT = 0x000D,
        GETTEXTLENGTH = 0x000E,
        PAINT = 0x000F,
        CLOSE = 0x0010,
        QUERYENDSESSION = 0x0011,
        QUIT = 0x0012,
        QUERYOPEN = 0x0013,
        ERASEBKGND = 0x0014,
        SYSCOLORCHANGE = 0x0015,
        SHOWWINDOW = 0x0018,
        ACTIVATEAPP = 0x001C,
        SETCURSOR = 0x0020,
        MOUSEACTIVATE = 0x0021,
        CHILDACTIVATE = 0x0022,
        QUEUESYNC = 0x0023,
        GETMINMAXINFO = 0x0024,

        WINDOWPOSCHANGING = 0x0046,
        WINDOWPOSCHANGED = 0x0047,

        CONTEXTMENU = 0x007B,
        STYLECHANGING = 0x007C,
        STYLECHANGED = 0x007D,
        DISPLAYCHANGE = 0x007E,
        GETICON = 0x007F,
        SETICON = 0x0080,
        NCCREATE = 0x0081,
        NCDESTROY = 0x0082,
        NCCALCSIZE = 0x0083,
        NCHITTEST = 0x0084,
        NCPAINT = 0x0085,
        NCACTIVATE = 0x0086,
        GETDLGCODE = 0x0087,
        SYNCPAINT = 0x0088,
        NCMOUSEMOVE = 0x00A0,
        NCLBUTTONDOWN = 0x00A1,
        NCLBUTTONUP = 0x00A2,
        NCLBUTTONDBLCLK = 0x00A3,
        NCRBUTTONDOWN = 0x00A4,
        NCRBUTTONUP = 0x00A5,
        NCRBUTTONDBLCLK = 0x00A6,
        NCMBUTTONDOWN = 0x00A7,
        NCMBUTTONUP = 0x00A8,
        NCMBUTTONDBLCLK = 0x00A9,

        SYSKEYDOWN = 0x0104,
        SYSKEYUP = 0x0105,
        SYSCHAR = 0x0106,
        SYSDEADCHAR = 0x0107,
        COMMAND = 0x0111,
        SYSCOMMAND = 0x0112,

        MOUSEMOVE = 0x0200,
        LBUTTONDOWN = 0x0201,
        LBUTTONUP = 0x0202,
        LBUTTONDBLCLK = 0x0203,
        RBUTTONDOWN = 0x0204,
        RBUTTONUP = 0x0205,
        RBUTTONDBLCLK = 0x0206,
        MBUTTONDOWN = 0x0207,
        MBUTTONUP = 0x0208,
        MBUTTONDBLCLK = 0x0209,
        MOUSEWHEEL = 0x020A,
        XBUTTONDOWN = 0x020B,
        XBUTTONUP = 0x020C,
        XBUTTONDBLCLK = 0x020D,
        MOUSEHWHEEL = 0x020E,


        CAPTURECHANGED = 0x0215,

        ENTERSIZEMOVE = 0x0231,
        EXITSIZEMOVE = 0x0232,

        IME_SETCONTEXT = 0x0281,
        IME_NOTIFY = 0x0282,
        IME_CONTROL = 0x0283,
        IME_COMPOSITIONFULL = 0x0284,
        IME_SELECT = 0x0285,
        IME_CHAR = 0x0286,
        IME_REQUEST = 0x0288,
        IME_KEYDOWN = 0x0290,
        IME_KEYUP = 0x0291,

        NCMOUSELEAVE = 0x02A2,

        DWMCOMPOSITIONCHANGED = 0x031E,
        DWMNCRENDERINGCHANGED = 0x031F,
        DWMCOLORIZATIONCOLORCHANGED = 0x0320,
        DWMWINDOWMAXIMIZEDCHANGE = 0x0321,

        #region Windows 7
        DWMSENDICONICTHUMBNAIL = 0x0323,
        DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326,
        #endregion

        USER = 0x0400,

        // This is the hard-coded message value used by WinForms for Shell_NotifyIcon.
        // It's relatively safe to reuse.
        TRAYMOUSEMESSAGE = 0x800, //WM_USER + 1024
        APP = 0x8000
    }

    [SuppressUnmanagedCodeSecurity]
    internal static class NativeMethods
    {
        /// <summary>
        /// Delegate declaration that matches WndProc signatures.
        /// </summary>
        public delegate IntPtr MessageHandler(WM uMsg, IntPtr wParam, IntPtr lParam, out bool handled);

        [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
        private static extern IntPtr _CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmdLine, out int numArgs);


        [DllImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
        private static extern IntPtr _LocalFree(IntPtr hMem);


        public static string[] CommandLineToArgvW(string cmdLine)
        {
            IntPtr argv = IntPtr.Zero;
            try
            {
                int numArgs = 0;

                argv = _CommandLineToArgvW(cmdLine, out numArgs);
                if (argv == IntPtr.Zero)
                {
                    throw new Win32Exception();
                }
                string[] result = new string[numArgs];

                for (int i = 0; i < numArgs; i++)
                {
                    IntPtr currArg = Marshal.ReadIntPtr(argv, i * Marshal.SizeOf(typeof(IntPtr)));
                    result[i] = Marshal.PtrToStringUni(currArg);
                }

                return result;
            }
            finally
            {

                IntPtr p = _LocalFree(argv);
                // Otherwise LocalFree failed.
                // Assert.AreEqual(IntPtr.Zero, p);
            }
        }

    }

    public interface ISingleInstanceApp
    {
        void OnSecondAppStarted();
    }

    /// <summary>
    /// 此类检查以确保一次仅运行此应用程序的一个实例
    /// </summary>
    /// <remarks>
    /// 应谨慎使用此类，因为它不进行安全检查。
    /// 例如：
    ///     如果使用此类的应用程序的一个实例以管理员身份运行，
    ///     则任何其他实例，即使它不是以管理员身份运行，
    ///     也可以使用命令行参数激活它。 
    /// 对于大多数应用程序来说，这不是什么大问题
    /// </remarks>
    public static class SingleInstance<TApplication> where TApplication : Application, ISingleInstanceApp
    {
        #region 常量
        /// <summary>
        /// 通道名称中使用的字符串分隔符
        /// </summary>
        private const string Delimiter = ":";
        /// <summary>
        /// 频道名称的后缀
        /// </summary>
        private const string ChannelNameSuffix = "SingeInstanceIPCChannel";
        #endregion

        #region 字段
        /// <summary>
        /// 应用程序互斥锁
        /// </summary>
        private static Mutex _singleInstanceMutex;
        #endregion

        #region 方法
        #region 公开
        #region 静态
        /// <summary>
        /// 检查尝试启动的应用程序实例是否是第一个实例。 如果没有，则激活第一个实例。
        /// </summary>
        /// <returns>如果这是应用程序的第一个实例，则为真</returns>
        public static bool InitializeAsFirstInstance(string uniqueName)
        {
            // 构建唯一的应用程序 ID 和 IPC 通道名称
            string applicationIdentifier = uniqueName + Environment.UserName;
            string channelName = string.Concat(applicationIdentifier, Delimiter, ChannelNameSuffix);

            // 根据唯一的应用程序 ID 创建互斥锁，以检查这是否是应用程序的第一个实例
            _singleInstanceMutex = new Mutex(true, applicationIdentifier, out bool firstInstance);
            _ = firstInstance ? CreateRemoteServiceAsync(channelName) : SignalFirstInstanceAsync(channelName);
            return firstInstance;

            // 创建远程服务，等待该程序再次启动时连接
            static async Task CreateRemoteServiceAsync(string channelName)
            {
                using NamedPipeServerStream pipeServer = new(channelName, PipeDirection.In);
                while (true)
                {
                    // 等待连接
                    await pipeServer.WaitForConnectionAsync();
                    // 使用Ui线程执行 LaunchAppSecondTime ,当前线程同步等待
                    if (Application.Current != null) Application.Current.Dispatcher.Invoke(LaunchAppSecondTime);
                    // 断开本次连接
                    pipeServer.Disconnect();
                }
            }

            // 向第一个实例发送信号
            static async Task SignalFirstInstanceAsync(string channelName)
            {
                // 创建连接到服务器的客户端管道
                using NamedPipeClientStream pipeClient = new(".", channelName, PipeDirection.Out);
                // 连接到可用管道
                await pipeClient.ConnectAsync(0);
            }
        }

        /// <summary>
        /// 清理单实例代码，清理共享资源，互斥体等
        /// </summary>
        public static void Cleanup()
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        #endregion
        #endregion

        #region 私有
        #region 静态
        /// <summary>
        /// 第二次启动程序
        /// </summary>
        private static void LaunchAppSecondTime()
        {
            if (Application.Current is null) return;
            // 执行程序的第二次启动
            ((TApplication)Application.Current).OnSecondAppStarted();
        }
        #endregion
        #endregion

        #region 没理解到用处的(或者觉得没用)
        /// <summary>
        /// Gets command line args - for ClickOnce deployed applications, command line args may not be passed directly, they have to be retrieved.
        /// </summary>
        /// <returns>List of command line arg strings.</returns>
        private static IList<string> GetCommandLineArgs(string uniqueApplicationName)
        {
            string[] args = null;

            try
            {
                // The application was not clickonce deployed, get args from standard API's
                args = Environment.GetCommandLineArgs();
            }
            catch (NotSupportedException)
            {

                // The application was clickonce deployed
                // Clickonce deployed apps cannot recieve traditional commandline arguments
                // As a workaround commandline arguments can be written to a shared location before 
                // the app is launched and the app can obtain its commandline arguments from the 
                // shared location               
                string appFolderPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), uniqueApplicationName);

                string cmdLinePath = Path.Combine(appFolderPath, "cmdline.txt");
                if (File.Exists(cmdLinePath))
                {
                    try
                    {
                        using (TextReader reader = new StreamReader(cmdLinePath, Encoding.Unicode))
                        {
                            args = NativeMethods.CommandLineToArgvW(reader.ReadToEnd());
                        }

                        File.Delete(cmdLinePath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            if (args == null)
            {
                args = new string[] { };
            }

            return new List<string>(args);
        }

        /// <summary>
        /// Callback for activating first instance of the application.
        /// </summary>
        /// <param name="arg">Callback argument.</param>
        /// <returns>Always null.</returns>
        private static object ActivateFirstInstanceCallback(object o)
        {
            LaunchAppSecondTime();
            return null;
        }
        #endregion
        #endregion
    }
}
