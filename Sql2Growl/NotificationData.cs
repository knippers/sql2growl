using System;
using System.Xml;
using System.IO;

namespace Sql2Growl
{
   public class NotificationData
   {
      public Guid NotificationID{ get; set; }

      public string Application { get; set; }
      public string Type { get; set; }
      public string Title { get; set; }
      public string Icon { get; set; }
      public string Message { get; set; }
      public string Password { get; set; }
      public string Host { get; set; }
      public int Port { get; set; }

      /// <summary>
      /// Special key, if true will clear all cached Growl objects (like Application, Connectors and types)
      /// and startes new caching
      /// </summary>
      public bool ClearCachedObjects { get; set; }

      public NotificationData()
      {
         ClearCachedObjects = false;
      }

      public string ConnectorKey
      {
         get { return Password + Host + Port; }
      }

      public string TypeKey
      {
         get { return string.IsNullOrEmpty(Type) ? string.Empty : Type.ToUpper().Replace(' ', '_'); }
      }

      public string IconFile
      {
         get { return string.IsNullOrEmpty(Icon)? string.Empty : Icon + ".png"; }
      }

      private string GetValue(XmlNode p_node, string p_xPath)
      {
         XmlNode node = p_node.SelectSingleNode(p_xPath);
         if (node == null)
            return string.Empty;

         return node.InnerText;
      }

      public void SetFromXml(object p_data)
      {
         if ((p_data is string) == false)
            throw new ArgumentException("Not a string", "Data");

         XmlDocument xmlDoc = new XmlDocument();
         xmlDoc.LoadXml((string)p_data);

         try
         {
            ClearCachedObjects = bool.Parse(GetValue(xmlDoc, "/Growl/ClearCachedObjects"));
         }
         catch { /* ignore */ }

         Application = GetValue( xmlDoc, "/Growl/Application" );
         Type = GetValue( xmlDoc, "/Growl/Type" );
         Title = GetValue( xmlDoc, "/Growl/Title" );
         Message = GetValue( xmlDoc, "/Growl/Message" );
         Icon = GetValue(xmlDoc, "/Growl/Icon");
         Password = GetValue( xmlDoc, "/Growl/Password" );
         Host = GetValue( xmlDoc, "/Growl/Host" );
         
         try
         {
            Port = int.Parse( GetValue( xmlDoc, "/Growl/Port" ) );
         }
         catch{ /* ignore */ }
      }

      public override string ToString()
      {
         return Application + " [" + Type + "]: " + Title + " - " + Message;
      }
   }
}
