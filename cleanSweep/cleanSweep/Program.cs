using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net;

namespace cleanSweep
{
    class Program
    {
        static void Main(string[] args)
        {
            string targetDir = null;
            string safe = null;
            string tag = null;
            string cmd = null;
            
            if (args.Length == 0)
            {
                Console.WriteLine("Commandline args: -target:targetDir, -safe:safeDirFile");
                return;
            }

            //process cmd args
            foreach (string arg in args)
            {
                //Console.WriteLine(arg);
                if (arg[0] != '-')
                {
                    Console.WriteLine("Command line tags must begin with dash (-)");
                    return;
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

                    //Console.WriteLine(tag+"  "+cmd);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Command line arg format: -tag:arg\nAlso, use quotes for args with spaces");
                    Console.WriteLine(e);
                    return;
                }
                if (tag == "target")
                {
                    targetDir = cmd;
                }
                else if (tag == "safe")
                {
                    safe = cmd;
                }

                else
                {
                    Console.WriteLine("Unrecognized arg: " + tag);
                    return;
                }
            }
            
            //try to split up safe by commas
            List<string> safes = new List<string>();
            try
            {
                //string[] safes = new string[safe.Split(',').Length];
                foreach (string s in safe.Split(','))
                {
                    safes.Add(s);
                    //Console.WriteLine(s);
                }
            }
            catch
            {
                safes.Add(safe);
                //Console.WriteLine(safe);
            }

            string[] files = null;
            string[] dirs = null;
            try
            {
                //get a list of the current dir level files
                files = Directory.GetFiles(targetDir);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get targetDir contents: " + targetDir);
                return;
            }

            try
            {
                //get a list of the current dir level dirs
                dirs = Directory.GetDirectories(targetDir);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to get targetDir directory list: " + targetDir);
                return;
            }

            foreach (string dir in dirs)
            {
                if (safes.Contains(dir.Split('\\')[1]) == false)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                        Console.WriteLine("Removed dir :" + dir);
                    }
                    catch (Exception e)
                    {
                            Console.WriteLine("Unable to remove dir: " + dir);
                            Console.WriteLine(e);
                    }
                }
            }
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (safes.Contains(name) == false)
                { 
                    try
                    {
                        File.Delete(file);
                        Console.WriteLine("Removed file :" + file);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Unable to remove file: " + file);
                        Console.WriteLine(e);
                    }    
                }               
            }
        }
    }
}
