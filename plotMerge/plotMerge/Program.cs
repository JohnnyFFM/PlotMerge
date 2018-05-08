using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace plotMerge
{
    class Program
    {
        static void Main(string[] args)
        {
            //no arguments provided
            if (args.Length < 2)
            {
                System.Console.WriteLine("Plotmerge v.1.0");
                System.Console.WriteLine("Syntax: plotMerge source target [memlimit] \n");
                System.Console.WriteLine("source\t\t Optmized Plot File Source (can be POC1 or POC2)");
                System.Console.WriteLine("target\t\t Optmized Plot File Target (can be POC1 or POC2)\n");
                System.Console.WriteLine("memlimit\t optional memory limit in MB (default:1024)\n");
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
                    System.Console.WriteLine("Error: invalid input for memlimit! ");
                    return;
                }
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
            if (isOptimizedPOC1PlotFileName(source))
            {
                spoc2 = false;
            }
            else
            {
                if (isOptimizedPOC2PlotFileName(source))
                {
                    spoc2 = true;
                }
                else
                {
                    System.Console.WriteLine("ERROR: source file format not recognized!");
                    return;
                }

            }

             if (isOptimizedPOC1PlotFileName(target))
            {
                tpoc2 = false;
            }
            else
            {
                if (isOptimizedPOC2PlotFileName(target))
                {
                    tpoc2 = true;
                }
                else
                {
                    System.Console.WriteLine("ERROR: target file format not recognized!");
                    return;
                }

            }
            shuffle = (spoc2 != tpoc2);
            plotfile src = parsePlotFileName(source);
            plotfile tar = parsePlotFileName(target);

            System.Console.WriteLine("INFO: source Plot File: starting Nonce: " + src.start.ToString() + ", last Nonce: " + (src.start + src.nonces - 1).ToString() + ", #nonces: " + src.nonces.ToString() + ", POC" + (spoc2 ? "2" : "1"));
            System.Console.WriteLine("INFO: target Plot File: starting Nonce: " + tar.start.ToString() + ", last Nonce: " + (tar.start + tar.nonces - 1).ToString() + ", #nonces: " + tar.nonces.ToString() + ", POC" + (tpoc2 ? "2" : "1"));
            if(shuffle)System.Console.WriteLine("INFO: POC1POC2 Shuffling activated.");

            //check matching ID
            if (src.id != tar.id)
            {
                System.Console.WriteLine("ERROR: numeric ID of source and target file not matching!");
                return;
            }
            //check file existance and filesizes
            if (System.IO.File.Exists(source))
            {
                long length = new System.IO.FileInfo(source).Length;
                if (length != (long)src.nonces * (2<<17))
                {
                    System.Console.WriteLine("ERROR: actual source file size not matching file name implied size!");
                    return;
                }
            }
            else
            {
                System.Console.WriteLine("ERROR: soure file not found!");
                return;
            }

            bool prealloc = false; //if target doesnt exist, filespace needs to be preallocated

            if (System.IO.File.Exists(target))
            {
                long length = new System.IO.FileInfo(target).Length;
                if (length != (long)tar.nonces * (2 << 17))
                {
                    System.Console.WriteLine("ERROR: actual target file size not matching file name implied size!");
                    return;
                }
            }
            else
            {
                prealloc = true;
                System.Console.WriteLine("INFO: new target file will be created!");
            }

            //check inclusion
            if (src.start < tar.start || src.start+src.nonces-1 > tar.start + tar.nonces - 1){
                System.Console.WriteLine("ERROR: source file noncesnot part of target file nonces!");
                return;
            }

            //do it!
            // calc maximum nonces to read (limit)
            int limit = Convert.ToInt32(memlimit) * 8192;

            //allocate memory
            Scoop scoop1 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop
            Scoop scoop2 = new Scoop(Math.Min(src.nonces, limit));  //space needed for one partial scoop
            
            //Create and open Reader/Writer
            ScoopReadWriter scoopReadWriter;
            scoopReadWriter = new ScoopReadWriter(source, target);
            scoopReadWriter.Open();

            //preallocate
            if(prealloc)scoopReadWriter.PreAlloc(tar.nonces);

            //initialise stats
            DateTime start = DateTime.Now;
            TimeSpan elapsed;
            TimeSpan togo;

            //loop scoop pairs
            for (int y = 0; y < 2048; y++)
            {
                //Console.WriteLine("Processing Scoop Pairs " + (y + 1).ToString() + "/2048");
                //loop partial scoop
                for (int z = 0; z < src.nonces; z += limit)
                {
                    scoopReadWriter.ReadScoop(y, src.nonces, z, scoop1, Math.Min(src.nonces - z, limit));
                    scoopReadWriter.ReadScoop(4095 - y, src.nonces, z, scoop2, Math.Min(src.nonces - z, limit));
                    if(shuffle)Poc1poc2shuffle(scoop1, scoop2, Math.Min(src.nonces - z, limit));
                    scoopReadWriter.WriteScoop(4095 - y, tar.nonces, z + src.start - tar.start, scoop2, Math.Min(src.nonces - z, limit));
                    scoopReadWriter.WriteScoop(y, tar.nonces, z + src.start - tar.start, scoop1, Math.Min(src.nonces - z, limit));
                }
                //update status
                elapsed = DateTime.Now.Subtract(start);
                togo = TimeSpan.FromTicks(elapsed.Ticks / (y + 1) * (2048 - y - 1));
                string completed = Math.Round((double)(y + 1) / 2048 * 100).ToString() + "%";
                string speed1 = Math.Round(((long)src.nonces / 4096 * 2 * (y + 1) / elapsed.TotalMinutes)).ToString() + " nonces/m ";
                string speed2 = "("+(Math.Round((double)src.nonces / (2 << 12) * (y + 1) / elapsed.TotalSeconds)).ToString() + "MB/s)";
                string speed = speed1 + speed2;
                Console.Write("Completed: " + completed + ", Elapsed: " + timeSpanToString(elapsed) + ", Remaining: " + timeSpanToString(togo) + ", Speed: " + speed + "          \r");
            }
            // close reader/writer
            scoopReadWriter.Close();
        }

        private static string timeSpanToString(TimeSpan timeSpan)
        {
            if (timeSpan.ToString().LastIndexOf(".") > -1) {
                return timeSpan.ToString().Substring(0, timeSpan.ToString().LastIndexOf("."));
            } else {
                return timeSpan.ToString();
            }
        }

        private static bool isOptimizedPOC1PlotFileName(string filename)
        {
            Regex rgx = new Regex(@"(.)*\d+(_)\d+(_)\d+(_)\d+$");

            if (rgx.IsMatch(filename))
            {
                plotfile temp = parsePlotFileName(filename);
                return temp.stagger == temp.nonces ? true : false;
            }
            else
            {
                return false;
            }
        }

        private static bool isOptimizedPOC2PlotFileName(string filename)
        {
            Regex rgx = new Regex(@"(.)*\d+(_)\d+(_)\d+$");
            return rgx.IsMatch(filename);
        }

        private static plotfile parsePlotFileName(string name)
        {
            string[] temp = name.Split('\\');
            string[] pfn = temp[temp.GetLength(0) - 1].Split('_');
            plotfile result;
            result.id = Convert.ToUInt64(pfn[0]);
            result.start = Convert.ToInt64(pfn[1]);
            result.nonces = Convert.ToInt32(pfn[2]);
            if (pfn.Length > 3) { result.stagger = Convert.ToInt32(pfn[3]); } else { result.stagger = result.nonces; }
            return result;
        }

        //Convert Poc1>Poc2 and vice versa
        private static void Poc1poc2shuffle(Scoop scoop1, Scoop scoop2, int limit)
        {
            byte buffer;
            for (int i = 0; i < limit; i++)
            {
                for (int j = 32; j < 64; j++)
                {
                    buffer = scoop1.byteArrayField[64 * i + j];
                    scoop1.byteArrayField[64 * i + j] = scoop2.byteArrayField[64 * i + j];
                    scoop2.byteArrayField[64 * i + j] = buffer;
                }
            }
        }

        struct plotfile
        {
            public ulong id;
            public long start;
            public int nonces;
            public int stagger;
        }
    }
}
