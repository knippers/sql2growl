using System;

namespace Sql2Growl
{
   public class CommandLine
   {
      private String[] m_arguments;

      public CommandLine(String[] p_arguments)
      {
         this.m_arguments = p_arguments;
      }

      protected Boolean Match(String p_argOne, String p_argTwo)
      {
         String argOne = p_argOne.ToLower();
         String argTwo = p_argTwo.ToLower();

         argOne = argOne.Substring(0, 1).Replace("/", "-") + argOne.Substring(1);
         argTwo = argTwo.Substring(0, 1).Replace("/", "-") + argTwo.Substring(1);

         if (argOne.Substring(0, 1).Equals("-") == false)
            argOne = "-" + argOne;

         if (argTwo.Substring(0, 1).Equals("-") == false)
            argTwo = "-" + argTwo;

         if (argOne.Equals(argTwo))
            return true;

         return false;
      }

      public Boolean FlagSet(String p_flag)
      {
         for (int i = 0; i < this.m_arguments.Length; i++)
         {
            if (Match(this.m_arguments[i], p_flag))
               return true;
         }
         return false;
      }

      public Boolean OptionSet(String p_option)
      {
         for (int i = 0; i < this.m_arguments.Length; i++)
         {
            if (Match(this.m_arguments[i], p_option))
            {
               if ((i + 1) < this.m_arguments.Length)
                  return true;
            }
         }
         return false;
      }

      public string GetOption(String p_option)
      {
         for (int i = 0; i < this.m_arguments.Length; i++)
         {
            if (Match(this.m_arguments[i], p_option))
            {
               if ((i + 1) < this.m_arguments.Length)
                  return this.m_arguments[i + 1];
            }
         }
         return string.Empty;
      }
   }
}

