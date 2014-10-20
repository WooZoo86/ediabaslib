﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ediabas;
using NDesk.Options;

namespace EdiabasCall
{
    class Program
    {
        [DllImport("api32.dll", EntryPoint = "__apiResultText")]
        private static extern bool __api32ResultText(uint handle, byte[] buf, string result, ushort set, string format);

        [DllImport("api32.dll", EntryPoint = "__apiResultChar")]
        private static extern bool __api32ResultChar(uint handle, out byte buf, string result, ushort set);

        private static readonly CultureInfo culture = CultureInfo.CreateSpecificCulture("en");
        private static Encoding encoding = Encoding.GetEncoding(1252);
        private static TextWriter outputWriter;
        private static bool compareOutput = false;
        private static uint apiHandle = 0;
        private static string lastJobInfo = string.Empty;
        private static int lastJobProgress = -1;

        static int Main(string[] args)
        {
            string cfgString = null;
            string sgbdFile = null;
            string outFile = null;
            bool appendFile = false;
            List<string> formatList = new List<string>();
            List<string> jobNames = new List<string>();
            bool show_help = false;

            var p = new OptionSet()
            {
                { "cfg=", "config string.",
                  v => cfgString = v },
                { "s|sgbd=", "sgbd file.",
                  v => sgbdFile = v },
                { "o|out=", "output file name.",
                  v => outFile = v },
                { "a|append", "append output file.",
                  v => appendFile = v != null },
                { "c|compare", "compare output.",
                  v => compareOutput = v != null },
                { "f|format=", "format for specific result. <result name>=<format string>",
                  v => formatList.Add(v) },
                { "j|job=", "<job name>#<job parameters semicolon separated>#<request results semicolon separated>#<standard job parameters semicolon separated>.\nFor binary job parameters perpend the hex string with| (e.g. |A3C2)",
                  v => jobNames.Add(v) },
                { "h|help",  "show this message and exit",
                  v => show_help = v != null },
            };

            List<string> extra;
            try
            {
                extra = p.Parse(args);
            }
            catch (OptionException e)
            {
                string thisName = Path.GetFileNameWithoutExtension(System.AppDomain.CurrentDomain.FriendlyName);
                Console.Write(thisName + ": ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `" + thisName + " --help' for more information.");
                return 1;
            }

            if (show_help)
            {
                ShowHelp(p);
                return 0;
            }

            if (string.IsNullOrEmpty(outFile))
            {
                outputWriter = Console.Out;
            }
            else
            {
                outputWriter = new StreamWriter(outFile, appendFile, encoding);
            }

            try
            {
                if (string.IsNullOrEmpty(sgbdFile))
                {
                    outputWriter.WriteLine("No sgbd file specified");
                    return 1;
                }

                if (jobNames.Count < 1)
                {
                    outputWriter.WriteLine("No jobs specified");
                    return 1;
                }

                string configString = "EcuPath=" + Path.GetDirectoryName(sgbdFile);
                if (!string.IsNullOrEmpty(cfgString))
                {
                    configString = cfgString;
                }
                if (!API.apiInitExt(string.Empty, string.Empty, string.Empty, configString))
                {
                    outputWriter.WriteLine("Init api failed");
                    return 1;
                }

                Type type = typeof(API);
                FieldInfo info = type.GetField("a", BindingFlags.NonPublic | BindingFlags.Static);
                if (info != null)
                {
                    object value = info.GetValue(null);
                    if (value.GetType() == typeof(uint))
                    {
                        apiHandle = (uint)value;
                    }
                }

                if (!string.IsNullOrEmpty(cfgString))
                {
                    API.apiSetConfig("EcuPath", Path.GetDirectoryName(sgbdFile));
                }

                string sgbdBaseFile = Path.GetFileNameWithoutExtension(sgbdFile);
                foreach (string jobString in jobNames)
                {
                    if (jobString.Length == 0)
                    {
                        outputWriter.WriteLine("Empty job string");
                        API.apiEnd();
                        return 1;
                    }

                    string[] parts = jobString.Split('#');
                    if ((parts.Length < 1) || (parts[0].Length == 0))
                    {
                        outputWriter.WriteLine("Empty job name");
                        API.apiEnd();
                        return 1;
                    }
                    string jobName = parts[0];
                    string jobArgs = null;
                    byte[] jobArgsData = null;
                    string jobResults = string.Empty;
                    byte[] jobArgsStdData = null;
                    if (parts.Length >= 2)
                    {
                        string argString = parts[1];
                        if (argString.Length > 0 && argString[0] == '|')
                        {   // binary data
                            jobArgsData = HexToByteArray(argString.Substring(1));
                        }
                        else
                        {
                            jobArgs = argString;
                            jobArgsData = encoding.GetBytes(argString);
                        }
                    }
                    if (parts.Length >= 3)
                    {
                        jobResults = parts[2];
                    }
                    if (parts.Length >= 4)
                    {
                        string argString = parts[3];
                        if (argString.Length > 0 && argString[0] == '|')
                        {   // binary data
                            jobArgsStdData = HexToByteArray(argString.Substring(1));
                        }
                        else
                        {
                            jobArgsStdData = encoding.GetBytes(argString);
                        }
                    }
                    outputWriter.WriteLine("JOB: " + jobName);

                    if (jobArgsStdData != null)
                    {
                        if (jobArgsData == null)
                        {
                            jobArgsData = new byte[0];
                        }
                        API.apiJobExt(sgbdBaseFile, jobName, jobArgsStdData, jobArgsStdData.Length, jobArgsData, jobArgsData.Length, jobResults, 0);
                    }
                    else if (jobArgs != null)
                    {
                        API.apiJob(sgbdBaseFile, jobName, jobArgs, jobResults);
                    }
                    else
                    {
                        if (jobArgsData == null)
                        {
                            jobArgsData = new byte[0];
                        }
#if false
                        // for test of large buffer handling buffer
                        byte[] buffer = new byte[API.APIMAXBINARY];
                        Array.Copy(jobArgsData, buffer, jobArgsData.Length);
                        API.apiJobData(sgbdBaseFile, jobName, buffer, jobArgsData.Length, jobResults);
#else
                        API.apiJobData(sgbdBaseFile, jobName, jobArgsData, jobArgsData.Length, jobResults);
#endif
                    }

                    lastJobInfo = string.Empty;
                    lastJobProgress = -1;
                    while (API.apiState() == API.APIBUSY)
                    {
                        PrintProgress();
                        Thread.Sleep(10);
                    }
                    if (API.apiState() == API.APIERROR)
                    {
                        outputWriter.WriteLine(string.Format(culture, "Error occured: 0x{0:X08} {1}", API.apiErrorCode(), API.apiErrorText()));
                        API.apiEnd();
                        return 1;
                    }
                    PrintProgress();
                    PrintResults(formatList);
                }

                API.apiEnd();
            }
            finally
            {
                outputWriter.Close();
            }

            return 0;
        }

