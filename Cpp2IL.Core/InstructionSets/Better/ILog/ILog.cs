using System;
using System.IO;

namespace Cpp2IL.Core.InstructionSets.Better.ILog
{
    public class ILog
    {
    
        private readonly static object _lock = new object();

        private const bool LogProto = false;
        private const bool WriteFile = true;
        private const string TAG = "ILog";
        private static string TEMP_LOGPATH = @"F:\AndroidFBY\APK\Cpp2Il";

        private static bool isCreateLogPath = false;
        
        private static void LogToFile(string fileName,string msg)
        {
            lock (_lock)
            {
                string logPath = TEMP_LOGPATH + @"\" + fileName + ".txt";
                if (File.Exists(logPath)&& !isCreateLogPath)
                {
                    File.Delete(logPath);
                }
                if (!File.Exists(logPath))
                {
                    isCreateLogPath = true;
                    File.Create(logPath).Close();
                }
                try
                {
                    File.AppendAllText(logPath, msg + "\n");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
     
    
        public static void LOGI(string tag,string s ,bool logToServer=false)
        {
            var log = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "-"+tag + " : "+s;
            Console.WriteLine(log);
        
            if (WriteFile)
            {
                LogToFile( "LOG_"+GetFileName(),log);
            }
        }

        private static string GetFileName()
        {

            return "B_Branch";
        }
        public static void LOGI(string s ,bool logToServer=false)
        {
            LOGI(TAG,s,logToServer);
        }

      

      


     
    }
}
