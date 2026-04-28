using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoStartVirtualInstrument
{
    internal class MainWindowModel
    {
        string ExePath = "VirtualInstrument.exe";

        #region 启动程序
        public string ComputerIPaddress { get; set; } = "";
        public int StartMachineID { get; set; } = 0;
        public int StartMachineCount { get; set; } = 10;
        public int ChannelCount { get; set; } = 16;
        #endregion

        #region 配置Mac文件
        public string StartLocalIP { get; set; } = "192.168.100.120";
        public int ConfigMachineCount { get; set; } = 160;
        public int DestPort { get; set; } = 6000;
        public int WaveType { get; set; } = 0;
        public int Frequency { get; set; } = 10;
        public float Amplitude { get; set; } = 1000f;
        #endregion

        #region 配置DeviceInfo
        public string StartDeviceInfoLocalIP { get; set; } = "192.168.100.120";
        public int DeviceInfoMachineCount { get; set; } = 10;
        #endregion

        public void StartExe()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(ComputerIPaddress))
                return;

            CommonIniFile autoStartIniFile = new CommonIniFile(Path.Combine(current, $"AutoStart.ini"));
            for (int i = StartMachineID; i < StartMachineID + StartMachineCount; i++)
            {
                autoStartIniFile.WriteString("NetParam", "CurrentMachineID", i.ToString());

                CommonIniFile commonIniFile = new CommonIniFile(Path.Combine(current, $"Mac{i}.ini"));
                commonIniFile.WriteString("NetParam", "DestIP", ComputerIPaddress);
                commonIniFile.WriteString("NetParam", "ChannelCount", ChannelCount.ToString());

                Process.Start(ExePath);
                Thread.Sleep(500);
            }
        }

        public void Config()
        {
            if (!string.IsNullOrEmpty(StartLocalIP) && IPAddress.TryParse(StartLocalIP, out IPAddress address))
            {
                string current = AppDomain.CurrentDomain.BaseDirectory;
                //批量设置DEMO参数
                for (int i = 0; i < ConfigMachineCount; i++)
                {
                    if (i != 0)
                        address = GetNextIPAddress(address);
                    CommonIniFile commonIniFile = new CommonIniFile(Path.Combine(current, $"Mac{i}.ini"));
                    commonIniFile.WriteString("NetParam", "LocalIP", address.ToString());
                    commonIniFile.WriteString("NetParam", "DestIP", ComputerIPaddress);
                    commonIniFile.WriteString("NetParam", "DestPort", DestPort.ToString());
                    commonIniFile.WriteString("NetParam", "ChannelCount", ChannelCount.ToString());
                    commonIniFile.WriteString("WaveParam", "Type", WaveType.ToString());
                    commonIniFile.WriteString("WaveParam", "Frequency", Frequency.ToString());
                    commonIniFile.WriteString("WaveParam", "Amplitude", Amplitude.ToString());
                }
            }
        }

        public void ConfigDevice(string filename)
        {
            if (!string.IsNullOrEmpty(StartDeviceInfoLocalIP) && IPAddress.TryParse(StartDeviceInfoLocalIP, out IPAddress address))
            {
                CommonIniFile commonIniFile = new CommonIniFile(filename);
                string section = "DeviceInfo_6000";
                commonIniFile.WriteString(section, "DeviceCount", DeviceInfoMachineCount.ToString());
                for (int i = 0; i < DeviceInfoMachineCount; i++)
                {
                    if (i != 0)
                        address = GetNextIPAddress(address);

                    commonIniFile.WriteString(section, "DeviceIP"+i, address.ToString());
                }
            }
        }

        /// <summary>
        /// 从 IPAddress 获取下一条 IP 地址
        /// </summary>
        public static IPAddress GetNextIPAddress(IPAddress ipAddress)
        {
            byte[] bytes = ipAddress.GetAddressBytes();

            // 从最低位开始递增
            for (int i = bytes.Length - 1; i >= 0; i--)
            {
                if (bytes[i] < 255)
                {
                    bytes[i]++;
                    break;
                }
                else
                {
                    bytes[i] = 0;
                    if (i == 0)
                    {
                        // 如果所有字节都为255，无法递增
                        throw new ArgumentException("无法递增到下一个有效的 IP 地址");
                    }
                }
            }
            return new IPAddress(bytes);
        }
    }

    public class CommonIniFile
    {
        private readonly string FFileName;

        [DllImport("kernel32")]
        static extern int GetPrivateProfileInt(string lpAppName, string lpKeyName, int nDefault, string lpFileName);

        [DllImport("kernel32")]
        static extern int GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault, StringBuilder lpReturnedString, int nSize, string lpFileName);

        [DllImport("kernel32")]
        static extern int GetPrivateProfileSection(string lpAppName, byte[] lpszReturnBuffer, int nSize, string lpFileName);

        [DllImport("kernel32")]
        static extern int WritePrivateProfileString(string lpAppName, string lpKeyName, string lpValue, string lpFileName);

        public CommonIniFile(string filename)
        {
            FFileName = filename;
        }

        public int ReadInt(string section, string key, int def)
        {
            return GetPrivateProfileInt(section, key, def, FFileName);
        }

        public int WriteString(string section, string key, string strValue)
        {
            return WritePrivateProfileString(section, key, strValue, FFileName);
        }

        public string ReadString(string section, string key, string def)
        {
            var sb = new StringBuilder(1024);
            GetPrivateProfileString(section, key, def, sb, 1024, FFileName);
            string s = sb.ToString();
            return s;
        }

        public string[] ReadSection(string section, int nSize = 2048)
        {
            byte[] buffer = new byte[nSize];
            GetPrivateProfileSection(section, buffer, nSize, FFileName);
            string[] temp = Encoding.UTF8.GetString(buffer).Trim('\0').Split('\0');
            return temp;
        }
    }
}
