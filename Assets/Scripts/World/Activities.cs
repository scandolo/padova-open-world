using System.Collections.Generic;
using Padova.Architecture;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>Paid jobs on the real road network: taxi fares, deliveries from real kitchens to real house numbers,
    /// the classic sprint and sprints to any landmark. J opens the menu.</summary>
    [DefaultExecutionOrder(130)]
    public sealed class Activities:MonoBehaviour
    {
        public OpenWorld Game;public CityTraffic Traffic;public Material BeaconMaterial;
        // Sprint from Corso Garibaldi to Prato della Valle; nodes are snapped to the nearest surveyed junction.
        public Vector3 RaceStart=new(330,0,700),RaceFinish=new(259,0,-898);
        public int Money,Fares;public float BestRace;public bool MenuOpen;
        public string Kind="",Phase="",Objective="";public float Clock,Limit,Fare,RouteLength;public Vector3 Target;public int Checkpoint,ImpactsAtStart;
        /// <summary>Deliveries made on this run and how many it takes; where the order came from and the address on the box.</summary>
        public int Drops,DropTarget=3;public string Shop="",Address="";
        string raceName="GARIBALDI → PRATO",raceGoal="Prato della Valle";bool raceRecord=true;
        public readonly List<Vector3> Route=new();public readonly List<Vector3> Checkpoints=new();
        public bool Active=>Kind!="";
        Transform beacon,nextBeacon;GameObject passenger;MaterialPropertyBlock block;float countdown,bannerAt=-10;string bannerTitle="",bannerSubtitle="";
        InputAction menuAction,taxiAction,raceAction,quitAction,deliveryAction,tourAction;bool menuRequested,taxiRequested,raceRequested,quitRequested,deliveryRequested,tourRequested;
        readonly System.Random random=new(611);readonly List<int> nearby=new();
        void Awake()
        {
            InputAction Button(string binding,System.Action action){var a=new InputAction(binding,InputActionType.Button,binding);a.performed+=_=>action();a.Enable();return a;}
            menuAction=Button("<Keyboard>/j",()=>menuRequested=true);taxiAction=Button("<Keyboard>/1",()=>taxiRequested=true);
            raceAction=Button("<Keyboard>/2",()=>raceRequested=true);quitAction=Button("<Keyboard>/0",()=>quitRequested=true);
            deliveryAction=Button("<Keyboard>/3",()=>deliveryRequested=true);tourAction=Button("<Keyboard>/4",()=>tourRequested=true);
            Money=PlayerPrefs.GetInt("padova.money",0);BestRace=PlayerPrefs.GetFloat("padova.race.best",0);
        }
        void Start()
        {
            block=new MaterialPropertyBlock();beacon=Beacon("Objective beacon",4,60);nextBeacon=Beacon("Next checkpoint beacon",3,30);
        }
        Transform Beacon(string name,float radius,float height)
        {
            var palette=(Material[])Game.City.City.Materials.Clone();if(BeaconMaterial)palette[(int)Slot.LampGlow]=BeaconMaterial;
            var sink=new MeshSink();sink.Cylinder(Slot.LampGlow,Vector3.zero,radius,height,28);
            var go=new GameObject(name);go.transform.SetParent(transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=sink.ToMesh(name,palette,out var materials,false);var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;
            r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;go.SetActive(false);return go.transform;
        }
        void Show(Transform t,Vector3 p,Color c){t.position=p-Vector3.up*.5f;block.SetColor("_BaseColor",c);t.GetComponent<MeshRenderer>().SetPropertyBlock(block);t.gameObject.SetActive(true);}
        RoadNetwork Net=>Traffic?Traffic.Network:null;
        void Update()
        {
            bool menu=menuRequested,taxi=taxiRequested,race=raceRequested,quit=quitRequested,delivery=deliveryRequested,tour=tourRequested;
            menuRequested=taxiRequested=raceRequested=quitRequested=deliveryRequested=tourRequested=false;
            if(Game.MapOpen || Game.Menu || Net==null)return;
            if(menu)MenuOpen=!MenuOpen;
            if(MenuOpen)
            {
                if(taxi){MenuOpen=false;StartTaxi();}else if(race){MenuOpen=false;StartRace();}else if(delivery){MenuOpen=false;StartDelivery();}else if(tour){MenuOpen=false;StartTour();}
                else if(quit){MenuOpen=false;if(Active)End("Activity abandoned.");}
            }
            if(!Active)return;
            Clock+=Time.deltaTime;
            if(Kind=="taxi")UpdateTaxi();else if(Kind=="delivery")UpdateDelivery();else UpdateRace();
        }
        bool InCar(string what)
        {
            if(Game.Mode=="drive")return true;
            // On foot: point at the nearest set of wheels.
            var parked=Game.Parked?Game.Parked.NearestTo(Game.Position,220):null;
            if(parked)Game.SetWaypoint(parked.transform.position,"Nearest parked "+parked.Spec.Label,false);
            else
            {
                CityCar best=null;foreach(var car in Game.Cars)if(!best||Vector3.Distance(car.transform.position,Game.Position)<Vector3.Distance(best.transform.position,Game.Position))best=car;
                if(best)Game.SetWaypoint(best.transform.position,"Nearest parked car",false);
            }
            Game.Notify(what+" needs wheels. Take a parked vehicle with E (waypoint set), or one from traffic.");return false;
        }
        public void StartTaxi()
        {
            if(Net==null)return;if(Active)End("");if(!InCar("Taxi"))return;
            Kind="taxi";Fares=0;NextFare();
        }
        // Pick-up somewhere 150–450 m away by road, beside the kerb on the right of the lane.
        void NextFare()
        {
            int pick=-1;nearby.Clear();Net.LanesNear(Game.Position,450,nearby);
            for(int i=0;i<60 && pick<0 && nearby.Count>0;i++)
            {
                int lane=nearby[random.Next(nearby.Count)];var l=Net.Lanes[lane];if(l.Length<30||!Net.Open(lane)||l.Class==Padova.City.CityRoads.Fast)continue;
                float d=Vector3.Distance(Net.Nodes[l.From],Game.Position);if(d<150||d>450)continue;
                if(Net.DriveRoute(Game.Position,Net.Nodes[l.From]).Count==0)continue;pick=lane;
            }
            if(pick<0){End("No fares around here. Try a busier street.");return;}
            float s=Net.Lanes[pick].Length*.5f;var p=Net.Sample(pick,s,out var tangent);
            var kerb=p+new Vector3(tangent.z,0,-tangent.x).normalized*2.6f;
            if(Physics.Raycast(kerb+Vector3.up*20,Vector3.down,out var hit,40,1<<8))kerb=hit.point;
            if(passenger)Destroy(passenger);
            if(Game.Crowd && Game.Crowd.Character)
            {
                passenger=Instantiate(Game.Crowd.Character,kerb,Quaternion.LookRotation(-new Vector3(tangent.z,0,-tangent.x)));passenger.name="Taxi passenger";passenger.SetActive(true);
                var animator=passenger.GetComponentInChildren<Animator>();if(animator){animator.SetBool("Grounded",true);animator.SetFloat("Speed",0);}
            }
            Phase="pickup";Target=p;Clock=0;Limit=0;Objective="Pick up the passenger on "+Net.Lanes[pick].Name;
            Game.SetWaypoint(Target,"Taxi pick-up · "+Net.Lanes[pick].Name);PlanRoute(Target);
        }
        void UpdateTaxi()
        {
            if(Game.Mode!="drive"){if(Phase=="dropoff"){End("You left the car. The passenger walked off without paying.");return;}Objective="Get back into a car to continue";HideBeacons();return;}
            var car=Game.Driving;bool stopped=car.Speed<3;float distance=Flat(Game.Position,Target);
            Show(beacon,Target,Phase=="pickup"?new Color(1,.78f,.2f,.35f):new Color(.3f,.95f,.55f,.35f));nextBeacon.gameObject.SetActive(false);
            if(Phase=="pickup")
            {
                Objective=distance<25?"Stop beside the passenger":Objective;
                if(distance<10 && stopped)
                {
                    if(passenger)passenger.SetActive(false);
                    var places=Game.City.Data.landmarks;var choices=new List<WorldPlace>();
                    foreach(var place in places){float d=Flat(new Vector3(place.x,0,place.z),Game.Position);if(d>350&&d<1400)choices.Add(place);}
                    Vector3 drop;string name;
                    if(choices.Count>0){var place=choices[random.Next(choices.Count)];drop=new Vector3(place.x,0,place.z);name=place.name;}
                    else{drop=Game.Position+Quaternion.Euler(0,random.Next(360),0)*Vector3.forward*700;name="";}
                    var (lane,s,_)=Net.Nearest(drop,true);Target=Net.Sample(lane,s,out _);if(name=="")name=Net.Lanes[lane].Name;
                    PlanRoute(Target);if(Route.Count==0){End("The passenger's destination is unreachable by road.");return;}
                    Phase="dropoff";Clock=0;Limit=Mathf.Round(RouteLength/7.5f+25);Fare=Mathf.Round(4+RouteLength*.012f);ImpactsAtStart=car.Impacts;
                    Objective="Drive to "+name;Game.SetWaypoint(Target,"Taxi drop-off · "+name);Game.Notify("Passenger on board · "+name+" · "+Mathf.RoundToInt(RouteLength)+" m · "+Limit+" s");
                }
            }
            else
            {
                if(distance<25)Objective="Stop to let the passenger out";
                if(distance<10 && stopped)
                {
                    int crashes=car.Impacts-ImpactsAtStart;float pay=Fare*(Clock<=Limit?1:.5f)+(Clock<Limit*.7f?5:0)-crashes*3;pay=Mathf.Max(2,Mathf.Round(pay));
                    Pay((int)pay);Fares++;
                    string receipt="+€"+pay+(Clock>Limit?"  ·  late":Clock<Limit*.7f?"  ·  incl. €5 tip":"")+(crashes>0?"  ·  "+crashes+" bumps −€"+crashes*3:"");
                    NextFare();Result("FARE COMPLETE",receipt);
                }
                else if(Clock>Limit*2)End("Too slow: the passenger got out and walked.");
            }
        }
        public void StartRace()
        {
            if(Net==null)return;if(Active)End("");if(!InCar("The sprint"))return;
            var lanes=Net.DriveRoute(RaceStart,RaceFinish);
            if(lanes.Count==0){Game.Notify("Race route unavailable.");return;}
            Checkpoints.Clear();Checkpoints.AddRange(Net.Along(lanes,140));RouteLength=Net.Length(lanes);
            raceName="GARIBALDI → PRATO";raceGoal="Prato della Valle";raceRecord=true;
            Kind="race";Phase="start";Target=Net.Nodes[Net.Lanes[lanes[0]].From];Checkpoint=0;Clock=0;Limit=0;Objective="Drive to the start on "+Net.Lanes[lanes[0]].Name;
            Game.SetWaypoint(Target,"Sprint start · Corso Garibaldi");PlanRoute(Target);
        }
        /// <summary>A sprint from wherever the car is to a landmark one to three kilometres away, on the fastest road route.</summary>
        public void StartTour()
        {
            if(Net==null)return;if(Active)End("");if(!InCar("A sprint"))return;
            var choices=new List<WorldPlace>();
            foreach(var place in Game.City.Data.landmarks){if(place.rank>2)continue;float d=Flat(new Vector3(place.x,0,place.z),Game.Position);if(d>900&&d<2800)choices.Add(place);}
            for(int tries=0;tries<8&&choices.Count>0;tries++)
            {
                var place=choices[random.Next(choices.Count)];var lanes=Net.DriveRoute(Game.Position,new Vector3(place.x,0,place.z));
                if(lanes.Count<3)continue;
                Checkpoints.Clear();Checkpoints.AddRange(Net.Along(lanes,140));if(Checkpoints.Count<4)continue;
                RouteLength=Net.Length(lanes);raceName="TO "+place.name.ToUpperInvariant();raceGoal=place.name;raceRecord=false;
                Kind="race";Phase="countdown";countdown=3;Checkpoint=0;Clock=0;Limit=0;Target=Checkpoints[0];Route.Clear();Route.AddRange(Checkpoints);
                Game.Notify("Sprint to "+place.name+" · "+(RouteLength/1000).ToString("0.0")+" km");return;
            }
            Game.Notify("No landmark within sprinting distance. Try nearer the centre.");
        }
        /// <summary>Collect an order at the nearest real kitchen, then take it to three real house numbers against the clock.</summary>
        public void StartDelivery()
        {
            if(Net==null)return;if(Active)End("");if(!InCar("Deliveries"))return;
            var places=Game.City.Places;Padova.City.CityPlace best=null;float bestScore=float.MaxValue;
            if(places!=null)foreach(var poi in places.pois)
            {
                if(poi.kind!="pizzeria"&&poi.kind!="restaurant"&&poi.kind!="fast_food"&&poi.kind!="bakery")continue;
                float d=Flat(new Vector3(poi.x,0,poi.z),Game.Position);if(d<50||d>1200)continue;
                if(poi.kind!="pizzeria")d+=220;      // a pizzeria if there is one about
                if(d<bestScore){bestScore=d;best=poi;}
            }
            if(best==null){Game.Notify("No kitchen within reach. Try a busier part of town.");return;}
            var (lane,s,_)=Net.Nearest(new Vector3(best.x,0,best.z),true);if(lane<0){Game.Notify("That kitchen cannot be reached by road.");return;}
            Kind="delivery";Phase="collect";Shop=string.IsNullOrEmpty(best.name)?"the "+best.kind:best.name;Drops=0;Address="";Target=Net.Sample(lane,s,out _);Clock=0;Limit=0;
            Objective="Collect the order at "+Shop;Game.SetWaypoint(Target,"Collect · "+Shop);PlanRoute(Target);
        }
        void NextDrop()
        {
            var book=Game.City.Addresses;
            for(int tries=0;tries<6&&book!=null;tries++)
            {
                int pick=book.Pick(Game.Position.x,Game.Position.z,220,950,random);if(pick<0)break;
                var (lane,s,off)=Net.Nearest(new Vector3(book.X[pick],0,book.Z[pick]),true);if(lane<0||off>60)continue;
                Target=Net.Sample(lane,s,out _);PlanRoute(Target);if(Route.Count==0)continue;
                Address=book.Label(pick);Phase="deliver";Clock=0;Limit=Mathf.Round(RouteLength/7f+22);Fare=Mathf.Round(5+RouteLength*.011f);ImpactsAtStart=Game.Driving?Game.Driving.Impacts:0;
                Objective="Deliver to "+Address;Game.SetWaypoint(Target,"Delivery · "+Address,false);return;
            }
            End("Nobody is ordering around here.");
        }
        void UpdateDelivery()
        {
            if(Game.Mode!="drive"){Objective="Get back on your wheels to continue";HideBeacons();return;}
            var car=Game.Driving;float distance=Flat(Game.Position,Target);
            Show(beacon,Target,Phase=="collect"?new Color(1,.55f,.2f,.35f):new Color(.3f,.95f,.55f,.35f));nextBeacon.gameObject.SetActive(false);
            if(Phase=="deliver"&&distance>25)Objective="Deliver to "+Address;else if(distance<25)Objective=Phase=="collect"?"Stop at the door":"Stop at "+Address;
            if(distance>=10||car.Speed>=3){if(Phase=="deliver"&&Clock>Limit*2.5f)End("The order went cold. Run over.");return;}
            if(Phase=="collect"){Game.Notify("Order on board · "+DropTarget+" addresses to go");NextDrop();return;}
            int crashes=car.Impacts-ImpactsAtStart;float pay=Fare*(Clock<=Limit?1:.5f)+(Clock<Limit*.7f?4:0)-crashes*2;pay=Mathf.Max(2,Mathf.Round(pay));Pay((int)pay);Drops++;
            string receipt="+€"+pay+(Clock>Limit?"  ·  late":Clock<Limit*.7f?"  ·  still hot: €4 tip":"")+(crashes>0?"  ·  "+crashes+" bumps −€"+crashes*2:"");
            if(Drops>=DropTarget){string shop=Shop;End("");Result("RUN COMPLETE",receipt+"  ·  "+shop+" has more");}
            else{string done="DELIVERED "+Drops+" / "+DropTarget;NextDrop();if(Active)Result(done,receipt);}
        }
        void UpdateRace()
        {
            if(Game.Mode!="drive"){End("Sprint abandoned: you left the car.");return;}
            var car=Game.Driving;
            if(Phase=="start")
            {
                Show(beacon,Target,new Color(.3f,.7f,1,.35f));nextBeacon.gameObject.SetActive(false);
                if(Flat(Game.Position,Target)<14 && car.Speed<3){Phase="countdown";countdown=3;Route.Clear();foreach(var p in Checkpoints)Route.Add(p);}
                return;
            }
            if(Phase=="countdown")
            {
                countdown-=Time.deltaTime;Objective=countdown>0?"Get ready · "+Mathf.CeilToInt(countdown):"GO!";
                if(countdown<=0){Phase="racing";Clock=0;ImpactsAtStart=car.Impacts;}
                ShowCheckpoints();return;
            }
            Objective="Checkpoint "+(Checkpoint+1)+" / "+Checkpoints.Count+"  ·  to "+raceGoal;
            if(Flat(Game.Position,Checkpoints[Checkpoint])<13)
            {
                Checkpoint++;
                if(Checkpoint>=Checkpoints.Count)
                {
                    float gold=RouteLength/15,silver=RouteLength/12,bronze=RouteLength/9;
                    int reward=Clock<=gold?60:Clock<=silver?35:Clock<=bronze?20:5;string medal=Clock<=gold?"GOLD":Clock<=silver?"SILVER":Clock<=bronze?"BRONZE":"finished";
                    bool record=raceRecord&&(BestRace<=0||Clock<BestRace);if(record){BestRace=Clock;PlayerPrefs.SetFloat("padova.race.best",BestRace);}
                    Pay(reward);End("");Result(medal.ToUpperInvariant(),Clock.ToString("0.0")+" s  ·  +€"+reward+(record?"  ·  new best":""));return;
                }
            }
            ShowCheckpoints();Game.SetWaypoint(Checkpoints[Checkpoint],"Checkpoint "+(Checkpoint+1),false);
        }
        void ShowCheckpoints()
        {
            Show(beacon,Checkpoints[Checkpoint],Checkpoint==Checkpoints.Count-1?new Color(1,.85f,.2f,.4f):new Color(.3f,.7f,1,.35f));
            if(Checkpoint+1<Checkpoints.Count)Show(nextBeacon,Checkpoints[Checkpoint+1],new Color(.3f,.7f,1,.15f));else nextBeacon.gameObject.SetActive(false);
        }
        void PlanRoute(Vector3 to)
        {
            Route.Clear();RouteLength=0;var lanes=Net.DriveRoute(Game.Position,to);
            Route.AddRange(Net.Along(lanes,25));RouteLength=Net.Length(lanes);
        }
        public void Charge(int amount){Pay(-Mathf.Min(amount,Money));}
        /// <summary>Money from outside the jobs: a caught hen, a secret found.</summary>
        public void Reward(int amount){Pay(amount);}
        void Pay(int amount){Money+=amount;PlayerPrefs.SetInt("padova.money",Money);PlayerPrefs.Save();}
        void Result(string title,string subtitle){bannerTitle=title;bannerSubtitle=subtitle;bannerAt=Time.unscaledTime;Game.Notify(title+" · "+subtitle);}
        void HideBeacons(){beacon.gameObject.SetActive(false);nextBeacon.gameObject.SetActive(false);}
        public void End(string message)
        {
            if(message!="")Game.Notify(message);
            Kind=Phase=Objective=Shop=Address="";Route.Clear();Checkpoints.Clear();HideBeacons();Game.HasWaypoint=false;if(passenger)Destroy(passenger);
        }
        static float Flat(Vector3 a,Vector3 b)=>Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        void OnGUI()
        {
            if(!Game || Game.MapOpen || Game.Menu)return;var old=GUI.matrix;Hud.Begin();
            Hud.Text(new Rect(1170,18,400,64),"€"+Money.ToString("N0"),58,Hud.Money,TextAnchor.UpperRight,false,true);
            if(Active)
            {
                // Timer under the wallet; objective as a subtitle above the bottom edge.
                string title=Kind=="taxi"?"TAXI · FARE "+(Fares+1):Kind=="delivery"?"DELIVERIES · "+Mathf.Min(Drops+1,DropTarget)+" / "+DropTarget:"SPRINT · "+raceName;
                string clock=Kind=="taxi"?(Phase=="dropoff"?Mathf.Max(0,Limit-Clock).ToString("0"):""):Kind=="delivery"?(Phase=="deliver"?Mathf.Max(0,Limit-Clock).ToString("0"):""):Phase=="racing"?Clock.ToString("0.0"):"";
                Hud.Text(new Rect(1070,118,500,28),title,24,Hud.Gold,TextAnchor.UpperRight,false,true);
                if(clock!="")Hud.Text(new Rect(1170,142,400,60),clock,52,Kind!="race"&&Clock>Limit*.8f?Hud.Danger:Color.white,TextAnchor.UpperRight,false,true);
                string detail=Kind=="taxi"&&Phase=="dropoff"?"Fare €"+Fare+(Clock>Limit?" · late: half fare":""):Kind=="delivery"&&Phase=="deliver"?"From "+Shop+" · €"+Fare+(Clock>Limit?" · late: half":""):Kind=="race"&&raceRecord&&BestRace>0?"Best "+BestRace.ToString("0.0")+" s":"";
                if(detail!="")Hud.Text(new Rect(1070,196,500,26),detail,19,Hud.Muted,TextAnchor.UpperRight,true);
                if(Objective!="")Hud.Text(new Rect(200,728,1200,40),Objective,30,Color.white,TextAnchor.MiddleCenter,true);
                if(Phase=="countdown")Hud.Banner(countdown>0?Mathf.CeilToInt(countdown).ToString():"GO!","",Hud.Gold,1-Mathf.Repeat(countdown,1));
            }
            float since=Time.unscaledTime-bannerAt;if(since<4)Hud.Banner(bannerTitle,bannerSubtitle,Hud.Money,since);
            if(MenuOpen)Menu();
            GUI.matrix=old;
        }
        void Menu()
        {
            Hud.Fill(new Rect(0,0,1600,1000),new Color(0,0,0,.4f));
            var r=new Rect(470,130,660,Active?672:612);Hud.Panel(r,new Color(.03f,.05f,.06f,.94f));
            Hud.Text(new Rect(r.x+32,r.y+22,500,56),"JOBS",48,Hud.Gold,TextAnchor.UpperLeft,false,true);
            Hud.Text(new Rect(r.x+32,r.y+30,r.width-64,40),"€"+Money.ToString("N0"),30,Hud.Money,TextAnchor.UpperRight,false,true);
            bool Card(float y,string key,string name,string text,string extra)
            {
                var c=new Rect(r.x+24,y,r.width-48,104);bool hover=c.Contains(Event.current.mousePosition);
                Hud.Panel(c,hover?new Color(1,1,1,.12f):new Color(1,1,1,.06f));
                Hud.Panel(new Rect(c.x+18,c.y+26,52,52),Hud.Cream);Hud.Text(new Rect(c.x+18,c.y+26,52,52),key,34,new Color(.08f,.1f,.12f),TextAnchor.MiddleCenter,false,true);
                Hud.Text(new Rect(c.x+92,c.y+14,c.width-110,40),name,34,Color.white,TextAnchor.UpperLeft,false,true);
                Hud.Text(new Rect(c.x+92,c.y+52,c.width-110,26),text,19,Hud.Muted,TextAnchor.UpperLeft,true);
                Hud.Text(new Rect(c.x+92,c.y+76,c.width-110,22),extra,16,Hud.Gold,TextAnchor.UpperLeft,true);
                return GUI.Button(c,GUIContent.none,GUIStyle.none);
            }
            if(Card(r.y+96,"1","TAXI","Pick up passengers and drive them across Padova.","Fares by distance · tip for speed · €3 per bump")){MenuOpen=false;StartTaxi();}
            if(Card(r.y+212,"2","SPRINT","Corso Garibaldi to Prato della Valle, checkpoints.",BestRace>0?"Best "+BestRace.ToString("0.0")+" s · gold €60":"Gold €60 · silver €35 · bronze €20")){MenuOpen=false;StartRace();}
            if(Card(r.y+328,"3","DELIVERIES","Collect at a real kitchen, deliver to three real house numbers.","Paid per drop · tip while it is hot · any wheels, a scooter is quickest")){MenuOpen=false;StartDelivery();}
            if(Card(r.y+444,"4","SPRINT TO A LANDMARK","From right here to a landmark across town, checkpoints.","Gold €60 · silver €35 · bronze €20")){MenuOpen=false;StartTour();}
            float y=r.y+564;
            if(Active){if(GUI.Button(new Rect(r.x+24,y,250,44),GUIContent.none,GUIStyle.none)){MenuOpen=false;End("Activity abandoned.");}Hud.Prompt(new Vector2(r.x+150,y+22),"0","Quit activity",18);y+=56;}
            Hud.Text(new Rect(r.x+32,y+4,r.width-64,26),"All need wheels: E beside any parked vehicle, or take one from traffic.   J closes",16,Hud.Muted,TextAnchor.UpperLeft,true);
        }
        void OnDestroy(){menuAction?.Dispose();taxiAction?.Dispose();raceAction?.Dispose();quitAction?.Dispose();deliveryAction?.Dispose();tourAction?.Dispose();}
    }
}
