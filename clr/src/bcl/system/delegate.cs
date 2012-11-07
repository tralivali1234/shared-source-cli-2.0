// ==++==
// 
//   
//    Copyright (c) 2006 Microsoft Corporation.  All rights reserved.
//   
//    The use and distribution terms for this software are contained in the file
//    named license.txt, which can be found in the root of this distribution.
//    By using this software in any fashion, you are agreeing to be bound by the
//    terms of this license.
//   
//    You must not remove this notice, or any other, from this software.
//   
// 
// ==--==
namespace System {

    using System;
    using System.Reflection;
    using System.Threading;
    using System.Runtime.Serialization;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    [Serializable()] 
    [ClassInterface(ClassInterfaceType.AutoDual)]
[System.Runtime.InteropServices.ComVisible(true)]
    public abstract class Delegate : ICloneable, ISerializable 
    {
        // _target is the object we will invoke on
        internal Object _target;

        // MethodBase, either cached after first request or assigned from a DynamicMethod
        internal MethodBase _methodBase;

        // _methodPtr is a pointer to the method we will invoke
        // It could be a small thunk if this is a static or UM call
        internal IntPtr _methodPtr;
        
        // In the case of a static method passed to a delegate, this field stores
        // whatever _methodPtr would have stored: and _methodPtr points to a
        // small thunk which removes the "this" pointer before going on
        // to _methodPtrAux.
        internal IntPtr _methodPtrAux;

        // This constructor is called from the class generated by the
        //  compiler generated code
        protected Delegate(Object target,String method)
        {
            if (target == null)
                throw new ArgumentNullException("target");
            
            if (method == null)
                throw new ArgumentNullException("method");
            
            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodName to
            // such and don't allow relaxed signature matching (which could make
            // the choice of target method ambiguous) for backwards
            // compatibility. The name matching was case sensitive and we
            // preserve that as well.
            if (!BindToMethodName(target, Type.GetTypeHandle(target), method,
                                  DelegateBindingFlags.InstanceMethodOnly |
                                  DelegateBindingFlags.ClosedDelegateOnly))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
        }
            
