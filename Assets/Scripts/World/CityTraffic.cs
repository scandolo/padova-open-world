using System.Collections.Generic;
using Padova.Architecture;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Autonomous cars on the surveyed road network, kept in a ring around the player.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class CityTraffic:MonoBehaviour
    {
        public OpenWorld Game;public int Count=34;public float Near=110,Far=420;
        public RoadNetwork Network{get;private set;}
        public int Active,Moving,Respawns,ClosedLanes;public float TotalTravel;
        public sealed class Vehicle{public GameObject Go;public Rigidbody Body;public Transform[] Wheels;public int Lane,Index;public float S,Speed,Cruise,Blocked,GhostUntil,Spin;public string Colour;}
        public readonly List<Vehicle> Vehicles=new();
        readonly System.Random random=new(1222);Mesh wheelMesh;Material[] wheelMaterials;readonly List<(Mesh mesh,Material[] materials,string name)> bodies=new();float nextRecycle;
        static readonly (string name,Color color)[] Paints={("silver",new Color(.72f,.74f,.76f)),("black",new Color(.06f,.065f,.07f)),("white",new Color(.92f,.92f,.9f)),("grey",new Color(.38f,.4f,.42f)),
            ("dark blue",new Color(.08f,.14f,.32f)),("red",new Color(.62f,.08f,.07f)),("bottle green",new Color(.08f,.26f,.16f)),("sand",new Color(.76f,.68f,.52f)),("yellow",new Color(.9f,.72f,.12f))};
        float Ground(float x,float z)
        {
            float guess=Game.City.Data.GroundAt(x,z);
            return Physics.Raycast(new Vector3(x,guess+30,z),Vector3.down,out var hit,60,1<<8)?hit.point.y:guess;
        }
        void Start()
        {
            if(!Game || Game.City.Data==null)return;
            Network=RoadNetwork.Build(Game.City.Data.roads,Ground);CloseObstructedLanes();
            var palette=Game.City.City.Materials;
            var wheels=new MeshSink();WorldShapes.Wheel(wheels);wheelMesh=wheels.ToMesh("Traffic wheel",palette,out wheelMaterials);
            foreach(var (name,color) in Paints)
            {
                var paint=new Material(palette[(int)Slot.VehicleBlue]){name="Traffic paint "+name};paint.SetColor("_BaseColor",color);paint.color=color;
                var custom=(Material[])palette.Clone();custom[(int)Slot.VehicleBlue]=paint;
                var sink=new MeshSink();WorldShapes.CarBody(sink,Slot.VehicleBlue);var mesh=sink.ToMesh("Traffic car "+name,custom,out var materials);
                bodies.Add((mesh,materials,name));
            }
            for(int i=0;i<Count;i++){var v=Create(i);if(Place(v,true))Vehicles.Add(v);else Destroy(v.Go);}
            Active=Vehicles.Count;
        }
        Vehicle Create(int i)
        {
            var (mesh,materials,name)=bodies[i%bodies.Count];
            var go=new GameObject("Traffic car "+(i+1));go.transform.SetParent(transform,false);go.layer=11;
            var body=new GameObject("Body");body.transform.SetParent(go.transform,false);body.AddComponent<MeshFilter>().sharedMesh=mesh;body.AddComponent<MeshRenderer>().sharedMaterials=materials;
            var box=go.AddComponent<BoxCollider>();box.center=new Vector3(0,.8f,0);box.size=new Vector3(1.74f,1.0f,4.1f);
            var rb=go.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;
            var v=new Vehicle{Go=go,Body=rb,Wheels=new Transform[4],Index=i,Colour=name,Cruise=8+(float)random.NextDouble()*3.5f};int w=0;
            foreach(float z in new[]{1.28f,-1.27f})foreach(float x in new[]{-.86f,.86f})
            {
                var wheel=new GameObject("Wheel");wheel.transform.SetParent(go.transform,false);wheel.transform.localPosition=new Vector3(x,.32f,z);
                wheel.AddComponent<MeshFilter>().sharedMesh=wheelMesh;wheel.AddComponent<MeshRenderer>().sharedMaterials=wheelMaterials;v.Wheels[w++]=wheel.transform;
            }
            return v;
        }
        /// <summary>Puts a car on a random lane in the ring around the player, out of the camera's close view when possible.</summary>
        bool Place(Vehicle v,bool initial)
        {
            var focus=Game.Position;var camera=Camera.main;
            for(int attempt=0;attempt<400;attempt++)
            {
                int lane=random.Next(Network.Lanes.Count);var l=Network.Lanes[lane];if(l.Length<20||!Network.Open(lane))continue;
                float s=(float)random.NextDouble()*(l.Length-10)+5;var p=Network.Sample(lane,s,out var tangent);
                float d=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(focus.x,focus.z));
                if(d>Far*.9f || d<(initial?25:Near))continue;
                if(!initial && camera && d<220){var vp=camera.WorldToViewportPoint(p);if(vp.z>0&&vp.x>-.1f&&vp.x<1.1f&&vp.y>-.1f&&vp.y<1.1f)continue;}
                bool crowded=false;foreach(var o in Vehicles)if(o!=v && (o.Go.transform.position-p).sqrMagnitude<18*18){crowded=true;break;}
                if(crowded || Physics.CheckBox(p+Vector3.up*.8f,new Vector3(.87f,.5f,2.05f),Quaternion.LookRotation(tangent.WithY(0)),(1<<9)|(1<<11)))continue;
                v.Go.SetActive(true);planned.Remove(v);v.Lane=lane;v.S=s;v.Speed=v.Cruise*.6f;v.Blocked=0;v.GhostUntil=0;
                v.Go.transform.SetPositionAndRotation(p,Quaternion.LookRotation(tangent.WithY(0)));v.Body.position=p;v.Body.rotation=v.Go.transform.rotation;
                return true;
            }
            return false;
        }
        /// <summary>Lowest speed demanded by curvature over the next <paramref name="ahead"/> metres.</summary>
        float CurveLimit(Vehicle v,float ahead)
        {
            Network.Sample(v.Lane,v.S,out var t0);int lane=v.Lane;float s=v.S+ahead;var l=Network.Lanes[lane];
            if(s>l.Length){s-=l.Length;lane=PeekNext(v);}
            Network.Sample(lane,s,out var t1);float angle=Vector3.Angle(t0.WithY(0),t1.WithY(0));
            return Mathf.Lerp(v.Cruise,3.2f,Mathf.InverseLerp(15,85,angle));
        }
        readonly Dictionary<Vehicle,int> planned=new();
        int PeekNext(Vehicle v){if(!planned.TryGetValue(v,out int next)||Network.Lanes[next].From!=Network.Lanes[v.Lane].To)planned[v]=next=Network.Next(v.Lane,random);return next;}
        void FixedUpdate()
        {
            if(Network==null || Game.MapOpen)return;
            float dt=Time.fixedDeltaTime;Moving=0;const int mask=(1<<9)|(1<<10)|(1<<11);
            foreach(var v in Vehicles)
            {
                if(!v.Go.activeSelf)continue;
                var t=v.Go.transform;float limit=CurveLimit(v,14);
                // Stop for the player, the player's car and other traffic in the lane ahead.
                float look=4+v.Speed*1.6f;bool blocked=false;RaycastHit hit=default;
                if(Physics.BoxCast(t.position+Vector3.up*.8f+t.forward*2.3f,new Vector3(.7f,.45f,.2f),t.forward,out hit,t.rotation,look,mask,QueryTriggerInteraction.Ignore))
                {blocked=hit.collider.attachedRigidbody!=v.Body && !(Time.time<v.GhostUntil && hit.collider.gameObject.layer==11);if(blocked)limit=Mathf.Min(limit,Mathf.Max(0,(hit.distance-1.5f)*.8f));}
                // A cast starting inside another car misses it, so also check the space just ahead of the bumper.
                bool overlapPerson=false;
                if(!blocked && Time.time>v.GhostUntil)foreach(var c in Physics.OverlapBox(t.position+Vector3.up*.8f+t.forward*3,new Vector3(.75f,.45f,.9f),t.rotation,(1<<10)|(1<<11),QueryTriggerInteraction.Ignore))
                    if(c.attachedRigidbody!=v.Body){blocked=true;limit=0;hit=default;overlapPerson=c.gameObject.layer==10;break;}
                // Cars wait for a person on foot indefinitely. Another car (head-on at a junction, or parked in the lane) is passed after a pause;
                // a car stuck on anything else is moved somewhere out of view.
                bool person=blocked && (overlapPerson || hit.collider && hit.collider.gameObject.layer==10);
                v.Blocked=blocked&&v.Speed<.3f&&!person?v.Blocked+dt:0;
                if(v.Blocked>5){v.Blocked=0;bool car=!hit.collider||hit.collider.gameObject.layer==11;if(car)v.GhostUntil=Time.time+3;else if(Place(v,false))Respawns++;}
                v.Speed=Mathf.MoveTowards(v.Speed,limit,(limit<v.Speed?7f:2.4f)*dt);
                v.S+=v.Speed*dt;
                while(v.S>Network.Lanes[v.Lane].Length){v.S-=Network.Lanes[v.Lane].Length;int next=PeekNext(v);planned.Remove(v);v.Lane=next;}
                var p=Network.Sample(v.Lane,v.S,out var tangent);var flat=tangent.WithY(0);
                var rotation=flat.sqrMagnitude>.001f?Quaternion.Slerp(v.Body.rotation,Quaternion.LookRotation(tangent),1-Mathf.Exp(-10*dt)):v.Body.rotation;
                TotalTravel+=Vector3.Distance(v.Body.position,p);v.Body.MovePosition(p);v.Body.MoveRotation(rotation);
                v.Spin+=v.Speed*dt/.31f*Mathf.Rad2Deg;
                if(v.Speed>.5f)Moving++;
            }
            if(Time.time>nextRecycle)
            {
                nextRecycle=Time.time+.5f;var focus=Game.Position;
                foreach(var v in Vehicles)
                {
                    var p=v.Go.transform.position;
                    if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(focus.x,focus.z))>Far && Place(v,false))Respawns++;
                }
            }
        }
        /// <summary>Surveyed centrelines pass through porticoes and narrow historic streets; traffic avoids lanes a car body would intersect.</summary>
        void CloseObstructedLanes()
        {
            var colliders=new List<Collider>();
            foreach(var c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))if(c.gameObject.layer==9 && !c.enabled){c.enabled=true;colliders.Add(c);}
            Physics.SyncTransforms();Network.Closed=new bool[Network.Lanes.Count];ClosedLanes=0;
            for(int i=0;i<Network.Lanes.Count;i++)
            {
                var l=Network.Lanes[i];
                // Whole length: pinch points at junctions and alley entrances sit within the first and last metres of a lane.
                for(float s=1;s<l.Length-1 && !Network.Closed[i];s+=2)
                {
                    var p=Network.Sample(i,s,out var tangent);if(tangent.WithY(0).sqrMagnitude<.001f)continue;
                    if(Physics.CheckBox(p+Vector3.up*.9f,new Vector3(.85f,.45f,1),Quaternion.LookRotation(tangent.WithY(0)),1<<9,QueryTriggerInteraction.Ignore)){Network.Closed[i]=true;ClosedLanes++;}
                }
            }
            foreach(var c in colliders)c.enabled=false;
        }
        void LateUpdate(){foreach(var v in Vehicles)foreach(var w in v.Wheels)w.localRotation=Quaternion.Euler(v.Spin,0,0);}
        /// <summary>Removes a traffic car so the player can take it over; another car appears out of view later.</summary>
        public void Release(Vehicle v,System.Action<Vehicle> replace)
        {
            replace?.Invoke(v);
            var fresh=Create(v.Index+Count*(Respawns+1));fresh.Index=v.Index;
            Vehicles[Vehicles.IndexOf(v)]=fresh;planned.Remove(v);Destroy(v.Go);
            if(!Place(fresh,false)){fresh.Go.transform.position=new Vector3(0,-500,0);fresh.Go.SetActive(false);}
        }
        public Vehicle NearestTo(Vector3 p,float radius)
        {
            Vehicle best=null;float bestD=radius;
            foreach(var v in Vehicles){if(!v.Go.activeSelf)continue;float d=Vector3.Distance(v.Go.transform.position,p);if(d<bestD){bestD=d;best=v;}}
            return best;
        }
        public (Mesh mesh,Material[] materials) Body(Vehicle v){foreach(var b in bodies)if(b.name==v.Colour)return (b.mesh,b.materials);return (bodies[0].mesh,bodies[0].materials);}
        public (Mesh mesh,Material[] materials) WheelMesh=>(wheelMesh,wheelMaterials);
    }
    static class VectorExtensions{public static Vector3 WithY(this Vector3 v,float y)=>new Vector3(v.x,y,v.z);}
}
