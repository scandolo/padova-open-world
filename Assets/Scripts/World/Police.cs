using System.Collections.Generic;
using Padova.Architecture;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Wanted level and police pursuit. Stealing a car or hitting traffic raises it; stay away from patrols to lose it.</summary>
    [DefaultExecutionOrder(125)]
    public sealed class Police:MonoBehaviour
    {
        public OpenWorld Game;public CityTraffic Traffic;public Activities Activities;public Material LightMaterial;
        public int Wanted,Busts,Escapes,Spawned;public float Evade,BustTimer;
        public const int MaxWanted=3;public const float EvadeSeconds=15,LoseDistance=110,BustDistance=9,BustSeconds=2.5f;
        public sealed class Unit{public CityCar Car;public readonly List<Vector3> Path=new();public int Next;public float Replan,Stuck,ReverseUntil,Lost,Closest=float.MaxValue,Stalled;public Renderer[] Lights;}
        public readonly List<Unit> Units=new();
        public float Nearest{get;private set;}=float.MaxValue;
        bool responding;float respondUntil;
        readonly System.Random random=new(113);Mesh body;Material[] bodyMaterials;Mesh lightMesh;Material[] lightMaterials;MaterialPropertyBlock block;
        int lastHits;CityCar watched;float heatCooldown,nextSpawn,clearedAt=-1,bannerAt=-10;string bannerTitle="",bannerSubtitle="";Color bannerColor;
        RoadNetwork Net=>Traffic?Traffic.Network:null;
        void Start()
        {
            block=new MaterialPropertyBlock();var palette=Game.City.City.Materials;
            var paint=new Material(palette[(int)Slot.VehicleBlue]){name="Police blue"};var blue=new Color(.05f,.13f,.38f);paint.SetColor("_BaseColor",blue);paint.color=blue;
            var custom=(Material[])palette.Clone();custom[(int)Slot.VehicleBlue]=paint;
            var sink=new MeshSink();WorldShapes.CarBody(sink,Slot.VehicleBlue);
            // White side band and roof sign, the Polizia di Stato livery pattern on a generic body.
            foreach(float side in new[]{-1f,1f})sink.Box(Slot.White,new Vector3(side*.865f,.66f,0),Vector3.right*.012f,Vector3.up*.07f,Vector3.forward*1.9f);
            body=sink.ToMesh("Police car",custom,out bodyMaterials);
            var lights=(Material[])palette.Clone();if(LightMaterial)lights[(int)Slot.LampGlow]=LightMaterial;
            var bar=new MeshSink();bar.Box(Slot.LampGlow,Vector3.zero,Vector3.right*.28f,Vector3.up*.07f,Vector3.forward*.12f);lightMesh=bar.ToMesh("Police light",lights,out lightMaterials,false);
        }
        public void Raise(int amount,string reason)
        {
            if(amount<=0)return;int before=Wanted;Wanted=Mathf.Clamp(Wanted+amount,0,MaxWanted);Evade=0;clearedAt=-1;responding=true;respondUntil=Time.time+45;
            if(Wanted!=before)Game.Notify(reason+" · WANTED "+new string('*',Wanted));
        }
        public void Clear(){Wanted=0;Evade=0;BustTimer=0;clearedAt=Time.time;}
        void Update()
        {
            if(Game.MapOpen || Net==null)return;float dt=Time.deltaTime;
            // Hit-and-run: a hard knock into a traffic car while driving.
            if(Game.Driving!=watched){watched=Game.Driving;lastHits=watched?watched.TrafficHits:0;}
            if(watched && watched.TrafficHits>lastHits){lastHits=watched.TrafficHits;if(Time.time>heatCooldown){heatCooldown=Time.time+3;Raise(1,"Hit and run");}}
            Nearest=float.MaxValue;foreach(var u in Units)Nearest=Mathf.Min(Nearest,Flat(u.Car.transform.position,Game.Position));
            if(Wanted>0)
            {
                if(Units.Count<Wanted && Time.time>nextSpawn){nextSpawn=Time.time+2;Spawn();}
                // Patrols spawn out of view; evasion only counts once they have had time to respond or have seen you.
                if(Nearest<LoseDistance)responding=false;else if(Time.time>respondUntil)responding=false;
                Evade=Nearest>LoseDistance&&!responding?Evade+dt:Mathf.Max(0,Evade-dt*3);
                if(Evade>=EvadeSeconds){Escapes++;Clear();Banner("ESCAPED","You lost the police",Hud.Money);return;}
                float playerSpeed=Game.Mode=="drive"?Game.Driving.Speed:0;
                BustTimer=Nearest<BustDistance && playerSpeed<2.5f?BustTimer+dt:Mathf.Max(0,BustTimer-dt);
                if(BustTimer>=BustSeconds)Bust();
            }
            else if(Units.Count>0 && clearedAt>=0 && Time.time>clearedAt+6)
            {
                foreach(var u in Units)Destroy(u.Car.gameObject);Units.Clear();
            }
            bool phase=Mathf.Repeat(Time.time,.5f)<.25f;
            foreach(var u in Units)for(int i=0;i<u.Lights.Length;i++)
            {
                bool on=Wanted>0 && (i==0)==phase;block.SetColor("_BaseColor",i==0?new Color(1,.1f,.08f,on?1:.25f):new Color(.15f,.35f,1,on?1:.25f));u.Lights[i].SetPropertyBlock(block);
            }
        }
        void Bust()
        {
            int fine=40*Wanted;Busts++;Clear();
            if(Activities){Activities.Charge(fine);if(Activities.Active)Activities.End("");}
            if(Game.Mode=="drive"){var car=Game.Driving;car.Body.linearVelocity=Vector3.zero;car.Body.angularVelocity=Vector3.zero;Game.ExitCar();}
            Banner("BUSTED","Fine €"+fine,Hud.Danger);
        }
        void Banner(string title,string subtitle,Color color){bannerTitle=title;bannerSubtitle=subtitle;bannerColor=color;bannerAt=Time.unscaledTime;Game.Notify(title+(subtitle!=""?" · "+subtitle:""));}
        /// <summary>A patrol appears on a lane 120–240 m away, out of the camera's close view when possible.</summary>
        void Spawn(float near=120,float far=240)
        {
            var focus=Game.Position;var camera=Camera.main;
            for(int attempt=0;attempt<50;attempt++)
            {
                int lane=random.Next(Net.Lanes.Count);var l=Net.Lanes[lane];if(l.Length<20||!Net.Open(lane))continue;
                var p=Net.Sample(lane,l.Length*.5f,out var tangent);float d=Flat(p,focus);
                if(d<near||d>far)continue;
                if(camera && attempt<35){var vp=camera.WorldToViewportPoint(p);if(vp.z>0&&vp.x>0&&vp.x<1&&vp.y>0&&vp.y<1)continue;}
                var rotation=Quaternion.LookRotation(tangent.WithY(0));
                if(Physics.CheckBox(p+Vector3.up*.85f,new Vector3(1,.5f,2.3f),rotation,(1<<9)|(1<<11),QueryTriggerInteraction.Ignore))continue;
                Create(p+Vector3.up*.3f,rotation);return;
            }
        }
        void Create(Vector3 p,Quaternion rotation)
        {
            Spawned++;var go=new GameObject("Police car "+Spawned);go.transform.SetParent(transform,false);go.transform.SetPositionAndRotation(p,rotation);
            var shell=new GameObject("Body");shell.transform.SetParent(go.transform,false);shell.AddComponent<MeshFilter>().sharedMesh=body;shell.AddComponent<MeshRenderer>().sharedMaterials=bodyMaterials;
            var lights=new Renderer[2];
            for(int i=0;i<2;i++)
            {
                var light=new GameObject(i==0?"Red light":"Blue light");light.transform.SetParent(go.transform,false);light.transform.localPosition=new Vector3(i==0?-.3f:.3f,1.68f,-.05f);
                light.AddComponent<MeshFilter>().sharedMesh=lightMesh;var r=light.AddComponent<MeshRenderer>();r.sharedMaterials=lightMaterials;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;lights[i]=r;
            }
            var (wheelMesh,wheelMaterials)=Traffic.WheelMesh;
            var car=CityCar.Equip(go,()=>{var w=new GameObject("Wheel");w.AddComponent<MeshFilter>().sharedMesh=wheelMesh;w.AddComponent<MeshRenderer>().sharedMaterials=wheelMaterials;return w.transform;});
            car.Autopilot=true;car.TopSpeed=24;Units.Add(new Unit{Car=car,Lights=lights});
        }
        void FixedUpdate()
        {
            if(Net==null || Game.MapOpen)return;float dt=Time.fixedDeltaTime;var target=Game.Position;
            var targetVelocity=Game.Mode=="drive"?Game.Driving.Body.linearVelocity:Vector3.zero;
            for(int i=Units.Count-1;i>=0;i--)
            {
                var u=Units[i];var car=u.Car;var t=car.transform;float d=Flat(t.position,target);
                if(Wanted==0){car.AutoThrottle=0;car.AutoSteer=0;car.AutoBrake=true;continue;}
                car.TopSpeed=24+Wanted*2;
                // Close and in sight: drive straight at the player. Otherwise follow the shortest road route.
                Vector3 aim;bool direct=d<45 && !Physics.Linecast(t.position+Vector3.up,target+Vector3.up,1<<9,QueryTriggerInteraction.Ignore);
                if(direct)aim=target+targetVelocity*.4f;
                else
                {
                    if(Time.time>u.Replan || u.Next>=u.Path.Count)
                    {
                        u.Replan=Time.time+2.5f;u.Path.Clear();u.Next=0;
                        var lanes=Net.DriveRoute(t.position,target);u.Path.AddRange(Net.Along(lanes,8));u.Path.Add(target);
                        // The route starts at the nearest junction, which may be behind the car: resume from the closest point instead.
                        float best=float.MaxValue;for(int k=0;k<u.Path.Count;k++){float e=Flat(t.position,u.Path[k]);if(e<best){best=e;u.Next=k;}}
                    }
                    float lookahead=6+car.Speed*.45f;
                    while(u.Next<u.Path.Count-1 && Flat(t.position,u.Path[u.Next])<lookahead)u.Next++;
                    aim=u.Path[u.Next];
                }
                // Overtake a traffic car in the way: aim into the opposite lane, or the right if a wall is on the left.
                if(d>BustDistance && Physics.BoxCast(t.position+Vector3.up*.8f+t.forward*2.4f,new Vector3(.8f,.4f,.2f),t.forward,out var block,t.rotation,10,1<<11,QueryTriggerInteraction.Ignore)
                    && block.rigidbody && block.rigidbody.isKinematic)
                {
                    var side=-t.right;if(Physics.Raycast(t.position+Vector3.up,side,4,1<<9,QueryTriggerInteraction.Ignore))side=t.right;
                    aim=block.point+t.forward*6+side*3.4f;
                }
                var local=t.InverseTransformPoint(aim);float angle=Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
                float steer=Mathf.Clamp(angle/30,-1,1),limit=Mathf.Lerp(car.TopSpeed,7,Mathf.InverseLerp(10,80,Mathf.Abs(angle)));
                float playerSpeed=targetVelocity.magnitude;
                if(d<BustDistance && playerSpeed<2.5f)limit=0;
                bool reversing=Time.time<u.ReverseUntil;
                if(reversing){car.AutoThrottle=-1;car.AutoSteer=-steer;car.AutoBrake=false;}
                else{car.AutoSteer=steer;car.AutoBrake=car.Speed>limit+3;car.AutoThrottle=car.Speed<limit?1:0;}
                // Stuck against a wall or car: back off briefly, then try again.
                u.Stuck=!reversing && limit>1 && car.Speed<1?u.Stuck+dt:0;
                if(u.Stuck>1.5f){u.Stuck=0;u.ReverseUntil=Time.time+1.2f;u.Replan=0;}
                u.Lost=d>380 || t.up.y<.3f?u.Lost+dt:0;
                // A slow patrol that has not closed in by 5 m for 6 s is wedged somewhere; replace it from out of view.
                if(d<u.Closest-5){u.Closest=d;u.Stalled=0;}else if(d>BustDistance*2 && car.Speed<2)u.Stalled+=dt;
                // Wedged or lost: replace it at once from a closer, out-of-view lane (90–160 m).
                if(u.Stalled>6||u.Lost>3){Destroy(car.gameObject);Units.RemoveAt(i);Spawn(90,160);}
            }
        }
        public IEnumerable<Vector3> Positions{get{foreach(var u in Units)yield return u.Car.transform.position;}}
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        void OnGUI()
        {
            float since=Time.unscaledTime-bannerAt;
            if(!Game || Game.MapOpen || Wanted==0 && since>4)return;
            var old=GUI.matrix;Hud.Begin();
            if(Wanted>0)
            {
                // Stars under the wallet; they flash while the patrols have lost sight of you.
                bool hidden=Nearest>LoseDistance&&!responding;float flash=hidden&&Mathf.Repeat(Time.unscaledTime,.6f)<.3f?.45f:1;
                for(int i=0;i<MaxWanted;i++)
                {
                    var r=new Rect(1570-(MaxWanted-i)*44,74,44,44);
                    Hud.Text(r,"★",40,i<Wanted?new Color(Hud.Gold.r,Hud.Gold.g,Hud.Gold.b,flash):new Color(1,1,1,.25f),TextAnchor.MiddleCenter);
                }
                Hud.Text(new Rect(1170,118,400,26),hidden?"OUT OF SIGHT  "+Mathf.CeilToInt(EvadeSeconds-Evade)+" s":responding&&Nearest>LoseDistance?"POLICE RESPONDING":Nearest<9999?"POLICE  "+Mathf.RoundToInt(Nearest)+" M":"POLICE INCOMING",22,hidden?Hud.Money:Hud.Danger,TextAnchor.UpperRight,false,true);
                if(hidden)Hud.Bar(new Rect(1400,146,170,7),Evade/EvadeSeconds,Hud.Money);
                if(BustTimer>0){Hud.Text(new Rect(200,690,1200,40),"MOVE! The police are boxing you in",30,Hud.Danger,TextAnchor.MiddleCenter,true);Hud.Bar(new Rect(650,734,300,8),BustTimer/BustSeconds,Hud.Danger);}
            }
            if(since<4)Hud.Banner(bannerTitle,bannerSubtitle,bannerColor,since);
            GUI.matrix=old;
        }
    }
}