        // This constructor is called from a class to generate a 
        // delegate based upon a static method name and the Type object
        // for the class defining the method.
        protected unsafe Delegate(Type target,String method)
        {
            if (target == null)
                throw new ArgumentNullException("target");

            if (!(target is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "target");

            if (target.IsGenericType && target.ContainsGenericParameters) 
                throw new ArgumentException(Environment.GetResourceString("Arg_UnboundGenParam"), "target");

            if (method == null)
                throw new ArgumentNullException("method");

            // This API existed in v1/v1.1 and only expected to create open
            // static delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            // The name matching was case insensitive (no idea why this is
            // different from the constructor above) and we preserve that as
            // well.
            BindToMethodName(null, target.TypeHandle, method,
                             DelegateBindingFlags.StaticMethodOnly |
                             DelegateBindingFlags.OpenDelegateOnly |
                             DelegateBindingFlags.CaselessMatching);
        }
            
        // Protect the default constructor so you can't build a delegate
        private Delegate()
        {
        }

        public Object DynamicInvoke(params Object[] args)
        {
            return DynamicInvokeImpl(args);
        }

        protected virtual object DynamicInvokeImpl(object[] args) 
        {
            RuntimeMethodHandle method = new RuntimeMethodHandle(GetInvokeMethod());
            RuntimeTypeHandle delegateType = Type.GetTypeHandle(this);
            RuntimeMethodInfo invoke = (RuntimeMethodInfo)RuntimeType.GetMethodBase(delegateType, method);
            return invoke.Invoke(this, BindingFlags.Default, null, args, null, true);
        }

            
        public override bool Equals(Object obj)
        {                
            if (obj == null || !InternalEqualTypes(this, obj))
                return false;      

            Delegate d = (Delegate) obj;

            // do an optimistic check first. This is hopefully cheap enough to be worth
            if (_target == d._target && _methodPtr == d._methodPtr && _methodPtrAux == d._methodPtrAux) 
                return true;

            // even though the fields were not all equals the delegates may still match
            // When target carries the delegate itself the 2 targets (delegates) may be different instances
            // but the delegates are logically the same
            // It may also happen that the method pointer was not jitted when creating one delegate and jitted in the other 
            // if that's the case the delegates may still be equals but we need to make a more complicated check

            if (_methodPtrAux.IsNull())
            {
                if (!d._methodPtrAux.IsNull())
                    return false; // different delegate kind
                // they are both closed over the first arg
                if (_target != d._target)
                    return false;
                // fall through method handle check
            }
            else
            {
                if (d._methodPtrAux.IsNull())
                    return false; // different delegate kind

                // Ignore the target as it will be the delegate instance, though it may be a different one
                /*
                if (_methodPtr != d._methodPtr)
                    return false;
                    */

                if (_methodPtrAux == d._methodPtrAux) 
                    return true;
                // fall through method handle check
            }

            // method ptrs don't match, go down long path
            // 
            if (_methodBase == null || d._methodBase == null) 
                return FindMethodHandle().Equals(d.FindMethodHandle());
            else 
                return _methodBase.Equals(d._methodBase);

        }

        public override int GetHashCode()
        {
            //
            // this is not right in the face of a method being jitted in one delegate and not in another
            // in that case the delegate is the same and Equals will return true but GetHashCode returns a
            // different hashcode which is not true.
            /*
            if (_methodPtrAux.IsNull())
                return unchecked((int)((long)this._methodPtr));
            else
                return unchecked((int)((long)this._methodPtrAux));
            */
            return GetType().GetHashCode();
        }

        public static Delegate Combine(Delegate a, Delegate b)
        {
            // boundry conditions -- if either (or both) delegates is null
            //      return the other.
            if ((Object)a == null) // cast to object for a more efficient test
                return b;
            if ((Object)b == null) // cast to object for a more efficient test
                return a;
                    
            if (!InternalEqualTypes(a, b))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTypeMis"));
            
            return  a.CombineImpl(b);
        }
            
[System.Runtime.InteropServices.ComVisible(true)]
        public static Delegate Combine(params Delegate[] delegates)
        {
            if (delegates == null || delegates.Length == 0)
                return null;
                    
            Delegate d = delegates[0];
            for (int i = 1; i < delegates.Length; i++)
                d = Combine(d,delegates[i]);
            
            return d;
        }
                    
        public virtual Delegate[] GetInvocationList()
        {
            Delegate[] d = new Delegate[1];
            d[0] = this;
            return d;
        }
            
        // This routine will return the method
        public MethodInfo Method
        {                
            get 
            { 
                return GetMethodImpl();
            }
        }
            
        protected virtual MethodInfo GetMethodImpl()
        {
            if (_methodBase == null) 
            {
                RuntimeMethodHandle method = FindMethodHandle();
                RuntimeTypeHandle declaringType = method.GetDeclaringType();
                // need a proper declaring type instance method on a generic type
                if (declaringType.IsGenericTypeDefinition() || declaringType.HasInstantiation()) 
                {
                    bool isStatic = (method.GetAttributes() & MethodAttributes.Static) != (MethodAttributes)0;
                    if (!isStatic) 
                    {
                        if (_methodPtrAux == (IntPtr)0) 
                        {
                            declaringType = _target.GetType().TypeHandle; // may throw NullRefException if the this is null but that's ok
                        }
                        else
                        {
                            // it's an open one, need to fetch the first arg of the instantiation
                            MethodInfo invoke = this.GetType().GetMethod("Invoke");
                            declaringType = invoke.GetParameters()[0].ParameterType.TypeHandle;
                        }
                    }
                }
                _methodBase = (MethodInfo)RuntimeType.GetMethodBase(declaringType, method);
            }
            return (MethodInfo)_methodBase; 
        }
            
        public Object Target
        {
            get
            {
                return GetTarget();
            }
        }
    
    
        public static Delegate Remove(Delegate source, Delegate value)
        {
            if (source == null)
                return null;
            
            if (value == null)
                return source;
            
            if (!InternalEqualTypes(source, value))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTypeMis"));
            
            return source.RemoveImpl(value);
        }

        public static Delegate RemoveAll(Delegate source, Delegate value)
        {
            Delegate newDelegate = null;

            do
            { 
                newDelegate = source;
                source = Remove(source, value);
            }
            while (newDelegate != source);

            return newDelegate;
        }
            
        protected virtual Delegate CombineImpl(Delegate d)                                                           
        {
            throw new MulticastNotSupportedException(Environment.GetResourceString("Multicast_Combine"));
        }
            
        protected virtual Delegate RemoveImpl(Delegate d)
        {
            return (d.Equals(this)) ? null : this;
        }
    
    
        public virtual Object Clone()
        {
            return MemberwiseClone();
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, Object target, String method)
        {
            return CreateDelegate(type, target, method, false, true);
        }
        
        // V1 API.
        public static Delegate CreateDelegate(Type type, Object target, String method, bool ignoreCase)
        {
            return CreateDelegate(type, target, method, ignoreCase, true);
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, Object target, String method, bool ignoreCase, bool throwOnBindFailure)
        {
            if (type == null)
                throw new ArgumentNullException("type");
            if (!(type is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (target == null)
                throw new ArgumentNullException("target");
            if (method == null)
                throw new ArgumentNullException("method");
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            Delegate d = InternalAlloc(type.TypeHandle);
            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            // We never generate a closed over null delegate and this is
            // actually enforced via the check on target above, but we pass
            // NeverCloseOverNull anyway just for clarity.
            if (!d.BindToMethodName(target, Type.GetTypeHandle(target), method,
                                    DelegateBindingFlags.InstanceMethodOnly |
                                    DelegateBindingFlags.ClosedDelegateOnly |
                                    DelegateBindingFlags.NeverCloseOverNull |
                                    (ignoreCase ? DelegateBindingFlags.CaselessMatching : 0)))
            {
                if (throwOnBindFailure)
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            
            return d;
        }
        
        // V1 API.
        public static Delegate CreateDelegate(Type type, Type target, String method)
        {
            return CreateDelegate(type, target, method, false, true);
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, Type target, String method, bool ignoreCase)
        {
            return CreateDelegate(type, target, method, ignoreCase, true);
        }

        // V1 API.
        public static Delegate CreateDelegate(Type type, Type target, String method, bool ignoreCase, bool throwOnBindFailure)
        {
            if (type == null)
                    throw new ArgumentNullException("type");
            if (!(type is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (target == null)
                throw new ArgumentNullException("target");
            if (!(target is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "target");
            if (target.IsGenericType && target.ContainsGenericParameters) 
                throw new ArgumentException(Environment.GetResourceString("Arg_UnboundGenParam"), "target");
            if (method == null)
                    throw new ArgumentNullException("method");
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                    throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            Delegate d = InternalAlloc(type.TypeHandle);
            // This API existed in v1/v1.1 and only expected to create open
            // static delegates. Constrain the call to BindToMethodName to such
            // and don't allow relaxed signature matching (which could make the
            // choice of target method ambiguous) for backwards compatibility.
            if (!d.BindToMethodName(null, target.TypeHandle, method,
                                    DelegateBindingFlags.StaticMethodOnly |
                                    DelegateBindingFlags.OpenDelegateOnly |
                                    (ignoreCase ? DelegateBindingFlags.CaselessMatching : 0)))
            {
                if (throwOnBindFailure)
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            
            return d;
        }
            
        // V1 API.
        public static Delegate CreateDelegate(Type type, MethodInfo method)
        {
            return CreateDelegate(type, method, true);
        }

        // V1 API.
        public static Delegate CreateDelegate(Type type, MethodInfo method, bool throwOnBindFailure)
        {
            // Validate the parameters.
            if (type == null)
                    throw new ArgumentNullException("type");
            if (!(type is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (method == null)
                throw new ArgumentNullException("method");
            if (!(method is RuntimeMethodInfo))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeMethodInfo"), "method");
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // Initialize the delegate...
            Delegate d = InternalAlloc(type.TypeHandle);
            // This API existed in v1/v1.1 and only expected to create closed
            // instance delegates. Constrain the call to BindToMethodInfo to
            // open delegates only for backwards compatibility. But we'll allow
            // relaxed signature checking and open static delegates because
            // there's no ambiguity there (the caller would have to explicitly
            // pass us a static method or a method with a non-exact signature
            // and the only change in behavior from v1.1 there is that we won't
            // fail the call).
            if (!d.BindToMethodInfo(null, method.MethodHandle, method.DeclaringType.TypeHandle,
                                    DelegateBindingFlags.OpenDelegateOnly |
                                    DelegateBindingFlags.RelaxedSignature))
            {
                if (throwOnBindFailure) 
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            return d;
        }
        
        // V2 API.
        public static Delegate CreateDelegate(Type type, Object firstArgument, MethodInfo method)
        {
            return CreateDelegate(type, firstArgument, method, true);
        }

        // V2 API.
        public static Delegate CreateDelegate(Type type, Object firstArgument, MethodInfo method, bool throwOnBindFailure)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (!(type is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (method == null)
                throw new ArgumentNullException("method");
            if (!(method is RuntimeMethodInfo))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeMethodInfo"), "method");
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // Initialize the method...
            Delegate d = InternalAlloc(type.TypeHandle);
            if (!d.BindToMethodInfo(firstArgument, method.MethodHandle, method.DeclaringType.TypeHandle,
                                    DelegateBindingFlags.RelaxedSignature))
            {
                if (throwOnBindFailure) 
                    throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
                d = null;
            }
            return d;
        }
            
        public static bool operator ==(Delegate d1, Delegate d2)
        {
            if ((Object)d1 == null)
                return (Object)d2 == null;
            
            return d1.Equals(d2);
        }
        
        public static bool operator != (Delegate d1, Delegate d2)
        {
            if ((Object)d1 == null)
                return (Object)d2 != null;
            
            return !d1.Equals(d2);
        }
    
        //
        // Implementation of ISerializable
        //
        
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotSupportedException();
        }
        //
        // internal implementation details (FCALLS and utilities)
        //

        // V2 internal API.
        internal unsafe static Delegate CreateDelegate(Type type, Object target, RuntimeMethodHandle method)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (!(type is RuntimeType))
                throw new ArgumentException(Environment.GetResourceString("Argument_MustBeRuntimeType"), "type");
            if (method.IsNullHandle())
                throw new ArgumentNullException("method");
            
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // Initialize the method...
            Delegate d = InternalAlloc(type.TypeHandle);
            // This is a new internal API added in Whidbey. Currently it's only
            // used by the dynamic method code to generate a wrapper delegate.
            // Allow flexible binding options since the target method is
            // unambiguously provided to us.
            if (!d.BindToMethodInfo(target, method, method.GetDeclaringType(), DelegateBindingFlags.RelaxedSignature))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));
            return d;
        }

        // Caution: this method is intended for deserialization only, no security checks are performed.
        internal static Delegate InternalCreateDelegate(Type type, Object firstArgument, MethodInfo method)
        {
            // Validate the parameters.
            if (type == null)
                throw new ArgumentNullException("type");
            if (method == null)
                throw new ArgumentNullException("method");
            Type c = type.BaseType;
            if (c == null || c != typeof(MulticastDelegate))
                throw new ArgumentException(Environment.GetResourceString("Arg_MustBeDelegate"),"type");
            
            // Initialize the method...
            Delegate d = InternalAlloc(type.TypeHandle);
            // This API is used by the formatters when deserializing a delegate.
            // They pass us the specific target method (that was already the
            // target in a valid delegate) so we should bind with the most
            // relaxed rules available (the result will never be ambiguous, it
            // just increases the chance of success with minor (compatible)
            // signature changes). We explicitly skip security checks here --
            // we're not really constructing a delegate, we're cloning an
            // existing instance which already passed its checks.
            if (!d.BindToMethodInfo(firstArgument, method.MethodHandle, method.DeclaringType.TypeHandle,
                                    DelegateBindingFlags.SkipSecurityChecks |
                                    DelegateBindingFlags.RelaxedSignature))
                throw new ArgumentException(Environment.GetResourceString("Arg_DlgtTargMeth"));

            return d;
        }

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern bool BindToMethodName(Object target, RuntimeTypeHandle methodType, String method, DelegateBindingFlags flags);
            
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern bool BindToMethodInfo(Object target, RuntimeMethodHandle method, RuntimeTypeHandle methodType, DelegateBindingFlags flags);
            
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static MulticastDelegate InternalAlloc(RuntimeTypeHandle type);
            
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static MulticastDelegate InternalAllocLike(Delegate d);

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern static bool InternalEqualTypes(object a, object b);

        // Used by the ctor. Do not call directly.
        // The name of this function will appear in managed stacktraces as delegate constructor.
        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        private extern void DelegateConstruct(Object target, IntPtr slot);

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetMulticastInvoke();

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetInvokeMethod();

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern RuntimeMethodHandle FindMethodHandle(); 

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetUnmanagedCallSite();

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr AdjustTarget(Object target, IntPtr methodPtr);

        [MethodImplAttribute(MethodImplOptions.InternalCall)]
        internal extern IntPtr GetCallStub(IntPtr methodPtr);

        internal virtual Object GetTarget()
        {
            return (_methodPtrAux.IsNull()) ? _target : null;
        }


    }

    // These flags effect the way BindToMethodInfo and BindToMethodName are allowed to bind a delegate to a target method. Their
    // values must be kept in sync with the definition in vm\comdelegate.h.
    internal enum DelegateBindingFlags
    {
        StaticMethodOnly    =   0x00000001, // Can only bind to static target methods
        InstanceMethodOnly  =   0x00000002, // Can only bind to instance (including virtual) methods
        OpenDelegateOnly    =   0x00000004, // Only allow the creation of delegates open over the 1st argument
        ClosedDelegateOnly  =   0x00000008, // Only allow the creation of delegates closed over the 1st argument
        NeverCloseOverNull  =   0x00000010, // A null target will never been considered as a possible null 1st argument
        CaselessMatching    =   0x00000020, // Use case insensitive lookup for methods matched by name
        SkipSecurityChecks  =   0x00000040, // Skip security checks (visibility, link demand etc.)
        RelaxedSignature    =   0x00000080, // Allow relaxed signature matching (co/contra variance)
    }
}
