import {vehicleFootprint} from './movement.js';

// The same boxes feed rendering, vehicle/foot collision, and camera collision.
export function roadStructures(terrain){const boxes=[];
 const add=(x,z,y,w,h,length,yaw,kind,road)=>{if(h<=0)return;const p=vehicleFootprint(x,z,yaw,w,length),xs=p.map(p=>p[0]),zs=p.map(p=>p[1]);boxes.push({x,z,y,w,h,length,yaw,kind,road,p,minX:Math.min(...xs),maxX:Math.max(...xs),minZ:Math.min(...zs),maxZ:Math.max(...zs),minY:y});};
 for(const profile of terrain.roads.profiles.values()){const road=profile.road;if(!road.crossing||road.k==='tram')continue;let run=0;
  for(let i=1;i<profile.points.length;i++){const a=profile.points[i-1],b=profile.points[i],x=(a[0]+b[0])/2,z=(a[1]+b[1])/2;if(terrain.prato(x,z))continue;const h=terrain.roads.sample(road,x,z),base=terrain.elevation(x,z),len=Math.hypot(b[0]-a[0],b[1]-a[1]);if(h-base<.7&&!profile.wet[i]&&!profile.wet[i-1])continue;
   const yaw=Math.atan2(b[0]-a[0],b[1]-a[1]);
   for(const side of [-1,1]){const px=x+Math.cos(yaw)*(road.w/2+.7)*side,pz=z-Math.sin(yaw)*(road.w/2+.7)*side;
    // Do not put a parapet across a connected approach or a parallel carriageway.
    if(terrain.roads.candidates(px,pz,terrain.modern?1.5:.25).some(s=>s.road!==road&&Math.abs(s.height-h)<(terrain.modern?3:1.5)))continue;
    add(px,pz,h,.28,1.1,len+.04,yaw,'parapet',road);
    // An elevated deck must have visible supports, including spans whose banks
    // are less than three metres below it. Never plant a pier on another road.
    if(run>=18&&h-base>1.4&&!terrain.roads.candidates(px,pz,terrain.modern?3:1).some(s=>s.road!==road&&(terrain.modern||s.height<h-2))){add(px,pz,base-.2,1.0,h-base+.2,1.0,yaw,'pier',road);}
   }run+=len;if(run>=24)run=0;
   // Use the local centre height for this short (approximately 6 m) span.
   // Moving a deck to a neighbouring road's height used to leave visible gaps
   // beneath its own asphalt and disconnected collision boxes at junctions.
   const deckTop=terrain.modern?h-.18:h-.05;
   add(x,z,deckTop-.35,road.w+.8,.35,len+.05,yaw,'deck',road);if(terrain.modern)boxes.at(-1).driveTopMin=deckTop+.12;
  }
 }
 if(terrain.modern)return boxes.filter(b=>{
  const candidates=[];for(const t of [-.5,-.25,0,.25,.5]){const x=b.x+Math.sin(b.yaw)*b.length*t,z=b.z+Math.cos(b.yaw)*b.length*t;for(const s of terrain.roads.candidates(x,z,b.kind==='deck'?0:1.3))if(s.road!==b.road&&!/footway|path|steps|cycleway|tram|pedestrian/.test(s.road.k))candidates.push(s);}
  // Parapets/piers may not obstruct a carriageway passing beside or below them.
  // Decks remain fixed to their own road: a neighbouring deck is not a reason
  // to translate this slab away from the surface used by rendering and physics.
  if(b.kind!=='deck')return !candidates.some(s=>s.height+2>b.minY&&s.height<b.minY+b.h+.2);
  return true;
 });
 return boxes;
}
