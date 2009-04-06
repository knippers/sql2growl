using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Sql2Growl
{
   public class Configuration
   {
      private Dictionary<string, string> m_configParameters = new Dictionary<string, string>();

      public Dictionary<string, string> ConfigParameters
      {
         get { return m_configParameters; }
      }

      public static string ConfigFile
      {
         get { return Path.Combine(Utility.ApplicationPath, Utility.ApplicationName + "Config.xml"); }
      }

      public Configuration()
      {
         Load(ConfigFile);
      }

      public string GetValue(string p_key)
      {
         return GetValue(p_key, string.Empty);
      }

      public string GetValue(string p_key, string p_default)
      {
         if (m_configParameters.ContainsKey(p_key) == false)
            return p_default;

         return m_configParameters[p_key];
      }

      public int GetIntValue(string p_key, int p_default)
      {
         string value = GetValue(p_key, string.Empty);

         if (string.IsNullOrEmpty(value))
            return p_default;

         try
         {
            return int.Parse(value);
         }
         catch 
         {
            return p_default;
         }
      }
      
      public void Load(string p_config)
      {
         using (FileStream configFile = new FileStream(p_config, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
         {
            XmlDocument xmlConfig = new XmlDocument();
            xmlConfig.Load(configFile);

            foreach (XmlNode rootNode in xmlConfig.SelectNodes(Utility.ApplicationName))
            {
               foreach (XmlNode keyNode in rootNode.SelectNodes("*"))
               {
                  if (m_configParameters.ContainsKey(keyNode.Name) == false)
                     m_configParameters.Add(keyNode.Name, keyNode.InnerText);
                  else
                     m_configParameters[keyNode.Name] = keyNode.InnerText;
               }
            }
         }
      }

      public void Save(string p_config)
      {
         XmlWriterSettings settings = new XmlWriterSettings();
         settings.Indent = true;

         XmlWriter writer = XmlWriter.Create(p_config, settings);

         writer.WriteStartDocument();
         writer.WriteStartElement(Utility.ApplicationName);

         foreach( string key in m_configParameters.Keys )
            writer.WriteElementString(key, m_configParameters[key]);

         writer.WriteEndElement();
         writer.Close();
      }
   }
}
