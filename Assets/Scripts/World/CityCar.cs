using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CityCar:MonoBehaviour
    {
        public WheelCollider[] Wheels;public Transform[] WheelVisuals;public bool Driven,Paused;
        public Vector3 Home;public Quaternion HomeRotation;public float DistanceTravelled;public int Impacts,TrafficHits;
        /// <summary>Shift turbo: drains in about three seconds, refills in about eight.</summary>
        public float Boost=1;public bool Boosting{get;private set;}
        /// <summary>AI input for police cars; ignored while the player drives.</summary>
        public bool Autopilot;public float AutoThrottle,AutoSteer,TopSpeed=22;public bool AutoBrake;
        public float Speed=>Body?Body.linearVelocity.magnitude:0;
        public Rigidbody Body{get;private set;}
        Vector3 lastPosition;
        void Awake(){Body=GetComponent<Rigidbody>();Body.centerOfMass=new Vector3(0,.28f,0);Home=transform.position;HomeRotation=transform.rotation;lastPosition=Home;}
        void FixedUpdate()
        {
            var k=Keyboard.current;float throttle=0,steer=0;bool brake=true;
            if(Driven && !Paused && k!=null)
            {
                throttle=(k.upArrowKey.isPressed||k.wKey.isPressed?1:0)-(k.downArrowKey.isPressed||k.sKey.isPressed?1:0);
                steer=(k.rightArrowKey.isPressed||k.dKey.isPressed?1:0)-(k.leftArrowKey.isPressed||k.aKey.isPressed?1:0);
                brake=k.spaceKey.isPressed;
            }
            else if(Autopilot && !Paused){throttle=AutoThrottle;steer=AutoSteer;brake=AutoBrake;}
            float dt=Time.fixedDeltaTime;
            Boosting=Driven && !Paused && k!=null && (k.leftShiftKey.isPressed||k.rightShiftKey.isPressed) && throttle>0 && Boost>0 && !brake;
            Boost=Mathf.Clamp01(Boost+(Boosting?-dt/3:dt/8));
            float forward=Vector3.Dot(Body.linearVelocity,transform.forward),top=Boosting?34:TopSpeed;
            if(Boosting)Body.AddForce(transform.forward*4200);
            foreach(var w in Wheels)
            {
                w.motorTorque=brake?0:(forward>top && throttle>0 || forward< -7 && throttle<0?0:throttle*(Boosting?1100:650));
                w.brakeTorque=brake?2400:Mathf.Abs(throttle)<.1f?80:0;
                if(w.transform.localPosition.z>0)w.steerAngle=steer*Mathf.Lerp(32,12,Speed/22);
            }
            Body.AddForce(-transform.up*Speed*12);
            DistanceTravelled+=Vector3.Distance(lastPosition,transform.position);lastPosition=transform.position;
            if(transform.position.y< -5 || Mathf.Abs(transform.position.x)>1380 || Mathf.Abs(transform.position.z)>1180)ResetCar();
        }
        void OnCollisionEnter(Collision collision)
        {
            if(!Driven || collision.gameObject.layer==8 || collision.relativeVelocity.magnitude<=4)return;
            Impacts++;if(Speed>5 && collision.rigidbody && collision.rigidbody.isKinematic && collision.gameObject.layer==11)TrafficHits++; // Only the player's own speed counts as a hit-and-run.
        }
        void LateUpdate(){for(int i=0;i<Wheels.Length;i++){Wheels[i].GetWorldPose(out var p,out var q);WheelVisuals[i].SetPositionAndRotation(p,q);}}
        /// <summary>Rigidbody, collider and WheelCollider suspension shared by parked and commandeered cars.</summary>
        public static CityCar Equip(GameObject car,Func<Transform> wheelVisual)
        {
            car.layer=11;
            var body=car.AddComponent<Rigidbody>();body.mass=1100;body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            var box=car.AddComponent<BoxCollider>();box.center=new Vector3(0,.8f,0);box.size=new Vector3(1.74f,1.0f,4.1f);
            var motor=car.AddComponent<CityCar>();motor.Wheels=new WheelCollider[4];motor.WheelVisuals=new Transform[4];int i=0;
            foreach(float z in new[]{1.28f,-1.27f})foreach(float x in new[]{-.86f,.86f})
            {
                var go=new GameObject("Suspension "+i);go.transform.SetParent(car.transform,false);go.transform.localPosition=new Vector3(x,.50f,z);
                var wheel=go.AddComponent<WheelCollider>();wheel.radius=.31f;wheel.mass=22;wheel.suspensionDistance=.25f;
                var spring=wheel.suspensionSpring;spring.spring=32000;spring.damper=4200;spring.targetPosition=.5f;wheel.suspensionSpring=spring;
                var friction=wheel.sidewaysFriction;friction.stiffness=1.8f;wheel.sidewaysFriction=friction;
                motor.Wheels[i]=wheel;motor.WheelVisuals[i]=wheelVisual();motor.WheelVisuals[i].SetParent(car.transform,false);motor.WheelVisuals[i].localPosition=new Vector3(x,.32f,z);i++;
            }
            return motor;
        }
        public void ResetCar(){Body.linearVelocity=Vector3.zero;Body.angularVelocity=Vector3.zero;Body.position=Home;Body.rotation=HomeRotation;transform.SetPositionAndRotation(Home,HomeRotation);lastPosition=Home;}
    }
}
