using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Seyra.SceneTools
{
    [Serializable]
    public abstract class MotionSpec
    {
        public Vector3 Axis = Vector3.up;
        public abstract string Kind { get; }
        public abstract JObject ToJson();
        // Specifications contain only value types and immutable strings. Keep exact float bits.
        public MotionSpec Copy() => (MotionSpec)MemberwiseClone();

        internal static JObject Object(JToken token) => token as JObject ?? throw new ArgumentException("Expected a motion object.");
        internal static void Fields(JObject value, params string[] allowed)
        {
            if (value.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Unknown motion field.");
        }
        internal static float Number(JToken token, double min, double max)
        {
            if (token == null || token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new ArgumentException("Expected a finite motion number.");
            double value = (double)token;
            float number = (float)value;
            if (double.IsNaN(value) || double.IsInfinity(value) || float.IsInfinity(number) || number < (float)min || number > (float)max)
                throw new ArgumentException("Motion number is outside the allowed range.");
            return number;
        }
        internal static Vector3 Vector(JToken token)
        {
            var value = Object(token);
            Fields(value, "x", "y", "z");
            return new Vector3(Number(value["x"], -float.MaxValue, float.MaxValue), Number(value["y"], -float.MaxValue, float.MaxValue), Number(value["z"], -float.MaxValue, float.MaxValue));
        }
        internal static JObject JsonVector(Vector3 value) => new JObject { ["x"] = value.x, ["y"] = value.y, ["z"] = value.z };
        internal static string Id(JToken token)
        {
            if (token?.Type != JTokenType.String) throw new ArgumentException("Expected a motion pivot ID.");
            string id = ((string)token).Trim();
            if (id.Length == 0 || id.Length > 128 || id.Equals("null", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid motion pivot ID.");
            return id;
        }
        static Vector3 AxisValue(JToken token)
        {
            if (token == null) return Vector3.up;
            Vector3 axis = Vector(token);
            // Scale first to normalize safely even near the limits of a float.
            float largest = Mathf.Max(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            if (largest == 0) throw new RoomToolException("MOTION_AXIS_INVALID", "Motion axis must be non-zero.");
            return (axis / largest).normalized;
        }
        public static MotionSpec Parse(JToken token)
        {
            var value = Object(token);
            if (value["kind"]?.Type != JTokenType.String) throw new ArgumentException("Expected a motion kind.");
            string kind = (string)value["kind"];
            switch (kind)
            {
                case "spin": Fields(value, "kind", "axis", "speed"); break;
                case "orbit": Fields(value, "kind", "axis", "speed", "pivotId", "pivotPoint"); break;
                case "bob": Fields(value, "kind", "axis", "amplitude", "period"); break;
                default: throw new ArgumentException("Unknown motion kind.");
            }
            Vector3 axis;
            try { axis = AxisValue(value["axis"]); }
            catch (ArgumentException) { throw new RoomToolException("MOTION_AXIS_INVALID", "Motion axis must be finite and non-zero."); }
            if (kind == "bob") return new PositionSpec { PositionKind = kind, Axis = axis,
                Amplitude = Number(value["amplitude"], .005, .5), Period = Number(value["period"], .2, 60) };
            float speed = Number(value["speed"], kind == "spin" ? -720 : -360, kind == "spin" ? 720 : 360);
            if (speed == 0) throw new ArgumentException("Motion speed must be non-zero.");
            if (kind == "spin") return new SpinSpec { Axis = axis, Speed = speed };
            if ((value.Property("pivotId") != null) == (value.Property("pivotPoint") != null))
                throw new RoomToolException("MOTION_PIVOT_INVALID", "Orbit requires exactly one pivotId or pivotPoint.");
            var orbit = new PositionSpec { PositionKind = kind, Axis = axis, Speed = speed };
            try
            {
                if (value.Property("pivotId") != null) orbit.PivotId = Id(value["pivotId"]);
                else orbit.PivotPoint = Vector(value["pivotPoint"]);
            }
            catch (ArgumentException) { throw new RoomToolException("MOTION_PIVOT_INVALID", "Invalid orbit pivot."); }
            return orbit;
        }
    }

    [Serializable]
    public sealed class SpinSpec : MotionSpec
    {
        public override string Kind => "spin";
        public float Speed;
        public override JObject ToJson() => new JObject { ["kind"] = Kind, ["axis"] = JsonVector(Axis), ["speed"] = Speed };
    }

    [Serializable]
    public sealed class PositionSpec : MotionSpec
    {
        public string PositionKind;
        public override string Kind => PositionKind;
        public string PivotId;
        public Vector3? PivotPoint;
        public float Speed, Amplitude, Period;
        public override JObject ToJson()
        {
            var value = new JObject { ["kind"] = Kind, ["axis"] = JsonVector(Axis) };
            if (Kind == "bob") { value["amplitude"] = Amplitude; value["period"] = Period; }
            else
            {
                value["speed"] = Speed;
                if (PivotId != null) value["pivotId"] = PivotId;
                else if (PivotPoint.HasValue) value["pivotPoint"] = JsonVector(PivotPoint.Value);
            }
            return value;
        }
    }

    [Serializable]
    public sealed class MotionState
    {
        public SpinSpec Spin;
        public PositionSpec Position;
        public bool Paused;
        public float SpinAngle, PositionAngle, Time;
        public Quaternion BaseRotation = Quaternion.identity;
        public Vector3 Offset, BasePosition;
        public Transform Pivot;

        public MotionState Copy()
        {
            var copy = (MotionState)MemberwiseClone();
            copy.Spin = Spin == null ? null : (SpinSpec)Spin.Copy();
            copy.Position = Position == null ? null : (PositionSpec)Position.Copy();
            return copy;
        }
        public JObject ToJson()
        {
            var spin = Spin?.ToJson();
            if (spin != null)
            {
                spin.Remove("kind"); spin["angle"] = SpinAngle;
                spin["baseRotation"] = new JObject { ["x"] = BaseRotation.x, ["y"] = BaseRotation.y, ["z"] = BaseRotation.z, ["w"] = BaseRotation.w };
            }
            var position = Position?.ToJson();
            if (position != null)
            {
                if (Position.Kind == "orbit")
                {
                    position["pivotId"] = Position.PivotId;
                    position["pivotPoint"] = Position.PivotPoint.HasValue ? MotionSpec.JsonVector(Position.PivotPoint.Value) : null;
                    position["angle"] = PositionAngle; position["offset"] = MotionSpec.JsonVector(Offset);
                }
                else { position["time"] = Time; position["basePosition"] = MotionSpec.JsonVector(BasePosition); }
            }
            return new JObject { ["paused"] = Paused, ["spin"] = spin, ["position"] = position };
        }
        public static MotionState Parse(JToken token)
        {
            var value = MotionSpec.Object(token);
            MotionSpec.Fields(value, "paused", "spin", "position");
            if (value["paused"]?.Type != JTokenType.Boolean || value.Property("spin") == null || value.Property("position") == null)
                throw new ArgumentException("Motion state requires paused, spin and position.");
            var result = new MotionState { Paused = (bool)value["paused"] };
            if (value["spin"].Type != JTokenType.Null)
            {
                var spin = (JObject)MotionSpec.Object(value["spin"]).DeepClone();
                MotionSpec.Fields(spin, "axis", "speed", "angle", "baseRotation");
                result.SpinAngle = MotionSpec.Number(spin["angle"], -360, 360);
                var rotation = MotionSpec.Object(spin["baseRotation"]);
                MotionSpec.Fields(rotation, "x", "y", "z", "w");
                result.BaseRotation = new Quaternion(MotionSpec.Number(rotation["x"], -1.001, 1.001), MotionSpec.Number(rotation["y"], -1.001, 1.001), MotionSpec.Number(rotation["z"], -1.001, 1.001), MotionSpec.Number(rotation["w"], -1.001, 1.001));
                float length = Quaternion.Dot(result.BaseRotation, result.BaseRotation);
                if (Mathf.Abs(length - 1) > .001f) throw new ArgumentException("Motion base rotation must be a unit quaternion.");
                spin.Remove("angle"); spin.Remove("baseRotation"); spin["kind"] = "spin";
                result.Spin = (SpinSpec)MotionSpec.Parse(spin);
            }
            if (value["position"].Type != JTokenType.Null)
            {
                var position = (JObject)MotionSpec.Object(value["position"]).DeepClone();
                string kind = (string)position["kind"];
                if (kind == "orbit")
                {
                    MotionSpec.Fields(position, "kind", "axis", "speed", "pivotId", "pivotPoint", "angle", "offset");
                    result.PositionAngle = MotionSpec.Number(position["angle"], -360, 360);
                    result.Offset = MotionSpec.Vector(position["offset"]);
                    position.Remove("angle"); position.Remove("offset");
                    if (position["pivotId"]?.Type == JTokenType.Null) position.Remove("pivotId");
                    if (position["pivotPoint"]?.Type == JTokenType.Null) position.Remove("pivotPoint");
                }
                else if (kind == "bob")
                {
                    MotionSpec.Fields(position, "kind", "axis", "amplitude", "period", "time", "basePosition");
                    result.Time = MotionSpec.Number(position["time"], 0, MotionSpec.Number(position["period"], .2, 60));
                    result.BasePosition = MotionSpec.Vector(position["basePosition"]);
                    position.Remove("time"); position.Remove("basePosition");
                }
                else throw new ArgumentException("Expected orbit or bob position state.");
                result.Position = (PositionSpec)MotionSpec.Parse(position);
            }
            if (result.Spin == null && result.Position == null) throw new ArgumentException("Motion state must contain a motion.");
            return result;
        }
    }
}
