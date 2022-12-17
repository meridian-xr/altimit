﻿using Altimit;
using Altimit.Networking;
using Altimit.Serialization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace Altimit.Networking
{
    // Handles synchronizating data for a session and initializing other session modules
    // All sessions must include this
    public partial class ReplicationSM : SessionModule<ReplicationAM, IReplicationAPI>, IReplicationAPI
    {
        //P2PPeerSM P2PPeer => Session.Get<P2PPeerSM>();

        HashSet<object> authorizedInstances = new HashSet<object>();
        Dictionary<object, HashSet<string>> authorizedInstanceProperties = new Dictionary<object, HashSet<string>>();

        // Used for scope
        public AHashset<object> TrackedInstances { get; } = new AHashset<object>();

        // Used for loopback avoidance
        HashSet<AID> lockedAssets = new HashSet<AID>();

        bool isLocked = false;
        Database InstanceDB => replicationAM.InstanceDB;

        ReplicationAM replicationAM => App.Get<ReplicationAM>();

        public Guid PeerAppID;

        public ReplicationSM()
        {
        }

        public override async Task OnAdded()
        {
            InstanceDB.onPropertyChanged += OnPropertyChanged;
            //InstanceDB.onInstanceAdded += OnInstanceAdded;
            InstanceDB.onElementAdded += OnElementAdded;
            InstanceDB.onElementRemoved += OnElementRemoved;
            InstanceDB.onMethodCalled += OnMethodCalled;
            InstanceDB.onInstanceRemoved += OnInstanceRemoved;
        }

        public virtual void Lock()
        {
            isLocked = true;
        }

        public virtual void Unlock()
        {
            isLocked = false;
        }

        #region Methods called exclusively from this session's peer to alter this app's globe, with tracking checks and loopback avoidance
        public virtual void ChangeProperty(object instance, string propertyName, object value)
        {
            if (instance == null)
                Logger.LogError($"Tried to change property on a null instance! Property named {propertyName} with a value of {value}.");

            instance.GetObserver().Update();
            Lock();
            App.Get<ReplicationAM>().InstanceDB.SetProperty(instance, propertyName, value);
            instance.GetObserver().Update();
            Unlock();
        }

        public async Task AddInstance(AID instanceID, Type type, object[] args)
        {
            if (App.Get<ReplicationAM>().InstanceDB.HasInstanceID(instanceID))
                return;

            // Filter constructor arguments before adding instance
            var propertyNames = type.GetATypeInfo().DefaultConstructorInfo.PropertyNames.ToArray();
            for (int i = 0; i < propertyNames.Length; i++)
            {
                args[i] = App.Get<ReplicationAM>().InstanceDB.FilterPropertyIn(type.GetPropertyInfo(propertyNames[i]), args[i]);
            }

            var instance = type.GetATypeInfo().DefaultConstructorInfo.Construct(args);
            InstanceDB.AddInstance(instanceID, instance);

            TrackInstance(instance);
        }

        // Retrieves an instance owned by this application
        // TODO: track globalized instance after sending local instance
        // It has to be globalized/tracked afterwards to avoid circular dependency issues, where two apps need eachother to resolve instances
        public async void RetrieveInstance(AID instanceID)
        {
            object instance;
            if (replicationAM.InstanceDB.TryGetInstance(instanceID, out instance))
            {
                await Peer.AddInstance(instanceID, instance.GetType(), instance.GetConstructorProperties(true).ToArray());
                TryTrackInstance(instance);
                
                if (!instance.GetType().IsReplicatedType())
                    Logger.LogError($"Not replicated type {instance.GetType()}!");

                foreach (var propertyByInfo in instance.GetPropertiesByInfo(true))
                {
                    if (propertyByInfo.Item1.CanSet)
                        Peer.ChangeProperty(instance, propertyByInfo.Item1.Name, propertyByInfo.Item2);
                }
                Peer.OnboardInstance(instance);
            }
            else {
                throw new NotImplementedException();
                /* add back in
                var (_, localInstance) = await replicationAM.LocalDB.FindOne(instanceID);
                if (localInstance == null)
                    Logger.LogError($"Failed to find instance with an ID of {instanceID} requested from peer!");

                instance = await InstanceDB.AddLocalInstanceAsync(localInstance, instanceID);
                await Peer.AddInstance(instanceID, instance.GetType(), instance.GetConstructorProperties(true).ToArray());
                TryTrackInstance(instance);

                foreach (var (propertyInfo, property) in localInstance.GetPropertiesByInfo())
                {
                    if (propertyInfo.CanSet)
                    {
                        replicationAM.SetProperty(ref instance, propertyInfo.Name, await InstanceDB.GlobalizeProperty(property));
                        //Peer.ChangeProperty(instance, propertyInfo.Name, instance.GetProperty(propertyInfo.Name, true));
                    }
                }
                Peer.OnboardInstance(instance);
                */
            }
        }

        public virtual void RemoveInstance(AID instanceID)
        {
            Lock();
            replicationAM.InstanceDB.RemoveInstance(instanceID);
            Unlock();
        }

        public virtual async Task<bool> TryAddAsset(AID instanceID)
        {
            return await replicationAM.InstanceDB.TryAddAsset(instanceID);
        }

        public async void AddAsset(AID instanceID, AssetMetadata assetMetadata, byte[] bytes)
        {
            lockedAssets.Add(instanceID);
            await replicationAM.InstanceDB.AddAsset(instanceID, assetMetadata, bytes);
            lockedAssets.Remove(instanceID);
        }

        public async void UpdateAsset(AID instanceID, byte[] bytes)
        {
            lockedAssets.Add(instanceID);
            await replicationAM.InstanceDB.UpdateAsset(instanceID, bytes);
            lockedAssets.Remove(instanceID);
        }

        public async void RemoveAsset(AID instanceID)
        {
            lockedAssets.Add(instanceID);
            await replicationAM.InstanceDB.RemoveAsset(instanceID);
            lockedAssets.Remove(instanceID);
        }

        public void AddElement(object instance, int index, object element)
        {
            Lock();
            instance.AddElement(index, element);
            Unlock();
        }

        public void RemoveElement(object instance, int index, object element)
        {
            Lock();
            instance.RemoveElement(index, element);
            Unlock();
        }

        public void CallMethod(object instance, string methodName, Type[] methodTypes, object[] args)
        {
            Lock();
            instance.CallMethod(methodName, methodTypes, args);
            Unlock();
        }

        bool IsLockedAsset(AID instanceID)
        {
            return lockedAssets.Contains(instanceID);
        }
        #endregion

        public virtual bool IsValidInstanceType(Type type)
        {
            return true;
        }

        #region Methods to alter this session's peer's globe, ensuring changes are within scope and haven't already been processed
        public virtual void OnPropertyChanged(object instance, string propertyName, object oldProperty)
        {
            if (instance == null)
                Logger.LogError("Property changed on a null instance!");

            var value = InstanceDB.GetInstanceProperty(instance, propertyName);

            if (isLocked || !IsTrackedInstance(instance) || (!IsOwner(instance) && !HasAuthority(instance, propertyName)))
                return;

            if (OS.LogReplication)
                Logger.Log($"Property changed named {instance.GetPropertyInfo(propertyName)} on instance with an ID of {instance.GetInstanceID()} with value {value.LocalToString()}.");

            Peer.ChangeProperty(instance, propertyName, value);
        }

        public void OnElementAdded(object instance, int index, object element)
        {
            if (!IsTrackedInstance(instance))
                return;

            if (!isLocked)
                Peer.AddElement(instance, index, element);
        }

        public void OnElementRemoved(object instance, int index, object element)
        {
            if (!IsTrackedInstance(instance))
                return;

            if (!isLocked)
                Peer.RemoveElement(instance, index, element);
        }

        public void OnMethodCalled(object instance, string methodName, Type[] methodTypes, object[] methodArgs)
        {
            if (!IsTrackedInstance(instance))
                return;

            if (OS.LogReplication)
                Logger.Log($"Called method {instance.GetMethodInfo(methodName, methodTypes)} " +
                    $"on instance with an ID of {instance.GetInstanceID()} with arguments {methodArgs.GlobalToString()}.");

            if (!isLocked)
                Peer.CallMethod(instance, methodName, methodTypes, methodArgs);
        }
        /*
        public virtual void OnSubInstancePassed(object instance, object subInstance)
        {
            if (isLocked || !IsValidInstanceType(instance.GetType()))
                return;

            if (IsTrackedInstance(instance))
                TryTrackInstance(subInstance);
        }
        */

        public virtual void OnInstanceAdded(object instance, bool isInstanceGroup)
        {
        }

        public virtual void OnInstanceRemoved(AID instanceID, object instance)
        {
            if (!IsTrackedInstance(instance))
                return;

            UntrackInstance(instanceID, instance);

            if (isLocked || !IsValidInstanceType(instance.GetType()))
                return;

            Peer.RemoveInstance(instanceID);
        }

        public void OnAssetAdded(AID instanceID, byte[] bytes)
        {
            if (isLocked || IsLockedAsset(instanceID))
                return;
            Peer.AddAsset(instanceID, replicationAM.InstanceDB.GetAssetMetadata(instanceID), bytes);
        }

        public void OnAssetUpdated(AID instanceID, byte[] bytes)
        {
            if (isLocked || IsLockedAsset(instanceID))
                return;
            Peer.UpdateAsset(instanceID, bytes);
        }

        public void PeerRemoveAsset(AID instanceID, byte[] bytes)
        {
            if (isLocked || IsLockedAsset(instanceID))
                return;
            Peer.RemoveAsset(instanceID);
        }
        #endregion

        public void SetAuthority(object instance, bool hasAuthority)
        {
            if (hasAuthority)
            {
                authorizedInstances.Add(instance);
            }
            else
            {
                authorizedInstances.Remove(instance);
            }

            Logger.Log($"Set authority of instance with an ID of {instance.GetInstanceID()} to {hasAuthority}.");

            if (replicationAM.IsOwner(instance))
                peerSession.Get<IReplicationAPI>().SetAuthority(instance, hasAuthority);
        }

        public void SetAuthority(object instance, string[] propertyNames, bool hasAuthority)
        {
            foreach (var propertyName in propertyNames)
            {
                SetAuthority(instance, propertyName, hasAuthority);
            }
        }

        public void SetAuthority(object instance, string propertyName, bool hasAuthority)
        {
            if (hasAuthority == HasAuthority(instance, propertyName))
                return;

            if (hasAuthority)
            {
                authorizedInstanceProperties.AddOrGet(instance).Add(propertyName);
            }
            else
            {
                HashSet<string> privilegedProperties;
                if (authorizedInstanceProperties.TryGetValue(instance, out privilegedProperties))
                    privilegedProperties.Remove(propertyName);
            }

            if (OS.LogReplication)
                Logger.Log($"Set authority of property named {instance.GetPropertyInfo(propertyName)} on instance with an ID of {instance.GetInstanceID()} to {hasAuthority}.");

            if (IsOwner(instance))
                Peer.SetAuthority(instance, propertyName, hasAuthority);
        }

        public virtual bool HasAuthority(object instance)
        {
            return authorizedInstances.Contains(instance);
        }

        public virtual bool IsOwner(object instance)
        {
            return replicationAM.IsOwner(instance);
        }

        public virtual bool HasAuthority(object instance, string propertyName)
        {
            HashSet<string> priviligedProperties;
            if (authorizedInstanceProperties.TryGetValue(instance, out priviligedProperties))
            {
                return priviligedProperties.Contains(propertyName);
            }
            else
            {
                return false;
            }
        }

        public void Update()
        {
        }

        public override void Dispose()
        {
            foreach (var instance in TrackedInstances.ToList())
            {
                UntrackInstance(instance.GetInstanceID(), instance);
            }

            InstanceDB.onPropertyChanged -= OnPropertyChanged;
            //InstanceDB.onInstanceAdded += OnInstanceAdded;
            InstanceDB.onElementAdded -= OnElementAdded;
            InstanceDB.onElementRemoved -= OnElementRemoved;
            InstanceDB.onMethodCalled -= OnMethodCalled;
            InstanceDB.onInstanceRemoved -= OnInstanceRemoved;
        }

        public async Task<T> Connect<T>() where T : ISessionModule
        {
            return (T)(await Connect(typeof(T)));
        }

        public virtual async Task<ISessionModule> Connect(Type moduleType)
        {
            // If already connected, return initialized module
            if (Session.HasModule(moduleType))
            {
                return Session.GetModule(moduleType);
            }
            else
            {
                // Otherwise grab from peer
                var peerSessionType = SessionExtensions.GetPeerSessionType(moduleType);
                await Peer.OnConnection(peerSessionType);

                if (!Session.HasModule(moduleType))
                {
                    if (Session.Peer is ProxyPeerSession)
                    {
                        ((ProxyPeerSession)Session.Peer).AddProxyModule(peerSessionType);
                    }

                    Session.AddModule(moduleType);
                }
                return Session.GetModule(moduleType);
            }
        }

        public async Task OnConnection(Type moduleType)
        {
            try
            {
                var sessionModuleType = App.GetSessionModuleTypes().SingleOrDefault(x => App.GetSessionModuleInterface(x) == moduleType);
                if (sessionModuleType == null)
                {
                    Logger.LogError($"Failed to find session module of type {moduleType}!");
                }

                if (!Session.HasModule(sessionModuleType))
                {
                    //logger.LogFormat("Adding requested module {0} as {1}.", moduleType.GetTypeName(), sessionModuleType.GetTypeName());
                    Session.AddModule((ISessionModule)Activator.CreateInstance(sessionModuleType));

                    var peerSessionType = SessionExtensions.GetPeerSessionType(sessionModuleType);
                    if (Session.Peer is ProxyPeerSession)
                    {
                        ((ProxyPeerSession)Session.Peer).AddProxyModule(peerSessionType);
                    }
                }
            } catch (Exception e)
            {
                OS.Logger.LogError(e);
            }
        }

        public void OnboardInstance(object instance)
        {
            // Todo: Don't include here but in ReplicationClientSM
            App.Get<ReplicationClientAM>().OnInstanceOnboarded(instance);
        }
    }
}