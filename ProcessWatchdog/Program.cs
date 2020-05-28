using SharpAdbClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;

namespace ProcessWatchdog
{
    //USER           PID  PPID     VSZ    RSS WCHAN            ADDR S NAME 
    class ProcInfo
    {
        public string User { get; set; }
        public int Pid { get; set; }
        public int Ppid { get; set; }
        public int Vsz { get; set; }
        public int Rss { get; set; }
        public int Wchan { get; set; }
        public string Addr { get; set; }
        public string S { get; set; }
        public string Name { get; set; }
    }

    class Program
    {
        static List<ProcInfo> ParsePSResponse(string response)
        {
            string[] lines = response.Split("\r\n");
            //root             1     0   20264   2896 0                   0 S init
            //USER           PID  PPID     VSZ    RSS WCHAN            ADDR S NAME 
            List<ProcInfo> procs = new List<ProcInfo>();
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
                            Ppid = int.Parse(parts[2]),
                            Vsz = int.Parse(parts[3]),
                            Rss = int.Parse(parts[4]),
                            Wchan = int.Parse(parts[5]),
                            Addr = parts[6],
                            S = parts[7],
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
            ConsoleOutputReceiver consoleOutput = new ConsoleOutputReceiver();

            client.ExecuteRemoteCommand("ps -A", devices[deviceNumber], consoleOutput);

            List<ProcInfo> procs = ParsePSResponse(consoleOutput.ToString()); 

            foreach(ProcInfo proc in procs)
            {
                Console.WriteLine($"\t{proc.Name}");
            }

            Console.Write("Process to monitor (name): ");
            string procName = Console.ReadLine();

            ProcInfo procToMonitor = procs.Where(n => n.Name == procName).FirstOrDefault(); 

            if(procToMonitor != null)
            {
                Console.WriteLine($"Watching {procToMonitor.Name} with PID {procToMonitor.Pid}..."); 
                for(; ;)
                {
                    consoleOutput = new ConsoleOutputReceiver();
                    client.ExecuteRemoteCommand("ps -A", devices[deviceNumber], consoleOutput);
                    procs = ParsePSResponse(consoleOutput.ToString());

                    if(procs.Any(n=>n.Pid == procToMonitor.Pid && n.Name == n.Name))
                    {
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Console.WriteLine("Broke! Dumping logs!");
                        break;
                    }
                }
            }

            consoleOutput = new ConsoleOutputReceiver();
            client.ExecuteRemoteCommand("logcat -d", devices[deviceNumber], consoleOutput);

            File.WriteAllText("logcat_dump.txt", consoleOutput.ToString()); 

            return; 
        }
    }
}
