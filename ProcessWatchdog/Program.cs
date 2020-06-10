using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using System.Text.RegularExpressions; 

namespace ProcessWatchdog
{
    //USER           PID  PPID     VSZ    RSS WCHAN            ADDR S NAME 
    class ProcInfo
    {
        public string User { get; set; }
        public int Pid { get; set; }
        public string Name { get; set; }
    }

    class Program
    {
        static List<ProcInfo> GetProcs(AdbClient client, DeviceData device)
        {
            ConsoleOutputReceiver consoleOutput = new ConsoleOutputReceiver();

            client.ExecuteRemoteCommand("ps", device, consoleOutput);

            string[] lines = consoleOutput.ToString().Split("\r\n");

            if(lines.Length < 10)
            {
                // some devices require -A
                consoleOutput = new ConsoleOutputReceiver();
                client.ExecuteRemoteCommand("ps -A", device, consoleOutput);
                lines = consoleOutput.ToString().Split("\r\n");
            }

            //root             1     0   20264   2896 0                   0 S init
            //USER           PID  PPID     VSZ    RSS WCHAN            ADDR S NAME 
            List<ProcInfo> procs = new List<ProcInfo>();
            string[] headers = lines[0].Split(' ').Where(n => n != "").ToArray();
            for (int c = 1; c < lines.Length; c++)
            {
                string[] parts = lines[c].Split(' ').Where(n => n != "").ToArray();
                if (parts.Length > 0)
                {
                    if (parts[0] != "system" && parts[0] != "root")
                    {
                        ProcInfo proc = new ProcInfo
                        {
                            User = parts[0],
                            Pid = int.Parse(parts[1]),
                            Name = parts[8],
                        };
                        procs.Add(proc);
                    }
                }
            }
            return procs;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Connecting to the ADB server. Please wait..");

            AdbServer server = new AdbServer();
            AdbServerStatus currentStatus = server.GetStatus(); 

            StartServerResult startStatus = server.StartServer(ConfigurationManager.AppSettings["AdbPath"], restartServerIfNewer: false);

            switch (startStatus)
            {
                case StartServerResult.AlreadyRunning:
                    Console.WriteLine("ADB daemon already running.");
                    break;
                case StartServerResult.RestartedOutdatedDaemon:
                    Console.WriteLine("Restarted outdated ADB daemon.");
                    break;
                case StartServerResult.Started:
                    Console.WriteLine("ADB daemon has been started.");
                    break; 
            }

            AdbClient client = new AdbClient();

            Console.WriteLine("Currently connected devices:");

            List<DeviceData> devices = client.GetDevices(); 
            for(int c=0;c<devices.Count;c++)
            {
                Console.WriteLine($"\t{c}: {devices[c].Name}"); 
            }

            Console.Write("Device to attach: ");
            int deviceNumber = int.Parse(Console.ReadLine());

            Console.WriteLine("Running processes: ");

            List<ProcInfo> procs = GetProcs(client, devices[deviceNumber]); 
            foreach (ProcInfo proc in procs)
            {
                Console.WriteLine($"\t{proc.Name}");
            }

            Console.Write("Process to monitor (name): ");
            string procName = Console.ReadLine();

            Console.Write("Keyword to search for: "); 
            string keyWord = Console.ReadLine(); 
            if(string.IsNullOrEmpty(keyWord))
            {
                keyWord = null; 
            }

            ProcInfo procToMonitor = procs.Where(n => n.Name == procName).FirstOrDefault(); 

            if(procToMonitor != null)
            {
                Console.WriteLine($"Watching {procToMonitor.Name} with PID {procToMonitor.Pid}...");
                DateTime lastLoggedAt = new DateTime(); 
                for(; ;)
                {
                    procs = GetProcs(client, devices[deviceNumber]);
                    if (procs.Any(n=>n.Pid == procToMonitor.Pid && n.Name == n.Name))
                    {
                        ConsoleOutputReceiver logcatInspect = new ConsoleOutputReceiver();
                        client.ExecuteRemoteCommand("logcat -d", devices[deviceNumber], logcatInspect);
                        string[] allLogs = logcatInspect.ToString().Split("\n"); 
                        foreach(string log in allLogs)
                        {
                            string dateTimeString = Regex.Match(log, @"\d{2}-\d{2} \d{1,2}:\d{1,2}:\d{1,2}.\d{1,3}").Value; 
                            if(!string.IsNullOrEmpty(dateTimeString))
                            {
                                DateTime loggedAt = DateTime.ParseExact(dateTimeString, "MM-dd HH:mm:ss.fff", null);
                                if (loggedAt > lastLoggedAt)
                                {
                                    if(keyWord != null && log.Contains(keyWord))
                                    {
                                        Console.WriteLine($"Keyword {keyWord} found: {log}"); 
                                    }
                                    lastLoggedAt = loggedAt; 
                                }
                            }
                        }
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Console.WriteLine("Broke! Dumping logs!");
                        break;
                    }
                }
            }

            ConsoleOutputReceiver consoleOutput = new ConsoleOutputReceiver();
            client.ExecuteRemoteCommand("logcat -d", devices[deviceNumber], consoleOutput);

            File.WriteAllText($"logcat_dump_{procToMonitor.Name}_{procToMonitor.Pid}.txt", consoleOutput.ToString()); 

            return; 
        }
    }
}
