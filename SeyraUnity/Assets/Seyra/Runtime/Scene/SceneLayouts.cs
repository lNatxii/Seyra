using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Seyra.Spatial;
using UnityEngine;

namespace Seyra.SceneTools
{
    public sealed partial class SceneToolService
    {
        sealed class LayoutToolException:ArgumentException
        {
            public readonly string Code;public readonly JObject Details;
            public LayoutToolException(string code,string message,JObject details=null):base(message){Code=code;Details=details;}
        }
        static void RestoreLayout(GameObject go,JObject specification)
        {
            var group=go.GetComponent<SeyraLayoutGroup>();
            if(specification!=null&&group==null)group=go.AddComponent<SeyraLayoutGroup>();
            if(group!=null)group.Configure(specification);
        }
        static void LayoutHierarchy(Transform t)
        {
            for(var p=t;p!=null;p=p.parent)
            {var s=p.localScale;if(s.x<=0||Mathf.Abs(s.x-s.y)>.00001f||Mathf.Abs(s.x-s.z)>.00001f)throw new ArgumentException("Layouts require positive uniform hierarchy scales.");}
        }
        void ApplyAnchor(GameObject child,JObject target,float gap,Pose view)
        {
            string id=(string)target["targetId"];
            var reference=PhysicalId(id)?null:Find(target["targetId"]);
            if(reference!=null&&(child.transform.IsChildOf(reference.transform)||reference.transform.IsChildOf(child.transform)))throw new ArgumentException("Anchor subtrees must be unrelated.");
            var points=reference==null?ResolvePhysical(id).points:GeometryPoints(reference).ToArray();
            var frame=Quaternion.Euler(0,view.rotation.eulerAngles.y,0);
            child.transform.rotation=SpatialGeometry.Yaw(child.transform.position-view.position);
            string side=(string)target["side"]??"above";int axis=side=="above"?1:side=="front"?2:0;
            child.transform.position+=RelativeOffset(SpatialGeometry.Project(GeometryPoints(child),frame),SpatialGeometry.Project(points,frame),axis,side!="left",gap,frame);
            Check(child.transform);
        }
        LayoutPlanner.PlanResult PlanLayout(JObject spec,int count,JArray targets,Transform exclude=null)
        {
            if(LayoutPlanner.IsRoomLayout((string)spec["layout"])&&PlacementService.Snapshot.State!=RoomState.Ready)
                throw new RoomToolException("ROOM_UNAVAILABLE","Room layouts require a ready captured room.");
            var view=PlacementService.View(activeRequest,currentYaw:activeRequest==null);
            var resolved=new List<Vector3[]>();
            if((string)spec["layout"]=="anchor")
            {
                if(targets==null||targets.Count!=count)throw new ArgumentException("anchor requires one target per item.");
                foreach(var entry in targets)
                {
                    var target=entry as JObject??throw new ArgumentException("Invalid target.");Keys(target,new[]{"targetId"},new[]{"side"});
                    if(!new[]{"above","front","left","right"}.Contains((string)target["side"]??"above"))throw new ArgumentException("Invalid anchor side.");
                    string id=String(target["targetId"],128);
                    if(PhysicalId(id))resolved.Add(ResolvePhysical(id).points);
                    else{var go=Find(target["targetId"]);if(go.GetComponent<GeneratedObject>()==null||!go.transform.IsChildOf(generated))throw new ArgumentException("Anchor target must be managed content.");resolved.Add(GeometryPoints(go).ToArray());}
                }
            }
            else if(targets!=null)throw new ArgumentException("targets is only valid for anchor.");
            var frame=Quaternion.Euler(0,view.rotation.eulerAngles.y,0);
            Pose Anchor(int i,Vector2 size)
            {
                var bounds=SpatialGeometry.Project(resolved[i],frame);var position=bounds.center;
                var rotation=SpatialGeometry.Yaw(position-view.position);
                // Use the same projected bounds offset as place_relative, with a card envelope.
                var corners=SpatialGeometry.Corners(new Bounds(Vector3.zero,new Vector3(size.x,size.y,.05f)),Matrix4x4.TRS(position,rotation,Vector3.one));
                string side=(string)targets[i]["side"]??"above";int axis=side=="above"?1:side=="front"?2:0;
                position+=RelativeOffset(SpatialGeometry.Project(corners,frame),bounds,axis,side!="left",(float)spec["gap"],frame);
                return new Pose(position,rotation);
            }
            LayoutPlanner.PlanResult plan;
            try{plan=LayoutPlanner.Plan(spec,count,view,Anchor,PlacementService.Snapshot,PlacementService.Reservations(exclude));}
            catch(ArgumentException error) when(error.Message.StartsWith("ROOM_UNAVAILABLE:")||error.Message.StartsWith("ROOM_REFERENCE_STALE:"))
            {throw new RoomToolException(error.Message.Split(':')[0],error.Message);}
            if((string)spec["layout"]=="anchor")
            {
                for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)
                {var size=LayoutPlanner.Size(spec["itemSize"]);if(Vector3.Distance(plan.Items[i].position,plan.Items[j].position)<size.magnitude)plan.Warnings.Add("overlap:"+i+","+j);}
            }
            return plan;
        }
        JObject LayoutPlanJson(JObject spec,int count,LayoutPlanner.PlanResult plan)=>new JObject{
            ["fits"]=plan.Fits,["layout"]=spec["layout"],["behavior"]=spec["behavior"],["count"]=count,["maxItems"]=plan.MaxItems,
            ["rows"]=plan.Rows,["columns"]=plan.Columns,["radius"]=plan.Radius>0?new JValue(plan.Radius):JValue.CreateNull(),
            ["items"]=new JArray(plan.Items.Select((pose,index)=>new JObject{["index"]=index,["position"]=Vector(pose.position),["rotation"]=Vector(pose.rotation.eulerAngles),["physicalId"]=plan.Candidates.Count>index?plan.Candidates[index].PhysicalId:null})),
            ["warnings"]=new JArray(plan.Warnings),["perWall"]=plan.PerWall.DeepClone(),["roomRevision"]=plan.RoomRevision,["simulated"]=plan.Simulated,["reason"]=plan.Reason??(plan.Fits?null:"LAYOUT_DOES_NOT_FIT")};
        void RequireFollowAvailable(JObject spec,GameObject except=null)
        {
            if((string)spec["behavior"]!="lazy_follow")return;
            var busy=LiveContent().Select(t=>t.GetComponent<SeyraLayoutGroup>()).FirstOrDefault(g=>g!=null&&g.gameObject!=except&&(string)g.Specification?["behavior"]=="lazy_follow");
            if(busy!=null)throw new LayoutToolException("LAYOUT_FOLLOW_BUSY","A following layout already exists.",new JObject{["rootId"]=Id(busy.gameObject)});
        }
        static LayoutPlanner.PlanResult SavedAnchorPlan(GameObject root,GameObject[] children)
        { var plan=new LayoutPlanner.PlanResult{Fits=true,MaxItems=24,Rows=1,Columns=children.Length,Root=new Pose(root.transform.position,root.transform.rotation)}; foreach(var child in children)plan.Items.Add(new Pose(child.transform.position,child.transform.rotation));plan.Warnings.Add("anchor_targets_not_persisted: saved poses retained");return plan; }
        JObject ExecuteLayout(string tool,JObject args)
        {
            if(tool=="layout_plan")
            {
                Keys(args,new[]{"layout","count","itemSize"},new[]{"behavior","centerSize","gap","params","targets"});
                int count=Int(args,"count",0,1,24);var spec=LayoutPlanner.Normalize(args,count);
                return LayoutPlanJson(spec,count,PlanLayout(spec,count,args["targets"] as JArray));
            }
            bool create=tool=="layout_create";if(!create&&tool!="layout_apply")throw new ArgumentException("Unknown layout tool.");
            GameObject root=null;JObject specification;GameObject[] children=null;JArray items=null,targets=null;
            var panels=new List<PanelContent>();var specs=new List<PresentationContent>();
            string name;
            if(create)
            {
                Keys(args,new[]{"name","layout","itemSize","items"},new[]{"behavior","centerSize","gap","params","parentId","media"});
                RegisterMediaArguments(args);
                name=String(args["name"],256);items=args["items"] as JArray??throw new ArgumentException("items must be an array.");
                specification=LayoutPlanner.Normalize(args,items.Count);
                if((string)specification["layout"]=="anchor")targets=new JArray();
                var live=LiveContent().Select(t=>t.GetComponent<ManagedPanel>()).Where(p=>p!=null&&p.IsPresentation).ToArray();
                var ids=new HashSet<string>(live.Select(p=>p.Presentation.Data.id));int presentationCount=live.Length;
                for(int i=0;i<items.Count;i++)
                {
                    var item=items[i] as JObject??throw new ArgumentException("Invalid layout item.");Keys(item,new[]{"name"},new[]{"panel","presentation","targetId","side"});String(item["name"],256);
                    if((item["panel"]!=null)==(item["presentation"]!=null))throw new ArgumentException("Supply exactly one panel or presentation.");
                    var size=i==0&&(string)specification["layout"]=="hub"&&specification["centerSize"]?.Type==JTokenType.Object?specification["centerSize"]:specification["itemSize"];
                    if(item["panel"]!=null)
                    {
                        var panel=item["panel"] as JObject??throw new ArgumentException("panel must be an object.");
                        Keys(panel,new[]{"version","kind","title","text","fontSize"});panel=(JObject)panel.DeepClone();panel["width"]=size["width"];panel["height"]=size["height"];
                        if((string)panel["kind"]=="panel"&&(float)size["height"]<.24f)throw new LayoutToolException("LAYOUT_SIZE_INVALID","Panel height must be at least .24 meters.");
                        panels.Add(PanelContent.Parse(panel));specs.Add(null);
                    }
                    else
                    {
                        if((float)size["width"]<.4f||(float)size["height"]<.4f)throw new LayoutToolException("LAYOUT_SIZE_INVALID","Presentation dimensions must be at least .4 meters.");
                        var presentation=PresentationContent.Parse(new JObject{["version"]=1,["width"]=size["width"],["height"]=size["height"],["presentation"]=item["presentation"].DeepClone()});
                        if(!ids.Add(presentation.Data.id))throw new ArgumentException("Duplicate presentation ID.");
                        presentationCount++;panels.Add(null);specs.Add(presentation);
                    }
                    if(targets!=null)targets.Add(new JObject{["targetId"]=item["targetId"]?.DeepClone(),["side"]=item["side"]??"above"});
                    else if(item["targetId"]!=null||item["side"]!=null)throw new ArgumentException("Targets require anchor.");
                }
                if(presentationCount>16)throw new RoomToolException("LAYOUT_TOO_MANY_PRESENTATIONS","The layout would exceed 16 live presentations.");
                if(uiCatalog==null||specs.Any(p=>p!=null)&&(presentations==null||presentations.Components!=uiCatalog))throw new InvalidOperationException("Compatible UI catalogs are required.");
                if((specs.Any(p=>p!=null)||panels.Any(p=>p?.kind=="panel"))&&UnityEngine.Object.FindFirstObjectByType<Oculus.Interaction.PointableCanvasModule>()==null)throw new InvalidOperationException("A PointableCanvasModule is required.");
                LayoutHierarchy(Parent(args["parentId"]??new JValue("Generated")).transform);
                RequireCapacity(2+items.Count*(uiCatalog.WindowPrefab!=null?uiCatalog.WindowPrefab.GetComponentsInChildren<Transform>(true).Length+32:64));
            }
            else
            {
                Keys(args,new[]{"rootId"},new[]{"layout","behavior","gap","params","order"});root=Find(args["rootId"]);Parent(args["rootId"]);
                if(root.transform==generated||root.GetComponent<GeneratedObject>()==null)throw new RoomToolException("LAYOUT_NOT_A_GROUP","A managed container is required.");
                children=root.transform.Cast<Transform>().Select(t=>t.gameObject).ToArray();
                if(children.Length<1||children.Length>24||children.Any(c=>c.GetComponent<ManagedPanel>()==null))throw new RoomToolException("LAYOUT_NOT_A_GROUP","Expected 1..24 direct managed cards.");
                foreach(var child in children){child.GetComponent<ManagedPanel>().RequireIntact();if(Vector3.Distance(child.transform.lossyScale,Vector3.one)>.00001f)throw new RoomToolException("LAYOUT_SIZE_INVALID","Apply requires cards at their declared world size.");}
                if(args["order"]!=null)
                {
                    var order=args["order"] as JArray??throw new ArgumentException("order must be an array.");
                    var ordered=order.Select(t=>Find(t)).ToArray();if(ordered.Length!=children.Length||ordered.Distinct().Count()!=children.Length||ordered.Any(c=>!children.Contains(c)))throw new ArgumentException("order must be a permutation of the direct children.");children=ordered;
                }
                var size=children[0].GetComponent<ManagedPanel>().Content;
                var savedTargets=root.GetComponent<SeyraLayoutGroup>()?.Specification?["targets"] as JArray;
                if(savedTargets!=null && args["order"]!=null)savedTargets=new JArray(children.Select(c=>savedTargets[c.transform.GetSiblingIndex()].DeepClone()));
                specification=root.GetComponent<SeyraLayoutGroup>()?.Specification??new JObject{["layout"]="plane",["itemSize"]=new JObject{["width"]=size.width,["height"]=size.height}};
                string oldLayout=(string)specification["layout"];
                if((string)args["layout"]=="anchor"&&oldLayout!="anchor")throw new ArgumentException("anchor requires layout_create with targets");
                if(args["layout"]!=null&&(string)args["layout"]!=oldLayout){specification.Remove("params");specification.Remove("centerSize");specification.Remove("behavior");}
                foreach(var key in new[]{"layout","behavior","gap","params"})if(args[key]!=null)specification[key]=args[key].DeepClone();
                specification=LayoutPlanner.Normalize(specification,children.Length);name=root.name;LayoutHierarchy(root.transform);
                for(int i=0;i<children.Length;i++)
                {
                    var content=children[i].GetComponent<ManagedPanel>().Content;var expected=i==0&&(string)specification["layout"]=="hub"&&specification["centerSize"]?.Type==JTokenType.Object?specification["centerSize"]:specification["itemSize"];
                    if(Math.Abs(content.width-(float)expected["width"])>.00001f||Math.Abs(content.height-(float)expected["height"])>.00001f)throw new RoomToolException("LAYOUT_SIZE_INVALID","All cards must match the group's declared sizes.");
                }
                if((string)specification["layout"]=="anchor")targets=savedTargets;
            }
            RequireFollowAvailable(specification,root);
            int total=create?items.Count:children.Length;var plan=!create && (string)specification["layout"]=="anchor" && targets==null ? SavedAnchorPlan(root,children) : PlanLayout(specification,total,targets,root!=null?root.transform:null);
            if(!plan.Fits)throw new LayoutToolException("LAYOUT_DOES_NOT_FIT",plan.Reason??"The layout does not fit. Group or select items.",new JObject{["maxItems"]=plan.MaxItems,["perWall"]=plan.PerWall.DeepClone()});
            bool automatic=activeRequest==null;string requestId=activeRequest;
            try
            {
                if(automatic){var request=ExecuteLifecycle("request_begin",new JObject{["name"]=name});requestId=activeRequest;}
                if(create)
                {
                    var parent=Parent(args["parentId"]??new JValue("Generated"));if(args["parentId"]==null||(string)args["parentId"]=="Generated")parent=Find(new JValue(activeRequest));
                    root=InstantiateManaged(name,parent.transform,"empty",null,activeRequest,Vector3.zero,Quaternion.identity,Vector3.one);children=new GameObject[total];
                    root.transform.SetPositionAndRotation(plan.Root.position,plan.Root.rotation);root.transform.localScale=Vector3.one/root.transform.parent.lossyScale.x;
                    for(int i=0;i<total;i++)children[i]=panels[i]!=null?InstantiatePanel((string)items[i]["name"],root.transform,panels[i],activeRequest,Vector3.zero,Quaternion.identity,Vector3.one):InstantiatePresentation((string)items[i]["name"],root.transform,specs[i],null,activeRequest,Vector3.zero,Quaternion.identity,Vector3.one);
                }
                else root.transform.SetPositionAndRotation(plan.Root.position,plan.Root.rotation);
                for(int i=0;i<total;i++){children[i].transform.SetPositionAndRotation(plan.Items[i].position,plan.Items[i].rotation);children[i].transform.SetSiblingIndex(i);Check(children[i].transform);}
                if(targets!=null)
                {var view=PlacementService.View(activeRequest,currentYaw:activeRequest==null);for(int i=0;i<total;i++)ApplyAnchor(children[i],(JObject)targets[i],(float)specification["gap"],view);}
                if(targets!=null)specification["targets"]=targets.DeepClone();
                RestoreLayout(root,specification);Check(root.transform);
                for(int i=0;i<total;i++)PlacementService.Register(children[i].transform,plan.Candidates.Count>i?plan.Candidates[i]:null);
                return new JObject{["rootId"]=Id(root),["entityId"]=Id(root),["requestGroupId"]=requestId,["layout"]=specification["layout"],["behavior"]=specification["behavior"],
                    ["items"]=new JArray(children.Select((c,i)=>new JObject{["index"]=i,["entityId"]=Id(c),["name"]=c.name})),["warnings"]=new JArray(plan.Warnings)};
            }
            finally{if(automatic)activeRequest=null;}
        }
    }
}
