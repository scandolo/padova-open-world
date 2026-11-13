using Padova.Architecture;
using Padova.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    [DefaultExecutionOrder(120)]
    public sealed class OpenWorld:MonoBehaviour
    {
        public ThirdPersonMotor Player;public WorldContext City;public CityCar[] Cars;public TourPlane Plane;public CityCrowd Crowd;
        public CityTraffic Traffic;public Activities Activities;public Police Police;public CityTraffic.Vehicle NearbyTraffic{get;private set;}
        /// <summary>Kerbside vehicles around the player, and the one within reach on foot.</summary>
        public ParkedCars Parked;public ParkedCar NearbyParked{get;private set;}
        public Texture2D MapTexture;public bool MapOpen;public string Mode="walk";
        /// <summary>Second map sheet at 1 m per pixel around the centre, and the world rectangles (x0, z0, x1, z1) both sheets cover.</summary>
        public Texture2D MapDetail;public Vector4 MapExtent=new Vector4(-1500,-1300,1500,1300),DetailExtent;
        /// <summary>Neighbourhood the player is in, announced when it changes.</summary>
        public string District="";float districtSince=-10;
        public CityCar Driving{get;private set;}public CityCar Nearby{get;private set;}
        public bool HasWaypoint;public Vector3 Waypoint;public string DestinationName="";
        public float MapSpan=800;public Vector2 MapCentre;public int Discoveries;
        public bool[] Visited;public string Notice="Welcome to Padova  ·  E to take a car  ·  J for activities";
        Vector3 returnPoint;float returnYaw;float noticeUntil=18,nextLocation,locationSince=-10;Vector2 dragStart;bool dragging;
        public Material MinimapMaterial;public bool ShowHelp=true;bool helpSeen;public string Location="";
        InputAction mapAction,interactAction,flightAction,resetAction,escapeAction,clickAction,helpAction;
        bool mapRequested,interactRequested,flightRequested,resetRequested,escapeRequested,clickDown,clickUp;
        /// <summary>Crash feedback: number of hard knocks answered with a camera jolt and a beat of slow motion.</summary>
        public int CrashJolts{get;private set;}
        int impactSeen;CityCar impactCar;float slowUntil;
        /// <summary>The world in play; null in menus-only scenes and after the scene unloads.</summary>
        public static OpenWorld Current{get;private set;}
        /// <summary>A full-screen menu (title, pause) is up.</summary>
        public bool Menu;
        /// <summary>True while the map (or anything else modal) has stopped the simulation.</summary>
        public bool Halted=>MapOpen||Menu;
        public Vector3 Position=>Mode=="drive"&&Driving?Driving.transform.position:Mode=="fly"?Plane.transform.position:Player.transform.position;
        public Rect MapRect=>new Rect(240,140,1120,680);
        /// <summary>Map sheet for a view: the detailed centre sheet when the view fits inside it, the whole comune otherwise.</summary>
        (Texture2D sheet,Vector4 extent) Sheet(Vector2 centre,float spanX,float spanZ)
        {
            var d=DetailExtent;
            if(MapDetail && spanX<2600 && centre.x-spanX*.5f>d.x && centre.x+spanX*.5f<d.z && centre.y-spanZ*.5f>d.y && centre.y+spanZ*.5f<d.w)return (MapDetail,d);
            return (MapTexture,MapExtent);
        }
        void Awake()
        {
            Current=this;
            InputAction Button(string name,string binding,System.Action action){var a=new InputAction(name,InputActionType.Button,binding);a.performed+=_=>action();a.Enable();return a;}
            mapAction=Button("Map","<Keyboard>/m",()=>mapRequested=true);
            interactAction=Button("Enter car","<Keyboard>/e",()=>interactRequested=true);interactAction.AddBinding("<Keyboard>/enter");interactAction.AddBinding("<Keyboard>/numpadEnter");
            flightAction=Button("Flight","<Keyboard>/f",()=>flightRequested=true);resetAction=Button("Reset vehicle","<Keyboard>/r",()=>resetRequested=true);
            escapeAction=Button("Close map","<Keyboard>/escape",()=>escapeRequested=true);
            helpAction=Button("Controls","<Keyboard>/h",()=>{ShowHelp=!ShowHelp;helpSeen=true;});
            clickAction=Button("Map click","<Mouse>/leftButton",()=>clickDown=true);clickAction.canceled+=_=>clickUp=true;
        }
        void Start()
        {
            if(Plane)Plane.Half=Player.WorldHalfSize-new Vector2(170,170);
            // Cars parked in the scene take their catalog models.
            var specs=new[]{VehicleCatalog.Berlina,VehicleCatalog.Utilitaria,VehicleCatalog.Utilitaria};var paints=new[]{new Color(.1f,.27f,.5f),new Color(.76f,.14f,.1f),new Color(.93f,.9f,.8f)};
            for(int i=0;i<Cars.Length&&i<specs.Length;i++)if(Cars[i])Cars[i]=VehicleFactory.Replace(Cars[i],specs[i],paints[i]);
            var hud=Player.GetComponent<WalkHud>();if(!hud)hud=FindFirstObjectByType<WalkHud>();if(hud){if(hud.GoalMarker)hud.GoalMarker.gameObject.SetActive(false);hud.enabled=false;}
            Notice="Welcome to Padova  ·  E to take a car  ·  J for activities";            Visited=new bool[City.Data.landmarks.Length];MapCentre=new Vector2(Player.transform.position.x,Player.transform.position.z);
        }
        void Update()
        {
            var k=Keyboard.current;if(k==null)return;
            bool interact=interactRequested,flight=flightRequested,reset=resetRequested,escape=escapeRequested,down=clickDown,up=clickUp;
            interactRequested=flightRequested=resetRequested=escapeRequested=clickDown=clickUp=false;
            if(ShowHelp && !helpSeen && Time.time>15){ShowHelp=false;helpSeen=true;}
            if(mapRequested){mapRequested=false;SetMap(!MapOpen);}
            // One place owns the clock: stopped while a modal screen is up, a beat of slow motion after a hard crash.
            Time.timeScale=Halted?0:Time.unscaledTime<slowUntil?.4f:1;
            if(MapOpen)
            {
                if(escape)SetMap(false);
                var mouse=Mouse.current;if(mouse!=null)
                {
                    Vector2 point=new Vector2(mouse.position.x.ReadValue()*1600/Screen.width,(Screen.height-mouse.position.y.ReadValue())*1000/Screen.height);
                    if(MapRect.Contains(point))
                    {
                        float scroll=mouse.scroll.y.ReadValue();if(Mathf.Abs(scroll)>0)MapSpan=Mathf.Clamp(MapSpan*Mathf.Exp(-scroll*.0015f),180,Mathf.Max(3000,MapExtent.z-MapExtent.x));
                        if(down){dragStart=point;dragging=false;}
                        if(mouse.leftButton.isPressed && (point-dragStart).magnitude>5)dragging=true;
                        if(mouse.leftButton.isPressed && dragging)
                        {var delta=mouse.delta.ReadValue();MapCentre-=new Vector2(delta.x*1600/Screen.width,delta.y*1000/Screen.height)*MapSpan/MapRect.width;}
                        if(up && !dragging)SetWaypoint(MapToWorld(point),"Custom waypoint");
                    }
                }
                return;
            }
            Nearby=null;if(Mode=="walk")foreach(var car in Cars)if(Vector3.Distance(car.transform.position,Player.transform.position)<5.5f){Nearby=car;break;}
            NearbyParked=Mode=="walk" && !Nearby && Parked?Parked.NearestTo(Player.transform.position,4.6f):null;
            NearbyTraffic=Mode=="walk" && !Nearby && !NearbyParked && Traffic?Traffic.NearestTo(Player.transform.position,5.5f):null;
            if(interact){if(Mode=="drive")ExitCar();else if(Mode=="walk" && Nearby)EnterCar(Nearby);else if(NearbyParked)TakeParked(NearbyParked);else if(NearbyTraffic!=null)TakeTrafficCar(NearbyTraffic);}
            if(flight){if(Mode=="fly")EndFlight();else LaunchFlight();}
            if(reset){if(Mode=="drive")RecoverCar();else if(Mode=="fly")EndFlight();}
            if(Mode=="drive")CrashFeedback();
            if(HasWaypoint && Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(Waypoint.x,Waypoint.z))<8 && Mode!="fly")
            {HasWaypoint=false;Notify("Arrived · "+DestinationName);}
            if(Mode!="fly")for(int i=0;i<Visited.Length;i++)
            {
                var p=City.Data.landmarks[i];if(!Visited[i]&&Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(p.x,p.z))<35)
                {Visited[i]=true;Discoveries++;Notify("Discovered · "+p.name);}
            }
        }
        public void Notify(string message){Notice=message;noticeUntil=Time.unscaledTime+7;}
        /// <summary>A hard knock jolts the camera and, when it is a real crash, slows time for an instant.</summary>
        void CrashFeedback()
        {
            if(impactCar!=Driving){impactCar=Driving;impactSeen=Driving.ImpactSerial;return;}
            if(Driving.ImpactSerial==impactSeen)return;impactSeen=Driving.ImpactSerial;
            float strength=Mathf.InverseLerp(4,26,Driving.LastImpact);Player.FollowCamera.AddShake(.25f+strength*.75f);CrashJolts++;
            if(Driving.LastImpact>11)slowUntil=Time.unscaledTime+.16f;
        }
        /// <summary>Height of the drivable surface at a point: the ground collider when one is loaded, the terrain model otherwise.</summary>
        public float SurfaceAt(Vector3 p)
        {
            float guess=City.Data.GroundAt(p.x,p.z);
            return Physics.Raycast(new Vector3(p.x,guess+30,p.z),Vector3.down,out var hit,60,1<<8)?hit.point.y:guess;
        }
        /// <summary>R while driving: the car is set upright on the nearest lane, facing along it. Works when flipped, wedged or lost.</summary>
        public void RecoverCar()
        {
            if(!Driving)return;var car=Driving;var p=car.transform.position;var rotation=Quaternion.Euler(0,car.transform.eulerAngles.y,0);
            var net=Traffic?Traffic.Network:null;
            if(net!=null)
            {
                var (lane,s,_)=net.Nearest(p,true);
                if(lane>=0)
                {
                    p=net.Sample(lane,s,out var tangent);var flat=tangent.WithY(0);
                    // Keep facing roughly the way the driver was heading: the opposite lane is a metre or two away anyway.
                    if(flat.sqrMagnitude>.001f)rotation=Quaternion.LookRotation(Vector3.Dot(flat,car.transform.forward)<0?-flat:flat);
                }
            }
            p.y=SurfaceAt(p)+.3f;car.Place(p,rotation);Player.FollowCamera.ResetCamera();Notify("Back on the road");
        }
        public void SetMap(bool open)
        {
            MapOpen=open;Player.ControlsPaused=open;Time.timeScale=open?0:1;
            if(open){MapCentre=new Vector2(Position.x,Position.z);Player.FollowCamera.ReleaseCursor();}
            if(Driving)Driving.Paused=open;Plane.Paused=open;
        }
        void PlayerActive(bool active,bool camera)
        {
            Player.enabled=active;Player.GetComponent<CharacterController>().enabled=active;Player.Visual.gameObject.SetActive(active);Player.FollowCamera.enabled=camera;
        }
        public void EnterCar(CityCar car)
        {
            if(MapOpen || Mode!="walk" || !car || Vector3.Distance(car.transform.position,Player.transform.position)>6)return;
            returnPoint=Player.transform.position;returnYaw=Player.FollowCamera.Yaw;PlayerActive(false,true);Driving=car;car.Driven=true;Mode="drive";
            Player.FollowCamera.Vehicle=car.transform;Player.FollowCamera.VehicleSize=Mathf.Clamp(car.Spec.Length/4.1f,.62f,2.2f);Player.FollowCamera.ResetCamera();
            Notify("Shift turbo  ·  Space handbrake  ·  R reset  ·  E get out");
        }
        void TakeTrafficCar(CityTraffic.Vehicle vehicle)
        {
            Traffic.Release(vehicle,v=>
            {
                var car=VehicleFactory.Drivable(v.Spec,v.Paint,v.Go.transform.position,v.Go.transform.rotation,transform);car.name=v.Spec.Label;
                AddCar(car);EnterCar(car);if(Police)Police.Raise(1,"Car theft reported");
            });
            NearbyTraffic=null;
        }
        /// <summary>A parked vehicle is there for the taking; now and then one has an alarm.</summary>
        void TakeParked(ParkedCar parked)
        {
            bool alarm=parked.Key%7==0;var car=Parked.Take(parked);NearbyParked=null;if(!car)return;
            AddCar(car);EnterCar(car);if(alarm && Police)Police.Raise(1,"Car alarm · theft reported");
        }
        public void AddCar(CityCar car){var cars=new System.Collections.Generic.List<CityCar>(Cars){car};Cars=cars.ToArray();}
        public void RemoveCar(CityCar car){var cars=new System.Collections.Generic.List<CityCar>(Cars);cars.Remove(car);Cars=cars.ToArray();}
        public bool ExitCar()
        {
            if(!Driving)return false;if(Driving.Speed>4){Notify("Slow down before getting out.");return false;}
            Vector3 exit=returnPoint;bool found=false;
            foreach(var direction in new[]{Driving.transform.right,-Driving.transform.right,-Driving.transform.forward})
            {
                var p=Driving.transform.position+direction*2.3f;
                if(!Physics.Raycast(p+Vector3.up*5,Vector3.down,out var hit,12,1<<8))continue;
                p=hit.point+Vector3.up*.04f;
                if(Physics.CheckCapsule(p+Vector3.up*.4f,p+Vector3.up*1.4f,.35f,1<<9))continue;
                exit=p;found=true;break;
            }
            if(!found){Notify("No room to exit here. Move to an open stretch.");return false;}
            float yaw=Driving.transform.eulerAngles.y;Driving.Driven=false;Driving=null;Player.FollowCamera.Vehicle=null;Mode="walk";RestoreFoot(exit,yaw);return true;
        }
        public void LaunchFlight()
        {
            if(MapOpen)return;if(Police && Police.Wanted>0){Notify("Lose the police before taking off.");return;}if(Mode=="drive"&&!ExitCar())return;
            returnPoint=Player.transform.position;returnYaw=Player.FollowCamera.Yaw;PlayerActive(false,false);Mode="fly";
            Plane.Launch(new Vector3(returnPoint.x,150,returnPoint.z),returnYaw);Notify("Sightseeing flight · Arrows climb / bank · F return to your walk");
        }
        public void EndFlight(){Plane.Flying=false;Plane.gameObject.SetActive(false);Mode="walk";RestoreFoot(returnPoint,returnYaw);}
        void RestoreFoot(Vector3 p,float yaw)
        {
            Player.transform.position=p;Player.VerticalSpeed=-2;PlayerActive(true,true);Player.Visual.rotation=Quaternion.Euler(0,yaw,0);Player.FollowCamera.ResetCamera();
        }
        void LateUpdate()
        {
            if(MapOpen || Mode=="walk")return;
            var camera=Player.FollowCamera.transform;Vector3 target,eye;
            if(Mode=="drive")
            {
                // The shared chase camera follows the car; feed it the car's motion.
                var follow=Player.FollowCamera;follow.VehicleSpeed=Driving.Speed;follow.VehicleBoosting=Driving.Boosting;follow.VehicleVelocity=Driving.Body.linearVelocity;
                follow.VehicleReversing=Driving.ForwardSpeed<-3;return;
            }
            else
            {
                var heading=Quaternion.Euler(0,Plane.transform.eulerAngles.y,0)*Vector3.forward;
                target=Plane.transform.position+heading*16-Vector3.up*4;
                eye=Plane.transform.position-heading*24+Vector3.up*18;
            }
            camera.position=Vector3.Lerp(camera.position,eye,1-Mathf.Exp(-6*Time.deltaTime));camera.rotation=Quaternion.Slerp(camera.rotation,Quaternion.LookRotation(target-camera.position),1-Mathf.Exp(-8*Time.deltaTime));
        }
        public Vector2 WorldToMap(Vector3 world)
        {var r=MapRect;return new Vector2(r.center.x+(world.x-MapCentre.x)*r.width/MapSpan,r.center.y-(world.z-MapCentre.y)*r.width/MapSpan);}
        public Vector3 MapToWorld(Vector2 point)
        {var r=MapRect;float x=MapCentre.x+(point.x-r.center.x)*MapSpan/r.width,z=MapCentre.y-(point.y-r.center.y)*MapSpan/r.width;return new Vector3(x,City.Data.GroundAt(x,z),z);}
        public void SetWaypoint(Vector3 point,string name,bool notify=true){Waypoint=point;DestinationName=name;HasWaypoint=true;if(notify)Notify("Waypoint set · "+name);}
        void Dot(Vector2 p,Color color,float size=9){Hud.Disc(p,size,color);}
        // Street or place under the player, refreshed about once a second for the location banner and minimap caption.
        void UpdateLocation()
        {
            if(Time.unscaledTime<nextLocation)return;nextLocation=Time.unscaledTime+.8f;string name="";
            foreach(var p in City.Data.landmarks)if(Vector2.Distance(new Vector2(p.x,p.z),new Vector2(Position.x,Position.z))<(p.rank<=1?70:38)){name=p.name;break;}
            string district=City.Places!=null?City.Places.DistrictAt(Position.x,Position.z):"";
            if(district!=District){District=district;if(district!="")districtSince=Time.unscaledTime;}
            if(name=="" && Traffic && Traffic.Network!=null){var (lane,_,distance)=Traffic.Network.Nearest(Position);if(lane>=0&&distance<30)name=Traffic.Network.Lanes[lane].Name;}
            if(name=="")name=Location==""?"Padova":Location;
            if(name!=Location){Location=name;locationSince=Time.unscaledTime;}
        }
        float Heading=>Mode=="drive"?Driving.transform.eulerAngles.y:Mode=="fly"?Plane.transform.eulerAngles.y:Player.Visual.eulerAngles.y;
        void Minimap()
        {
            var centre=new Vector2(158,840);const float radius=118;float span=Mode=="fly"?1100:Mode=="drive"?380+Driving.Speed*7:260;
            float view=Camera.main?Camera.main.transform.eulerAngles.y:0,s=Mathf.Sin(view*Mathf.Deg2Rad),c=Mathf.Cos(view*Mathf.Deg2Rad);
            Hud.Disc(centre,radius*2+16,new Color(0,0,0,.35f));Hud.Disc(centre,radius*2+6,new Color(.98f,.95f,.88f,.9f));
            var rect=new Rect(centre.x-radius,centre.y-radius,radius*2,radius*2);
            if(MinimapMaterial && Event.current.type==EventType.Repaint)
            {
                var (sheet,e)=Sheet(new Vector2(Position.x,Position.z),span*1.5f,span*1.5f);float sw=e.z-e.x,sh=e.w-e.y;
                MinimapMaterial.SetVector("_Center",new Vector4((Position.x-e.x)/sw,(Position.z-e.y)/sh,0,0));MinimapMaterial.SetVector("_Scale",new Vector4(span/sw,span/sh,0,0));
                MinimapMaterial.SetFloat("_Angle",view*Mathf.Deg2Rad);Graphics.DrawTexture(rect,sheet,MinimapMaterial);
            }
            else if(!MinimapMaterial)Hud.Disc(centre,radius*2,new Color(.2f,.24f,.25f,.9f));
            // World offset → minimap, rotated so the camera's view points up; off-map markers stick to the rim.
            Vector2 ToMini(Vector3 world,bool clamp)
            {
                float dx=world.x-Position.x,dz=world.z-Position.z;var p=new Vector2(dx*c-dz*s,dx*s+dz*c)*(radius/(span/2));
                if(clamp&&p.magnitude>radius-9)p=p.normalized*(radius-9);return new Vector2(centre.x+p.x,centre.y-p.y);
            }
            bool Inside(Vector2 m)=>(m-centre).magnitude<radius-4;
            if(Activities && Activities.Active)for(int i=0;i<Activities.Route.Count;i++){var m=ToMini(Activities.Route[i],false);if(Inside(m))Dot(m,Hud.Cool,6);}
            for(int i=0;i<Visited.Length;i++){var p=City.Data.landmarks[i];var m=ToMini(new Vector3(p.x,0,p.z),false);if(Inside(m))Dot(m,Visited[i]?new Color(.75f,.45f,.25f,.9f):Hud.Cream,Visited[i]?7:10);}
            foreach(var car in Cars){if(car==Driving)continue;var m=ToMini(car.transform.position,false);if(Inside(m))Dot(m,new Color(.2f,.55f,.9f),8);}
            if(Police){bool blink=Mathf.Repeat(Time.unscaledTime,.5f)<.25f;foreach(var p in Police.Positions){var m=ToMini(p,true);Dot(m,Color.white,13);Dot(m,blink?Hud.Danger:new Color(.2f,.4f,1),9);}}
            if(HasWaypoint){var m=ToMini(Waypoint,true);Dot(m,new Color(0,0,0,.6f),17);Dot(m,Hud.Gold,13);}
            var n=ToMini(Position+new Vector3(0,0,1),false)-centre;var north=centre+n.normalized*(radius+1);Dot(north,new Color(.1f,.12f,.13f),22);
            Hud.Text(new Rect(north.x-11,north.y-11,22,22),"N",15,Color.white,TextAnchor.MiddleCenter,true);
            var old=GUI.matrix;GUIUtility.RotateAroundPivot(Heading-view,centre);
            Hud.Text(new Rect(centre.x-15,centre.y-17,30,30),"▲",24,Color.white,TextAnchor.MiddleCenter);GUI.matrix=old;
        }
        void OnGUI()
        {
            if(!Player || City.Data==null)return;var old=GUI.matrix;Hud.Begin();
            if(MapOpen){DrawMap();GUI.matrix=old;return;}
            UpdateLocation();Minimap();
            float sinceDistrict=Time.unscaledTime-districtSince;
            if(sinceDistrict<5 && District!="")
            {
                float a=Mathf.Clamp01(sinceDistrict*2)*Mathf.Clamp01((5-sinceDistrict)*1.2f);
                Hud.Text(new Rect(300,96,1000,34),"PADOVA  ·  QUARTIERE",20,new Color(1,1,1,a*.8f),TextAnchor.MiddleCenter,true);
                Hud.Text(new Rect(200,124,1200,90),District.ToUpperInvariant(),76,new Color(Hud.Cream.r,Hud.Cream.g,Hud.Cream.b,a),TextAnchor.MiddleCenter,false,true);
            }
            Hud.Text(new Rect(292,868,420,30),Location,24,Color.white,TextAnchor.UpperLeft,true);
            Hud.Text(new Rect(292,898,420,24),Discoveries+" / "+Visited.Length+" places discovered",17,Hud.Muted);
            Hud.Text(new Rect(292,922,420,24),"<b>M</b> map   ·   <b>J</b> activities   ·   <b>H</b> controls",17,Hud.Muted);
            // Area name appears big when you enter a new street or piazza, then fades.
            float since=Time.unscaledTime-locationSince;
            if(since<4.5f){float a=Mathf.Clamp01(since*3)*Mathf.Clamp01((4.5f-since)*1.5f);Hud.Text(new Rect(700,Mode=="drive"?700:840,860,80),Location.ToUpperInvariant(),58,new Color(Hud.Cream.r,Hud.Cream.g,Hud.Cream.b,a),TextAnchor.LowerRight,false,true);}
            if(Mode=="drive")
            {
                Hud.Text(new Rect(1290,812,240,110),Mathf.RoundToInt(Driving.Speed*3.6f).ToString(),104,Driving.Boosting?Hud.Gold:Color.white,TextAnchor.LowerRight,false,true);
                Hud.Text(new Rect(1500,872,80,40),"KM/H",22,Hud.Muted,TextAnchor.LowerLeft,false,true);
                Hud.Text(new Rect(1330,920,250,22),Driving.Boosting?"TURBO":"SHIFT  TURBO",16,Driving.Boosting?Hud.Gold:Hud.Muted,TextAnchor.UpperRight,true);
                Hud.Bar(new Rect(1350,944,220,9),Driving.Boost,Driving.Boosting?new Color(1,.55f,.15f):new Color(1,.78f,.3f,.85f));
            }
            else if(Mode=="fly")
            {
                Hud.Text(new Rect(1290,812,240,110),Mathf.RoundToInt(Plane.Speed*3.6f).ToString(),104,Color.white,TextAnchor.LowerRight,false,true);
                Hud.Text(new Rect(1500,872,80,40),"KM/H",22,Hud.Muted,TextAnchor.LowerLeft,false,true);
                Hud.Text(new Rect(1290,920,280,26),"ALTITUDE  "+Mathf.RoundToInt(Plane.Altitude)+" M",20,Hud.Cool,TextAnchor.UpperRight,true);
            }
            if(Time.unscaledTime<noticeUntil)
            {
                float a=Mathf.Clamp01((noticeUntil-Time.unscaledTime)*2);float w=Mathf.Min(720,Hud.Width(Notice,21,true)+44);
                Hud.Panel(new Rect(32,30,w,52),new Color(Hud.Ink.r,Hud.Ink.g,Hud.Ink.b,Hud.Ink.a*a));Hud.Fill(new Rect(32,38,4,36),new Color(Hud.Gold.r,Hud.Gold.g,Hud.Gold.b,a));
                Hud.Text(new Rect(52,30,w-30,52),Notice,21,new Color(1,1,1,a),TextAnchor.MiddleLeft,true);
            }
            if(HasWaypoint && Camera.main)
            {
                var p=Camera.main.WorldToScreenPoint(Waypoint+Vector3.up*3);
                if(p.z>0)
                {
                    var v=new Vector2(p.x*1600/Screen.width,(Screen.height-p.y)*1000/Screen.height);float distance=Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(Waypoint.x,Waypoint.z));
                    var o=GUI.matrix;GUIUtility.RotateAroundPivot(45,v);Hud.Fill(new Rect(v.x-10,v.y-10,20,20),new Color(0,0,0,.5f));Hud.Fill(new Rect(v.x-7,v.y-7,14,14),Hud.Gold);GUI.matrix=o;
                    Hud.Text(new Rect(v.x-80,v.y+14,160,24),Mathf.RoundToInt(distance)+" m",18,Color.white,TextAnchor.UpperCenter,true);
                }
            }
            if(Mode=="drive" && Driving.Stuck)Hud.Prompt(new Vector2(800,790),"R",Driving.transform.up.y<.35f?"Flip the car back onto its wheels":"Stuck? Put the car back on the road");
            if(Nearby && Mode=="walk")Hud.Prompt(new Vector2(800,790),"E","Drive the "+Nearby.name);
            else if(NearbyParked && Mode=="walk")Hud.Prompt(new Vector2(800,790),"E","Take the "+NearbyParked.Spec.Label);
            else if(NearbyTraffic!=null)Hud.Prompt(new Vector2(800,790),"E","Steal the "+NearbyTraffic.Colour);
            if(ShowHelp)Help();
            Hud.Text(new Rect(34,978,1300,20),"Comune di Padova / Regione Veneto · IODL 2.0   ·   Names © OpenStreetMap · ODbL   ·   Poly Haven / Kenney · CC0   ·   Façades and street life are authored typology",12,new Color(1,1,1,.5f));
            GUI.matrix=old;
        }
        void Help()
        {
            string[][] rows=Mode=="drive"?new[]{new[]{"W A S D / ↑ ← ↓ →","Drive and steer"},new[]{"SHIFT","Turbo"},new[]{"SPACE","Handbrake"},new[]{"E","Get out"},new[]{"Q","Horn"},new[]{"R","Reset car"},new[]{"C","Camera view"},new[]{"T","Time of day"},new[]{"M","Map"},new[]{"J","Activities"}}
                :Mode=="fly"?new[]{new[]{"↑ ↓","Climb / descend"},new[]{"← →","Bank"},new[]{"SHIFT / SPACE","Faster / slower"},new[]{"F","Land and walk"},new[]{"M","Map"}}
                :new[]{new[]{"W A S D / ↑ ← ↓ →","Walk"},new[]{"SHIFT","Sprint"},new[]{"SPACE","Jump"},new[]{"E","Drive / steal a car"},new[]{"F","Sightseeing flight"},new[]{"MOUSE DRAG","Look around"},new[]{"C","Camera view"},new[]{"T","Time of day"},new[]{"M","Map"},new[]{"J","Activities"}};
            float h=78+rows.Length*36;var r=new Rect(1180,150,390,h);Hud.Panel(r,Hud.Ink);
            Hud.Text(new Rect(r.x+24,r.y+16,340,40),"CONTROLS",34,Hud.Gold,TextAnchor.UpperLeft,false,true);Hud.Text(new Rect(r.x+24,r.y+20,342,30),"H to hide",15,Hud.Muted,TextAnchor.UpperRight);
            for(int i=0;i<rows.Length;i++){float y=r.y+66+i*36;Hud.Text(new Rect(r.x+24,y,170,30),rows[i][0],17,Hud.Cream,TextAnchor.MiddleLeft,true);Hud.Text(new Rect(r.x+196,y,180,30),rows[i][1],18,Color.white,TextAnchor.MiddleLeft);}
        }
        bool Button(Rect r,string key,string action)
        {
            var centre=r.center;Hud.Prompt(centre,key,action,18);return GUI.Button(r,GUIContent.none,GUIStyle.none);
        }
        void DrawMap()
        {
            Hud.Fill(new Rect(0,0,1600,1000),new Color(0,0,0,.45f));Hud.Panel(new Rect(180,62,1240,812),new Color(.03f,.05f,.06f,.95f));
            Hud.Text(new Rect(214,74,700,60),"PADOVA",52,Hud.Cream,TextAnchor.UpperLeft,false,true);Hud.Text(new Rect(410,92,600,30),"City map",22,Hud.Muted,TextAnchor.UpperLeft,true);
            if(Button(new Rect(1225,84,160,40),"M","Close"))SetMap(false);
            var r=MapRect;float h=MapSpan*r.height/r.width;
            var (sheet,e)=Sheet(MapCentre,MapSpan,h);float sw=e.z-e.x,sh=e.w-e.y;
            GUI.DrawTextureWithTexCoords(r,sheet,new Rect((MapCentre.x-MapSpan/2-e.x)/sw,(MapCentre.y-h/2-e.y)/sh,MapSpan/sw,h/sh));
            var label=Hud.Style(15,true);
            GUI.BeginGroup(r);
            if(Activities && Activities.Active)foreach(var point in Activities.Route)Dot(WorldToMap(point)-r.position,Hud.Cool,6);
            if(Police)foreach(var point in Police.Positions){var p=WorldToMap(point)-r.position;Dot(p,Color.white,15);Dot(p,Hud.Danger,10);}
            foreach(var car in Cars){var p=WorldToMap(car.transform.position)-r.position;if(p.x>0&&p.y>0&&p.x<r.width&&p.y<r.height){Dot(p,Color.white,13);Dot(p,new Color(.14f,.45f,.75f),9);Hud.Text(new Rect(p.x+9,p.y-11,130,24),"Car",15,new Color(.1f,.2f,.3f),TextAnchor.UpperLeft,true);}}
            var occupied=new System.Collections.Generic.List<Rect>();
            foreach(var place in City.Data.landmarks)
            {
                var p=WorldToMap(new Vector3(place.x,0,place.z))-r.position;
                if(p.x<0||p.y<0||p.x>r.width||p.y>r.height)continue;
                Dot(p,new Color(.7f,.29f,.15f),place.rank<=1?10:6);
                // Zoomed out, only the city's icons are named.
                if(place.rank>1 && MapSpan>1400)continue;
                float width=label.CalcSize(new GUIContent(place.name)).x+8;
                var rect=new Rect(Mathf.Clamp(p.x+8,0,r.width-width),p.y-10,width,22);
                for(int attempt=0;attempt<10;attempt++)
                {
                    bool overlaps=false;foreach(var other in occupied)if(rect.Overlaps(other)){overlaps=true;break;}
                    if(!overlaps)break;rect.y+=23;if(rect.yMax>r.height)rect.y=p.y-33-attempt*23;
                }
                occupied.Add(rect);var old=GUI.color;GUI.color=new Color(.16f,.2f,.23f);GUI.Label(rect,place.name,label);GUI.color=old;
            }
            var current=WorldToMap(Position)-r.position;Dot(current,Color.white,20);Dot(current,new Color(.12f,.56f,.55f),13);
            if(HasWaypoint){var p=WorldToMap(Waypoint)-r.position;Dot(p,new Color(0,0,0,.6f),18);Dot(p,Hud.Gold,14);}
            GUI.EndGroup();
            Hud.Text(new Rect(214,834,960,26),"Drag to pan   ·   Scroll to zoom   ·   Click to set a waypoint   ·   Blue: cars   ·   Red: police",18,Hud.Muted,TextAnchor.UpperLeft,true);
            if(Button(new Rect(1190,826,195,40),"C","Centre on me"))MapCentre=new Vector2(Position.x,Position.z);
        }
        void OnDestroy(){if(Current==this)Current=null;Time.timeScale=1;mapAction?.Dispose();interactAction?.Dispose();flightAction?.Dispose();resetAction?.Dispose();escapeAction?.Dispose();clickAction?.Dispose();helpAction?.Dispose();}
    }
}
