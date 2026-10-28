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
                throw new RoomToolException("MOTION_DRIVEN","Pause or remove the behavior first.");
        }
        public static DrivenChannels Channels(Transform t)
        {
            var channels=DrivenChannels.None;foreach(var value in entries.Values)if(value.transform!=null&&value.transform==t)channels|=value.channels;return channels;
        }
    }
}