        static void PrintProgress()
        {
            string jobInfo = string.Empty;
            int jobProgress = API.apiJobInfo(out jobInfo);
            if ((jobProgress != lastJobProgress) || (jobInfo != lastJobInfo))
            {
                string message = string.Empty;
                if (jobProgress >= 0)
                {
                    message += string.Format("{0,3}% ", jobProgress);
                }
                if (jobInfo.Length > 0)
                {
                    message += string.Format("'{0}'", jobInfo);
                }
                if (message.Length > 0)
                {
                    outputWriter.WriteLine("Progress: " + message);
                }
            }
            lastJobProgress = jobProgress;
            lastJobInfo = jobInfo;
        }

        static void PrintResults(List<string> formatList)
        {
            ushort resultSets;
            if (API.apiResultSets(out resultSets))
            {
                for (ushort set = 0; set <= resultSets; set++)
                {
                    if (API.apiErrorCode() != API.EDIABAS_ERR_NONE)
                    {
                        break;
                    }
                    outputWriter.WriteLine(string.Format(culture, "DATASET: {0}", set));
                    ushort results;
                    if (API.apiResultNumber(out results, set))
                    {
                        Dictionary<string, string> resultDict = new Dictionary<string,string>();
                        for (ushort result = 1; result <= results; result++)
                        {
                            if (API.apiErrorCode() != API.EDIABAS_ERR_NONE)
                            {
                                break;
                            }
                            string resultName;
                            if (API.apiResultName(out resultName, result, set))
                            {
                                string resultText = string.Empty;
                                int resultFormat;

                                if (API.apiResultFormat(out resultFormat, resultName, set))
                                {
                                    switch (resultFormat)
                                    {
                                        case API.APIFORMAT_CHAR:
                                            {
                                                if (apiHandle == 0)
                                                {
                                                    char resultChar;
                                                    if (API.apiResultChar(out resultChar, resultName, set))
                                                    {
                                                        resultText = string.Format(culture, "C: {0} 0x{0:X02}", (sbyte)resultChar);
                                                    }
                                                }
                                                else
                                                {
                                                    byte resultByte;
                                                    if (__api32ResultChar(apiHandle, out resultByte, resultName, set))
                                                    {
                                                        resultText = string.Format(culture, "C: {0} 0x{0:X02}", (sbyte)resultByte);
                                                    }
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_BYTE:
                                            {
                                                byte resultByte;
                                                if (API.apiResultByte(out resultByte, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "B: {0} 0x{0:X02}", resultByte);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_INTEGER:
                                            {
                                                short resultShort;
                                                if (API.apiResultInt(out resultShort, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "I: {0} 0x{0:X04}", resultShort);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_WORD:
                                            {
                                                ushort resultWord;
                                                if (API.apiResultWord(out resultWord, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "W: {0} 0x{0:X04}", resultWord);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_LONG:
                                            {
                                                int resultInt;
                                                if (API.apiResultLong(out resultInt, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "L: {0} 0x{0:X08}", resultInt);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_DWORD:
                                            {
                                                uint resultUint;
                                                if (API.apiResultDWord(out resultUint, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "D: {0} 0x{0:X08}", resultUint);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_REAL:
                                            {
                                                double resultDouble;
                                                if (API.apiResultReal(out resultDouble, resultName, set))
                                                {
                                                    resultText = string.Format(culture, "R: {0}", resultDouble);
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_TEXT:
                                            {
                                                if (apiHandle == 0)
                                                {
                                                    string resultString;
                                                    if (API.apiResultText(out resultString, resultName, set, ""))
                                                    {
                                                        resultText = resultString;
                                                    }
                                                }
                                                else
                                                {
                                                    byte[] dataBuffer = new byte[API.APIMAXTEXT];
                                                    if (__api32ResultText(apiHandle, dataBuffer, resultName, set, ""))
                                                    {
                                                        int length = Array.IndexOf(dataBuffer, (byte)0);
                                                        if (length < 0)
                                                        {
                                                            length = API.APIMAXTEXT;
                                                        }
                                                        resultText = encoding.GetString(dataBuffer, 0, length);
                                                    }
                                                }
                                                break;
                                            }

                                        case API.APIFORMAT_BINARY:
                                            {
                                                byte[] resultByteArray;
                                                uint resultLength;
                                                if (API.apiResultBinaryExt(out resultByteArray, out resultLength, API.APIMAXBINARYEXT, resultName, set))
                                                {
                                                    for (int i = 0; i < resultLength; i++)
                                                    {
                                                        resultText += string.Format(culture, "{0:X02} ", resultByteArray[i]);
                                                    }
                                                }
                                                break;
                                            }
                                    }
                                }

                                foreach (string format in formatList)
                                {
                                    string[] words = format.Split(new char[] { '=' }, 2);
                                    if (words.Length == 2)
                                    {
                                        if (string.Compare(words[0], resultName, StringComparison.OrdinalIgnoreCase) == 0)
                                        {
                                            if (apiHandle == 0)
                                            {
                                                string resultString;
                                                if (API.apiResultText(out resultString, resultName, set, words[1]))
                                                {
                                                    resultText += " F: " + resultString;
                                                }
                                            }
                                            else
                                            {
                                                byte[] dataBuffer = new byte[API.APIMAXTEXT];
                                                if (__api32ResultText(apiHandle, dataBuffer, resultName, set, words[1]))
                                                {
                                                    int length = Array.IndexOf(dataBuffer, (byte)0);
                                                    if (length < 0)
                                                    {
                                                        length = API.APIMAXTEXT;
                                                    }
                                                    resultText += " F: " + encoding.GetString(dataBuffer, 0, length);
                                                }
                                            }
                                        }
                                    }
                                }

                                if (!resultDict.ContainsKey(resultName))
                                {
                                    resultDict.Add(resultName, resultText);
                                }
                            }
                        }

                        foreach (string key in resultDict.Keys.OrderBy(x => x))
                        {
                            string resultText = resultDict[key];
                            outputWriter.WriteLine(key + ": " + resultText);
                        }
                    }
                }
                outputWriter.WriteLine();
            }
            if (API.apiErrorCode() != API.EDIABAS_ERR_NONE)
            {
                outputWriter.WriteLine(string.Format(culture, "Error occured: 0x{0:X08} {1}", API.apiErrorCode(), API.apiErrorText()));
            }
        }

        static byte[] HexToByteArray(string valueStr)
        {
            byte[] result;
            try
            {
                result = Enumerable.Range(0, valueStr.Length)
                 .Where(x => x % 2 == 0)
                 .Select(x => Convert.ToByte(valueStr.Substring(x, 2), 16))
                 .ToArray();
            }
            catch (Exception)
            {
                result = new byte[0];
            }

            return result;
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: " + Path.GetFileNameWithoutExtension(System.AppDomain.CurrentDomain.FriendlyName) + " [OPTIONS]");
            Console.WriteLine("EDIABAS call");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }
    }
}
