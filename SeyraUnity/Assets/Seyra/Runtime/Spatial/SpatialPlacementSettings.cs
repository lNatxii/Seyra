using UnityEngine;
namespace Seyra.Spatial
{
    [CreateAssetMenu(menuName="Seyra/Spatial Placement Settings")]
    public sealed class SpatialPlacementSettings : ScriptableObject
    {
        public const float EdgeMargin=.1f, ReservationGap=.05f, MinDistance=.8f, MaxDistance=3, WallGap=.03f, TableGap=.02f;
        public const float MaxYaw=45, MaxElevation=35, MinReadingAngle=12, MinLabelReadingAngle=1.4f;
        public const float PreferredMin=1.2f, PreferredMax=2, FirstDistance=1.3f, EyeDrop=.15f, ComfortYaw=30, TriggerYaw=45, ComfortMin=.9f, ComfortMax=1.8f, TriggerMin=.8f, TriggerMax=2.1f, LongTurn=90, HeadSpeed=20, ResponseTime=.5f, SnapDistance=.18f, SnapAngle=45;
        internal static readonly float[] AirDistances={1.3f,1.6f,2}, AirAngles={0,-25,25,-40,40}, GridFractions={.25f,.5f,.75f};
        internal static readonly float[] TabletopInsetFractions={0,.25f,.5f,.75f,1};
        public const float FollowDelay=1.5f, HeadStableTime=.6f, FollowSpeed=.35f, FollowAngularSpeed=25, Cooldown=3, InteractionDelay=1;
    }
}
