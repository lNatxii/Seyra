using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seyra.SceneTools
{
    [Flags] public enum DrivenChannels { None=0, Position=1, Rotation=2 }
    /// <summary>Owners register independently; scene identity scopes queries to each runtime.</summary>
    public static class DrivenTransform
    {
        static readonly Dictionary<object,(Transform transform,DrivenChannels channels)> entries=new Dictionary<object,(Transform,DrivenChannels)>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset()=>entries.Clear();
        public static void Register(Transform t,DrivenChannels channels,object owner)
        {if(t==null||owner==null)throw new ArgumentException("A transform and owner are required.");entries[owner]=(t,channels);}
        public static void Unregister(object owner){if(owner!=null)entries.Remove(owner);}
        public static void Require(Transform t,DrivenChannels changed)
        {
            if((Channels(t)&changed)!=DrivenChannels.None)
                throw new RoomToolException("MOTION_DRIVEN","This object is animated or follows a behavior. Remove its motion or behavior first, or move its parent.");
        }
        public static DrivenChannels Channels(Transform t) => Channels(t, null);
        public static DrivenChannels Channels(Transform t, object exceptOwner)
        {
            var channels=DrivenChannels.None;
            foreach(var entry in entries)
                if(!ReferenceEquals(entry.Key,exceptOwner)&&entry.Value.transform!=null&&entry.Value.transform==t)channels|=entry.Value.channels;
            return channels;
        }
    }
}
