﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

using Shadowsocks.Controller;
using Shadowsocks.Controller.Hotkeys;
using Shadowsocks.Util;
using Shadowsocks.View;

namespace Shadowsocks
{
    static class Program
    {
        public static ShadowsocksController MainController { get; private set; }
        public static MenuViewController MenuController { get; private set; }
        public static string[] Args { get; private set; }
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // .NET Framework 4.7.2 on Win7 compatibility
            System.Net.ServicePointManager.SecurityProtocol |= 
                System.Net.SecurityProtocolType.Tls | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls12;

            // store args for further use
            Args = args;
            // Check OS since we are using dual-mode socket
            if (!Utils.IsWinVistaOrHigher())
            {
                MessageBox.Show(I18N.GetString("Unsupported operating system, use Windows Vista at least."),
                "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Check .NET Framework version
            if (!Utils.IsSupportedRuntimeVersion())
            {
                MessageBox.Show(I18N.GetString("Unsupported .NET Framework, please update to 4.6.2 or later."),
                "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                Process.Start(
                    "https://www.microsoft.com/download/details.aspx?id=53344");
                return;
            }

            Utils.ReleaseMemory(true);
            using (Mutex mutex = new Mutex(false, $"Global\\Shadowsocks_{Application.StartupPath.GetHashCode()}"))
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // handle UI exceptions
                Application.ThreadException += Application_ThreadException;
                // handle non-UI exceptions
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                Application.ApplicationExit += Application_ApplicationExit;
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AutoStartup.RegisterForRestart(true);

                if (!mutex.WaitOne(0, false))
                {
                    Process[] oldProcesses = Process.GetProcessesByName("Shadowsocks");
                    if (oldProcesses.Length > 0)
                    {
                        Process oldProcess = oldProcesses[0];
                    }
                    MessageBox.Show(I18N.GetString("Find Shadowsocks icon in your notify tray.")
                        + Environment.NewLine
                        + I18N.GetString("If you want to start multiple Shadowsocks, make a copy in another directory."),
                        I18N.GetString("Shadowsocks is already running."));
                    return;
                }
                Directory.SetCurrentDirectory(Application.StartupPath);
#if DEBUG
                Logging.OpenLogFile();

                // truncate privoxy log file while debugging
                string privoxyLogFilename = Utils.GetTempPath("privoxy.log");
                if (File.Exists(privoxyLogFilename))
                    using (new FileStream(privoxyLogFilename, FileMode.Truncate)) { }
#else
                Logging.OpenLogFile();
#endif
                MainController = new ShadowsocksController();
                MenuController = new MenuViewController(MainController);
                HotKeys.Init(MainController);
                MainController.Start();
                Application.Run();
            }
        }

        private static int exited = 0;
        
        
        ----------------------------------------------------------------------------------------
            //方法CurrentDomain_UnhandledException 当前未知handle域异常
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (Interlocked.Increment(ref exited) == 1)
            {
                string errMsg = e.ExceptionObject.ToString();
                Logging.Error(errMsg);
                MessageBox.Show(
                    $"{I18N.GetString("Unexpected error, shadowsocks will exit. Please report to")} https://github.com/shadowsocks/shadowsocks-windows/issues {Environment.NewLine}{errMsg}",
                    "Shadowsocks non-UI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        
        -----------------------------------------------------------------------------------------
           //方法Application_ThreadException 程序线程异常
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            if (Interlocked.Increment(ref exited) == 1)
            {
                string errorMsg = $"Exception Detail: {Environment.NewLine}{e.Exception}";
                Logging.Error(errorMsg);
                MessageBox.Show(
                    $"{I18N.GetString("Unexpected error, shadowsocks will exit. Please report to")} https://github.com/shadowsocks/shadowsocks-windows/issues {Environment.NewLine}{errorMsg}",
                    "Shadowsocks UI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        
        
        -------------------------------------------------------------------------------
        //方法 SystemEvents_PowerModeChanged 系统电源模块事件变更
        
        private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Resume:
                    Logging.Info("os wake up");
                    if (MainController != null)
                    {
                        System.Threading.Tasks.Task.Factory.StartNew(() =>
                        {
                            Thread.Sleep(10 * 1000);
                            try
                            {
                                MainController.Start(false);
                                Logging.Info("controller started");
                            }
                            catch (Exception ex)
                            {
                                Logging.LogUsefulException(ex);
                            }
                        });
                    }
                    break;
                case PowerModes.Suspend:
                    if (MainController != null)
                    {
                        MainController.Stop();
                        Logging.Info("controller stopped");
                    }
                    Logging.Info("os suspend");
                    break;
            }
        }

        
        
        
        -----------------------------------------------------------------------------------------------------
        //方法Application_ApplicationExit 程序异常
        
        private static void Application_ApplicationExit(object sender, EventArgs e)
        {
            // detach static event handlers
            Application.ApplicationExit -= Application_ApplicationExit;
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            Application.ThreadException -= Application_ThreadException;
//销毁
            HotKeys.Destroy();

       //主控制不为空时关闭 
            if (MainController != null)
            {
                MainController.Stop();
                MainController = null;
            }
        }
    }
}
