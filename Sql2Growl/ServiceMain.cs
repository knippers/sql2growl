using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using log4net;
using System.Collections;

namespace Sql2Growl
{
   public class ServiceMain : System.ServiceProcess.ServiceBase
   {
      private ILog m_logger;
      private Implementation m_serviceImplementation;

      public ILog Logger
      {
         get { return m_logger; }
      }

      public Configuration Config
      {
         get;
         private set;
      }

      private ServiceMain() : this(false) { }

      private ServiceMain(bool p_consoleStart)
      {
         try
         {
            #region NT Service options
            ServiceName = Utility.ApplicationName + " Service";
            AutoLog = false;
            CanStop = true;
            #endregion

            #region Init log4net
            m_logger = Utility.SetupLog4Net(p_consoleStart);
            if (m_logger == null)
               throw new Exception("Error initializing Log4Net (null)");
            #endregion

            #region Init config
            if (File.Exists(Configuration.ConfigFile) == false)
            {
               m_logger.FatalFormat("Configuration file {0} does not exist", Configuration.ConfigFile);
               return;
            }

            Config = new Configuration();
            #endregion

            Utility.SetLogTreshold(Config.GetValue("LogLevel", "Debug"));

            AppDomain.CurrentDomain.UnhandledException +=
                new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            m_serviceImplementation = new Implementation(this);

            #region Console Start
            if (p_consoleStart)
            {
               Console.CancelKeyPress +=new ConsoleCancelEventHandler(
                  delegate( object sender, ConsoleCancelEventArgs e )
                  {
                     if (m_logger.IsDebugEnabled)
                        m_logger.Debug("Console Exit...");

                     Stop(true);

                     Environment.ExitCode = 0;
                  }
               );

               if (m_logger.IsDebugEnabled)
                  m_logger.Debug("Console startup...");

               m_serviceImplementation.Start();
            }
            #endregion
         }
         catch (Exception ex)
         {
            // try to log to log4net
            // 
            if (m_logger != null && m_logger.IsErrorEnabled == true)
               m_logger.Error(ex);

            Console.WriteLine("ERR: " + ex.Message);
            Environment.Exit(1);
         }
      }

      private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
      {
         if (m_logger != null)
         {
            if (e.IsTerminating == true)
               m_logger.Fatal(e.ExceptionObject);
            else
               m_logger.Error(e.ExceptionObject);
         }
      }

      private void Stop(bool p_consoleStop)
      {
         try
         {
            if (m_logger.IsDebugEnabled)
               m_logger.Debug("Stop requested.");

            bool done = false;

            // Add a force timeout thread, to make sure we do not end 
            // up waiting forever for the StopListening code to finish
            //
            Thread timeoutThread = new Thread(new ThreadStart(delegate()
            {
               try
               {
                  Utility.Sleep(10 * 1000);

                  if (m_logger != null && m_logger.IsInfoEnabled == true)
                     m_logger.Info("Forced server stop");

                  done = true;
               }
               catch (ThreadInterruptedException) { /* ignore */ }
            }));
            timeoutThread.IsBackground = !p_consoleStop;
            timeoutThread.Start();

            Thread stopThread = new Thread(new ThreadStart(delegate()
            {
               try
               {
                  m_serviceImplementation.Stop();

                  if (m_logger.IsInfoEnabled)
                     m_logger.Info("Server stopped.");

                  done = true;
               }
               catch (ThreadInterruptedException) { /* ignore */ }
            }));
            stopThread.IsBackground = !p_consoleStop;
            stopThread.Start();

            // Wait for either the StopService or the TimeoutThread to 
            // set done to true
            // 
            while (done == false)
            {
               Utility.Sleep(50);
            }
         }
         catch (Exception ex)
         {
            if (m_logger.IsErrorEnabled)
               m_logger.Error("Error stopping server.", ex);
         }
      }

      # region BaseService overrides
      protected override void OnStart(string[] args)
      {
         if (m_serviceImplementation == null)
            return;
         
         if (m_logger.IsDebugEnabled)
            m_logger.Debug("Service starting...");

         new Thread(new ThreadStart(m_serviceImplementation.Start)).Start();
      }

