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

namespace prep_hpc
{
    class Program
    {
       
        //eventhandler thread for HPC
        private static ManualResetEvent manualEvent = new ManualResetEvent(false);
        
        static void Main(string[] args)
        {

            Console.WriteLine("Utility to setup a sweep run\n");
            Console.WriteLine("-- sets up nodeDir (removes if existing)\n");
            Console.WriteLine("-- copies client utility to nodes\n");
            Console.WriteLine("-- makes a single (localMaster) copy of files on nodes\n");
            //required args 
            //path to runfiles
            string filePath = null;
            //root dir on slaves
            string nodeDir = null;
            //username 
            string userName = null;
            //password
            string password = null;
            
            //client
            string clientExe = "hpc_client_util.exe";
            
            //node file 
            string nodeFile = null;
            //cluster UNC name
            string clusterName = null;

            bool updateOnly = false;


            if (parse_cmd_args(args, ref filePath, ref nodeFile, ref nodeDir,
                               ref clusterName, ref userName, ref password,ref updateOnly) == false)
            {
                Console.WriteLine("parse cmd args fail...");
                Console.WriteLine("required commandline args: -filePath:path to folder with complete set of files");
                Console.WriteLine("                           -nodeDir:root dir on the compute nodes");
                Console.WriteLine("                           -userName: domain user name");
                //Console.WriteLine("                           -password: domain password");
                Console.WriteLine("\noptional (default)       -nodeFile:file with node UNC name(s) to use(all)");                
                Console.WriteLine("                              negative for numCores less than max ");
                Console.WriteLine("                           -clusterName:cluster UNC name (babeshn010)");
                return;
            }
            //set clusterName
            if (clusterName == null)
                clusterName = "IGSBABESHN010";

            //get files in filePath
            string[] dataFiles = null;
            try
            {
                dataFiles = Directory.GetFiles(filePath);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get file list for filePath:\n    " + filePath);
                Console.WriteLine(e);
                return;
            }
    
            //try to find a copy of the node-side client
            string clientPath = null;
            if (clientPath == null)
            {
                string[] localFiles = null;
                try
                {
                    localFiles = Directory.GetFiles(".\\");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to get local file list");
                    Console.WriteLine(e);
                    return;
                }
                foreach (string file in localFiles)
                {
                    if (Path.GetFileName(file) == clientExe)
                        clientPath = Path.GetFullPath(file);
                }
            }
            //if still not found, give up
            if (clientPath == null)
            {
                Console.WriteLine("could not find client in  local folder: "+clientExe);
                return;
            }
                        
            
            //
            //
            //**********************HPC portion*************************
            //
            //

            IScheduler scheduler = null;
            try
            {
                // Make the scheduler and connect to the local host.
                scheduler = new Scheduler();
                scheduler.Connect(clusterName);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to connect to cluster:\n   " + clusterName);
                Console.WriteLine(e);
                return;
            }

           
            List<string> clusterNodes = new List<string>();
            // Get all the nodes in the compute node group.

            try
            {
                clusterNodes = convert(scheduler.GetNodesInNodeGroup("ComputeNodes"));                   
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get cluster node list:\n   " + clusterName);
                Console.WriteLine(e);
                return;
            }
            
            //get the nodes in the nodeFile
            List<string> fileNodes = new List<string>();
            if (nodeFile != null)
            {

                try
                {
                    fileNodes = get_node_list(nodeFile);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to get node list from nodeFile:\n   " + nodeFile);
                    Console.WriteLine(e);                   
                    return;
                }
            }
            else
                fileNodes = clusterNodes;  

            //build requestedNodes
            List<string> requestedNodes = new List<string>();
            foreach (string fnode in fileNodes)
            {
                foreach (string cnode in clusterNodes)
                {
                    if (fnode.ToUpper() == cnode.ToUpper())
                    {
                        ISchedulerNode node = scheduler.OpenNodeByName(fnode);
                        if (node.Reachable == true)
                        {
                            requestedNodes.Add(fnode);
                            Console.WriteLine("compute node added: " + fnode);
                        }
                        else
                        {
                            Console.WriteLine("compute node not reachable: " + fnode);
                        }
                    
                    }
                }
            }
            if (requestedNodes.Count == 0)
            {
                Console.WriteLine("no usable compute nodes found");
                return;
            }
            
            //first remove existing node dir
            //
            bool success = true;
            string[] oneDirLevelUp = get_up_level_dir(nodeDir);                        
            string task = @"rmdir "+oneDirLevelUp[1]+" /S /Q";
            Console.WriteLine("removing (possibly) existing nodeDir");            
            success = submit_job(scheduler, task, oneDirLevelUp[0], requestedNodes,userName, password,true);
            if (success == false)
            {
                return;
            }
            
            //now make the dir
            //
            
            task = @"mkdir " + oneDirLevelUp[1];
            Console.WriteLine("making new nodeDir");
            success = submit_job(scheduler, task, oneDirLevelUp[0], requestedNodes, userName, password, true);
            if (success == false)
            {
                return;
            }

            //now copy client to slaves
            //
            string localHost = Environment.MachineName;
            string clientUnc = get_master_unc(localHost, clientPath);
            task = @"copy " + clientUnc;
            Console.WriteLine("Copying client to nodes");
            success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, true);
            if (success == false)
            {                
                return;
            }

