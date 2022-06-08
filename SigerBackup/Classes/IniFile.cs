using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SigerBackup.Classes
{
    internal class IniFile
    {
        private readonly string _path;
        private readonly string _appDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string _exe = Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()?.Location);
        private readonly string _iniFile = $@"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\SMF\{AppDomain.CurrentDomain.FriendlyName}.ini";

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string value, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string @default, StringBuilder retVal, int size, string filePath);

        public IniFile(string iniPath = null)
        {
            if (iniPath != null && !iniPath.Contains(":"))
                iniPath = $"{_appDir}{iniPath}";

            _path = new FileInfo(iniPath ?? _iniFile).FullName;
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? string.Empty);
        }

        public string Read(string key, string section = null)
        {
            var retVal = new StringBuilder(255);
            _ = GetPrivateProfileString(section ?? _exe, key, "", retVal, 255, _path);
            return retVal.ToString();
        }

        public void Write(string key, string value, string section = null)
        {
            WritePrivateProfileString(section ?? _exe, key, value, _path);
        }

        public void DeleteKey(string key, string section = null)
        {
            Write(key, null, section ?? _exe);
        }

        public void DeleteSection(string section = null)
        {
            Write(null, null, section ?? _exe);
        }

        public bool KeyExists(string key, string section = null)
        {
            return Read(key, section).Length > 0;
        }
    }
}