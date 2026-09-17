import {RoadSurfaces} from './road-surfaces.js';
import {SpatialIndex, nearestOnSegment, pointInside, clamp, safeRoadPoint} from './core.js';
import {vehicleBlocked} from './movement.js';

const smooth=t=>{t=clamp(t,0,1);return t*t*(3-2*t);};
export const ROAD_TOP=.06;
export const PRATO={x:-35,z:858,yaw:-.23,outer:[90,135],inner:[81,126]};
export function pratoLocal(x,z){const dx=x-PRATO.x,dz=z-PRATO.z,c=Math.cos(PRATO.yaw),s=Math.sin(PRATO.yaw);return {x:c*dx-s*dz,z:s*dx+c*dz};}

// Heights are metres, without vertical exaggeration. Hydrology is a continuous
// regional approximation; the source map has no surveyed water levels / locks.
export class Terrain {
  constructor(grid, map, {modern=false}={}){
    this.modern=modern;
    if(!grid||grid.version!==1||grid.width<2||grid.height<2||!(grid.step>0)||grid.heights.length!==grid.width*grid.height||!grid.heights.every(Number.isFinite)||grid.waterPlane?.length!==3||!grid.waterPlane.every(Number.isFinite))throw new Error('Invalid terrain data');
    this.grid=grid;this.fountains=[];this.waterIndex=new SpatialIndex(80);this.bridgeIndex=new SpatialIndex(80);
    const add=(index,item,p,pad=0)=>{const xs=p.map(v=>v[0]),zs=p.map(v=>v[1]);index.add(item,Math.min(...xs)-pad,Math.min(...zs)-pad,Math.max(...xs)+pad,Math.max(...zs)+pad);};
    for(const r of map.water.filter(r=>!r.tunnel&&r.layer>=0||!r.tunnel&&r.layer===undefined))for(let i=1;i<r.p.length;i++){
      const a=r.p[i-1],b=r.p[i],count=Math.max(1,Math.ceil(Math.hypot(b[0]-a[0],b[1]-a[1])/80));
      for(let j=0;j<count;j++){const p=[a[0]+(b[0]-a[0])*j/count,a[1]+(b[1]-a[1])*j/count],q=[a[0]+(b[0]-a[0])*(j+1)/count,a[1]+(b[1]-a[1])*(j+1)/count];add(this.waterIndex,{a:p,b:q,w:r.w},[p,q],r.w/2+12);}
    }
    for(const a of map.areas)if(a.k==='water'){const area=Math.abs(a.p.reduce((sum,p,i)=>{const q=a.p[(i+1)%a.p.length];return sum+p[0]*q[1]-q[0]*p[1];},0))/2;if(area<150&&pointInside(-77.6,-41.7,a.p)){a.fountain=true;this.fountains.push(a);}else {a.shallow=area<150;const cx=a.p.reduce((s,p)=>s+p[0],0)/a.p.length,cz=a.p.reduce((s,p)=>s+p[1],0)/a.p.length;add(this.waterIndex,{p:a.p,level:a.shallow?this.elevation(cx,cz)-.35:undefined},a.p,12);}}
    for(const road of map.roads)if(road.b){
      const lengths=road.p.slice(1).map((p,i)=>Math.hypot(p[0]-road.p[i][0],p[1]-road.p[i][1])),total=lengths.reduce((a,b)=>a+b,0);let offset=0;
      for(let i=1;i<road.p.length;i++){const a=road.p[i-1],b=road.p[i];add(this.bridgeIndex,{a,b,w:road.w,offset,length:lengths[i-1],total},[a,b],road.w/2+2);offset+=lengths[i-1];}
    }
    this.pratoHeight=this.elevation(PRATO.x,PRATO.z);this.roads=new RoadSurfaces(map,this);
  }
  elevation(x,z){const g=this.grid,u=clamp((x-g.x0)/g.step,0,g.width-1),v=clamp((z-g.z0)/g.step,0,g.height-1),i=Math.min(g.width-2,Math.floor(u)),j=Math.min(g.height-2,Math.floor(v)),a=u-i,b=v-j,h=(i,j)=>g.heights[j*g.width+i];return h(i,j)*(1-a)*(1-b)+h(i+1,j)*a*(1-b)+h(i,j+1)*(1-a)*b+h(i+1,j+1)*a*b;}
  waterHeight(x,z){const p=this.grid.waterPlane;return this.waterSample(x,z).level??(p[0]+p[1]*x+p[2]*z-1.8);}
  prato(x,z){const p=pratoLocal(x,z);if((p.x/115)**2+(p.z/163)**2>1)return null;const canal=(p.x/90)**2+(p.z/135)**2<1&&(p.x/81)**2+(p.z/126)**2>1;return {canal,bridge:Math.abs(p.x)<5.5||Math.abs(p.z)<4.5};}
  waterSample(x,z){let distance=Infinity,level;for(const r of this.waterIndex.near(x,z)){
    let d=Infinity;if(r.p){for(let i=0;i<r.p.length;i++){const q=nearestOnSegment(x,z,r.p[i],r.p[(i+1)%r.p.length]);d=Math.min(d,Math.hypot(q.x-x,q.z-z));}if(pointInside(x,z,r.p))d=-Math.max(.01,d);}
    else{const q=nearestOnSegment(x,z,r.a,r.b);d=Math.hypot(q.x-x,q.z-z)-r.w/2;}
    if(d<distance){distance=d;level=r.level;}
  }return {distance,level};}
  waterDistance(x,z){return this.waterSample(x,z).distance;}
  bridge(x,z,margin=0,referenceY=null){return this.roads?.bridge(x,z,referenceY)||null;}
  waterAt(x,z,margin=0,referenceY=null){
    // A bridge only masks the water for an actor actually on its deck. Otherwise
    // someone falling beneath an overpass could never trigger water recovery.
    if(this.modern){const support=this.roads.at(x,z,referenceY,margin);if(support&&(referenceY===null||referenceY>=support.height-.75)&&support.height>this.waterHeight(x,z)+.5)return null;}
    const prato=this.prato(x,z);if(prato)return prato.canal&&!prato.bridge?this.pratoHeight-1.5:null;
    if(this.modern){const road=this.roads.at(x,z,referenceY,margin);if(road&&(referenceY===null||referenceY>=road.height-.75)&&road.height>this.waterHeight(x,z)+.5)return null;}
    const crossing=this.bridge(x,z,margin,referenceY);if(crossing&&(referenceY===null||referenceY>=crossing.height-.75))return null;
    return this.waterDistance(x,z)<margin?this.waterHeight(x,z):null;
  }
  groundHeight(x,z){
    const prato=this.prato(x,z);if(prato)return this.pratoHeight+(prato.canal?-3:0);
    let raw=this.elevation(x,z);
    // Terrain tiles are 16 m wide, so the road influence must extend beyond a tile
    // diagonal. This prevents an unsampled grass corner from interpolating through
    // asphalt while retaining true grade separation for bridges and tunnels.
    const road=this.roads?.at(x,z,null,28),layer=Number(road?.road?.layer)||0,gradeSeparated=!!road&&(road.road.crossing||road.road.tunnel||layer!==0);
    if(road&&!gradeSeparated){
      const apron=Math.max(18,Math.min(26,road.road.w*1.6+12)),blend=1-smooth((road.d-road.road.w/2)/apron);
      raw=raw*(1-blend)+(road.height-.015)*blend;
    }
    const d=this.waterDistance(x,z);if(this.modern&&road&&road.d<=road.road.w/2&&!gradeSeparated)return raw;if(d>10)return raw;const channel=this.waterHeight(x,z)-1.5;return channel+(Math.max(raw,this.waterHeight(x,z)+.8)-channel)*smooth((d+1)/11);
  }
  height(x,z,referenceY=null){
    if(this.modern){
      const support=this.roads.at(x,z,referenceY);
      if(support){
        const top=support.height+ROAD_TOP;
        // Do not teleport a vehicle from below an overpass onto its elevated deck.
        if(referenceY!==null&&support.road.crossing&&top-referenceY>1.25)return this.groundHeight(x,z)+.05;
        return top;
      }
      // Road meshes include a 30 cm verge and, on ordinary streets, a 1.2 m
      // pavement. The physics support must extend over precisely these edges;
      // otherwise a wheel crossing the visible pavement abruptly drops into
      // the interpolated terrain or riverbank.
      const verge=this.roads.at(x,z,referenceY,1.2);
      if(verge){
        const road=verge.road,separated=road.crossing||road.tunnel||Number(road.layer)!==0;
        const urban=!/^(motorway|motorway_link|trunk|trunk_link|track|path|footway|cycleway|pedestrian|steps|tram)$/.test(road.k);
        const margin=urban&&!separated&&this.waterDistance(x,z)>1?1.2:.3;
        const top=verge.height+ROAD_TOP;
        if(verge.d<=road.w/2+margin&&(referenceY===null||Math.abs(top-referenceY)<1.25))return top+(margin> .3?.012:-.035);
      }
    }
    const prato=this.prato(x,z);if(prato)return this.pratoHeight+(prato.bridge?.36:prato.canal?-3:.18);
    const road=this.roads?.at(x,z,referenceY);return road?road.height+ROAD_TOP:this.groundHeight(x,z)+.05;
  }
  slope(x,z,yaw,wheelbase=2.5,referenceY=null){const dx=Math.sin(yaw)*wheelbase/2,dz=Math.cos(yaw)*wheelbase/2;return -Math.atan2(this.height(x+dx,z+dz,referenceY)-this.height(x-dx,z-dz,referenceY),wheelbase);}
  dry(x,z,radius=.4,referenceY=null){for(const [dx,dz] of [[0,0],[radius,0],[-radius,0],[0,radius],[0,-radius]])if(this.waterAt(x+dx,z+dz,0,referenceY)!==null)return false;return true;}
}

