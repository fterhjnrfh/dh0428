using System;
using System.IO;

namespace DHHandle
{
    public class Utility
    {
        protected static object m_lock = new object();

        public static string BaseDir = System.AppDomain.CurrentDomain.BaseDirectory;

        public static string CfgDir = Path.Combine(BaseDir, "config");
        public static string ParamDir = Path.Combine(BaseDir, "param");

        public static void WriteLog(Exception ex)
        {
            WriteLog(ex.StackTrace + ex.Message);
        }

        public static void WriteLog(string msg)
        {
            lock (m_lock)
            {
                string dir = Path.Combine(BaseDir, "Logs");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                FileInfo fi = new FileInfo(Path.Combine(dir, "DHDAS.log"));
                StreamWriter sw;
                if (!fi.Exists)
                {
                    sw = fi.CreateText();
                }
                else if (fi.Length > 262144)
                {
                    fi.CopyTo(Path.Combine(dir, string.Format("DHDAS_{0}.log", DateTime.Now.ToString("yyyyMMddHHmmssff"))), overwrite: true);
                    sw = fi.CreateText();
                }
                else
                {
                    sw = fi.AppendText();
                }
                using (sw)
                {
                    sw.WriteLine("[ Log : at {0} ]\t{1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), msg);
                }
                fi = null;
            }
        }
    }
}
