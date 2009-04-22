using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using Growl.Connector;
using log4net;
using System.IO;

namespace Sql2Growl
{
   internal enum CommonSqlErrors
   {
      Unknown = 0,
      Deadlock = 1,
      ObjectNotFound = 2,
      UserError = 4
   }
   
   public class Implementation
   {
      private static readonly object m_locker = new object();

      private ServiceMain m_parent;

      private ILog m_logger = LogManager.GetLogger("Main");

      private SqlConnection m_dbConnection;
      private SqlCommand m_getNextRequestSp;
      private int m_queryExecuteIntervalSec = 60;

      private Thread m_notificationRequester;
      private bool m_notificationStopRequester = false;

      private Dictionary<string, Sql2GrowlConnector> m_growlConnectors = 
         new Dictionary<string, Sql2GrowlConnector>();

      private NotificationData m_lastNotification;

      private int m_errorCount = 0;

      public Implementation(ServiceMain p_parent)
      {
         m_parent = p_parent;

         m_notificationRequester = new Thread(new ThreadStart(NotificationRequestLoop));

         m_queryExecuteIntervalSec = m_parent.Config.GetIntValue("QueryExecuteIntervalSec", 60);

         SqlConnectionStringBuilder connectStr = new SqlConnectionStringBuilder();
         connectStr.ConnectionString = m_parent.Config.GetValue("ConnectString");

         // Setting this to true is needed to be able to stop the service gracefully (instead 
         // of killing it after x seconds when the spGetNextNotification is blocking)
         // 
         connectStr.AsynchronousProcessing = true;

         m_dbConnection = new SqlConnection( connectStr.ToString() );

         try
         {
            CheckConnection();
         }
         catch (Exception ex)
         {
            throw ex;
         }
      }

      #region Sql code

      private void CheckConnection()
      {
         if (m_dbConnection == null)
            return;

         if (m_dbConnection.State != ConnectionState.Open &&
            m_dbConnection.State != ConnectionState.Connecting)
         {
            m_dbConnection.Open();
            if (m_logger.IsDebugEnabled)
            {
               m_logger.Debug("Database (re)connected:");
               m_logger.Debug("  db     : " + m_dbConnection.Database);
               m_logger.Debug("  version: " + m_dbConnection.ServerVersion);
            }
         }
      }

      private CommonSqlErrors GetErrorCode(SqlException p_exception)
      {
         if (p_exception == null || p_exception.Errors == null)
            return CommonSqlErrors.Unknown;

         if (p_exception.Errors.Count == 0)
            return CommonSqlErrors.Unknown;

         switch (p_exception.Errors[0].Number)
         {
            case 1205: return CommonSqlErrors.Deadlock;
            case 2812: return CommonSqlErrors.ObjectNotFound;
            case 50000: return CommonSqlErrors.UserError;
            default: return CommonSqlErrors.Unknown;
         }
      }

      private void Rollback(SqlTransaction p_transaction)
      {
         if (p_transaction == null)
            return;

         try
         {
            p_transaction.Rollback();
         }
         catch (ObjectDisposedException)
         {
            // Ignore this exception, it means that the p_transaction object is already disposed
         }
         catch (InvalidOperationException ioex)
         {
            // A rollback will for example fail if the database already did the rollback

            if (m_logger.IsDebugEnabled)
               m_logger.Debug("Rollback failed", ioex);
         }
      }

