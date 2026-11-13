using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    public sealed class TourPlane:MonoBehaviour
    {
        public bool Flying,Paused;public float Speed=38,Altitude,DistanceTravelled;public float MinimumAltitude=93;public int Recoveries;
        /// <summary>Half-size of the area the tour stays inside (m from the origin); the plane turns back at its edge.</summary>
        public Vector2 Half=new Vector2(1330,1130);
        public Transform Propeller;float heading,pitch,bank;
        public void Launch(Vector3 above,float yaw){heading=yaw;pitch=bank=0;Speed=38;transform.SetPositionAndRotation(above,Quaternion.Euler(0,yaw,0));Flying=true;gameObject.SetActive(true);}
        void Update()
        {
            if(!Flying || Paused)return;
            float dt=Mathf.Min(Time.deltaTime,.05f);var k=Keyboard.current;float turn=0,climb=0;
            if(k!=null)
            {
                turn=(k.rightArrowKey.isPressed||k.dKey.isPressed?1:0)-(k.leftArrowKey.isPressed||k.aKey.isPressed?1:0);
                climb=(k.upArrowKey.isPressed||k.wKey.isPressed?1:0)-(k.downArrowKey.isPressed||k.sKey.isPressed?1:0);
                Speed=Mathf.MoveTowards(Speed,k.spaceKey.isPressed?20:k.leftShiftKey.isPressed||k.rightShiftKey.isPressed?64:38,12*dt);
            }
            bank=Mathf.Lerp(bank,-turn*38,dt*3);pitch=Mathf.Lerp(pitch,-climb*23,dt*2);heading+=turn*27*dt;
            var rotation=Quaternion.Euler(pitch,heading,bank);var from=transform.position;var to=from+(Quaternion.Euler(pitch,heading,0)*Vector3.forward)*Speed*dt;
            if(Mathf.Abs(to.x)>Half.x || Mathf.Abs(to.z)>Half.y){heading=Mathf.Atan2(-to.x,-to.z)*Mathf.Rad2Deg;to=from;}
            // Flight assist keeps this sightseeing aircraft clear of the surveyed skyline.
            float minimum=MinimumAltitude; // Highest imported roof is 72.938 m, plus 20 m clearance.
            if(Physics.SphereCast(to+Vector3.up*250,5,Vector3.down,out var hit,500,(1<<8)|(1<<9)))minimum=Mathf.Max(minimum,hit.point.y+22);
            to.y=Mathf.Clamp(to.y,minimum,300);Altitude=to.y;
            transform.SetPositionAndRotation(to,rotation);DistanceTravelled+=Vector3.Distance(from,to);
            if(Propeller)Propeller.Rotate(Vector3.forward,1600*dt,Space.Self);
        }
    }
}
