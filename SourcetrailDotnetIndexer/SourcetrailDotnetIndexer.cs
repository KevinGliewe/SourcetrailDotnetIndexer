﻿using SourcetrailDotnetIndexer.PdbSupport;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SourcetrailDotnetIndexer
{
    partial class SourcetrailDotnetIndexer
    {
        private readonly Assembly assembly;
        private readonly NamespaceFilter nameFilter;
        private readonly NamespaceFilter namespaceFollowFilter;
        private DataCollector dataCollector;
        private TypeHandler typeHandler;
        private MethodReferenceVisitor referenceVisitor;
        private ILParser ilParser;
        private PdbLocator pdbLocator;

        // list of methods that we have to analyze after collecting all types
        private readonly List<CollectedMethod> collectedMethods = new List<CollectedMethod>();

        public SourcetrailDotnetIndexer(Assembly assembly, NamespaceFilter nameFilter, NamespaceFilter namespaceFollowFilter)
        {
            this.assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            this.nameFilter = nameFilter ?? throw new ArgumentNullException(nameof(nameFilter));
            this.namespaceFollowFilter = namespaceFollowFilter ?? throw new ArgumentNullException(nameof(namespaceFollowFilter));
        }

        public void Index(string outputFileName)
        {
            // create the Sourcetrail data collector
            dataCollector = new DataCollector(outputFileName);

            pdbLocator = new PdbLocator();
            pdbLocator.AddAssembly(assembly);

            // set up the type handler
            typeHandler = new TypeHandler(assembly, nameFilter, namespaceFollowFilter, dataCollector, pdbLocator);
            typeHandler.MethodCollected += (sender, args) => collectedMethods.Add(args.CollectedMethod);

            // set up the visitor for parsed methods
            referenceVisitor = new MethodReferenceVisitor(typeHandler, dataCollector, pdbLocator);
            referenceVisitor.ParseMethod += (sender, args) => CollectReferencesFromILCode(
                args.CollectedMethod.Method, args.CollectedMethod.MethodId, args.CollectedMethod.ClassId);

            ilParser = new ILParser(referenceVisitor);

            try
            {
                Console.WriteLine("Collecting types...");
                // collect all types first
                foreach (var type in assembly.GetTypes())
                {
                    typeHandler.AddToDbIfValid(type);
                }
                Console.WriteLine("{1}Collected {0} types{1}", typeHandler.NumCollectedTypes, Environment.NewLine);
                // then parse IL of colected methods
                HandleCollectedMethods();
            }
            finally
            {
                dataCollector.Dispose();
            }
        }

        public void HandleCollectedMethods()
        {
            Console.WriteLine("Parsing IL... ({0} methods){1}", collectedMethods.Count, Environment.NewLine);
            // then dive into methods and collect, what they reference
            for (var i = 0; i < collectedMethods.Count; i++)
            {
                var method = collectedMethods[i];
                CollectReferencesFromILCode(method.Method, method.MethodId, method.ClassId);
            }
        }

        public void CollectReferencesFromILCode(MethodBase method, int methodId, int classId)
        {
            ilParser.Parse(method, methodId, classId);
        }
    }
}
