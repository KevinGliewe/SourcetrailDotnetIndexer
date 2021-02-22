﻿using CoatiSoftware.SourcetrailDB;
using SourcetrailDotnetIndexer.PdbSupport;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SourcetrailDotnetIndexer
{
    /// <summary>
    /// A visitor for references found in IL-code of a method
    /// </summary>
    class MethodReferenceVisitor
    {
        private readonly BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private readonly Assembly assembly;
        private readonly TypeHandler typeHandler;
        private readonly DataCollector dataCollector;
        private readonly PdbLocator pdbLocator;

        /// <summary>
        /// Invoked when a method needs to be parsed
        /// </summary>
        public EventHandler<CollectedMethodEventArgs> ParseMethod;

        public MethodReferenceVisitor(Assembly assembly,
                                      TypeHandler typeHandler,
                                      DataCollector dataCollector,
                                      PdbLocator pdbLocator)
        {
            this.assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            this.typeHandler = typeHandler ?? throw new ArgumentNullException(nameof(typeHandler));
            this.dataCollector = dataCollector ?? throw new ArgumentNullException(nameof(dataCollector));
            this.pdbLocator = pdbLocator ?? throw new ArgumentNullException(nameof(pdbLocator));
        }

        public void VisitMethodCall(MethodBase originatingMethod,
                                    int ilOffsetOfCall,
                                    MethodBase calledMethod,
                                    int referencingMethodId,
                                    int referencingClassId)
        {
            var targetClassId = 0;
            // do not collect members of foreign assemblies
            if (calledMethod.DeclaringType.Assembly == assembly)
            {
                targetClassId = typeHandler.AddToDbIfValid(calledMethod.DeclaringType);
            }
            // check, if this is an async method
            if (targetClassId == 0 && calledMethod.DeclaringType.IsAsyncStateMachineOf(originatingMethod, out var asyncWorker))
            {
                // dive into the async worker, as that is where the "meat" of the method is
                ParseMethod?.Invoke(this, new CollectedMethodEventArgs(new CollectedMethod(asyncWorker, referencingMethodId, referencingClassId)));
            }
            // collect method parameters
            // note that we just assume the parameters are used, e.g. the code may actually pass null as an argument
            // to get this right, we would have to look at the actual arguments, that get passed to the method
            // this is a bit more involved, maybe something for a future version...
            // (i think, i'll leave it this way, the value passed may be a parameter itself, so we can never be sure in these cases)
            foreach (var mParam in calledMethod.GetParameters())
            {
                var paramTypeId = typeHandler.AddToDbIfValid(mParam.ParameterType);
                if (paramTypeId > 0 && paramTypeId != referencingClassId)  // ignore self-references (e.g. when passing "this" as a parameter)
                {
                    dataCollector.CollectReference(referencingClassId, paramTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, paramTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfCall);
                }
            }
            if (targetClassId > 0)
            {
                if (referencingClassId != targetClassId)   // ignore self-references
                {
                    dataCollector.CollectReference(referencingClassId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfCall);
                }
                var targetMethodId = typeHandler.CollectMember(calledMethod, out var targetKind);
                if (targetMethodId > 0)
                {
                    var refId = dataCollector.CollectReference(referencingMethodId, targetMethodId,
                        targetKind == SymbolKind.SYMBOL_METHOD ? ReferenceKind.REFERENCE_CALL : ReferenceKind.REFERENCE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfCall);
                }
                // if target is an interface, link to implementors as well
                // (seems to be consistent with behavior of VS 2019 when looking at references)
                List<Type> implementors = new List<Type>();
                if (calledMethod.DeclaringType.IsInterface)
                    implementors.AddRange(typeHandler.GetInterfaceImplementors(calledMethod.DeclaringType));
                foreach (var implementor in implementors)
                {
                    var implTypeId = typeHandler.AddToDbIfValid(implementor);
                    if (implTypeId > 0)
                    {
                        dataCollector.CollectReference(referencingClassId, implTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        var refId = dataCollector.CollectReference(referencingMethodId, implTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        CollectReferenceLocation(originatingMethod, refId, ilOffsetOfCall);
                    }
                    foreach (var implMethod in implementor.GetMethods(flags))
                    {
                        // use correct overload
                        if (implMethod.Name == calledMethod.Name && implMethod.HasSameParameters(calledMethod))
                        {
                            var implMethodId = typeHandler.CollectMember(implMethod, out var targetImplKind);
                            var refId = dataCollector.CollectReference(referencingMethodId, implMethodId,
                                targetImplKind == SymbolKind.SYMBOL_METHOD ? ReferenceKind.REFERENCE_CALL : ReferenceKind.REFERENCE_USAGE);
                            CollectReferenceLocation(originatingMethod, refId, ilOffsetOfCall);
                            break;
                        }
                    }
                }
            }            
        }

        public void VisitFieldReference(MethodBase originatingMethod,
                                        int ilOffsetOfReference,
                                        FieldInfo referencedField,
                                        int referencingMethodId,
                                        int referencingClassId)
        {
            var targetClassId = typeHandler.AddToDbIfValid(referencedField.DeclaringType);
            if (targetClassId > 0)
            {
                if (referencingClassId != targetClassId)   // ignore self-references
                {
                    dataCollector.CollectReference(referencingClassId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                }
                var fieldId = typeHandler.CollectMember(referencedField, out _);
                if (fieldId > 0)
                {
                    //dataCollector.CollectReference(classId, fieldId, ReferenceKind.REFERENCE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, fieldId, ReferenceKind.REFERENCE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                }
                var fieldTypeId = typeHandler.AddToDbIfValid(referencedField.FieldType);
                if (fieldTypeId > 0)
                {
                    if (referencingClassId != fieldTypeId)
                    {
                        dataCollector.CollectReference(referencingClassId, fieldTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        var refId = dataCollector.CollectReference(referencingMethodId, fieldTypeId, ReferenceKind.REFERENCE_TYPE_USAGE);
                        CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                    }
                }
            }
        }

        public void VisitTypeReference(MethodBase originatingMethod,
                                       int ilOffsetOfReference,
                                       Type type,
                                       int referencingMethodId,
                                       int referencingClassId)
        {
            var targetClassId = typeHandler.AddToDbIfValid(type);
            if (targetClassId > 0)
            {
                if (referencingClassId != targetClassId)       // ignore self-references
                {
                    dataCollector.CollectReference(referencingClassId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                }
            }
        }

        public void VisitMethodReference(MethodBase originatingMethod,
                                         int ilOffsetOfReference,
                                         MethodBase referencedMethod,
                                         int referencingMethodId,
                                         int referencingClassId)
        {
            var targetClassId = 0;
            // do not collect members of foreign assemblies
            if (referencedMethod.DeclaringType.Assembly == assembly)
                targetClassId = typeHandler.AddToDbIfValid(referencedMethod.DeclaringType);
            if (targetClassId > 0)
            {
                if (referencingClassId != targetClassId)       // ignore self-references
                {
                    dataCollector.CollectReference(referencingClassId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    var refId = dataCollector.CollectReference(referencingMethodId, targetClassId, ReferenceKind.REFERENCE_TYPE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                }
                var targetMethodId = typeHandler.CollectMember(referencedMethod, out _);
                if (targetMethodId > 0)
                {
                    var refId = dataCollector.CollectReference(referencingMethodId, targetMethodId, ReferenceKind.REFERENCE_USAGE);
                    CollectReferenceLocation(originatingMethod, refId, ilOffsetOfReference);
                }
            }
            // attempt to detect inline lambdas (e.g. foo.Select(item => bar(item))
            // or lambda methods in a compiler-generated class
            // NOTE: not sure, if we should collect it here, as we have only a REFERENCE to a method, not a method-call
            // (do not collect for now)
            //if (referencedMethod.IsLambdaOf(originatingMethod))
            //{
            //    ParseMethod?.Invoke(this, new CollectedMethodEventArgs(new CollectedMethod(referencedMethod, methodId, classId)));
            //}
        }

        private void CollectReferenceLocation(MethodBase referencingMethod, int referenceId, int ilOffsetOfReference)
        {
            var pdbReader = pdbLocator.GetPdbReaderForAssembly(referencingMethod.DeclaringType.Assembly);
            if (pdbReader != null)
            {
                var pdbMethod = pdbReader.GetMethod(referencingMethod.MetadataToken);
                if (pdbMethod != null)
                {
                    var seq = pdbMethod.GetSequenceForILOffset(ilOffsetOfReference);
                    if (seq != null)
                    {
                        var fileId = dataCollector.CollectFile(pdbMethod.DocumentName, pdbMethod.LanguageName);
                        // had to subtract 1 from EndColumn, otherwise text disappears in Sourcetrail
                        DataCollector.CollectReferenceLocation(referenceId, fileId,
                            seq.StartLine, seq.StartColumn, seq.EndLine, seq.EndColumn - 1);
                    }
                }
            }
        }
    }
}
