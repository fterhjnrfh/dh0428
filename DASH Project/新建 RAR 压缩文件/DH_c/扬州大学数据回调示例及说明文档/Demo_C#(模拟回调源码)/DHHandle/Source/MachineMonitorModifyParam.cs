using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;

namespace DHHandle
{
    /// <summary>
    /// 参数相关
    /// </summary>
    public partial class MachineMonitor
    {
        public bool SaveMacParameter(string path)
        {
            int res = HardWare_StandardC.SaveMacParameter(path);
            Utility.WriteLog("导出参数文件结果  " + res + "  " + path);
            return res == 1;
        }

        public bool LoadMacParameter(string path)
        {
            int res = HardWare_StandardC.LoadMacParameter(path);
            Utility.WriteLog("导入参数文件结果  " + res + "  " + path);
            return res == 1;
        }

        private void LoadParamFile()
        {
            int res;
            if (Directory.Exists(Utility.ParamDir))
            {
                string paramFileName = GetParamFile(true);
                if (File.Exists(paramFileName))
                {
                    res = HardWare_StandardC.LoadMacParameter(Utility.ParamDir);
                    Utility.WriteLog("导入参数文件结果  " + res + "  " + paramFileName);
                }
                string balanceFileName = GetParamFile(false);
                if (File.Exists(balanceFileName))
                {
                    bool isSuccess = true;
                    XmlDocument doc = new XmlDocument();
                    try
                    {
                        doc.Load(balanceFileName);
                    }
                    catch
                    {
                        isSuccess = false;
                    }
                    if (isSuccess)
                    {
                        res = HardWare_StandardC.DownBalanceData(doc.InnerXml.Length, doc.InnerXml);
                        Utility.WriteLog("导入零点文件结果  " + res + " " + balanceFileName);
                    }
                }
            }
        }

        private string GetParamFile(bool bParamFile)
        {
            if (bParamFile)
            {
                return Path.Combine(Utility.ParamDir, "AllGroupChannel.xml");
            }
            else
            {
                string[] files = Directory.GetFiles(Utility.ParamDir, "*.blc");
                if (files != null && files.Length > 0)
                {
                    return files[0];
                }
            }
            return "";
        }

        public int GetChannelMeasureType(HardChannel channel)
        {
            int measureType = 0;
            try
            {
                HardWare_StandardC.GetChannelMeasureType(channel.m_nDeviceID, channel.m_strDeviceIP, channel.m_nChannelID, out measureType);
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            return measureType;
        }

        public List<string> GetParamSelectValue(HardChannel channel, int nParamShowID)
        {
            IntPtr intptr = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);

            string strParamValue = "";
            try
            {
                int size;
                HardWare_StandardC.GetMacChnParamListValue(channel.m_nDeviceID, channel.m_strDeviceIP, channel.m_nChannelID, nParamShowID, intptr.ToInt64(), HardWare_StandardC.StandardCapacity, out size);
                strParamValue = StringInfoHelper.GetStringFromInptr(intptr, size);
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            Marshal.FreeHGlobal(intptr);
            return StringInfoHelper.ParseString(strParamValue);
        }

        public string GetMacChnCurrentParam(HardChannel channel, int nParamShowID)
        {
            IntPtr intptr = Marshal.AllocHGlobal(HardWare_StandardC.StandardCapacity);

            string strParamValue = "";
            try
            {
                int size;
                HardWare_StandardC.GetMacChnCurrentParam(channel.m_nDeviceID, channel.m_nChannelID, channel.m_strDeviceIP, nParamShowID, intptr.ToInt64(), HardWare_StandardC.StandardCapacity, out size);
                strParamValue = StringInfoHelper.GetStringFromInptr(intptr, size);
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
            Marshal.FreeHGlobal(intptr);
            return strParamValue;
        }

        public void ModifyParamAndSendCode(HardChannel channel, int paramShowID, string strParamValue)
        {
            try
            {
                HardWare_StandardC.ModifyMacChnParam(channel.m_nDeviceID, channel.m_strDeviceIP, channel.m_nChannelID, paramShowID, strParamValue);
            }
            catch (System.Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
        }

        public void WriteIOChnOutputValue(HardChannel channel, int value)
        {
            try
            {
                HardWare_StandardC.WriteIOChnOutputValue(channel.m_nDeviceID, channel.m_strDeviceIP.Length, channel.m_strDeviceIP, channel.m_nChannelID, 0, value);
            }
            catch (Exception ex)
            {
                Utility.WriteLog(ex.Message + ex.StackTrace);
            }
        }
    }
}
