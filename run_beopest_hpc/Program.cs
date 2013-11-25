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

namespace run_beopest_hpc
{
    class Program
    {
        //eventhandler thread for HPC
        private static ManualResetEvent manualEvent = new ManualResetEvent(false);
        
        static void Main(string[] args)
        {
            //required args 
            //path to runfiles
            string filePath = null;
            //root dir on slaves
            string nodeDir = null;
            //pest case
            string pestCase = null;
            //username 
            string userName = null;
            //password
            string password = null;
            
            //optional
            //master dir
            string masterDir = null;
            //node file 
            string nodeFile = null;
            //exec name
            string execName = null;
            //execArgs
            string execArgs = null;
            //client command
            string clientExe = null;
            //client command line
            string clientArgs = null;
            //cluster UNC name
            string clusterName = null;
            //number of cores to use on each node
            int numCores = -999;
            //port number
            int portNum = -999;
            //master start delay
            int delay = 0;
            //flag to potentially not starting a master
            bool masterFlag = true;
            //flag to use existing node-side slave dirs
            bool updateOnly = false;               
            //flag for debugging - pauses after each job is submitted
            bool jobWait = false;
            //flag to remove working dirs when finished
            bool remove = false;

            if (parse_cmd_args(args, ref filePath, ref masterDir, ref nodeFile, ref nodeDir,ref execName,
                               ref numCores, ref pestCase,ref portNum, ref clientExe, ref clientArgs,
                               ref clusterName, ref userName, ref password, ref delay, ref masterFlag, 
                               ref updateOnly, ref jobWait, ref remove) == false)
            {
                Console.WriteLine("parse cmd args fail...");
                Console.WriteLine("required commandline args: -filePath:path to folder with complete set of files");
                Console.WriteLine("                           -nodeDir:root dir on the compute nodes");
                Console.WriteLine("                           -pestCase: pest case name");
                Console.WriteLine("                           -userName: domain user name");                
                Console.WriteLine("\noptional (default)       -nodeFile:file with node UNC name(s) to use(all)");
                Console.WriteLine("                           -masterDir:directory to run for master(.\\master)");
                Console.WriteLine("                             if not passed existing, \".\\master\" is removed!");
                Console.WriteLine("                           -execName:executable name (\"beopest64.exe\")");
                Console.WriteLine("                           -numCores:number of cores per node (processor count)");
                Console.WriteLine("                             negative for numCores less than max ");
                Console.WriteLine("                           -portNum:TCP/IP port number (4004)");    
                Console.WriteLine("                           -clientExe:node side client (hpc_client_util.exe)");
                Console.WriteLine("                           -clientCmdLine:passed only if client passed");
                Console.WriteLine("                             use \" \" if clientCmdLine contains spaces"); 
                Console.WriteLine("                           -clusterName:cluster UNC name (babeshn010)");
                Console.WriteLine("                           -delay:time to wait after master start (0 seconds)");
                Console.WriteLine("                           -noMaster:if passed, no master started, only slaves");
                Console.WriteLine("                           -updateOnly:use existing node dirs and update newer files from headnode");
                return;
            }
            //set clusterName
            if (clusterName == null)
                clusterName = "IGSBABESHN010";
            
            //set execName
            if (execName == null)
                execName = "beopest64.exe"; 

            //set clientExe
            if (clientExe == null)
                clientExe = "hpc_client_util.exe";

            //set port number 
            if (portNum == -999)
                portNum = 4004;

            if (password == null)
            {
                Console.WriteLine("Enter network password:");
                password = Convert.ToString(Console.Read());
            }


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

            // make sure pestCase.pst exists and execCmd exists
            bool execFlag = false, pstFlag = false;
            foreach (string file in dataFiles)
            {
                if (Path.GetFileName(file) == execName)
                    execFlag = true;
                else if (Path.GetFileNameWithoutExtension(file) == pestCase && Path.GetExtension(file) == ".pst")
                    pstFlag = true;
            }
            if (execFlag == false)
            {
                Console.WriteLine("executable not found in file path folder:\n    " + execName);
                return;
            }
            if (pstFlag == false)
            {
                Console.WriteLine("pestCase.pst not found in file path folder:\n    " + pestCase);
                return;
            }

            //set numCores
            //if (numCores == -999)
            //{
            //    numCores = Environment.ProcessorCount;
            //}
            //else if (numCores < 0)
            //{
            //    numCores = Environment.ProcessorCount - numCores;
            //}

            //setup master dir           
            string currentDir = Directory.GetCurrentDirectory();
            bool newMaster = false;
            
            if ((masterFlag) && (masterDir == null))
            {
                //if a master is needed and the masterDir is null, make a new master
                masterDir = currentDir + @"\" + "master";
                newMaster = true;
            
                if (Directory.Exists(masterDir))
                {
                    Console.WriteLine("master dir already exists:\n    " + masterDir+"...removing...");
                    try
                    {
                        Directory.Delete(masterDir, true);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to remove default master dir:\n    " + masterDir);
                        Console.WriteLine(e);
                        return;
                    }              
                }
                try
                {
                    Directory.CreateDirectory(masterDir);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to create master dir:\n   " + masterDir);
                    Console.WriteLine(e);
                    return;
                }
            }
            //else
            //{
             
            //    if (Directory.Exists(masterDir) == false)
            //    {
            //        Console.WriteLine("Unable to find existing master dir:\n   "+masterDir);
            //        return;
            //    }
            //}
                
            //try to find a copy of the node-side client
            string clientPath = null;
            //first filePath files
            foreach (string file in dataFiles)
            {
                if (Path.GetFileName(file) == clientExe)
                    clientPath = Path.GetFullPath(file);

            }
            //next look in the current dir
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
                Console.WriteLine("could not find client in filePath folder\n or local folder: "+clientExe);
                return;
            }
            
