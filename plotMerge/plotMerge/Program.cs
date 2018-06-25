using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace plotMerge
{
    class Program
    {
        static AutoResetEvent[] autoEvents;
        static ScoopReadWriter scoopReadWriter1;
        static ScoopReadWriter scoopReadWriter2;
        static Boolean ddio = false;
        static Boolean halt = false;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetDiskFreeSpace(string lpRootPathName, out int lpSectorsPerCluster, out int lpBytesPerSector, out int lpNumberOfFreeClusters, out int lpTotalNumberOfClusters);

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(CurrentDomain_ProcessExit);
            //no arguments provided
            if (args.Length < 2)
            {
                System.Console.WriteLine("Plotmerge v.1.4");
                System.Console.WriteLine("Syntax: plotMerge source target [memlimit] \n");
                System.Console.WriteLine("source\t\t Optmized Plot File Source (can be POC1 or POC2)");
                System.Console.WriteLine("target\t\t Optmized Plot File Target (can be POC1 or POC2)\n");
                System.Console.WriteLine("memlimit\t optional memory limit in MB (default:1024, maximum:8192)\n");
                System.Console.WriteLine("Example:\t plotMerge XXXXXXXXXXXXXXXXXXXX_2000_1000_1000 XXXXXXXXXXXXXXXXXXXX_0_10000");
                System.Console.WriteLine("\t\t writes nonces 2000-2999 from source file to target file and converts to poc2 on-the-fly");
                return;
            }
            //read arguments
            String source = args[0];
            String target = args[1];
            int memlimit;
            if (args.Length == 3)
            {
                bool test = int.TryParse(args[2], out memlimit);
                if (test == false)
                {
                    Console.Error.WriteLine("ERR: invalid input for memlimit!");
                    return;
                }
                //enfore a limit slightly lower than maximum to avoid int overflows 
                memlimit = Math.Min(memlimit, 8184);
            }
            else
            {
                memlimit = 1024;
            }
            
            //check mergeability
            //plot file names valid, size of plotfiles valid, source file nonces part of target file nonces

            Boolean spoc2;  //source is POC2
            Boolean tpoc2;  //target is POC2
            Boolean shuffle; //Poc1Poc2 shuffling needed

            //check plot file names
            if (IsOptimizedPOC1PlotFileName(source))
            {
                spoc2 = false;
            }
            else
            {
                if (IsOptimizedPOC2PlotFileName(source))
                {
                    spoc2 = true;
                }
                else
                {
                    Console.Error.WriteLine("ERR: source file format not recognized!");
                    return;
                }

            }

             if (IsOptimizedPOC1PlotFileName(target))
            {
                tpoc2 = false;
            }
            else
            {
                if (IsOptimizedPOC2PlotFileName(target))
                {
                    tpoc2 = true;
                }
                else
                {
                    Console.Error.WriteLine("ERR: target file format not recognized!");
                    return;
                }

            }
            shuffle = (spoc2 != tpoc2);
            Plotfile src = ParsePlotFileName(source);
            Plotfile tar = ParsePlotFileName(target);

            System.Console.WriteLine("INFO: source Plot File: starting Nonce: " + src.start.ToString() + ", last Nonce: " + (src.start + (uint)src.nonces - 1).ToString() + ", #nonces: " + src.nonces.ToString() + ", POC" + (spoc2 ? "2" : "1"));
            System.Console.WriteLine("INFO: target Plot File: starting Nonce: " + tar.start.ToString() + ", last Nonce: " + (tar.start + (uint)tar.nonces - 1).ToString() + ", #nonces: " + tar.nonces.ToString() + ", POC" + (tpoc2 ? "2" : "1"));
            if(shuffle)System.Console.WriteLine("INFO: POC1POC2 Shuffling activated.");

            //check matching ID
            if (src.id != tar.id)
            {
                Console.Error.WriteLine("ERR: numeric ID of source and target file not matching!");
                return;
            }

            //check source file existance and filesizes
            if (System.IO.File.Exists(source))
            {
                long length = new System.IO.FileInfo(source).Length;
                if (length != (long)src.nonces * (2<<17))
                {
                    Console.Error.WriteLine("ERR: actual source file size not matching file name implied size!");
                    return;
                }
            }
            else
            {
                Console.Error.WriteLine("ERR: soure file not found!");
                return;
            }

            //if target file doesnt exist, filespace needs to be preallocated
            bool prealloc = false;
            if (System.IO.File.Exists(target))
            {
                long length = new System.IO.FileInfo(target).Length;
                if (length != (long)tar.nonces * (2 << 17))
                {
                    Console.Error.WriteLine("ERR: actual target file size not matching file name implied size!");
                    return;
                }
            }
            else
            {
                prealloc = true;
                System.Console.WriteLine("INFO: new target file will be created!");
            }

            //check inclusion
            if (src.start < tar.start || src.start+(uint)src.nonces-1 > tar.start + (uint)tar.nonces - 1){
                Console.Error.WriteLine("ERR: source file noncesnot part of target file nonces!");
                return;
            }

            //do it!
            // calc maximum nonces to read (limit)
            int limit = Convert.ToInt32(memlimit) * 4096;

            //allocate memory
            Scoop scoop1 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop
            Scoop scoop2 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop
            Scoop scoop3 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop
            Scoop scoop4 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop           

            //check if sectors and nonces are aligned for easy direct I/O
            Boolean dio = true; 
            int SectorsPerCluster;
            int BytesPerSectorA;
            int BytesPerSectorB;
            int NumberOfFreeClusters;
            int TotalNumberOfClusters;
            FileInfo file = new FileInfo(source);
            DriveInfo drive = new DriveInfo(file.Directory.Root.FullName);
            GetDiskFreeSpace(drive.Name, out SectorsPerCluster, out BytesPerSectorA, out NumberOfFreeClusters, out TotalNumberOfClusters);
            file = new FileInfo(target);
            drive = new DriveInfo(file.Directory.Root.FullName);
            GetDiskFreeSpace(drive.Name, out SectorsPerCluster, out BytesPerSectorB, out NumberOfFreeClusters, out TotalNumberOfClusters);

            if (ddio || (src.nonces % (BytesPerSectorA / 64) != 0) || (tar.nonces % (BytesPerSectorB / 64) != 0)){
                dio = false;
            }

            //create and open Reader/Writer
            scoopReadWriter1 = new ScoopReadWriter(source);
            scoopReadWriter2 = new ScoopReadWriter(target);
            if (!(scoopReadWriter1.OpenR(dio) && scoopReadWriter2.OpenW(dio))){
                return;
            };

            //preallocate disk space
            if (prealloc)
            {
                if (!scoopReadWriter2.PreAlloc(tar.nonces))
                {
                   return;
                }
            }

            //initialise stats
            DateTime start = DateTime.Now;
            TimeSpan elapsed;
            TimeSpan togo;

            //create masterplan     
            int loops = (int)Math.Ceiling((double)(src.nonces) / limit);
            TaskInfo[] masterplan = new TaskInfo[2048*loops];
            for (int y = 0; y < 2048; y++)
            {
                int zz = 0;
                //loop partial scoop               
                for (int z = 0; z < src.nonces; z += limit)
                {
                    masterplan[y*loops+zz] = new TaskInfo();
                    masterplan[y*loops+zz].reader = scoopReadWriter1;
                    masterplan[y*loops+zz].writer = scoopReadWriter2;
                    masterplan[y*loops+zz].y = y;
                    masterplan[y*loops+zz].z = z;
                    masterplan[y*loops+zz].x = y*loops+zz;
                    masterplan[y*loops+zz].limit = limit;
                    masterplan[y*loops+zz].src = src;
                    masterplan[y*loops+zz].tar = tar;
                    masterplan[y*loops+zz].scoop1 = scoop1;
                    masterplan[y*loops+zz].scoop2 = scoop2;
                    masterplan[y*loops+zz].scoop3 = scoop3;
                    masterplan[y*loops+zz].scoop4 = scoop4; 
                    masterplan[y*loops+zz].shuffle = shuffle;
                    masterplan[y*loops+zz].end = masterplan.LongLength;
                    zz += 1;
                }
            }

            //work masterplan
            //perform first read
            Th_read(masterplan[0]);

            autoEvents = new AutoResetEvent[]
            {
                new AutoResetEvent(false),
                new AutoResetEvent(false)
            };

            //perform reads and writes parallel
            for (long x = 1; x < masterplan.LongLength; x++)
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(Th_write), masterplan[x-1]);
                ThreadPool.QueueUserWorkItem(new WaitCallback(Th_read), masterplan[x]);
                WaitHandle.WaitAll(autoEvents);
                if (halt)
                {
                    Console.Error.WriteLine("ERR: Shutting down!");
                    return;
                }

                //update status
                elapsed = DateTime.Now.Subtract(start);
                togo = TimeSpan.FromTicks(elapsed.Ticks / (masterplan[x].y + 1) * (2048 - masterplan[x].y - 1));
                string completed = Math.Round((double)(masterplan[x].y + 1) / 2048 * 100).ToString() + "%";
                string speed1 = Math.Round((double)src.nonces / 4096 * 2 * (masterplan[x].y + 1) * 60 / (elapsed.TotalSeconds + 1)).ToString() + " nonces/m ";
                string speed2 = "(" + (Math.Round((double)src.nonces / (2 << 12) * (masterplan[x].y + 1) / (elapsed.TotalSeconds + 1))).ToString() + "MB/s)";
                string speed = speed1 + speed2;
                Console.Write("Completed: " + completed + ", Elapsed: " + TimeSpanToString(elapsed) + ", Remaining: " + TimeSpanToString(togo) + ", Speed: " + speed + "          \r");
            }
            //perform last write
            if (!halt) Th_write(masterplan[masterplan.LongLength-1]);
            if (halt)
            {
                Console.Error.WriteLine("ERR: Shutting down!");
                return;
            }

            // close reader/writer
            scoopReadWriter1.Close();
            scoopReadWriter2.Close();
            Console.Write("All done!");
        }

        public static void Th_read(object stateInfo)
        {
            TaskInfo ti = (TaskInfo)stateInfo;

                //determine cache cycle and front scoop back scoop cycle to alternate
                if (ti.x % 2 == 0)
                {
                if (!halt) halt = halt || !ti.reader.ReadScoop(ti.y, ti.src.nonces, ti.z, ti.scoop1, Math.Min(ti.src.nonces - ti.z, ti.limit));
                if (!halt) halt = halt || !ti.reader.ReadScoop(4095 - ti.y, ti.src.nonces, ti.z, ti.scoop2, Math.Min(ti.src.nonces - ti.z, ti.limit));
                    if (ti.shuffle) Poc1poc2shuffle(ti.scoop1, ti.scoop2, Math.Min(ti.src.nonces - ti.z, ti.limit));
                }
                else
                {
                if (!halt) halt = halt || !ti.reader.ReadScoop(4095 - ti.y, ti.src.nonces, ti.z, ti.scoop4, Math.Min(ti.src.nonces - ti.z, ti.limit));
                if (!halt) halt = halt || !ti.reader.ReadScoop(ti.y, ti.src.nonces, ti.z, ti.scoop3, Math.Min(ti.src.nonces - ti.z, ti.limit));
                    if (ti.shuffle) Poc1poc2shuffle(ti.scoop3, ti.scoop4, Math.Min(ti.src.nonces - ti.z, ti.limit));
                }
                if (ti.x != 0) autoEvents[0].Set();
        }

        public static void Th_write(object stateInfo)
        {
            TaskInfo ti = (TaskInfo)stateInfo;
            if (ti.x % 2 == 0)
                {
                    if (!halt) halt = halt || !ti.writer.WriteScoop(ti.y, ti.tar.nonces, ti.z + (long)(ti.src.start - ti.tar.start), ti.scoop1, Math.Min(ti.src.nonces - ti.z, ti.limit));
                    if (!halt) halt = halt || !ti.writer.WriteScoop(4095 - ti.y, ti.tar.nonces, ti.z + (long)(ti.src.start - ti.tar.start), ti.scoop2, Math.Min(ti.src.nonces - ti.z, ti.limit));
                }
                else
                {
                    if (!halt) halt = halt || !ti.writer.WriteScoop(4095 - ti.y, ti.tar.nonces, ti.z + (long)(ti.src.start - ti.tar.start), ti.scoop4, Math.Min(ti.src.nonces - ti.z, ti.limit));
                    if (!halt) halt = halt || !ti.writer.WriteScoop(ti.y, ti.tar.nonces, ti.z + (long)(ti.src.start - ti.tar.start), ti.scoop3, Math.Min(ti.src.nonces - ti.z, ti.limit)); 
                }
            if (ti.x != (ti.end - 1))
            {
                autoEvents[1].Set();
            }
        }

        struct TaskInfo
        {
            public ScoopReadWriter reader;
            public ScoopReadWriter writer;
            public int y;
            public int z;
            public int x;
            public int limit;
            public Plotfile src;
            public Plotfile tar;
            public Scoop scoop1;
            public Scoop scoop2;
            public Scoop scoop3;
            public Scoop scoop4;
            public bool shuffle;
            public long end;
        }

        //Pretty Print Timespan
        private static string TimeSpanToString(TimeSpan timeSpan)
        {
            if (timeSpan.ToString().LastIndexOf(".") > -1) {
                return timeSpan.ToString().Substring(0, timeSpan.ToString().LastIndexOf("."));
            } else {
                return timeSpan.ToString();
            }
        }

        //Check for PoC1 filename
        private static bool IsOptimizedPOC1PlotFileName(string filename)
        {
            Regex rgx = new Regex(@"(.)*\d+(_)\d+(_)\d+(_)\d+$");

            if (rgx.IsMatch(filename))
            {
                Plotfile temp = ParsePlotFileName(filename);
                return temp.stagger == temp.nonces ? true : false;
            }
            else
            {
                return false;
            }
        }

        //Check for PoC2 filename
        private static bool IsOptimizedPOC2PlotFileName(string filename)
        {
            Regex rgx = new Regex(@"(.)*\d+(_)\d+(_)\d+$");
            return rgx.IsMatch(filename);
        }

        //Parse PlotFileName
        private static Plotfile ParsePlotFileName(string name)
        {
            string[] temp = name.Split('\\');
            string[] pfn = temp[temp.GetLength(0) - 1].Split('_');
            Plotfile result;
            result.id = Convert.ToUInt64(pfn[0]);
            result.start = Convert.ToUInt64(pfn[1]);
            result.nonces = Convert.ToInt32(pfn[2]);
            if (pfn.Length > 3) { result.stagger = Convert.ToInt32(pfn[3]); } else { result.stagger = result.nonces; }
            return result;
        }

        //Convert Poc1>Poc2 and vice versa
        private static void Poc1poc2shuffle(Scoop scoop1, Scoop scoop2, int limit)
        {
            byte[] buffer = new byte[32];
            for (int i = 0; i < limit; i++)
            {
                Buffer.BlockCopy(scoop1.byteArrayField, 64 * i + 32, buffer, 0, 32);
                Buffer.BlockCopy(scoop2.byteArrayField, 64 * i + 32, scoop1.byteArrayField, 64 * i + 32, 32);
                Buffer.BlockCopy(buffer, 0, scoop2.byteArrayField, 64 * i + 32, 32);
            }
        }

        //Plotfile structure
        struct Plotfile
        {
            public ulong id;
            public ulong start;
            public int nonces;
            public int stagger;
        }

        //Cleanup & Exit
        static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            //Cleanup
            if (scoopReadWriter1 != null) scoopReadWriter1.Close();
            if (scoopReadWriter2 != null) scoopReadWriter2.Close();
            Console.WriteLine("End.");
        }

    }
}