      private NotificationData GetNextNotification()
      {
         SqlTransaction transaction = m_dbConnection.BeginTransaction();
         try
         {
            if (m_getNextRequestSp == null)
            {
               m_getNextRequestSp = m_dbConnection.CreateCommand();
               m_getNextRequestSp.CommandText = m_parent.Config.GetValue("NotificationProcedure");
               m_getNextRequestSp.CommandType = CommandType.StoredProcedure;
               m_getNextRequestSp.Parameters.Add("@ReturnValue", SqlDbType.Int)
                   .Direction = ParameterDirection.ReturnValue;

               m_getNextRequestSp.Parameters.Add("@TimeoutSec", SqlDbType.Int);

               m_getNextRequestSp.Prepare();
            }

            m_getNextRequestSp.CommandTimeout = m_queryExecuteIntervalSec + 30;
            m_getNextRequestSp.Parameters["@TimeoutSec"].Value = m_queryExecuteIntervalSec;

            m_getNextRequestSp.Transaction = transaction;

            NotificationData notification = null;

            using (SqlDataReader sqlReader = m_getNextRequestSp.ExecuteReader(CommandBehavior.SingleResult))
            {
               int returnValue = 0;
               if (m_getNextRequestSp.Parameters["@ReturnValue"].Value != null)
                  returnValue = (int)m_getNextRequestSp.Parameters["@ReturnValue"].Value;

               if (returnValue == 0 && sqlReader != null && sqlReader.HasRows == true)
               {
                  notification = new NotificationData();

                  while (sqlReader.Read() == true) // we should only get one row
                  {
                     notification.NotificationID = (Guid)sqlReader["NotificationID"];
                     notification.SetFromXml(sqlReader["NotificationXml"]);
                  }
               }
               else
               {
                  if (m_logger.IsInfoEnabled == true && returnValue != 0)
                  {
                     m_logger.InfoFormat("{0} procedure returned: {1}",
                        m_getNextRequestSp.CommandText, returnValue);
                  }

                  // no notification selected
                  // 
                  notification = null;
               }
            }
            transaction.Commit();

            return notification;
         }
         catch (ThreadAbortException)
         {
            if (m_getNextRequestSp != null)
               m_getNextRequestSp.Cancel();

            throw;
         }
         catch (SqlException sex)
         {
            Rollback(transaction);

            if ( GetErrorCode(sex) == CommonSqlErrors.Deadlock )
            {
               if (m_logger.IsDebugEnabled)
               {
                  m_logger.DebugFormat("{0} procedure call was picked to be the victim of a deadlock",
                     m_getNextRequestSp.CommandText);
               }

               return null; // as if no notification was returned
            }

            if ( GetErrorCode( sex ) == CommonSqlErrors.ObjectNotFound )
            {
               // We need to reinitialize the procedure call
               // 
               m_getNextRequestSp = null;
            }

            throw sex;
         }
         catch (Exception)
         {
            Rollback(transaction);
            throw;
         }
      }

      #endregion

      #region Notify helper methods

      private Sql2GrowlConnector CheckConnector(NotificationData p_notification)
      {
         if (m_growlConnectors.ContainsKey(p_notification.ConnectorKey) == false)
         {
            Sql2GrowlConnector connector = new Sql2GrowlConnector();

            if (string.IsNullOrEmpty(p_notification.Password) == false)
            {
               if (string.IsNullOrEmpty(p_notification.Host) == false && p_notification.Port > 0)
               {
                  connector.Connector = new GrowlConnector(p_notification.Password, p_notification.Host, p_notification.Port);
               }
               else
               {
                  connector.Connector = new GrowlConnector(p_notification.Password);
               }
            }

            connector.Connector.EncryptionAlgorithm = Cryptography.SymmetricAlgorithmType.PlainText;
            connector.Connector.ErrorResponse += new GrowlConnector.ResponseEventHandler(connector_ErrorResponse);

            m_growlConnectors.Add(p_notification.ConnectorKey, connector);
         }

         return m_growlConnectors[p_notification.ConnectorKey];
      }

      private Sql2GrowlApplication CheckApplication(NotificationData p_notification)
      {
         if (m_growlConnectors[p_notification.ConnectorKey].Applications.ContainsKey(
            p_notification.Application) == false)
         {
            Growl.CoreLibrary.Resource icon = null;

            string iconFile = Path.Combine(Utility.ApplicationPath, "Icons");

            if (string.IsNullOrEmpty(p_notification.IconFile) == false)
            {
               iconFile = Path.Combine(iconFile, p_notification.IconFile);
            }
            else
            {
               iconFile = Path.Combine(iconFile, "Sql2GrowlDefault.png");
            }

            if (File.Exists(iconFile) == true)
               icon = new Uri(iconFile).ToString();

            m_growlConnectors[p_notification.ConnectorKey].AddApplication(
               p_notification.Application, icon);
         }

         return m_growlConnectors[p_notification.ConnectorKey].Applications[p_notification.Application];
      }

      #endregion