            //copy files to master
            if (newMaster == true)
            {
                try
                {
                    copy_folder(filePath, masterDir);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to copy files to master dir:\n   " + masterDir);
                    Console.WriteLine(e);
                    return;
                }
            }

            //start beopest master
            Process master = new Process();
            if (masterFlag)
            {
                string masterCmd = " " + pestCase + " /h  :" + portNum;                
                try
                {
                    master = run_wait(masterDir, execName, masterCmd, delay);
                    Console.WriteLine(master.Id);

                }
                catch (Exception e)
                {

                    Console.WriteLine("Unable to start master successfully,\n  adding full path to masterDir and retrying");
                    Console.WriteLine(e);
                    masterDir = currentDir + @"\" + masterDir;
                    try
                    {
                        master = run_wait(masterDir, execName, masterCmd, delay);
                        Console.WriteLine(master.Id);
                    }
                    catch (Exception e2)
                    {
                        Console.WriteLine("Still unable to start master successfully");
                        Console.WriteLine(e2);

                        return;
                    }
                }
                Console.WriteLine("Master started successfully in " + masterDir);
            }
            else
            {
                Console.WriteLine("No master started.  Adding current path to masterDir");
                masterDir = currentDir + @"\" + filePath;
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
                scheduler.SetClusterParameter("AffinityType", "NoJobs");
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to connect to cluster:\n   " + clusterName);
                Console.WriteLine(e);
                master.Kill();
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
                master.Kill();
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
                    master.Kill();
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
                if (masterFlag) master.Kill();               
                return;
            }
            string localHost = Environment.MachineName;
            bool success = false;
            string task;
            string masterUnc = get_master_unc(localHost, masterDir);

            if (!updateOnly)
            {

                //first remove existing node dir
                //
                
                string[] oneDirLevelUp = get_up_level_dir(nodeDir);
                task = @"rmdir " + oneDirLevelUp[1] + " /S /Q";
                Console.WriteLine("removing (possibly) existing nodeDir");
                success = submit_job(scheduler, task, oneDirLevelUp[0], requestedNodes, userName, password, true,"-rmdir");
                if (success == false)
                {
                    if (masterFlag) master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }
                if (jobWait)
                {
                    Console.WriteLine("hit any key to continue");
                    Console.ReadKey();
                }


                //now make the dir
                //

                task = @"mkdir " + oneDirLevelUp[1];
                Console.WriteLine("making new nodeDir");
                success = submit_job(scheduler, task, oneDirLevelUp[0], requestedNodes, userName, password, true,"-mkdir");
                if (success == false)
                {
                    if (masterFlag) master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }
                if (jobWait)
                {
                    Console.WriteLine("hit any key to continue");
                    Console.ReadKey();
                }
                //now copy hpc_client_util to slaves
                //
                
                string clientUnc = get_master_unc(localHost, clientPath);
                task = @"copy " + clientUnc;
                Console.WriteLine("Copying client to nodes");
                success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, true,"-clientCopy");
                if (success == false)
                {
                    if (masterFlag) master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }
                if (jobWait)
                {
                    Console.WriteLine("hit any key to continue");
                    Console.ReadKey();
                }
                //start the slaves on each node
                //               
                Console.WriteLine("starting client util");

                if (execArgs == null)
                    execArgs = " " + pestCase + " /h " + localHost + ":" + portNum;

