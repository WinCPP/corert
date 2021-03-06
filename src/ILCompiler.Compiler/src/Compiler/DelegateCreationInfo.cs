// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using Internal.IL;
using Internal.IL.Stubs;
using Internal.TypeSystem;
using ILCompiler.DependencyAnalysis;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler
{
    /// <summary>
    /// Captures information required to generate a ReadyToRun helper to create a delegate type instance
    /// pointing to a specific target method.
    /// </summary>
    public sealed class DelegateCreationInfo
    {
        /// <summary>
        /// Gets the node corresponding to the method that initializes the delegate.
        /// </summary>
        public IMethodNode Constructor
        {
            get;
        }

        /// <summary>
        /// Gets the node representing the target method of the delegate.
        /// </summary>
        public ISymbolNode Target
        {
            get;
        }

        /// <summary>
        /// Gets an optional node passed as an additional argument to the constructor.
        /// </summary>
        public IMethodNode Thunk
        {
            get;
        }

        private DelegateCreationInfo(IMethodNode constructor, ISymbolNode target, IMethodNode thunk = null)
        {
            Constructor = constructor;
            Target = target;
            Thunk = thunk;
        }

        /// <summary>
        /// Constructs a new instance of <see cref="DelegateCreationInfo"/> set up to construct a delegate of type
        /// '<paramref name="delegateType"/>' pointing to '<paramref name="targetMethod"/>'.
        /// </summary>
        public static DelegateCreationInfo Create(TypeDesc delegateType, MethodDesc targetMethod, NodeFactory factory)
        {
            TypeSystemContext context = delegateType.Context;
            DefType systemDelegate = context.GetWellKnownType(WellKnownType.MulticastDelegate).BaseType;

            int paramCountTargetMethod = targetMethod.Signature.Length;
            if (!targetMethod.Signature.IsStatic)
            {
                paramCountTargetMethod++;
            }

            DelegateInfo delegateInfo = context.GetDelegateInfo(delegateType.GetTypeDefinition());
            int paramCountDelegateClosed = delegateInfo.Signature.Length + 1;
            bool closed = false;
            if (paramCountDelegateClosed == paramCountTargetMethod)
            {
                closed = true;
            }
            else
            {
                Debug.Assert(paramCountDelegateClosed == paramCountTargetMethod + 1);
            }

            if (targetMethod.Signature.IsStatic)
            {
                MethodDesc invokeThunk;
                MethodDesc initMethod;

                if (!closed)
                {
                    // Open delegate to a static method
                    if (targetMethod.IsNativeCallable)
                    {
                        // If target method is native callable, create a reverse PInvoke delegate
                        initMethod = systemDelegate.GetKnownMethod("InitializeReversePInvokeThunk", null);
                        invokeThunk = delegateInfo.Thunks[DelegateThunkKind.ReversePinvokeThunk];

                        // You might hit this when the delegate is generic: you need to make the delegate non-generic.
                        // If the code works on Project N, it's because the delegate is used in connection with
                        // AddrOf intrinsic (please validate that). We don't have the necessary AddrOf expansion in
                        // the codegen to make this work without actually constructing the delegate. You can't construct
                        // the delegate if it's generic, even on Project N.
                        // TODO: Make this throw something like "TypeSystemException.InvalidProgramException"?
                        Debug.Assert(invokeThunk != null, "Delegate with a non-native signature for a NativeCallable method");
                    }
                    else
                    {
                        initMethod = systemDelegate.GetKnownMethod("InitializeOpenStaticThunk", null);
                        invokeThunk = delegateInfo.Thunks[DelegateThunkKind.OpenStaticThunk];
                    }
                }
                else
                {
                    // Closed delegate to a static method (i.e. delegate to an extension method that locks the first parameter)
                    invokeThunk = delegateInfo.Thunks[DelegateThunkKind.ClosedStaticThunk];
                    initMethod = systemDelegate.GetKnownMethod("InitializeClosedStaticThunk", null);
                }

                var instantiatedDelegateType = delegateType as InstantiatedType;
                if (instantiatedDelegateType != null)
                    invokeThunk = context.GetMethodForInstantiatedType(invokeThunk, instantiatedDelegateType);

                return new DelegateCreationInfo(
                    factory.MethodEntrypoint(initMethod),
                    factory.MethodEntrypoint(targetMethod),
                    factory.MethodEntrypoint(invokeThunk));
            }
            else
            {
                if (!closed)
                    throw new NotImplementedException("Open instance delegates");

                bool useUnboxingStub = targetMethod.OwningType.IsValueType;

                IMethodNode targetMethodNode;
                string initializeMethodName = "InitializeClosedInstance";
                MethodDesc targetCanonMethod = targetMethod.GetCanonMethodTarget(CanonicalFormKind.Specific);
                if (targetMethod.HasInstantiation)
                {
                    Debug.Assert(!targetMethod.IsVirtual, "TODO: delegate to generic virtual method");

                    if (targetMethod != targetCanonMethod)
                    {
                        // Closed delegates to generic instance methods need to be constructed through a slow helper that
                        // checks for the fat function pointer case (function pointer + instantiation argument in a single
                        // pointer) and injects an invocation thunk to unwrap the fat function pointer as part of
                        // the invocation if necessary.
                        initializeMethodName = "InitializeClosedInstanceSlow";
                        targetMethodNode = factory.FatFunctionPointer(targetMethod, useUnboxingStub);
                    }
                    else
                    {
                        targetMethodNode = factory.MethodEntrypoint(targetMethod, useUnboxingStub);
                    }
                }
                else
                {
                    // If the method can be canonicalized, point to the canon method body, but track the dependencies.
                    if (targetMethod != targetCanonMethod)
                    {
                        targetMethodNode = factory.ShadowConcreteMethod(targetMethod, useUnboxingStub);
                    }
                    else
                    {
                        targetMethodNode = factory.MethodEntrypoint(targetMethod, useUnboxingStub);
                    }
                }

                return new DelegateCreationInfo(
                    factory.MethodEntrypoint(systemDelegate.GetKnownMethod(initializeMethodName, null)),
                    targetMethodNode);
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as DelegateCreationInfo;
            return other != null && Constructor == other.Constructor
                && Target == other.Target && Thunk == other.Thunk;
        }

        public override int GetHashCode()
        {
            return Constructor.GetHashCode() ^ Target.GetHashCode();
        }
    }
}