      private void Notify(NotificationData p_notification)
      {
         if (p_notification == null)
            return;

         if (p_notification.ClearCachedObjects == true)
         {
            m_growlConnectors.Clear(); // should put objects on GC list, but not sure of this
            
            if (m_logger.IsInfoEnabled)
               m_logger.Info("Cleared the cached Growl objects");
         }

         Sql2GrowlConnector connector = CheckConnector(p_notification);

         Sql2GrowlApplication application = CheckApplication(p_notification);

         // Every time a new type is added we have to register the application again with 
         // the new type (and the previously registered types)
         // 
         if (application.AddType( p_notification.TypeKey, p_notification.Type ) == true )
            connector.Connector.Register( application, application.Types );

         connector.Connector.Notify( new Notification(p_notification.Application, p_notification.TypeKey,
            "ID", p_notification.Title, p_notification.Message ) );

         m_lastNotification = p_notification;
      }

      private void connector_ErrorResponse(Response response)
      {
         if (m_logger.IsInfoEnabled)
         {
            m_logger.InfoFormat("Notification response error: {0}/{1} {2}",
               response.ErrorCode, response.ErrorDescription, response.InResponseTo);
         }

         if (m_lastNotification != null && ( 
            response.ErrorCode == 401 || response.ErrorCode == 402 ) ) // Application and/or type not registered
         {
            // The Sql2Growl service is designed to loop forever :) so if someone deletes our registered
            // applications and types we want to register them again.

            m_lastNotification.ClearCachedObjects = true;
            Notify(m_lastNotification);
         }
      }
      
      // the main thread loop
      //
      private void NotificationRequestLoop()
      {
         try
         {
            while (m_notificationStopRequester == false)
            {
               try
               {
                  // check db connection and open again if needed
                  //
                  CheckConnection();

                  NotificationData notification = GetNextNotification();

                  // Reset the error counter
                  // 
                  m_errorCount = 0;

                  if (notification == null)
                  {
                     continue; // go ask again to the database if there is work
                  }

                  Notify(notification);

                  if (m_logger.IsDebugEnabled)
                     m_logger.DebugFormat("Received notification: {0}", notification.ToString());
               }
               catch (ThreadAbortException)
               {
                  throw;
               }
               catch (Exception ex)
               {
                  if (m_logger.IsWarnEnabled)
                     m_logger.Warn("Poll error: " + ex.Message, ex);

                  if (m_errorCount < 12)
                     m_errorCount++;

                  // We don't know what happened here, but to be sure go to sleep for a while
                  // before continueing
                  // On subsequent errors increase the time to sleep
                  //
                  #region Sleep
                  
                  // Don't simply go to sleep for x seconds, that would mean we cannot be interrupted
                  // for x seconds, instead go into a loop of 1 second sleeps so we can get interrupted 
                  // after max 1 second
                  // 
                  int sleepTime = 5000 * m_errorCount / 1000;
                  while (m_notificationStopRequester == false && sleepTime > 0)
                  {
                     Utility.Sleep(1000);
                     sleepTime--;
                  }
                  #endregion
               }
            }
         }
         catch (ThreadAbortException)
         {
            if (m_logger.IsDebugEnabled)
               m_logger.Debug("Requested to stop");
         }
      }

      #region Start/Stop

      public void Start()
      {
         try
         {
            lock (m_locker)
            {
               if (m_logger.IsDebugEnabled)
                  m_logger.Debug("Starting...");

               m_notificationStopRequester = false;
               m_notificationRequester.Start();

               if (m_logger.IsDebugEnabled)
                  m_logger.Debug("Started");
            }
         }
         catch (Exception ex)
         {
            m_logger.Warn(ex);
         }
      }

      public void Stop()
      {
         try
         {
            lock (m_locker)
            {
               if (m_logger.IsDebugEnabled)
                  m_logger.Debug("Stopping...");

               m_notificationStopRequester = true;

               // we want to do a gracefull shutdown of the thread
               // 
               if (m_notificationRequester != null)
               {
                  m_notificationRequester.Abort();
                  while (m_notificationRequester.IsAlive == true || 
                     m_notificationRequester.ThreadState == ThreadState.WaitSleepJoin)
                  {
                     Utility.Sleep(50);
                  }
               }

               // close the database connection if it is still open
               // 
               if (m_dbConnection != null && m_dbConnection.State == ConnectionState.Open)
                  m_dbConnection.Close();

               if (m_logger.IsDebugEnabled)
                  m_logger.Debug("Stopped");
            }
         }
         catch (Exception ex)
         {
            m_logger.Warn(ex);
         }
      }
      #endregion

   }
}
