using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.IO.Compression;
using Microsoft.Hpc.Scheduler;
using Microsoft.Hpc.Scheduler.Properties;


namespace get_cluster_info
{
    class Program
    {
        static void Main(string[] args)
        {
            string clusterName = null;

            try
            {
                clusterName = args[0];
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get clusterName from commandline args");
                Console.WriteLine(e);
                return;
            }


            IScheduler scheduler = null;
            try
            {
                scheduler = new Scheduler();
                scheduler.Connect(clusterName);                
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to connect to cluster:\n   " + clusterName);
                Console.WriteLine(e);                
                return;
            }
            IStringCollection clusterNodes;// = new IStringCollection();
            try
            {
                clusterNodes = scheduler.GetNodesInNodeGroup("ComputeNodes");
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get cluster node list:\n   " + clusterName);
                Console.WriteLine(e);                
                return;
            }
            foreach(string nodename in clusterNodes)
            {
                Console.WriteLine("found node: " + nodename);
            }

        }
    }
}
