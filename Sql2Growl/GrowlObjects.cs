using System;
using System.Collections.Generic;
using Growl.Connector;

namespace Sql2Growl
{
   public class Sql2GrowlConnector
   {
      public GrowlConnector Connector { get; internal set; }
      public Dictionary<string,Sql2GrowlApplication> Applications { get; private set; }

      public Sql2GrowlConnector()
      {
         Connector = new GrowlConnector();
         Applications = new Dictionary<string, Sql2GrowlApplication>();
      }

      public bool AddApplication(string p_application, Growl.CoreLibrary.Resource p_icon)
      {
         if (Applications.ContainsKey(p_application) == false)
         {
            Applications.Add(p_application, new Sql2GrowlApplication(p_application, p_icon));
            return true;
         }

         return false;
      }
   }
   
   public class Sql2GrowlApplication : Application
   {
      private Dictionary<string, NotificationType> m_notificationTypes = new Dictionary<string, NotificationType>();

      public NotificationType[] Types
      {
         get
         {
            List<NotificationType> types = new List<NotificationType>();

            foreach (NotificationType type in m_notificationTypes.Values)
               types.Add(type);

            return types.ToArray();
         }
      }

      public bool AddType(string p_name, string p_displayName)
      {
         if (m_notificationTypes.ContainsKey(p_name) == false)
         {
            m_notificationTypes.Add(p_name, new NotificationType(p_name, p_displayName));
            return true;
         }

         return false;
      }

      public Sql2GrowlApplication(string p_application): this( p_application, null ){}

      public Sql2GrowlApplication(string p_application, Growl.CoreLibrary.Resource p_icon): base( p_application )
      {
         if( p_icon != null )
            this.Icon = p_icon;
      }
   }
}