      protected override void OnStop()
      {
         if (m_serviceImplementation == null)
            return;
         
         if (m_logger.IsDebugEnabled)
            m_logger.Debug("Service stopping...");

         Stop(false);
      }
      #endregion

      public static void Main(string[] args)
      {
         Utility.SetExecutingAssembly(typeof(ServiceMain));

         if (args == null || args.Length == 0)
         {
            // normal service startup

            Environment.CurrentDirectory = Utility.ApplicationPath;
            ServiceBase.Run(new ServiceMain(false));
            return;
         }

         CommandLine commandLine = new CommandLine(args);

         if (commandLine.FlagSet("help") || commandLine.FlagSet("?") ||
            commandLine.FlagSet("h"))
         {
            #region Usage
            Console.WriteLine("Usage:");
            Console.WriteLine("-console | -install [-username name -password passwd]| -uninstall");
            Environment.Exit(1);
            #endregion
         }
         else if (commandLine.FlagSet("console"))
         {
            new ServiceMain(true);
         }
         else if (commandLine.FlagSet("install"))
         {
            #region Install service
            try
            {
               if (Utility.IsServiceInstalled(Utility.ApplicationName + " Service") == true)
               {
                  Console.WriteLine("Service '" + Utility.ApplicationName +
                     " Service' already installed.");
                  return;
               }

               string username = null;
               string password = null;
               if (commandLine.OptionSet("username"))
               {
                  username = commandLine.GetOption("username");
                  if (commandLine.OptionSet("password") == false)
                  {
                     Console.WriteLine("If username is set, password must also be set");
                     Environment.Exit(1);
                  }
                  password = commandLine.GetOption("password");
               }

               string result = Utility.ExecuteInstallUtil(true, username, password);

               if (string.IsNullOrEmpty(result) == false)
                  throw new Exception(result);

               Console.WriteLine(
                  "Service '" + Utility.ApplicationName + " Service' installed.");
            }
            catch (Exception ex)
            {
               Console.WriteLine("ERR: " + ex.Message);
            }
            #endregion
         }
         else if (commandLine.FlagSet("uninstall"))
         {
            #region Uninstall service
            try
            {
               if (Utility.IsServiceInstalled(Utility.ApplicationName + " Service") == false)
               {
                  Console.WriteLine("Service '" + Utility.ApplicationName +
                     " Service' is not installed.");
                  return;
               }

               string result = Utility.ExecuteInstallUtil(false, null, null);

               if (string.IsNullOrEmpty(result) == false)
                  throw new Exception(result);

               Console.WriteLine(
                  "Service '" + Utility.ApplicationName + " Service' uninstalled.");
            }
            catch (Exception ex)
            {
               Console.WriteLine("ERR: " + ex.Message);
            }
            #endregion
         }
      }
   }

   #region RunInstaller code
   /// <summary>
   /// Class should be extended in Program.cs as local class for the
   /// InstallUtil to actually work
   /// </summary>
   [RunInstaller(true)]
   public class Sql2GrowlServiceInstaller : Installer
   {
      public Sql2GrowlServiceInstaller()
      {
         Utility.SetExecutingAssembly(typeof(ServiceMain));

         ServiceProcessInstaller spi = new ServiceProcessInstaller();

         if (Environment.CommandLine != null && Environment.CommandLine.IndexOf("/username") != -1)
         {
            spi.Account = ServiceAccount.User;
         }
         else
         {
            spi.Account = ServiceAccount.LocalSystem;
         }

         ServiceInstaller si = new ServiceInstaller();
         si.ServiceName = Utility.ApplicationName + " Service";
         si.Description = Utility.ApplicationName + " Service";
         si.DisplayName = Utility.ApplicationName + " Service";
         si.StartType = ServiceStartMode.Automatic;

         Installers.AddRange(new Installer[] { spi, si });
      }

      public override void Install(IDictionary stateSaver)
      {
         if (Utility.IsServiceInstalled(Utility.ApplicationName + " Service") == true)
         {
            System.Diagnostics.Trace.WriteLine("Service is already installed", "Sql2Growl");
            //return;
         }

         base.Install(stateSaver);
      }

