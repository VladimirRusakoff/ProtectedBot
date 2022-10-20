using System;
using System.IO;
using System.Text;

namespace ProtectBot
{
    public class LogFileNew
    {
        private FileStream LFile;

        private string CurrentFile;

        private string fileName;

        public LogFileNew(string _fileName)
        {
            fileName = _fileName;
            CurrentFile = string.Format("{0}_{1}", DateTime.Now.ToString("yyyy-MM-dd"), fileName);
            if (!Directory.Exists(AppDomain.CurrentDomain.BaseDirectory + "/log/"))
                Directory.CreateDirectory(AppDomain.CurrentDomain.BaseDirectory + "/log/");

            LFile = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "/log/" + CurrentFile + ".log", FileMode.Append);
        }

        public void WriteLine(string logLine, bool isTime = true)
        {
            try
            {
                if (!(CurrentFile.Contains(string.Format("{0}", DateTime.Now.ToString("yyyy-MM-dd")))))
                {
                    CurrentFile = string.Format("{0}_{1}", DateTime.Now.ToString("yyyy-MM-dd"), fileName);
                    LFile = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "/log/" + CurrentFile + ".log", FileMode.Append);
                }
                byte[] line;
                if (isTime)
                    line = Encoding.UTF8.GetBytes(DateTime.Now.ToString("yyyy-MM-dd H:mm:ss.fff zzz") + "\t" + logLine + "\r\n");
                else
                    line = Encoding.UTF8.GetBytes(logLine + "\r\n");
                LFile.Write(line, 0, line.Length);
                LFile.Flush();
            }
            catch (Exception ex)
            {

            }
        }
    }
}
