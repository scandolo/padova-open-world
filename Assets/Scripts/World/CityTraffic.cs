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
        public sealed class Vehicle{public GameObject Go;public Rigidbody Body;public Transform[] Wheels;public Quaternion[] WheelRest;public int Lane,Index;public float S,Speed,Cruise,Blocked,GhostUntil,Spin;public string Colour;public VehicleSpec Spec;public Color Paint;}
        public readonly List<Vehicle> Vehicles=new();
        readonly System.Random random=new(1222);float nextRecycle;
        /// <summary>With the survey graph, lanes are tested against architecture when traffic first needs them (colliders only exist near the player).</summary>
        bool lazy;float[] laneChecked;readonly List<int> nearby=new();
        float Ground(float x,float z)
        {
            float guess=Game.City.Data.GroundAt(x,z);
            return Physics.Raycast(new Vector3(x,guess+30,z),Vector3.down,out var hit,60,1<<8)?hit.point.y:guess;
        }
        void Awake()
        {
            // Build the lane graph before anything asks for routes. Lane heights use the terrain model, which roads lie on.
            if(!Game || Game.City.Data==null)return;
            var roads=Game.City.Roads;
            if(roads!=null){Network=RoadNetwork.Build(roads,Game.City.Data.GroundAt);lazy=true;laneChecked=new float[Network.Lanes.Count];}
        }
        System.Collections.IEnumerator Start()
        {
            if(!Game || Game.City.Data==null)yield break;
            if(Network==null){Network=RoadNetwork.Build(Game.City.Data.roads,Ground);CloseObstructedLanes();}
            // Cars are placed against real walls: wait for the streamed colliders around the player.
            var streamer=Game.City.Streamer;float deadline=Time.realtimeSinceStartup+8;
            while(lazy && streamer && (streamer.Pending>0||streamer.MissingColliders>0) && Time.realtimeSinceStartup<deadline)yield return null;
            for(int i=0;i<Count;i++){var v=Create(i);if(Place(v,true))Vehicles.Add(v);else{v.Go.transform.position=new Vector3(0,-500,0);v.Go.SetActive(false);Vehicles.Add(v);}}
            Active=Vehicles.Count;
        }
        Vehicle Create(int i)
        {
            float pick=(float)random.NextDouble();var spec=VehicleCatalog.PickTraffic(pick);var paint=VehicleCatalog.Paint(spec,random.Next(1000));
            var go=VehicleFactory.Model(spec,paint,out var wheels,out _);go.name="Traffic "+spec.Name+" "+(i+1);go.transform.SetParent(transform,false);go.layer=11;
            var box=go.AddComponent<BoxCollider>();box.center=new Vector3(0,spec.Height*.52f,0);box.size=new Vector3(spec.Width,spec.Height*.72f,spec.Length);
            var rb=go.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;
            var v=new Vehicle{Go=go,Body=rb,Wheels=wheels,WheelRest=new Quaternion[wheels.Length],Index=i,Colour=spec.Label,Spec=spec,Paint=paint,Cruise=8+(float)random.NextDouble()*3.5f};
            for(int w=0;w<wheels.Length;w++)v.WheelRest[w]=wheels[w].localRotation;
            return v;
        }
        /// <summary>Open to traffic. A lane of the survey graph is tested once against the architecture around it, when colliders are loaded there.</summary>
        bool Usable(int lane)
        {
            if(!Network.Open(lane))return false;
            if(!lazy || Time.time<laneChecked[lane])return true;
            var l=Network.Lanes[lane];var focus=Game.Position;bool whole=true;
            for(float s=1;s<l.Length-1;s+=2.5f)
            {
                var p=Network.Sample(lane,s,out var tangent);
                float dx=p.x-focus.x,dz=p.z-focus.z;if(dx*dx+dz*dz>360*360){whole=false;continue;}
                var flat=tangent.WithY(0);if(flat.sqrMagnitude<.001f)continue;
                if(Physics.CheckBox(p+Vector3.up*.9f,new Vector3(.85f,.45f,1),Quaternion.LookRotation(flat),1<<9,QueryTriggerInteraction.Ignore)){Network.Closed[lane]=true;ClosedLanes++;return false;}
            }
            laneChecked[lane]=whole?float.MaxValue:Time.time+4;
            return true;
        }
        /// <summary>Puts a car on a random lane in the ring around the player, out of the camera's close view when possible.</summary>
        bool Place(Vehicle v,bool initial)
        {
            var focus=Game.Position;var camera=Camera.main;
            Network.LanesNear(focus,Far,nearby);if(nearby.Count==0)return false;
            for(int attempt=0;attempt<(lazy?90:400);attempt++)
            {
                int lane=nearby[random.Next(nearby.Count)];var l=Network.Lanes[lane];if(l.Length<20||!Usable(lane))continue;
                float s=(float)random.NextDouble()*(l.Length-10)+5;var p=Network.Sample(lane,s,out var tangent);
                float d=Vector2.Distance(new Vector2(p.x,p.z),new Vector2(focus.x,focus.z));
                if(d>Far*.9f || d<(initial?25:Near))continue;
                if(!initial && camera && d<220){var vp=camera.WorldToViewportPoint(p);if(vp.z>0&&vp.x>-.1f&&vp.x<1.1f&&vp.y>-.1f&&vp.y<1.1f)continue;}
                bool crowded=false;foreach(var o in Vehicles)if(o!=v && (o.Go.transform.position-p).sqrMagnitude<18*18){crowded=true;break;}
                if(crowded || Physics.CheckBox(p+Vector3.up*.8f,new Vector3(v.Spec.Width*.5f,.5f,v.Spec.Length*.5f),Quaternion.LookRotation(tangent.WithY(0)),(1<<9)|(1<<11)))continue;
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
            // Cruise follows the road: about town pace on streets, quicker on main and fast roads, never above what the vehicle can do.
            var here=Network.Lanes[v.Lane];float cruise=Mathf.Min(here.Limit*.9f,v.Cruise*(here.Class==3?2.2f:here.Class==2?1.25f:here.Class==0?.7f:1),v.Spec.TopSpeed*.9f);
            return Mathf.Lerp(cruise,3.2f,Mathf.InverseLerp(15,85,angle));
        }
        readonly Dictionary<Vehicle,int> planned=new();
        int PeekNext(Vehicle v)
        {
            if(planned.TryGetValue(v,out int next)&&Network.Lanes[next].From==Network.Lanes[v.Lane].To)return next;
            if(lazy)foreach(int o in Network.Outgoing[Network.Lanes[v.Lane].To])Usable(o);
            return planned[v]=Network.Next(v.Lane,random);
        }
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
                bool ended=false;
                while(v.S>Network.Lanes[v.Lane].Length)
                {
                    int next=PeekNext(v);planned.Remove(v);
                    // The end of a one-way road out of town, or a junction with nowhere a car may go: leave the scene.
                    if(next==v.Lane||!Network.Open(next)){ended=true;break;}
                    v.S-=Network.Lanes[v.Lane].Length;v.Lane=next;
                }
                if(ended){if(Place(v,false))Respawns++;else{v.S=Network.Lanes[v.Lane].Length;v.Speed=0;}continue;}
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
        void LateUpdate(){foreach(var v in Vehicles)for(int w=0;w<v.Wheels.Length;w++)v.Wheels[w].localRotation=v.WheelRest[w]*Quaternion.Euler(v.Spin,0,0);}
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
    }
    static class VectorExtensions{public static Vector3 WithY(this Vector3 v,float y)=>new Vector3(v.x,y,v.z);}
}