      public override void Uninstall(IDictionary stateSaver)
      {
         base.Uninstall(stateSaver);

         if (Context == null || Context.IsParameterTrue("delete") == false)
         {
            System.Diagnostics.Trace.WriteLine("Context delete condition not set", "Sql2Growl");
            return;
         }

         try
         {
            ServiceController service = new ServiceController(Utility.ApplicationName + " Service");

            if (service != null && service.Status != ServiceControllerStatus.Stopped)
               service.Stop();

            // wait for 10 seconds for the service to stop 
            //
            service.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 0, 10));
         }
         catch (InvalidOperationException ioex)
         {
            System.Diagnostics.Trace.WriteLine(string.Format("Failed to stop service '{0}': {1}",
               Utility.ApplicationName + " Service", ioex.Message), "Sql2Growl");
         }
         catch (System.ServiceProcess.TimeoutException)
         {
            System.Diagnostics.Trace.WriteLine(string.Format(
               "Failed to stop service '{0}' in 10 seconds",
               Utility.ApplicationName + " Service"), "Sql2Growl");
         }

         try
         {
            foreach (string logFile in Directory.GetFiles(
               Utility.ApplicationPath, Utility.ApplicationName + ".log*"))
            {
               File.Delete(logFile);
            }
            System.Diagnostics.Trace.WriteLine("Logfiles deleted", "Sql2Growl");
         }
         catch (Exception ex)
         {
            System.Diagnostics.Trace.WriteLine("Logfile delete error: " + ex.Message, "Sql2Growl");
         }
      }

      #region OnAfterInstall
      protected override void OnAfterInstall(IDictionary savedState)
      {
         base.OnAfterInstall(savedState);

         if (Context == null || string.IsNullOrEmpty(Context.Parameters["server"]) == true)
            return;

         try
         {
            Configuration config = new Configuration();
            config.Load(Configuration.ConfigFile);

            if (config.GetValue("AfterSetup", "False").Equals("False"))
            {
               string connectStr = config.GetValue("ConnectString", string.Empty);
               if (string.IsNullOrEmpty(connectStr) == false)
               {
                  connectStr = "Server=" + Context.Parameters["server"] + ";User ID=" +
                     Context.Parameters["username"] + ";Password=" + Context.Parameters["password"] +
                     ";Connection Timeout=60";

                  System.Diagnostics.Trace.WriteLine("New connectstr: " + connectStr, "Sql2Growl");

                  config.ConfigParameters["ConnectString"] = connectStr;

                  config.ConfigParameters["AfterSetup"] = "True";

                  config.Save(Path.ChangeExtension(Configuration.ConfigFile, ".tmp"));

                  Utility.MoveFile(Path.ChangeExtension(Configuration.ConfigFile, ".tmp"),
                     Configuration.ConfigFile);
               }
            }
         }
         catch (Exception ex)
         {
            System.Diagnostics.Trace.WriteLine(ex.Message, "Sql2Growl");
         }
      }
      #endregion

      #region OnCommitted
      protected override void OnCommitted(IDictionary savedState)
      {
         try
         {
            base.OnCommitted(savedState);
         }
         catch (Exception ex)
         {
            System.Diagnostics.Trace.WriteLine(string.Format(
               "base.OnCommitted error: {0}",ex.Message, "Sql2Growl"));
         }

         try
         {
            ServiceController service = new ServiceController(Utility.ApplicationName + " Service");
            
            if( service != null && service.Status != ServiceControllerStatus.Running )
               service.Start();

            // wait for 10 seconds for the service to start 
            //
            service.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan( 0, 0, 10 ) );
         }
         catch( InvalidOperationException ioex )
         {
            System.Diagnostics.Trace.WriteLine(string.Format( "Failed to start service '{0}': {1}",
               Utility.ApplicationName + " Service", ioex.Message ), "Sql2Growl");
         }
         catch( System.ServiceProcess.TimeoutException )
         {
            System.Diagnostics.Trace.WriteLine(string.Format( 
               "Failed to start service '{0}' in 10 seconds",
               Utility.ApplicationName + " Service"), "Sql2Growl");
         }
      }
      #endregion
   }
   #endregion
}