export function safeDryRoad(pos,graph,collision,terrain,spec=null,allowed=()=>true){
  const radius=spec?spec.width/2:.4;
  const point=safeRoadPoint(pos,graph,collision,radius,p=>allowed(p)&&p.x>-5960&&p.x<7240&&p.z>-6470&&p.z<6220&&terrain.dry(p.x,p.z,spec?spec.length/2+.3:1)&&(!spec||!vehicleBlocked(p.x,p.z,p.yaw,collision,spec,terrain.roads.sample(p.segment.road,p.x,p.z)+ROAD_TOP)),p=>terrain.roads.sample(p.segment.road,p.x,p.z)+ROAD_TOP);
  if(point&&terrain.modern)point.y=terrain.roads.sample(point.segment.road,point.x,point.z)+ROAD_TOP;return point;
}

// Independent from rendering, so falling/recovery remains deterministic at 60 Hz.
export class WaterRecovery {
  constructor(){this.active=false;this.lastDry=null;this.elapsed=0;}
  remember(pose,terrain){if(!this.active&&terrain.dry(pose.x,pose.z,3))this.lastDry={x:pose.x,z:pose.z,yaw:pose.yaw};}
  enter(y,waterY){if(this.active)return false;this.active=true;this.elapsed=0;this.y=Math.max(y,waterY+.6);this.waterY=waterY;this.vy=0;return true;}
  step(dt){this.elapsed+=dt;this.vy-=12*dt;this.y+=this.vy*dt;return this.elapsed>=1.35;}
  reset(){this.active=false;this.elapsed=0;}
}