            //now finally run client to make one localMaster copy
            //
            string masterUnc = get_master_unc(localHost, Path.GetFullPath(filePath));
            Console.WriteLine("starting client");

            task = clientExe+" -src:" + masterUnc + " ";
            task = task + " -n:0";
            success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, false);
            if (success == false)
            {
                return;
            }
            return;
       }

        private static string get_master_unc(string localHost, string masterDir)
        {
            //string localHost = Environment.MachineName;
            string[] split = masterDir.Split('\\');
            StringBuilder masterUnc = new StringBuilder(@"\\"+localHost);
            for (int i = 1; i < split.Length; i++)
            {
                masterUnc.Append(@"\" + split[i]);
            }
            return masterUnc.ToString();
        }

        private static bool submit_job(IScheduler scheduler, string taskName, string workDir, List<string> requestedNodes,
                                       string userName, string password,bool waitFlag)
        {
            try
            {
                ISchedulerJob job = null;
                ISchedulerTask task = null;
                Console.WriteLine("  working dir: " + workDir);
                Console.WriteLine("  task: " + taskName);
                foreach (string node in requestedNodes)
                {

                    job = scheduler.CreateJob();
                    job.UnitType = JobUnitType.Node;
                    job.IsExclusive = true;
                    
                    StringCollection nodes = new StringCollection() { node };
                    Console.WriteLine("    node: " + node);
                    job.RequestedNodes = nodes;
                    job.Name = node;
                    task = job.CreateTask();
                    task.CommandLine = taskName;
                    task.WorkDirectory = workDir;
                    task.StdOutFilePath = workDir + "\\output.txt";
                    task.StdErrFilePath = workDir + "\\error.txt";
                    job.AddTask(task);
                    scheduler.SubmitJob(job, userName, password);

                }

                //manualEvent.WaitOne();
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("unable to start job successfully: " + taskName);
                Console.WriteLine(e);
                return false;
            }

        }


        private static string[] get_up_level_dir(string nodeDir)
        {
            string[] split = nodeDir.Split('\\');
            StringBuilder up = new StringBuilder("");
            for (int i = 0; i < split.Length - 1; i++)
            {
                up.Append(split[i] + "\\");
            }
            string[] st = new string[2];
            st[0] = up.ToString();
            st[1] = split[split.Length-1];
            if (st[1] == "")
            {
                st[1] = split[split.Length - 2];
                st[0].Remove(st[0].Length - 1);
            }

            return st;
        }


        public static List<string> convert(IStringCollection collection)
        {
            List<string> list = new List<string>();
            foreach (string item in collection)
            {
                list.Add(item);
            }
            return list;
        }

       
       private static List<string> get_node_list(string nodeFile)
       {
           TextReader tr = new StreamReader(nodeFile);
           List<string> nodeList = new List<string>();
           string line = null;
           while ((line = tr.ReadLine()) != null)
           {
               //split on multiple whitespaces
               if (line[0] != '#')
               {
                   string[] raw = Regex.Split(line, @"\s+");
                   if (raw[0].Length > 0)
                   {
                       foreach (string r in raw)
                       {
                           nodeList.Add(r);

                       }
                   }
               }
           }
           tr.Close();
           return nodeList;
       }
       

        public static bool parse_cmd_args(string[] args, ref string filePath,
                                          ref string nodeFile, ref string nodeDir,
                                          ref string clusterName, ref string userName,
                                          ref string password, ref bool updateOnly)
        {
            string tag = null;
            string cmd = null;

            if (args.Length == 0)
            {
                Console.WriteLine("zero-length cmd arg array");

                return false;
            }

            foreach (string arg in args)
            {
                //Console.WriteLine(arg);
                if (arg[0] != '-')
                {
                    Console.WriteLine("Command line tags must begin with dash (-)  "+arg);
                    return false;
                }
                try
                {
                    if (arg.Split(':').Length == 2)
                    {
                        tag = arg.Split(':')[0].Substring(1);
                        cmd = arg.Split(':')[1];
                    }
                    else if (arg.Split(':').Length > 2)
                    {
                        tag = arg.Split(':')[0].Substring(1);
                        cmd = arg.Split(':')[1];
                        for (int i = 2; i < arg.Split(':').Length; i++)
                        {
                            cmd = cmd + ':' + arg.Split(':')[i];
                        }
                    }
                    else
                    {
                        tag = arg.Split(':')[0].Substring(1);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Command line arg format: -tag:arg\nAlso, use double quotes for args with spaces");
                    Console.WriteLine(e);
                    return false;
                }
                //if (tag == "filePath")
                if (String.Compare(tag,"filePath",true) == 0)
                {
                    filePath = cmd;
                }
                
                //else if (tag == "nodeFile")
                else if (String.Compare(tag, "nodeFile", true) == 0)
                {
                    nodeFile = cmd;
                }
                //else if (tag == "nodeDir")
                else if (String.Compare(tag, "nodeDir", true) == 0)
                {
                    nodeDir = cmd;
                }
                //else if (tag == "clusterName")
                else if (String.Compare(tag, "clusterName", true) == 0)
                {
                    clusterName = cmd;
                }
                //else if (tag == "userName")
                else if (String.Compare(tag, "userName", true) == 0)
                {
                    userName = cmd;
                }
                //else if (tag == "password")
                else if (String.Compare(tag, "password", true) == 0)
                {
                    password = cmd;
                }
                else if (String.Compare(tag, "updateOnly", true) == 0)
                {
                    updateOnly = true;
                }
                else
                {
                    Console.WriteLine("Unrecognized tag: " + tag);
                    return false;
                }
            }
            if (filePath == null)
            {
                Console.WriteLine("filePath not processed");
                return false;
            }
            if (nodeDir == null)
            {
                Console.WriteLine("nodeDir not processed");
                return false;
            }
            
            if (userName == null)
            {
                Console.WriteLine("userName not processed");
                return false;
            }
            if (password == null)
            {
                //Console.WriteLine("password not processed");
                Console.WriteLine("Enter Password:");
                password = Console.ReadLine();
                //return false;
            }
            return true;
        }


        private static void JobStateCallback(object sender, IJobStateEventArg args)
        {
            Console.WriteLine("Job state: {0}\n", args.NewState);
            
            if (JobState.Canceled == args.NewState ||
                JobState.Failed == args.NewState ||
                JobState.Finished == args.NewState)
            {
                manualEvent.Set();
            }
        }

    }
}
