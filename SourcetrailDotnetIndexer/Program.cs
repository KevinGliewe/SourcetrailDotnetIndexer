﻿using SourcetrailDotnetIndexer.PdbSupport;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SourcetrailDotnetIndexer
{
    partial class Program
    {
        private static void TestPdb()
        {
            //using (var reader = new PdbReader(@"C:\Sourcen\SourcetrailDotnetIndexer\SourcetrailDotnetIndexer\bin\x64\Debug\SourcetrailDotnetIndexer.exe"))
            //{
            //    reader.Open();
            //}
            var locator = new PdbLocator();
            locator.AddAssembly(Assembly.GetExecutingAssembly());

        }

        static void Main(string[] args)
        {
            //TestPdb();
            //return;

            if (!ProcessCommandLine(args))
            {
                Usage();
                Environment.ExitCode = 1;
                return;
            }
            if (!File.Exists(startAssembly))
            {
                Console.WriteLine("Assembly no found: {0}", startAssembly);
                Environment.ExitCode = 1;
                return;
            }
            try
            {
                // outputPathAndFilename takes precedence if specified
                if (!string.IsNullOrWhiteSpace(outputPathAndFilename))
                    outputPath = Path.GetDirectoryName(outputPathAndFilename);

                if (!Directory.Exists(outputPath))
                    Directory.CreateDirectory(outputPath);

                var nameFilter = new NamespaceFilter(nameFilters);

                AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += CurrentDomain_AssemblyResolve;
                var assembly = Assembly.ReflectionOnlyLoadFrom(startAssembly);
                Console.WriteLine("Indexing assembly {0}{1}", startAssembly, Environment.NewLine);

                var sw = Stopwatch.StartNew();
                var indexer = new SourcetrailDotnetIndexer(assembly, nameFilter);

                var outFileName = string.IsNullOrWhiteSpace(outputPathAndFilename)
                    ? Path.ChangeExtension(Path.GetFileName(startAssembly), ".srctrldb")
                    : Path.GetFileName(outputPathAndFilename);
                indexer.Index(Path.Combine(outputPath, outFileName));
                
                sw.Stop();

                Console.WriteLine("{0}Sourcetrail database has been generated at {1}", 
                    Environment.NewLine, Path.Combine(outputPath, outFileName));
                Console.WriteLine("Time taken: {0}", sw.Elapsed);
            }
            catch (Exception ex)
            {
                Console.WriteLine("{0}An exception occurred:{0}{1}", Environment.NewLine, ex);
                Environment.ExitCode = 2;
            }

            // only useful if running from within VisualStudio
            if (waitAtEnd)
            {
                Console.WriteLine("{0}{0}Press Enter to exit", Environment.NewLine);
                Console.ReadLine();
            }
        }        
    }
}
