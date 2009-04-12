using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;

namespace Sql2Growl
{
   public class Utility
   {
      private static Assembly m_executingAssembly;

      public static void SetExecutingAssembly(Type p_type)
      {
         m_executingAssembly = Assembly.GetAssembly(p_type);
      }

      public static Assembly ExecutingAssembly
      {
         get
         {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (m_executingAssembly != null)
               assembly = m_executingAssembly;

            return assembly;
         }
      }

      public static String ApplicationPath
      {
         get
         {
            return Path.GetDirectoryName(ExecutingAssembly.Location);
         }
      }

      public static String ApplicationName
      {
         get
         {
            return Path.GetFileNameWithoutExtension(ExecutingAssembly.Location);
         }
      }

      /// <summary>
      /// Setup a log4net log object
      /// </summary>
      /// <returns>initialized log object</returns>
      public static ILog SetupLog4Net(bool p_logToConsole)
      {
         PatternLayout layout =
            new PatternLayout("%date [%2thread] %-5level [%c] - %m%n");

         RollingFileAppender appender = new RollingFileAppender();
         appender.Layout = layout;
         appender.File = ApplicationName + ".log";
         appender.AppendToFile = true;
         appender.RollingStyle = RollingFileAppender.RollingMode.Size;
         appender.MaxSizeRollBackups = 5;
         appender.MaximumFileSize = "2048KB";
         appender.StaticLogFileName = true;
         appender.AppendToFile = true;
         appender.ActivateOptions();
         log4net.Config.BasicConfigurator.Configure(appender);

         if (p_logToConsole == true)
         {
            ConsoleAppender consoleAppender = new ConsoleAppender();
            consoleAppender.Layout = layout;
            consoleAppender.ActivateOptions();
            log4net.Config.BasicConfigurator.Configure(consoleAppender);
         }

         // I am not a big fan of eventviewer logging, but if you are
         // uncomment the next lines
         /*
         EventLogAppender eventViewerAppender = new EventLogAppender();
         eventViewerAppender.Layout = layout;
         eventViewerAppender.Threshold = Level.Info;
         eventViewerAppender.ActivateOptions();
         log4net.Config.BasicConfigurator.Configure(eventViewerAppender);
         */

         return LogManager.GetLogger(ApplicationName);
      }

      public static void SetLogTreshold(string p_level)
      {
         if (string.IsNullOrEmpty(p_level))
            return;
         
         foreach (Level level in LogManager.GetRepository().LevelMap.AllLevels)
         {
            if (level.Name.Equals(p_level.ToUpper()) == true)
            {
               LogManager.GetRepository().Threshold = level;
            }
         }
      }

      public static bool IsServiceInstalled(string p_serviceName)
      {
         try
         {
            ServiceController serviceCtrl = new ServiceController(p_serviceName);

            if (serviceCtrl == null || string.IsNullOrEmpty(serviceCtrl.ServiceName) == true)
               return false;

            return true;
         }
         catch { return false; }
      }

      /// <summary>
      /// Execute the .NET service install tool to install or uninstall a .NET service
      /// </summary>
      /// <param name="Install">true to install, false to uninstall</param>
      /// <returns>empty string if all ok, errormessage if an error occured</returns>
      public static string ExecuteInstallUtil(bool p_install, string p_username, string p_password)
      {
         string result = string.Empty;

         string installUtilExe = System.Runtime.InteropServices
            .RuntimeEnvironment.GetRuntimeDirectory() + "\\InstallUtil.exe";

         string arguments = "/LogFile= \"" + Path.Combine(ApplicationPath, ApplicationName) + ".exe\"";
         if (p_install == true)
         {
            if (string.IsNullOrEmpty(p_username) == false && string.IsNullOrEmpty(p_password) == false)
            {
               if (p_username.IndexOf("\\") == -1)
                  p_username = ".\\" + p_username;
               arguments = " /username=" + p_username + " /password=" + p_password + " " + arguments;
            }
         }
         else
         {
            arguments = "/u " + arguments;
         }

         int exitCode = 0;
         ArrayList stdOutLines = new ArrayList();

         #region Execute install util executable

         using (Process process = new Process())
         {
            process.StartInfo.WorkingDirectory = ApplicationPath;
            process.StartInfo.FileName = installUtilExe;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = false;

            process.Start();

            Thread stdOut = new Thread(new ThreadStart(delegate()
            {
               String line = null;
               while ((line = process.StandardOutput.ReadLine()) != null)
               {
                  if (string.IsNullOrEmpty(line.Trim()))
                     continue;

                  stdOutLines.Add(line);
               }
            }));
            stdOut.Start();

            if (process.WaitForExit(15 * 1000))
               exitCode = process.ExitCode;

            if (stdOut != null)
               stdOut.Join();
         }

         #endregion

         if (exitCode != 0)
         {
            if (p_install == true)
               result = "ERROR Installing:\n";
            else
               result = "ERROR Uninstalling:\n";

            for (int i = 4; i < stdOutLines.Count; i++) // skip 4 info lines
               result += stdOutLines[i].ToString() + "\n";
         }

         if (p_install == true)
         {
            try
            {
               File.Delete(Path.Combine(ApplicationPath, ApplicationName) + ".InstallState");
            }
            catch { /* ignore */ }
         }

         return result;
      }

      public static void MoveFile(string p_from, string p_to)
      {
         if (string.IsNullOrEmpty(p_from))
            throw new ArgumentNullException("From");

         if (string.IsNullOrEmpty(p_to))
            throw new ArgumentNullException("To");

         if (File.Exists(p_from) == false)
            throw new ArgumentException("File '" + p_from + "' does not exist", "From");

         if (Path.GetFullPath(p_from).Equals(Path.GetFullPath(p_to)))
            return;

         if (File.Exists(p_to))
            File.Delete(p_to);

         File.Move(p_from, p_to);
      }

      public static void Sleep(int p_millisecondsTimeout)
      {
         try { Thread.Sleep(p_millisecondsTimeout); }
         catch { /* ignore */ }
      }
   }
}