                task = clientExe + " -src:" + masterUnc;
                if (numCores != -999)
                {
                    task = task + " -n:" + numCores;
                }
                if (remove)
                {
                    task = task + "-rmDir";
                }
                   
                task = task + " -cmdExec:" + execName + " -cmdArgs:\"" + execArgs + "\"";                
                success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, false,"-run");
                if (success == false)
                {
                    master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }
                if (jobWait)
                {
                    Console.WriteLine("hit any key to continue");
                    Console.ReadKey();
                }
            }
            else
            {
                Console.WriteLine("using existing directory structure and hpc_client_util, updating files only...");                
                Console.WriteLine("starting client util to update localMaster");

                if (execArgs == null)
                    execArgs = " " + pestCase + " /h " + localHost + ":" + portNum;

                task = clientExe + " -src:" + masterUnc + " ";
                task = task + " -cmdExec:" + execName + " -cmdArgs:\"" + execArgs + "\"" + " -updateOnly -n:0";                
                success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, false,"-updateLocal");
                if (success == false)
                {
                    master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }

                Console.WriteLine("starting client util to update local node dirs and start slaves");
                if (execArgs == null)
                    execArgs = " " + pestCase + " /h " + localHost + ":" + portNum;

                task = clientExe;
                task = task + " -cmdExec:" + execName + " -cmdArgs:\"" + execArgs + "\"" + " -updateOnly";
                if (numCores != -999)
                {
                    task = task + " -n:" + numCores;
                }
                success = submit_job(scheduler, task, nodeDir, requestedNodes, userName, password, false,"-updateAndRun");
                if (success == false)
                {
                    master.Kill();
                    Console.WriteLine("Error execucting task: " + task);
                    return;
                }
            



            }

            
            
            while (true)
            {
                try
                {

                    if (master.HasExited)
                        break;
                }
                catch (Exception e)
                {
                    break;
                }
            
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
                                       string userName, string password,bool waitFlag,string taskSuffix="")
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
                    job.Name = node + taskSuffix;
                    task = job.CreateTask();
                    task.CommandLine = taskName;
                    task.WorkDirectory = workDir;
                    task.StdOutFilePath = workDir + "\\output.txt";
                    task.StdErrFilePath = workDir + "\\error.txt";
                    task.Name = node;
                    job.AddTask(task);
                    scheduler.SubmitJob(job, userName, password);

                }
                if (waitFlag)
                {
                    Console.WriteLine("Waiting for job to finish...");
                    while (job.State != JobState.Finished)
                    {
                        System.Threading.Thread.Sleep(5000);                        

                    }
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


       public static Process run_wait(string dir, string exe, string cmdLine, int delay)
       {
           StringBuilder p_output = new StringBuilder("");
       
           Console.WriteLine("Running exe: " + exe + " on dir :" + dir);
           Console.WriteLine("  with command line:"+cmdLine);

           //create a new process instance without a shell
           Process p = new Process();
           //p.StartInfo.UseShellExecute = false;

           //the name and path of the exec
           p.StartInfo.FileName = dir + @"\" + exe;

           //set the working dir of this process
           p.StartInfo.WorkingDirectory = dir;

           //redirect
           //p.StartInfo.UseShellExecute = false;
           //p.StartInfo.RedirectStandardInput = true;
           //p.StartInfo.RedirectStandardOutput = true;

           p.StartInfo.Arguments = cmdLine;

           //start
           p.Start();
           Thread.Sleep(delay * 1000);
           return p;
           
           /*
           StreamReader output = p.StandardOutput;           
           while (true)
           {
               string io_out = output.ReadLine();
               p_output.Append(io_out);
               
               if (Regex.IsMatch(p_output.ToString(), "Waiting for at least one slave to appear", RegexOptions.IgnoreCase))
               {
                   return p;
               }
               else if (p.HasExited)
               {
                   return new Process();
               }
           }
            */
       }


        public static void run_wait(string dir, string exe, string cmdLine,bool waitFlag)
        {
        
            Console.WriteLine("Running exe: " + exe + " on dir :" + dir);

            //create a new process instance without a shell
            Process p = new Process();
            p.StartInfo.UseShellExecute = false;

            //the name and path of the exec
            p.StartInfo.FileName = dir + @"\" + exe;

            //set the working dir of this process
            p.StartInfo.WorkingDirectory = dir;

            //redirect
            //p.StartInfo.UseShellExecute = false;
            //p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;

            p.StartInfo.Arguments = cmdLine;
            
            //start
            p.Start();

            //if waitFlg is true, then wait for beopest to startup before continuing
            StreamReader output = p.StandardOutput;
            if (waitFlag)
                p.WaitForExit();            

            return;
        }


        public static void copy_folder(string sourceFolder, string destFolder)
        {
            //if the destination folder doesn't exists, make it
            if (!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);

            //get a list of the current dir level files
            string[] files = Directory.GetFiles(sourceFolder);

            //loop over each file
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string dest = Path.Combine(destFolder, name);
                File.Copy(file, dest);
            }
            //get a list of the current dir level folders
            string[] folders = Directory.GetDirectories(sourceFolder);

            //loop over each folder
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                string dest = Path.Combine(destFolder, name);

                //recursively copy each file/folder
                copy_folder(folder, dest);
            }
        }


        public static bool parse_cmd_args(string[] args, ref string filePath,ref string masterDir,
                                          ref string nodeFile, ref string nodeDir,
                                          ref string execName,ref int numCores, 
                                          ref string pestCase,ref int portNum,
                                          ref string clientExe, ref string clientArgs,
                                          ref string clusterName, ref string userName,
                                          ref string password, ref int delay, 
                                          ref bool masterFlag, ref bool updateOnly,
                                          ref bool jobWait, ref bool remove)
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
                    Console.WriteLine("Command line tags must begin with dash (-)");
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
                    Console.WriteLine("Command line arg format: -tag:arg\nAlso, use quotes for args with spaces");
                    Console.WriteLine(e);
                    return false;
                }
                //if (tag == "filePath")
                if (String.Compare(tag,"filePath",true) == 0)
                {
                    filePath = cmd;
                }
                //else if (tag == "masterDir")
                else if (String.Compare(tag, "masterDir", true) == 0)
                {
                    masterDir = cmd;
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
                //else if (tag == "execName")
                else if (String.Compare(tag, "execName", true) == 0)
                {
                    execName = cmd;
                }
                //else if (tag == "pestCase")
                else if (String.Compare(tag, "pestCase", true) == 0)
                {
                    pestCase = cmd;
                }
                //else if (tag == "clientExe")
                else if (String.Compare(tag, "clientExe", true) == 0)
                {
                    clientExe = cmd;
                }
                //else if (tag == "clientArgs")
                else if (String.Compare(tag, "clientArgs", true) == 0)
                {
                    clientArgs = cmd;
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
                //else if (tag == "numCores")
                else if (String.Compare(tag, "numCores", true) == 0)
                {
                    try
                    {
                        numCores = Convert.ToInt32(cmd);
                    }
                    catch (Exception e)
                    {
                        if ((cmd == "h") || (cmd == "H"))
                        {
                            numCores = -100;
                        }
                        else
                        {
                            Console.WriteLine("Unable to parse numCores argument to int: " + cmd);
                            Console.WriteLine(e);
                            return false;
                        }
                    }
                }
                
                //else if (tag == "delay")
                else if (String.Compare(tag,"delay",true) == 0)
                {
                    try
                    {
                        delay = Convert.ToInt32(cmd);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to parse delay argument to int: " + cmd);
                        Console.WriteLine(e);
                        return false;
                    }
                }

                //else if (tag == "portNum")
                else if (String.Compare(tag, "portNum", true) == 0)
                {
                    try
                    {
                        portNum = Convert.ToInt32(cmd);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to parse portNum argument to int: " + cmd);
                        Console.WriteLine(e);
                        return false;
                    }
                }

                //else if (tag == "noMaster")
                else if (String.Compare(tag, "noMaster", true) == 0)
                {
                    masterFlag = false;
                }
                else if (String.Compare(tag, "updateOnly", true) == 0)
                {
                    updateOnly = true;
                }

                else if (String.Compare(tag, "jobWait", true) == 0)
                {
                    jobWait = true;
                }

                else if (String.Compare(tag, "remove", true) == 0)
                {
                    remove = true;
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
            if (pestCase == null)
            {
                Console.WriteLine("pestCase not processed");
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
                ConsoleKeyInfo key;
                do
                {
                    key = Console.ReadKey(true);
                    if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                    {
                        password += key.KeyChar;
                        Console.Write('*');
                    }
                    else
                    {
                        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                        {
                            password = password.Substring(0, (password.Length - 1));
                            Console.Write("\b \b");
                        }
                    }
                } while (key.Key != ConsoleKey.Enter);
                Console.Write("\n");
                
            }
            if ((clientExe != null) && (clientArgs == null))
            {
                Console.WriteLine("Client args must be passed if clientExe is passed");
                return false;
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
