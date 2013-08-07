using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net;
using System.Diagnostics.PerformanceData;

namespace hpc_client_util
{
    class Program
    {
        
       
        static void Main(string[] args)
        {
            Console.WriteLine("**********hpc_client_util v0.1**********");

            string srcFolderPath = null;
            string destinationPath = null;
            string localPath = null;
            string commandLineExec = null;
            string commandLineArgs = null;
            string slavePath = null;
            string tag = null;
            string cmd = null;
            int coreCount = -999;
            bool rmDir = false;
            bool updateOnly = false;
            bool tryAffinity = true;
            List<int> affinity = new List<int>()
            {
                Convert.ToInt32("0000000000000001", 2),
                Convert.ToInt32("0000000000000010", 2),
                Convert.ToInt32("0000000000000100", 2),                                   
                Convert.ToInt32("0000000000001000", 2),
                Convert.ToInt32("0000000000010000", 2),
                Convert.ToInt32("0000000000100000", 2),
                Convert.ToInt32("0000000001000000", 2),
                Convert.ToInt32("0000000010000000", 2),
                Convert.ToInt32("0000000100000000", 2),
                Convert.ToInt32("0000001000000000", 2),
                Convert.ToInt32("0000010000000000", 2),
                Convert.ToInt32("0000100000000000", 2),
                Convert.ToInt32("0001000000000000", 2),
                Convert.ToInt32("0010000000000000", 2),
                Convert.ToInt32("0100000000000000", 2),
                Convert.ToInt32("1000000000000000", 2),
            };


             if (parse_cmd_args(args, ref srcFolderPath, ref coreCount, ref commandLineArgs,
                                          ref commandLineExec, ref destinationPath, ref rmDir,
                                          ref slavePath, ref updateOnly, ref tryAffinity) == false)                                   
            {
                Console.WriteLine("parse cmd args fail...");
                Console.WriteLine("commandline args:    -cmdExec:command to execute (not passed = copy files only ");
                Console.WriteLine("                     -cmdArgs:commandline arguments to pass to cmdExec");
                Console.WriteLine("                     -src:sourceFolderPath (\".\\)");
                Console.WriteLine("                     -n:numLocalSlaves (num cores)");
                Console.WriteLine("                     -rmDir (true)\n");
                Console.WriteLine("                     -slavePath (.\\)");
                Console.WriteLine("                     -updateOnly (false)\n");
                Console.WriteLine("where           n = 0 makes only a local master copy");
                Console.WriteLine("                n < 0 num of cores less than total  \"n\" folders");
                Console.WriteLine("                rmDir removes the slav dir on completion (for sweeps");
                Console.WriteLine("                slavePath is the path to the working dir within slave folder");
                Console.WriteLine("                updateOnly only copies files that are newer than existing files");
                return;
            }

            
            //check core count
            if (coreCount == -999)
            {
                coreCount = Environment.ProcessorCount;
            }
            else if (coreCount == -100)
            {
                coreCount = Environment.ProcessorCount / 2;
            }
            else if (coreCount < 0)
            {
                coreCount = Environment.ProcessorCount + coreCount;
            }

            if (coreCount > Environment.ProcessorCount)
            {
                Console.WriteLine("Warning - coreCount greater than number of processors: " + coreCount);
            }


            if (coreCount <= 0)
            {
                Console.WriteLine("ERROR - coreCount less than 1: " + coreCount);
                return;
            }


            //some screen output
            if (commandLineExec == null)
            {
                Console.WriteLine("commandLineExec and CommandLineArgs not passed, copying files only");
            }
            else
            {

                Console.WriteLine("commandLineExec: " + commandLineExec);
                Console.WriteLine("commandLineArgs: " + commandLineArgs);
            }
            if (srcFolderPath == null)
            {
                Console.WriteLine("srcFolderPath not passed, attempting to use existing localMaster/slave datasets ");
            }
            else
            {
                Console.WriteLine("scrFolderPath: " + srcFolderPath);

            }

            if (destinationPath != null)
            {
                Console.WriteLine("destinationPath: " + destinationPath);
                Console.WriteLine("destinationPath passed - resetting n to 1");
            }


            Console.WriteLine("coreCount: " + coreCount);
            Console.WriteLine("Rmdir: " + rmDir);

            //get local path
            try
            {
                localPath = Directory.GetCurrentDirectory();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to derive local path\n");
                Console.WriteLine(e);
                return;
            }

            string localMasterPath = localPath + @"\localMaster";

            //create a local master dir and copy the dataset into it
            if ((coreCount >= 0) && (srcFolderPath != null))
            {
                // Get the file listing of the master directory
                string[] dataFiles = null;
                try
                {
                    dataFiles = Directory.GetFiles(srcFolderPath);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to get file list from src - check src path: " + srcFolderPath);
                    Console.WriteLine(e);
                    return;
                }

                //Check to see if local master exists 
                
                if ((!updateOnly) && (Directory.Exists(localMasterPath)))
                {
                    Directory.Delete(localMasterPath, true);
                    Console.WriteLine("Removing local master dir...");
                }

                // Make the local master folder.
                if (!Directory.Exists(localMasterPath))
                {
                    try
                    {
                        Directory.CreateDirectory(localMasterPath);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to create local datafile directory: " + localMasterPath);
                        Console.WriteLine(e);
                        return;
                    }
                }


                else
                {
                    if (!Directory.Exists(localMasterPath))
                    {
                        throw new DirectoryNotFoundException("localMasterPath " + localMasterPath + " not found for updating");
                    }
                }


                //Copy one set of datafiles to this node from master folder
                try
                {

                    copy_folder(srcFolderPath, localMasterPath,updateOnly);
                    Console.WriteLine("Successful Copy from master to local...");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to copy master to slave: " + localMasterPath);
                    Console.WriteLine(e);
                    return;
                }
            }
            srcFolderPath = localMasterPath;

            //if coreCount passed as 0, exit after making local master file setif (coreCount == 0)
            if (coreCount == 0)
            {
                Console.WriteLine("coreCount = 0, only making a local master copy of files");
                return;
            }
            else if (coreCount > 0)
            {
                if (destinationPath != null)
                {
                    coreCount = 1;
                }
            }

            else if (coreCount < 0)
            {
                if (destinationPath != null)
                {
                    coreCount = -1;
                }
                Console.WriteLine("coreCount < 0, using existing local");
                //coreCount *= -1;
            }


            //setup performance counters     
            //Console.WriteLine(PerformanceCounterCategory.GetCategories().ToString());
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            




            //loop over each core on this slave node
            bool success = true;
            int[] pIds = new int[Math.Abs(coreCount)];
            string[] slaveDirs = new string[Math.Abs(coreCount)];
            string currentCpu;
            string currentRam;
            string hostname = Environment.MachineName;
            for (int i = 1; i <= Math.Abs(coreCount); i++)
            {
                //write out the current system utilization
                currentCpu = cpuCounter.NextValue() + "%";
                currentRam = ramCounter.NextValue() + "Mb";
                Console.WriteLine("Slave " + i + " cpu usage: " + currentCpu + " ram usage: " + currentRam);

                //build current slave folder
                string thisSlaveDir = localPath + @"\" + hostname + "_" + i.ToString();

                if (destinationPath != null)
                {
                    thisSlaveDir = localPath + "\\" + destinationPath;
                }

                slaveDirs[i - 1] = thisSlaveDir;

                if ((srcFolderPath != null) || (destinationPath != null))
                {

                    //if current slave folder exists, remove it
                    if ((!updateOnly) && (Directory.Exists(thisSlaveDir)))
                    {
                        try
                        {
                            Directory.Delete(thisSlaveDir, true);
                            Console.WriteLine("Removed local slave dir: " + thisSlaveDir);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Unable to remove existing local slave dir: " + thisSlaveDir);
                            Console.WriteLine(e);
                            success = false;
                        }

                    }

                    //create a folder for current slave
                    if ((success == true) && (!Directory.Exists(thisSlaveDir)))
                    {
                        try
                        {
                            Directory.CreateDirectory(thisSlaveDir);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("***Unable to create to slave : \n" + thisSlaveDir);
                            Console.WriteLine(e);
                            success = false;
                        }
                    }

                    //if the folder was successfully created, copy files from local master into it
                    if (success == true)
                    {
                        try
                        {
                            Console.WriteLine("Copying from localMaster to " + thisSlaveDir);
                            copy_folder(localMasterPath, thisSlaveDir,updateOnly);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Unable to copy to slave dir: " + thisSlaveDir);
                            Console.WriteLine(e);
                            success = false;
                        }
                    }
                }

                //if files copied succesfully, try to run                               
                if ((success == true) && (commandLineExec != null))
                {
                    try
                    {
                        Console.WriteLine("Starting slave: " + thisSlaveDir);
                        Console.WriteLine("with commandLineExec: " + commandLineExec);
                        Console.WriteLine("and with commandLineArgs: " + commandLineArgs);

                        Process thisSlave = new Process();
                        //thisSlave.StartInfo.UseShellExecute = false;

                        if (slavePath != null)
                        {
                            thisSlaveDir = thisSlaveDir + @"\" + slavePath;
                        }


                        //the name and path of the beopest exec
                        thisSlave.StartInfo.FileName = thisSlaveDir + @"\" + commandLineExec;


                        //set the working dir of this slave process
                        thisSlave.StartInfo.WorkingDirectory = thisSlaveDir;

                        //make no window
                        //thisSlave.StartInfo.CreateNoWindow = true;

                        //set the cmdline args
                        thisSlave.StartInfo.Arguments = commandLineArgs;

                        //start as minimized
                        thisSlave.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;


                        //start the slave
                        thisSlave.Start();

                        //track pIds
                        pIds[i - 1] = thisSlave.Id;


                        //set processor affinity
                        if (tryAffinity)
                        {
                            try
                            {
                                thisSlave.ProcessorAffinity = (System.IntPtr)affinity[i];
                                Console.WriteLine("Current slave affinity to processor: {0}", thisSlave.ProcessorAffinity);
                            }
                            catch
                            {
                                Console.WriteLine("Unable to set affinity for slave: " + thisSlaveDir);
                            }
                        }
                        
                        

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to start slave successfully: " + thisSlaveDir);
                        Console.WriteLine(e);
                        return;
                    }
                }
            }

            if (commandLineExec == null)
            {
                return;
            }

            //if all slaves started successfully, then infintely loop
            while (success == true)
            {
                bool found = false;

                //sleep fot 5 sec
                Thread.Sleep(5000);
                for (int i = 1; i <= Math.Abs(coreCount); i++)
                {
                    foreach (Process p in Process.GetProcesses())
                    {
                        if (pIds[i - 1] == p.Id)
                        {
                            found = true;
                            break;
                        }
                    }
                    if ((found == false) && (rmDir == true))
                    {
                        try
                        {
                            Console.WriteLine("Removing slaveDir: " + slaveDirs[i - 1]);
                            Directory.Delete(slaveDirs[i - 1], true);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Unable to remove slaveDir: " + slaveDirs[i - 1]);
                        }
                    }
                }
                if (found == false)
                {
                    return;
                }
            }
        }

