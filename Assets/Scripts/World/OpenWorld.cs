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
        public CityTraffic Traffic;public Activities Activities;public CityTraffic.Vehicle NearbyTraffic{get;private set;}
        public Texture2D MapTexture;public bool MapOpen;public string Mode="walk";
        public CityCar Driving{get;private set;}public CityCar Nearby{get;private set;}
        public bool HasWaypoint;public Vector3 Waypoint;public string DestinationName="";
        public float MapSpan=800;public Vector2 MapCentre;public int Discoveries;
        public bool[] Visited;public string Notice="Explore the market. Cars are marked on the map.";
        Vector3 returnPoint;float returnYaw;float noticeUntil=18;Vector2 dragStart;bool dragging;GUIStyle title,body,small,label;
        InputAction mapAction,interactAction,flightAction,resetAction,escapeAction,clickAction;
        bool mapRequested,interactRequested,flightRequested,resetRequested,escapeRequested,clickDown,clickUp;
        public Vector3 Position=>Mode=="drive"&&Driving?Driving.transform.position:Mode=="fly"?Plane.transform.position:Player.transform.position;
        public Rect MapRect=>new Rect(240,140,1120,680);
        void Awake()
        {
            InputAction Button(string name,string binding,System.Action action){var a=new InputAction(name,InputActionType.Button,binding);a.performed+=_=>action();a.Enable();return a;}
            mapAction=Button("Map","<Keyboard>/m",()=>mapRequested=true);
            interactAction=Button("Enter car","<Keyboard>/enter",()=>interactRequested=true);interactAction.AddBinding("<Keyboard>/numpadEnter");
            flightAction=Button("Flight","<Keyboard>/f",()=>flightRequested=true);resetAction=Button("Reset vehicle","<Keyboard>/r",()=>resetRequested=true);
            escapeAction=Button("Close map","<Keyboard>/escape",()=>escapeRequested=true);
            clickAction=Button("Map click","<Mouse>/leftButton",()=>clickDown=true);clickAction.canceled+=_=>clickUp=true;
        }
        void Start()
        {
            var hud=Player.GetComponent<WalkHud>();if(!hud)hud=FindFirstObjectByType<WalkHud>();if(hud){if(hud.GoalMarker)hud.GoalMarker.gameObject.SetActive(false);hud.enabled=false;}
            Visited=new bool[City.Data.landmarks.Length];MapCentre=new Vector2(Player.transform.position.x,Player.transform.position.z);
        }
        void Update()
        {
            var k=Keyboard.current;if(k==null)return;
            bool interact=interactRequested,flight=flightRequested,reset=resetRequested,escape=escapeRequested,down=clickDown,up=clickUp;
            interactRequested=flightRequested=resetRequested=escapeRequested=clickDown=clickUp=false;
            if(mapRequested){mapRequested=false;SetMap(!MapOpen);}
            if(MapOpen)
            {
                if(escape)SetMap(false);
                var mouse=Mouse.current;if(mouse!=null)
                {
                    Vector2 point=new Vector2(mouse.position.x.ReadValue()*1600/Screen.width,(Screen.height-mouse.position.y.ReadValue())*1000/Screen.height);
                    if(MapRect.Contains(point))
                    {
                        float scroll=mouse.scroll.y.ReadValue();if(Mathf.Abs(scroll)>0)MapSpan=Mathf.Clamp(MapSpan*Mathf.Exp(-scroll*.0015f),180,3000);
                        if(down){dragStart=point;dragging=false;}
                        if(mouse.leftButton.isPressed && (point-dragStart).magnitude>5)dragging=true;
                        if(mouse.leftButton.isPressed && dragging)
                        {var delta=mouse.delta.ReadValue();MapCentre-=new Vector2(delta.x*1600/Screen.width,delta.y*1000/Screen.height)*MapSpan/MapRect.width;}
                        if(up && !dragging)SetWaypoint(MapToWorld(point),"Custom waypoint");
                    }
                }
                return;
            }
            Nearby=null;if(Mode=="walk")foreach(var car in Cars)if(Vector3.Distance(car.transform.position,Player.transform.position)<4.5f){Nearby=car;break;}
            NearbyTraffic=Mode=="walk" && !Nearby && Traffic?Traffic.NearestTo(Player.transform.position,4.5f):null;
            if(interact){if(Mode=="drive")ExitCar();else if(Mode=="walk" && Nearby)EnterCar(Nearby);else if(NearbyTraffic!=null)TakeTrafficCar(NearbyTraffic);}
            if(flight){if(Mode=="fly")EndFlight();else LaunchFlight();}
            if(reset){if(Mode=="drive")Driving.ResetCar();else if(Mode=="fly")EndFlight();}
            if(HasWaypoint && Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(Waypoint.x,Waypoint.z))<8 && Mode!="fly")
            {HasWaypoint=false;Notify("Arrived · "+DestinationName);}
            if(Mode!="fly")for(int i=0;i<Visited.Length;i++)
            {
                var p=City.Data.landmarks[i];if(!Visited[i]&&Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(p.x,p.z))<35)
                {Visited[i]=true;Discoveries++;Notify("Discovered · "+p.name);}
            }
        }
        public void Notify(string message){Notice=message;noticeUntil=Time.unscaledTime+7;}
        public void SetMap(bool open)
        {
            MapOpen=open;Player.ControlsPaused=open;Time.timeScale=open?0:1;
            if(open){MapCentre=new Vector2(Position.x,Position.z);Player.FollowCamera.ReleaseCursor();}
            if(Driving)Driving.Paused=open;Plane.Paused=open;
        }
        void PlayerActive(bool active)
        {
            Player.enabled=active;Player.GetComponent<CharacterController>().enabled=active;Player.Visual.gameObject.SetActive(active);Player.FollowCamera.enabled=active;
        }
        public void EnterCar(CityCar car)
        {
            if(MapOpen || Mode!="walk" || !car || Vector3.Distance(car.transform.position,Player.transform.position)>5)return;
            returnPoint=Player.transform.position;returnYaw=Player.FollowCamera.Yaw;PlayerActive(false);Driving=car;car.Driven=true;Mode="drive";Notify("Arrow keys drive · Space brake · Return exit");
        }
        void TakeTrafficCar(CityTraffic.Vehicle vehicle)
        {
            Traffic.Release(vehicle,v=>
            {
                var go=new GameObject(v.Colour+" compact");go.transform.SetParent(transform);go.transform.SetPositionAndRotation(v.Go.transform.position,v.Go.transform.rotation);
                var (mesh,materials)=Traffic.Body(v);var body=new GameObject("Body");body.transform.SetParent(go.transform,false);
                body.AddComponent<MeshFilter>().sharedMesh=mesh;body.AddComponent<MeshRenderer>().sharedMaterials=materials;
                var (wheelMesh,wheelMaterials)=Traffic.WheelMesh;
                var car=CityCar.Equip(go,()=>{var wheel=new GameObject("Wheel");wheel.AddComponent<MeshFilter>().sharedMesh=wheelMesh;wheel.AddComponent<MeshRenderer>().sharedMaterials=wheelMaterials;return wheel.transform;});
                var cars=new System.Collections.Generic.List<CityCar>(Cars){car};Cars=cars.ToArray();EnterCar(car);
            });
            NearbyTraffic=null;
        }
        public bool ExitCar()
        {
            if(!Driving)return false;if(Driving.Speed>2){Notify("Brake before getting out.");return false;}
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
            Driving.Driven=false;Driving=null;Mode="walk";RestoreFoot(exit,returnYaw);return true;
        }
        public void LaunchFlight()
        {
            if(MapOpen)return;if(Mode=="drive"&&!ExitCar())return;
            returnPoint=Player.transform.position;returnYaw=Player.FollowCamera.Yaw;PlayerActive(false);Mode="fly";
            Plane.Launch(new Vector3(returnPoint.x,150,returnPoint.z),returnYaw);Notify("Sightseeing flight · Arrows climb / bank · F return to your walk");
        }
        public void EndFlight(){Plane.Flying=false;Plane.gameObject.SetActive(false);Mode="walk";RestoreFoot(returnPoint,returnYaw);}
        void RestoreFoot(Vector3 p,float yaw)
        {
            Player.transform.position=p;Player.VerticalSpeed=-2;PlayerActive(true);Player.Visual.rotation=Quaternion.Euler(0,yaw,0);Player.FollowCamera.ResetCamera();
        }
        void LateUpdate()
        {
            if(MapOpen || Mode=="walk")return;
            var camera=Player.FollowCamera.transform;Vector3 target,eye;
            if(Mode=="drive")
            {
                target=Driving.transform.position+Vector3.up*1.4f;
                var delta=-Driving.transform.forward*8+Vector3.up*3.1f;
                if(Physics.SphereCast(target,.25f,delta.normalized,out var hit,delta.magnitude,ThirdPersonMotor.WorldMask))delta=delta.normalized*Mathf.Max(1,hit.distance-.15f);
                eye=target+delta;
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
        static void Panel(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Styles()
        {
            if(title!=null)return;
            GUIStyle S(int size,Color color,FontStyle weight=FontStyle.Normal){var s=new GUIStyle(GUI.skin.label){fontSize=size,fontStyle=weight};s.normal.textColor=color;return s;}
            title=S(23,new Color(.98f,.94f,.85f),FontStyle.Bold);body=S(17,Color.white);small=S(13,new Color(.82f,.86f,.85f));label=S(14,new Color(.13f,.20f,.23f),FontStyle.Bold);
        }
        void Dot(Vector2 p,Color color,float size=9){Panel(new Rect(p.x-size/2,p.y-size/2,size,size),color);}
        void OnGUI()
        {
            if(!Player || City.Data==null)return;Styles();var old=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/1000f,1));
            var ink=new Color(.035f,.07f,.09f,.90f);Panel(new Rect(26,25,445,81),ink);
            GUI.Label(new Rect(45,35,425,35),"PADOVA  /  CENTRO STORICO",title);
            GUI.Label(new Rect(45,75,425,22),"Explore  ·  "+Discoveries+" / "+Visited.Length+" places discovered",small);
            Panel(new Rect(1320,25,252,81),ink);GUI.Label(new Rect(1340,36,220,30),Mode=="drive"?Mathf.RoundToInt(Driving.Speed*3.6f)+" km/h":Mode=="fly"?Mathf.RoundToInt(Plane.Speed*3.6f)+" km/h   "+Mathf.RoundToInt(Plane.Altitude)+" m":"ON FOOT  ·  N ↑",body);
            if(GUI.Button(new Rect(1340,71,210,25),"M   ·   Open city map"))SetMap(!MapOpen);
            if(Time.unscaledTime<noticeUntil){Panel(new Rect(26,121,710,45),ink);GUI.Label(new Rect(43,130,680,28),Notice,body);}
            if(HasWaypoint)
            {
                float distance=Vector2.Distance(new Vector2(Position.x,Position.z),new Vector2(Waypoint.x,Waypoint.z));
                Panel(new Rect(26,180,410,57),ink);GUI.Label(new Rect(43,187,380,26),DestinationName,body);GUI.Label(new Rect(43,214,380,22),Mathf.RoundToInt(distance)+" m  ·  straight-line distance",small);
                if(!MapOpen){var p=Camera.main.WorldToScreenPoint(Waypoint+Vector3.up*4);if(p.z>0){var v=new Vector2(p.x*1600/Screen.width,(Screen.height-p.y)*1000/Screen.height);Dot(v,new Color(1,.7f,.22f),13);}}
            }
            if(Nearby && Mode=="walk"&&!MapOpen){Panel(new Rect(590,770,420,50),ink);GUI.Label(new Rect(610,783,390,30),"RETURN  ·  Drive the "+Nearby.name,body);}
            if(NearbyTraffic!=null && !MapOpen){Panel(new Rect(590,770,420,50),ink);GUI.Label(new Rect(610,783,390,30),"RETURN  ·  Take the "+NearbyTraffic.Colour+" car",body);}
            Panel(new Rect(26,899,1240,65),ink);
            GUI.Label(new Rect(44,910,1205,26),Mode=="walk"?"Arrow keys move  ·  Shift run  ·  Space jump  ·  Return enter car  ·  M map  ·  F fly":Mode=="drive"?"↑ accelerate  ·  ↓ reverse  ·  ← → steer  ·  Space brake  ·  Return exit  ·  M map  ·  R reset car":"↑ climb  ·  ↓ descend  ·  ← → bank  ·  Shift faster  ·  Space slow  ·  M map  ·  F return to walking",body);
            GUI.Label(new Rect(44,939,1205,22),Mode=="walk"?"Q / E orbit  ·  Click-drag look  ·  C centre camera  ·  Tab toggle run  ·  R return to the market":Mode=="drive"?"Brake to a stop before getting out. R returns this car to its parking position.":"Flight assistance keeps the plane above the skyline. F returns you to your previous walking position.",small);
            Panel(new Rect(0,979,1600,21),ink);GUI.Label(new Rect(26,980,1550,21),"Comune di Padova / Regione Veneto · IODL 2.0  |  Names © OpenStreetMap · ODbL  |  Poly Haven / Kenney · CC0  |  Façade detail and street life are authored",small);
            if(MapOpen)DrawMap();GUI.matrix=old;
        }
        void DrawMap()
        {
            Panel(new Rect(180,67,1240,805),new Color(.035f,.07f,.09f,.98f));GUI.Label(new Rect(213,82,900,35),"PADOVA  /  CITY MAP",title);
            if(GUI.Button(new Rect(1220,85,163,32),"Close  ·  M / Esc"))SetMap(false);
            var r=MapRect;float h=MapSpan*r.height/r.width;
            GUI.DrawTextureWithTexCoords(r,MapTexture,new Rect((MapCentre.x-MapSpan/2+1500)/3000,(MapCentre.y-h/2+1300)/2600,MapSpan/3000,h/2600));
            GUI.BeginGroup(r);
            if(Activities && Activities.Active)foreach(var point in Activities.Route)Dot(WorldToMap(point)-r.position,new Color(.1f,.55f,.7f),5);
            foreach(var car in Cars){var p=WorldToMap(car.transform.position)-r.position;if(p.x>0&&p.y>0&&p.x<r.width&&p.y<r.height){Dot(p,new Color(.14f,.45f,.62f),10);GUI.Label(new Rect(p.x+9,p.y-11,130,24),"Car",label);}}
            var occupied=new System.Collections.Generic.List<Rect>();
            foreach(var place in City.Data.landmarks)
            {
                var p=WorldToMap(new Vector3(place.x,0,place.z))-r.position;
                if(p.x<0||p.y<0||p.x>r.width||p.y>r.height)continue;
                Dot(p,new Color(.7f,.29f,.15f),8);
                float width=label.CalcSize(new GUIContent(place.name)).x+8;
                var rect=new Rect(Mathf.Clamp(p.x+8,0,r.width-width),p.y-10,width,22);
                for(int attempt=0;attempt<10;attempt++)
                {
                    bool overlaps=false;foreach(var other in occupied)if(rect.Overlaps(other)){overlaps=true;break;}
                    if(!overlaps)break;rect.y+=23;if(rect.yMax>r.height)rect.y=p.y-33-attempt*23;
                }
                occupied.Add(rect);GUI.Label(rect,place.name,label);
            }
            var current=WorldToMap(Position)-r.position;Dot(current,Color.white,18);Dot(current,new Color(.12f,.56f,.55f),12);
            if(HasWaypoint){var p=WorldToMap(Waypoint)-r.position;Dot(p,new Color(.95f,.56f,.06f),14);}
            GUI.EndGroup();
            GUI.Label(new Rect(213,833,920,25),"Drag to pan  ·  Scroll to zoom  ·  Click to set a waypoint  ·  Blue squares: drivable cars  ·  N ↑",small);
            if(GUI.Button(new Rect(1212,831,166,27),"Centre on me"))MapCentre=new Vector2(Position.x,Position.z);
        }
        void OnDestroy(){Time.timeScale=1;mapAction?.Dispose();interactAction?.Dispose();flightAction?.Dispose();resetAction?.Dispose();escapeAction?.Dispose();clickAction?.Dispose();}
    }
}
