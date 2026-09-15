import {vehicleFootprint} from './movement.js';
import {ROAD_SURFACE_OFFSET,BRIDGE_DECK_THICKNESS,SURFACE_TYPES} from './world-geometry-config.js';

// The same boxes feed rendering, vehicle/foot collision, and camera collision.
export function roadStructures(terrain){const boxes=[];
 const add=(x,z,y,w,h,length,yaw,kind,road)=>{if(h<=0)return;const p=vehicleFootprint(x,z,yaw,w,length),xs=p.map(p=>p[0]),zs=p.map(p=>p[1]);boxes.push({x,z,y,w,h,length,yaw,kind,road,p,minX:Math.min(...xs),maxX:Math.max(...xs),minZ:Math.min(...zs),maxZ:Math.max(...zs),minY:y,surfaceType:SURFACE_TYPES.BRIDGE,isBridge:true,isTunnel:false,supportsTerrainSnap:false,collisionOwner:road,collisionOwnerId:road?.surfaceId,isIntentionalInvisibleCollider:false});};
 for(const profile of terrain.roads.profiles.values()){const road=profile.road;if(!road.crossing||road.k==='tram')continue;let run=0;
  for(let i=1;i<profile.points.length;i++){const a=profile.points[i-1],b=profile.points[i],x=(a[0]+b[0])/2,z=(a[1]+b[1])/2;if(terrain.prato(x,z))continue;const h=terrain.roads.sample(road,x,z),base=terrain.elevation(x,z),len=Math.hypot(b[0]-a[0],b[1]-a[1]);if(h-base<.7&&!profile.wet[i]&&!profile.wet[i-1])continue;
   const yaw=Math.atan2(b[0]-a[0],b[1]-a[1]);
   for(const side of [-1,1]){const px=x+Math.cos(yaw)*(road.w/2+.7)*side,pz=z-Math.sin(yaw)*(road.w/2+.7)*side;
    if(terrain.roads.candidates(px,pz,terrain.modern?1.5:.25).some(s=>s.road!==road&&Math.abs(s.height-h)<(terrain.modern?3:1.5)))continue;
    add(px,pz,h,.28,1.1,len+.04,yaw,'parapet',road);
    if(run>=24&&h-base>3&&!terrain.roads.candidates(px,pz,terrain.modern?2:1).some(s=>s.road!==road&&(terrain.modern||s.height<h-2)))add(px,pz,base-.2,1.0,h-base+.2,1.0,yaw,'pier',road);
   }run+=len;if(run>=30)run=0;
   const roadTop=Math.min(terrain.roads.sample(road,...a),terrain.roads.sample(road,...b),h)+ROAD_SURFACE_OFFSET,deckTop=roadTop-.025;
   add(x,z,deckTop-BRIDGE_DECK_THICKNESS,road.w+.8,BRIDGE_DECK_THICKNESS,len+.05,yaw,'deck',road);if(terrain.modern)boxes.at(-1).driveTopMin=roadTop;
  }
 }
 if(terrain.modern)return boxes.filter(b=>{
  const candidates=[];for(const t of [-.5,-.25,0,.25,.5]){const x=b.x+Math.sin(b.yaw)*b.length*t,z=b.z+Math.cos(b.yaw)*b.length*t;for(const s of terrain.roads.candidates(x,z,b.kind==='deck'?0:1.3))if(s.road!==b.road&&!/footway|path|steps|cycleway|tram|pedestrian/.test(s.road.k))candidates.push(s);}
  if(b.kind!=='deck')return !candidates.some(s=>s.height+2>b.minY&&s.height<b.minY+b.h+.2);
  const adjacent=candidates.filter(s=>Math.abs(s.height+ROAD_SURFACE_OFFSET-b.driveTopMin)<2);
  if(adjacent.length){b.driveTopMin=Math.min(b.driveTopMin,...adjacent.map(s=>s.height+ROAD_SURFACE_OFFSET));b.minY=b.y=b.driveTopMin-BRIDGE_DECK_THICKNESS-.025;}
  return true;
 });
 return boxes;
}