        public static void copy_folder(string sourceFolder, string destFolder, bool updateOnly)
        {
            //if the destination folder doesn't exists, make it
            if (!Directory.Exists(destFolder))
                //if (updateOnly) throw new FileNotFoundException("Cannot find dest folder "+destFolder+" to update files");
                Directory.CreateDirectory(destFolder);

            //get a list of the current dir level files
            string[] files = Directory.GetFiles(sourceFolder);


            //save some time, rather than create temporaries inside the loop
            DateTime src_crt, dest_crt, src_mod, dest_mod;
            DateTime src_max, dest_max;
            //loop over each file
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string dest = Path.Combine(destFolder, name);
                if (updateOnly)
                {
                    //check if the dest file exists
                    if (File.Exists(dest))
                    {
                        //get the source and dest file write attributes
                        src_crt = File.GetCreationTimeUtc(file);
                        src_mod = File.GetLastWriteTimeUtc(file);
                        if (src_mod > src_crt) src_max = src_mod;
                        else src_max = src_crt;

                        dest_crt = File.GetCreationTimeUtc(dest);
                        dest_mod = File.GetLastWriteTimeUtc(dest);
                        if (dest_mod > dest_crt) dest_max = dest_mod;
                        else dest_max = dest_crt;
                        if (src_max > dest_max)
                        {
                            File.Copy(file, dest, true);
                        }
                    }
                    else
                    {
                        File.Copy(file, dest);
                    }
                }
                else
                {
                    File.Copy(file, dest);
                }
            }
            //get a list of the current dir level folders
            string[] folders = Directory.GetDirectories(sourceFolder);

