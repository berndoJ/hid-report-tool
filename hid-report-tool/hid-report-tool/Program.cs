﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HidLibrary;
using System.IO;
using System.Threading;

namespace hid_report_tool
{
    public class Program
    {
        private static IEnumerable<HidDevice> dev_list;
        private static HidDevice selected_device = null;

        public static void Main(string[] args)
        {
            bool program_termination_flag = false;
            dev_list = HidDevices.Enumerate();
            Console.WriteLine("HID Report Tool v1.0\nCopyright (c) 2019 by Johannes Berndorfer.\n");
            while(!program_termination_flag)
            {
                Console.Write("HRT# ");
                string raw_cmd = Console.ReadLine();
                if (ExecCommand(raw_cmd) == 1)
                    program_termination_flag = true;
            }
        }

        public static int ExecCommand(string cmd)
        {
            List<string> cmds = cmd.Split(' ').ToList();
            switch (cmds[0])
            {
                case "device":
                    if (cmds.Count < 2)
                    {
                        Console.WriteLine("\"device\" expects at least 1 argument.");
                        break;
                    }
                    switch (cmds[1])
                    {
                        case "list":
                            // List all available HID devices.
                            dev_list = HidDevices.Enumerate();
                            Console.WriteLine($"Found {dev_list.Count()} HID devices:");
                            for (int i = 0; i < dev_list.Count(); i++)
                            {
                                HidDevice d = dev_list.ElementAt(i);
                                Console.WriteLine($"[{i}] {d.Description} [VID: 0x{d.Attributes.VendorId.ToString("X4").ToLower()}] [PID: 0x{d.Attributes.ProductId.ToString("X4").ToLower()}] [Connected: {d.IsConnected.ToString()}] [IsOpen: {d.IsOpen.ToString()}]");
                            }
                            break;
                        case "select":
                            // Select ID from list.
                            if (cmds.Count < 3)
                            {
                                Console.WriteLine("Syntax: device select <List ID>");
                                break;
                            }
                            uint id = 0;
                            if (!uint.TryParse(cmds[2], out id))
                            {
                                Console.WriteLine("Given argument is not a non-negative integer.");
                                break;
                            }
                            if (id >= dev_list.Count())
                            {
                                Console.WriteLine("This device does not exist. List all devices with \"device list\".");
                                break;
                            }
                            selected_device = dev_list.ElementAt((int)id);
                            Console.WriteLine($"Selected device {id}. [VID: 0x{selected_device.Attributes.VendorId.ToString("X4").ToLower()}] [PID: 0x{selected_device.Attributes.ProductId.ToString("X4").ToLower()}]");
                            break;
                        case "deselect":
                            if (selected_device == null)
                            {
                                Console.WriteLine("Nothing to deselect.");
                            }
                            else
                            {
                                selected_device = null;
                                Console.WriteLine("Deselected device.");
                            }
                            break;
                        default:
                            Console.WriteLine("Syntax: device <list|select|deselect> [...]");
                            break;
                    }
                    break;
                case "report":
                    if (cmds.Count() < 2)
                    {
                        Console.WriteLine("\"report\" expects at least 1 argument.");
                        break;
                    }
                    switch (cmds[1])
                    {
                        case "send":
                            if (selected_device == null)
                            {
                                Console.WriteLine("No device selected.");
                                break;
                            }

                            selected_device.OpenDevice();

                            byte report_id = 0;
                            byte[] report_data_raw;
                            byte[] report_data = new byte[selected_device.Capabilities.OutputReportByteLength - 1];

                            if (cmds.Count() < 4)
                            {
                                Console.WriteLine("Syntax: report send <report-id> <report-data>");
                                break;
                            }

                            if (!byte.TryParse(cmds[2], out report_id))
                            {
                                Console.WriteLine("The report id given is not a valid id.");
                                break;
                            }

                            try
                            {
                                report_data_raw = StringToByteArray(cmds[3].Replace("-", "").Replace(":", ""));
                            }
                            catch (Exception)
                            {
                                Console.WriteLine("Invalid report data.");
                                break;
                            }

                            for (int i = 0; i < selected_device.Capabilities.OutputReportByteLength - 1; i++)
                            {
                                if (i < report_data_raw.Length)
                                {
                                    report_data[i] = report_data_raw[i];
                                }
                            }

                            Console.WriteLine($"Sending report to the HID device... [ID: {report_id}] [Data Size: {report_data.Length} bytes]");
                            Console.WriteLine("Data: ");
                            bool initial_byte = true;
                            foreach (byte b in report_data)
                            {
                                if (!initial_byte)
                                {
                                    Console.Write("-");
                                }
                                else
                                {
                                    initial_byte = false;
                                }
                                Console.Write($"{b.ToString("X2").ToLower()}");
                            }
                            Console.WriteLine();

                            HidReport report = new HidReport(selected_device.Capabilities.OutputReportByteLength);
                            report.ReportId = report_id;
                            report.Data = report_data;
                            bool report_sent = selected_device.WriteReportSync(report);

                            selected_device.CloseDevice();

                            if (report_sent)
                            {
                                Console.WriteLine("HID report successfully sent.");
                            }
                            else
                            {
                                Console.WriteLine("An error occurred while sending the report.");
                            }
                            break;
                        case "listen":

                            break;
                        default:
                            Console.WriteLine("Syntax: report <send|listen> [...]");
                            break;
                    }
                    break;
                case "exec":
                    if (cmds.Count() < 2)
                    {
                        Console.WriteLine("\"exec\" expects at least 1 argument.");
                        break;
                    }
                    string execpath = cmds[1];
                    string fcontent = "";
                    bool cmd_echo = true;
                    bool cmd_delay = false;
                    if (File.Exists(execpath))
                    {
                        fcontent = File.ReadAllText(execpath);
                    }
                    else
                    {
                        Console.WriteLine("Couldn't find specified script file.");
                        return 0;
                    }
                    List<string> fc_lines = fcontent.Replace("\r", "").Split('\n').ToList<string>();
                    foreach (string l in fc_lines)
                    {
                        if (l.StartsWith("#") || l == "") // Comments
                            continue;

                        if (l.StartsWith("@"))
                        {
                            switch (l.Substring(1))
                            {
                                case "cmd-echo-off":
                                    cmd_echo = false;
                                    break;
                                case "cmd-delay":
                                    cmd_delay = true;
                                    break;
                                default:
                                    Console.WriteLine("[PARSING ERROR]: Couldn't recognise script line: " + l);
                                    break;
                            }
                        }
                        else
                        {
                            if (cmd_echo)
                            {
                                Console.WriteLine(">> " + l);
                            }
                            ExecCommand(l);
                            if (cmd_delay)
                            {
                                Thread.Sleep(50);
                            }
                        }
                    }
                    break;
                case "clear":
                    Console.Clear();
                    break;
                case "terminate":
                case "end":
                case "exit":
                    return 1;
                case "":
                    break;
                default:
                    Console.WriteLine("Invalid Command!");
                    break;
            }
            return 0;
        }

        private static byte[] StringToByteArray(string s)
        {
            return Enumerable.Range(0, s.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(s.Substring(x, 2), 16))
                .ToArray();
        }
    }
}
