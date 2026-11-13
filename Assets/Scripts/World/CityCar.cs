using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>
    /// Arcade vehicle for the player and the police. Four ray-cast springs carry the body; drive, braking, grip and
    /// steering are applied directly, so each vehicle's feel is a handful of numbers in <see cref="VehicleSpec"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CityCar:MonoBehaviour
    {
        /// <summary>Wheel colliders saved with older scenes. Only their position and radius are used; they are switched off at runtime.</summary>
        public WheelCollider[] Wheels;
        public Transform[] WheelVisuals;public bool Driven,Paused;
        public Vector3 Home;public Quaternion HomeRotation;public float DistanceTravelled;public int Impacts,TrafficHits;
        /// <summary>Shift turbo: drains in about three seconds, refills in about eight.</summary>
        public float Boost=1;public bool Boosting{get;private set;}
        /// <summary>AI input for police cars; ignored while the player drives.</summary>
        public bool Autopilot;public float AutoThrottle,AutoSteer,TopSpeed=22;public bool AutoBrake;
        public float Speed=>Body?Body.linearVelocity.magnitude:0;
        public Rigidbody Body{get;private set;}
        public bool Handbrake{get;private set;}
        [NonSerialized]public VehicleSpec Spec;
        /// <summary>Body mesh that rolls, pitches and (two-wheelers) leans; wheels stay with the suspension.</summary>
        [NonSerialized]public Transform Visual;
        /// <summary>Optional fork or handlebar pivot from a prefab; turns with the steering.</summary>
        [NonSerialized]public Transform SteerPivot;
        /// <summary>TURBO cheat: the meter never drains.</summary>
        [NonSerialized]public bool EndlessBoost;
        /// <summary>Suspension points in local space, at the wheel centre with the spring fully compressed:
        /// front-left, front-right, rear-left, rear-right. Two- and three-wheelers still ride on four.</summary>
        public Vector3[] Anchors{get;private set;}
        public float WheelRadius{get;private set;}
        public bool Grounded{get;private set;}
        /// <summary>Signed speed along the nose, m/s.</summary>
        public float ForwardSpeed{get;private set;}
        /// <summary>0 gripping, 1 fully sideways; drives tyre squeal and skid feedback.</summary>
        public float Slip{get;private set;}
        public float Throttle{get;private set;}
        public float Steering=>steering;
        /// <summary>Seconds since the wheels last touched the ground, and the longest flight so far.</summary>
        public float AirTime{get;private set;}
        public float LongestAir{get;private set;}
        /// <summary>Seconds spent upside down, on its side, or pushing against something without moving.</summary>
        public float StuckFor{get;private set;}
        public bool Stuck=>StuckFor>3;
        /// <summary>Closing speed (m/s) of the latest hard knock and a counter that changes with each one.</summary>
        public float LastImpact{get;private set;}
        public int ImpactSerial{get;private set;}
        /// <summary>Seconds from the last standstill to 100 km/h (-1 until reached) and the highest speed since; for tuning by measurement.</summary>
        public float ZeroToHundred{get;private set;}=-1;
        public float PeakSpeed{get;private set;}
        float sinceRest,restFor;
        Vector3 lastPosition,lastVelocity;float steering,gripScale=1,spin,steerAngle,roll,pitch,spring,damper;
        float[] compression;int[] pairA,pairB;bool[] steered;Quaternion[] wheelRest;Quaternion visualRest=Quaternion.identity;bool bound;
        static readonly RaycastHit[] hits=new RaycastHit[8];
        /// <summary>Spring travel, and how far it is compressed by the vehicle standing still.</summary>
        public const float Travel=.22f,RestCompression=.09f;
        /// <summary>Everything a wheel can stand on: all layers except the walking player, water, UI and effects.</summary>
        const int SurfaceMask=~((1<<1)|(1<<2)|(1<<4)|(1<<5)|(1<<10));

        void Awake()
        {
            Body=GetComponent<Rigidbody>();Home=transform.position;HomeRotation=transform.rotation;lastPosition=Home;
            // Cars saved in the scene before the catalog existed carry the hatchback body.
            if(Spec==null)Apply(VehicleCatalog.Berlina);
        }
        /// <summary>Takes the handling numbers of <paramref name="spec"/>. The springs are sized for the mass so every type sits level.</summary>
        public void Apply(VehicleSpec spec)
        {
            Spec=spec;if(!Body)Body=GetComponent<Rigidbody>();
            Body.mass=spec.Mass;Body.centerOfMass=new Vector3(0,Mathf.Max(.2f,spec.Height*.2f),0);Body.angularDamping=.6f;TopSpeed=spec.TopSpeed;
            if(Anchors==null && Wheels!=null && Wheels.Length>0)
            {
                Anchors=new Vector3[Wheels.Length];WheelRadius=Wheels[0].radius;
                for(int i=0;i<Wheels.Length;i++){Anchors[i]=Wheels[i].transform.localPosition;Wheels[i].enabled=false;}
            }
            if(Anchors==null)return;
            // Stiffness that carries a quarter of the weight at the rest compression; damping a little under critical.
            float load=spec.Mass/Anchors.Length;spring=load*9.81f/RestCompression;damper=1.2f*load*Mathf.Sqrt(9.81f/RestCompression);compression=new float[Anchors.Length];
            int visuals=WheelVisuals!=null?WheelVisuals.Length:0;pairA=new int[visuals];pairB=new int[visuals];
            for(int i=0;i<visuals;i++)
            {
                // A single visible wheel sits between a pair of suspension points.
                if(visuals==Anchors.Length){pairA[i]=pairB[i]=i;}
                else if(visuals==2){pairA[i]=i*2;pairB[i]=i*2+1;}
                else{pairA[i]=i==0?0:i+1;pairB[i]=i==0?1:i+1;}
            }
            if(!Visual)Visual=transform.Find("Body");
            if(!Visual)foreach(Transform child in transform)if(child.GetComponent<MeshRenderer>()&&Array.IndexOf(WheelVisuals,child)<0){Visual=child;break;}
            if(Visual&&!bound)visualRest=Visual.localRotation;
            // Wheels keep whatever orientation their model gave them (mirrored right-hand wheels, for instance) and spin about the axle.
            steered=new bool[visuals];if(!bound)wheelRest=new Quaternion[visuals];
            for(int i=0;i<visuals;i++)
            {
                if(!WheelVisuals[i])continue;
                steered[i]=Anchors[pairA[i]].z>0&&!(SteerPivot&&WheelVisuals[i].IsChildOf(SteerPivot));
                if(!bound)wheelRest[i]=WheelVisuals[i].localRotation;
            }
            bound=true;
        }

        /// <summary>Yaw rate (rad/s) available at <paramref name="speed"/> m/s: lots of lock when slow, never more than the tyres
        /// can turn into a curve when fast. The handbrake lifts that cap so the tail can come round: about a right angle a second.</summary>
        public static float SteerRate(VehicleSpec spec,float speed,bool handbrake)
        {
            speed=Mathf.Abs(speed);
            float rate=spec.Steer*(1.08f+.42f/(1+Mathf.Pow(speed/38f,1.35f)))*Mathf.Clamp01(speed/5f);
            float gripped=Mathf.Min(rate,spec.Grip*.9f/Mathf.Max(speed,.1f));
            return handbrake?Mathf.Max(gripped*1.15f,Mathf.Min(rate*1.45f,Mathf.Lerp(1.9f,1.2f,speed/40)*spec.Steer/1.15f)):gripped;
        }
        /// <summary>Acceleration (m/s²) at <paramref name="speed"/>: full up to three quarters of the top speed, then fading to nothing.</summary>
        public static float Drive(VehicleSpec spec,float speed,float top,bool boosting)
        {
            if(speed>=top)return -Mathf.Min(3,(speed-top)*4);
            return spec.Accel*(boosting?1.8f:1)*Mathf.Clamp01((top-speed)/(top*.25f));
        }
        /// <summary>Spring force (N) for one suspension point: linear over the travel, then a stiff bump stop so the body never
        /// drops onto its floor. <paramref name="closing"/> is how fast the spring is being compressed, m/s.</summary>
        public static float SpringForce(float stiffness,float damping,float compression,float closing)
        {
            float force=stiffness*compression+damping*closing;
            if(compression>Travel)force+=stiffness*8*(compression-Travel);
            return Mathf.Clamp(force,0,stiffness*Travel*8);
        }

        void FixedUpdate()
        {
            float dt=Time.fixedDeltaTime;var k=Keyboard.current;float throttle=0;bool brake=false,boostKey=false;Handbrake=false;
            if(Driven && !Paused && k!=null)
            {
                throttle=(k.upArrowKey.isPressed||k.wKey.isPressed?1:0)-(k.downArrowKey.isPressed||k.sKey.isPressed?1:0);
                float keys=(k.rightArrowKey.isPressed||k.dKey.isPressed?1:0)-(k.leftArrowKey.isPressed||k.aKey.isPressed?1:0);
                // Keyboard steering ramps in and recentres quickly instead of snapping to full lock.
                steering=Mathf.MoveTowards(steering,keys,(keys==0||keys*steering<0?7:4)*dt);
                Handbrake=k.spaceKey.isPressed;boostKey=k.leftShiftKey.isPressed||k.rightShiftKey.isPressed;
            }
            else if(Autopilot && !Paused){throttle=AutoThrottle;steering=Mathf.Clamp(AutoSteer,-1,1);brake=AutoBrake;}
            else
            {
                // Parked: hold still. Once it has settled the body sleeps (and floats on its springs) until something knocks it.
                brake=true;steering=0;
                if(Body.IsSleeping()){Slip=0;ForwardSpeed=0;return;}
                restFor=Body.linearVelocity.sqrMagnitude<.0025f&&Body.angularVelocity.sqrMagnitude<.0025f?restFor+dt:0;
                if(restFor>1){restFor=0;Body.Sleep();return;}
            }
            Throttle=throttle;
            if(Speed<.3f && Mathf.Abs(throttle)<.05f){sinceRest=0;ZeroToHundred=-1;PeakSpeed=0;}
            else{sinceRest+=dt;PeakSpeed=Mathf.Max(PeakSpeed,Speed);if(ZeroToHundred<0 && Speed>=27.78f)ZeroToHundred=sinceRest;}
            Boosting=Driven && boostKey && throttle>0 && Boost>0 && !Handbrake;
            Boost=Mathf.Clamp01(Boost+(Boosting?(EndlessBoost?0:-dt/3):dt/8));

            Vector3 up=transform.up,forward=transform.forward,right=transform.right;
            // Suspension: one ray per corner, a spring and damper along the body's up axis.
            int touching=0;float reach=Travel+WheelRadius;
            for(int i=0;i<Anchors.Length;i++)
            {
                var origin=transform.TransformPoint(Anchors[i]);float nearest=float.MaxValue;
                int count=Physics.RaycastNonAlloc(origin,-up,hits,reach,SurfaceMask,QueryTriggerInteraction.Ignore);
                for(int h=0;h<count;h++)if(hits[h].rigidbody!=Body && hits[h].distance<nearest)nearest=hits[h].distance;
                if(nearest>reach){compression[i]=0;continue;}
                float squeeze=reach-nearest;touching++;compression[i]=Mathf.Min(squeeze,Travel);
                Body.AddForceAtPosition(up*SpringForce(spring,damper,squeeze,-Vector3.Dot(Body.GetPointVelocity(origin),up)),origin);
            }
            Grounded=touching*2>=Anchors.Length;
            var velocity=Body.linearVelocity;float vf=Vector3.Dot(velocity,forward),vl=Vector3.Dot(velocity,right),mass=Body.mass;ForwardSpeed=vf;
            if(Grounded)
            {
                if(AirTime>.25f)LongestAir=Mathf.Max(LongestAir,AirTime);
                AirTime=0;
                // Along the nose. Braking and coasting cancel gravity too, so a stopped car holds on a slope or a bridge.
                float gravity=Vector3.Dot(Physics.gravity,forward),a;
                bool footBrake=throttle*vf< -.5f && Mathf.Abs(vf)>1;
                float hold=Driven||Autopilot?Spec.Brake:6;
                if(brake||footBrake)a=Mathf.Clamp(-vf/dt-gravity,-hold,hold);
                else if(Handbrake)a=Mathf.Clamp(-vf/dt-gravity,-Spec.Brake*.3f,Spec.Brake*.3f)+throttle*Spec.Accel*.35f;
                else if(Mathf.Abs(throttle)>.05f)
                {
                    float direction=Mathf.Sign(throttle),top=direction>0?(Boosting?TopSpeed*Spec.BoostSpeed/Spec.TopSpeed:TopSpeed):Spec.Reverse;
                    a=direction*Drive(Spec,vf*direction,top,Boosting);
                }
                else a=Mathf.Clamp(-vf/dt-gravity,-1.6f,1.6f);
                Body.AddForce(forward*(a*mass));

                // Across the nose: the tyres kill sideways motion up to their grip; past that the car slides.
                // The handbrake drops rear grip at once and it comes back over about half a second, so slides recover instead of snapping.
                gripScale=Mathf.MoveTowards(gripScale,Handbrake?.34f:1,(Handbrake?6:1.9f)*dt);
                float limit=Spec.Grip*gripScale,want=-vl*14-Vector3.Dot(Physics.gravity,right),lateral=Mathf.Clamp(want,-limit,limit);
                if(Spec.Tip>0)Body.AddForceAtPosition(right*(lateral*mass),Body.worldCenterOfMass+up*Spec.Tip);
                else Body.AddForce(right*(lateral*mass));
                // Most of the sideways speed the tyres scrub off comes back as forward speed, so a slide carries the car through the corner.
                if(vf>2 && throttle>=0 && !brake)Body.AddForce(forward*(Mathf.Clamp(-lateral*vl/vf*.7f,0,9)*mass));
                Slip=Mathf.Clamp01((Mathf.Abs(vl)-1.2f)/5f);

                // Steering sets a yaw rate; without input the nose eases back onto the direction of travel.
                float target=steering*SteerRate(Spec,vf,Handbrake)*(vf>=-.5f?1:-1);
                if(Mathf.Abs(steering)<.05f && vf>3)target+=Mathf.Clamp(Mathf.Atan2(vl,vf)*2.4f,-1.2f,1.2f);
                Body.AddTorque(up*((target-Vector3.Dot(Body.angularVelocity,up))*9),ForceMode.Acceleration);
            }
            else
            {
                AirTime+=dt;Slip=0;
                // In the air: a little pitch and yaw from the keys, and the body levels itself so jumps land on the wheels.
                Body.AddTorque(right*(throttle*1.3f)+up*(steering*1.1f)+Vector3.Cross(up,Vector3.up)*4-Body.angularVelocity*.9f,ForceMode.Acceleration);
            }
            if(Spec.Lean)
            {
                // Two-wheelers never fall over: a strong upright spring replaces the rider's balance.
                var fall=Vector3.Cross(up,Vector3.up);fall-=Vector3.Project(fall,right);
                Body.AddTorque(fall*60-forward*(Vector3.Dot(Body.angularVelocity,forward)*10),ForceMode.Acceleration);
            }
            bool wedged=Driven && Mathf.Abs(throttle)>.5f && Speed<.6f;
            StuckFor=up.y<.35f||wedged?StuckFor+dt:0;
            DistanceTravelled+=Vector3.Distance(lastPosition,transform.position);lastPosition=transform.position;lastVelocity=velocity;
            if(transform.position.y< -80)ResetCar();
        }
        void OnCollisionEnter(Collision collision)
        {
            float closing=collision.relativeVelocity.magnitude;
            if(!Driven || collision.gameObject.layer==8 || closing<=4)return;
            Impacts++;LastImpact=closing;ImpactSerial++;
            if(Speed>5 && collision.rigidbody && collision.rigidbody.isKinematic && collision.gameObject.layer==11)TrafficHits++; // Only the player's own speed counts as a hit-and-run.
        }
        void LateUpdate()
        {
            float dt=Time.deltaTime;if(dt<=0 || !Driven && !Autopilot && Body.IsSleeping())return;
            spin=Mathf.Repeat(spin+ForwardSpeed*dt/WheelRadius*Mathf.Rad2Deg,360);
            steerAngle=Mathf.Lerp(steerAngle,steering*Mathf.Lerp(32,7,Mathf.Clamp01(Speed/30)),1-Mathf.Exp(-14*dt));
            // The body leans on its springs (or into the turn on two wheels); the wheels follow the suspension.
            float sideways=Vector3.Dot(Body.angularVelocity,transform.up)*ForwardSpeed;
            float lean=!Grounded?0:Spec.Lean?-Mathf.Clamp(Mathf.Atan(sideways/9.81f)*Mathf.Rad2Deg,-38,38):Mathf.Clamp(sideways*.55f,-6,6);
            float dive=Grounded?Mathf.Clamp(-Vector3.Dot(Body.linearVelocity-lastVelocity,transform.forward)/Mathf.Max(Time.fixedDeltaTime,.001f)*.22f,-3,3):0;
            roll=Mathf.Lerp(roll,lean,1-Mathf.Exp(-7*dt));pitch=Mathf.Lerp(pitch,dive,1-Mathf.Exp(-6*dt));
            if(Visual)Visual.localRotation=visualRest*Quaternion.Euler(pitch,0,roll);
            if(SteerPivot)SteerPivot.localRotation=Quaternion.Euler(0,steerAngle,0);
            for(int i=0;i<WheelVisuals.Length;i++)
            {
                var visual=WheelVisuals[i];if(!visual)continue;
                visual.localRotation=Quaternion.Euler(0,steered[i]?steerAngle:0,0)*Quaternion.Euler(spin,0,0)*wheelRest[i];
                // Two-wheelers carry their wheels on the leaning body; everything else follows its suspension.
                if(Spec.Lean)continue;
                var p=(Anchors[pairA[i]]+Anchors[pairB[i]])*.5f;p.y-=Travel-(compression[pairA[i]]+compression[pairB[i]])*.5f;
                visual.position=transform.TransformPoint(p);
            }
        }
        /// <summary>Rigidbody, collider and suspension for a hatchback-sized body; kept for scenes and scripts that predate the catalog.</summary>
        public static CityCar Equip(GameObject car,Func<Transform> wheelVisual)
        {
            var visuals=new Transform[4];for(int i=0;i<4;i++){visuals[i]=wheelVisual();visuals[i].SetParent(car.transform,false);}
            return Equip(car,VehicleCatalog.Berlina,visuals);
        }
        /// <summary>Rigidbody, collider and suspension sized from <paramref name="spec"/>. <paramref name="wheelVisuals"/> are ordered
        /// front-left, front-right, rear-left, rear-right (front, rear-left, rear-right on three wheels; front, rear on two).</summary>
        public static CityCar Equip(GameObject car,VehicleSpec spec,Transform[] wheelVisuals)
        {
            car.layer=11;
            var body=car.AddComponent<Rigidbody>();body.mass=spec.Mass;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            float bottom=Mathf.Max(.22f,spec.WheelRadius*.95f),top=Mathf.Max(bottom+.4f,spec.Height*.86f);
            var box=car.AddComponent<BoxCollider>();box.center=new Vector3(0,(top+bottom)*.5f,0);box.size=new Vector3(spec.Width,top-bottom,spec.Length);
            var motor=car.AddComponent<CityCar>();motor.WheelVisuals=wheelVisuals;motor.WheelRadius=spec.WheelRadius;motor.Anchors=new Vector3[4];int i=0;
            // Narrow hidden pairs stand in for a single wheel; the upright spring (two wheels) or the wide rear axle (three) does the balancing.
            float frontHalf=spec.Wheels==4?spec.Track*.5f:spec.Wheels==3?.1f:Mathf.Max(.12f,spec.Track*.5f),rearHalf=spec.Wheels==2?frontHalf:spec.Track*.5f;
            // With the vehicle standing still the wheel centre is one radius above the origin, so the origin rests on the road.
            foreach(float z in new[]{spec.Wheelbase*.5f,-spec.Wheelbase*.5f})foreach(float side in new[]{-1f,1f})
                motor.Anchors[i++]=new Vector3(side*(z>0?frontHalf:rearHalf),spec.WheelRadius+Travel-RestCompression,z);
            motor.Apply(spec);
            return motor;
        }
        /// <summary>Back to where it was parked.</summary>
        public void ResetCar(){Place(Home,HomeRotation);}
        /// <summary>Puts the car down upright and at rest; used for reset, recovery and test fixtures.</summary>
        public void Place(Vector3 position,Quaternion rotation)
        {
            Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;Body.position=position;Body.rotation=rotation;transform.SetPositionAndRotation(position,rotation);
            lastPosition=position;lastVelocity=Vector3.zero;StuckFor=0;AirTime=0;steering=0;gripScale=1;restFor=0;Physics.SyncTransforms();Body.WakeUp();
        }
        /// <summary>The 9 key: straight up, far enough to clear a five-storey palazzo.</summary>
        public void Launch(float height)
        {
            var v=Body.linearVelocity;v.y=Mathf.Sqrt(2*Mathf.Abs(Physics.gravity.y)*height);Body.linearVelocity=v;Body.WakeUp();
        }
    }
}