            //loop over each folder
            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);
                string dest = Path.Combine(destFolder, name);

                //recursively copy each file/folder
                copy_folder(folder, dest, updateOnly);
            }
        }
        public static bool parse_cmd_args(string[] args, ref string srcFolderPath, ref int coreCount, ref string commandLineArgs,
                                          ref string commandLineExec, ref string destinationPath, ref bool rmDir,
                                          ref string slavePath, ref bool updateOnly, ref bool tryAffinity)
        {
            string tag = null;
            string cmd = null;
            //process cmd args
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


                    //Console.WriteLine(tag+"  "+cmd);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Command line arg format: -tag:arg\nAlso, use quotes for args with spaces");
                    Console.WriteLine(e);
                    return false;
                }
                //if (tag == "src")
                if (String.Compare(tag,"src",true) == 0)
                {
                    srcFolderPath = cmd;
                }
                //else if (tag == "n")
                else if (String.Compare(tag,"n",true) == 0)
                {
                    coreCount = int.Parse(cmd);                 
                }
                //else if (tag == "cmdExec")
                else if (String.Compare(tag, "cmdExec", true) == 0)
                {
                    commandLineExec = cmd;

                }
                //else if (tag == "cmdArgs")
                else if (String.Compare(tag, "cmdArgs", true) == 0)
                {
                    commandLineArgs = cmd;
                }
                //else if (tag == "dest")
                else if (String.Compare(tag, "dest", true) == 0)
                {
                    destinationPath = cmd;
                }
                //else if (tag == "rmDir")
                else if (String.Compare(tag, "rmdir", true) == 0)
                {
                    rmDir = true;
                }
                else if (String.Compare(tag, "slavePath", true) == 0)
                {
                    slavePath = cmd;
                }
                else if (String.Compare(tag, "updateOnly", true) == 0)
                {
                    updateOnly = true;
                }
                else if (String.Compare(tag, "tryAffinity", true) == 0)
                {
                    tryAffinity = true;
                }

                else
                {
                    Console.WriteLine("Unrecognized arg: " + tag);
                    return false;
                }
            }
            return true;

        }
    }
}
        