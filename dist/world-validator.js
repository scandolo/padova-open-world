import {
  ROAD_ALIGNMENT_TOLERANCE,PHYSICS_ALIGNMENT_TOLERANCE,SEAM_TOLERANCE,
  CURB_HEIGHT,GROUND_RENDER_OFFSET,SURFACE_EPSILON,SURFACE_TYPES,ROAD_SURFACE_OFFSET
} from './world-geometry-config.js';

const DRIVABLE=/^(motorway|motorway_link|trunk|trunk_link|primary|primary_link|secondary|secondary_link|tertiary|tertiary_link|residential|living_street|unclassified|service)$/;
const SIDEWALK=/^(footway|path|pedestrian)$/;
const finite=n=>Number.isFinite(n);
const push=(report,kind,detail)=>{report[kind]=(report[kind]||0)+1;if(report.errors.length<200)report.errors.push({kind,...detail});};

export class WorldValidator{
 static emptyReport(chunkId='world'){return {chunkId,testedVertices:0,healedVertices:0,roadErrors:0,sidewalkErrors:0,seamErrors:0,colliderErrors:0,missingSurfaces:0,maxHeightError:0,physicsMismatch:0,bridgeErrors:0,riverErrors:0,invalidGeometry:0,degenerateTriangles:0,errors:[],passed:true};}
 static auditChunk({chunkId='world',chunk=null,terrain,collision=null,group=null,chunkSize=320}={}){
  const r=this.emptyReport(chunkId);if(!terrain){push(r,'missingSurfaces',{reason:'terrain missing'});return this.finish(r);}
  const segments=chunk?.roads||this.allSegments(terrain);
  for(const s of segments)this.auditRoad(s,r,terrain);
  if(chunk)this.auditSeams(chunk,r,terrain,chunkSize);
  if(collision&&chunk)this.auditColliders(chunk,r,collision,chunkSize);
  if(group)this.auditGeometry(group,r);
  if(!chunk)this.auditRivers(r,terrain);
  return this.finish(r);
 }
 static allSegments(terrain){const out=[];for(const p of terrain.roads?.profiles?.values?.()||[])for(let i=1;i<p.points.length;i++)out.push({a:p.points[i-1],b:p.points[i],road:p.road,profile:p,ia:p.ids[i-1],ib:p.ids[i],i:i-1});return out;}
 static segmentHeight(terrain,s,t,x,z){if(s.profile&&s.ia!==undefined)return terrain.roads.segmentHeight(s,t);return terrain.roads.sample(s.road,x,z);}
 static auditRoad(s,r,terrain){
  const road=s.road||s.profile?.road;if(!road)return;const len=Math.hypot(s.b[0]-s.a[0],s.b[1]-s.a[1]);if(len<.01)return;
  const n=Math.max(1,Math.ceil(len/3)),dx=(s.b[0]-s.a[0])/len,dz=(s.b[1]-s.a[1])/len,nx=-dz,nz=dx;
  for(let j=0;j<=n;j++){const t=j/n,x=s.a[0]+(s.b[0]-s.a[0])*t,z=s.a[1]+(s.b[1]-s.a[1])*t,base=this.segmentHeight(terrain,s,t,x,z),expected=base+ROAD_SURFACE_OFFSET;r.testedVertices++;
   if(!finite(expected)){push(r,'missingSurfaces',{x,z,road:road.surfaceId});continue;}
   const physical=terrain.getWalkableSurfaceHeight?terrain.getWalkableSurfaceHeight(x,z,expected):terrain.height(x,z,expected),delta=Math.abs(physical-expected);r.maxHeightError=Math.max(r.maxHeightError,finite(delta)?delta:Infinity);
   if(!finite(physical)||delta>PHYSICS_ALIGNMENT_TOLERANCE){push(r,'physicsMismatch',{x,z,expected,actual:physical,road:road.surfaceId});if(DRIVABLE.test(road.k))push(r,'roadErrors',{x,z,reason:'physics mismatch',delta});}
   const special=road.isBridge||road.isTunnel||road.crossing||road.tunnel||Number(road.layer)!==0;
   if(!special&&DRIVABLE.test(road.k)){const ground=terrain.getTerrainHeight?terrain.getTerrainHeight(x,z):terrain.groundHeight(x,z),renderedGround=ground-GROUND_RENDER_OFFSET;if(renderedGround>expected-SURFACE_EPSILON)push(r,'roadErrors',{x,z,reason:'terrain intrusion',roadTop:expected,ground:renderedGround});}
   if(road.isBridge||road.crossing){const overWater=terrain.waterDistance(x,z)<0;if(overWater){const water=terrain.waterHeight(x,z);if(expected<=water+.5)push(r,'bridgeErrors',{x,z,reason:'bridge/water clearance',expected,under:water});}else{const below=[...terrain.roads.candidates(x,z,1)].filter(c=>c.road!==road&&c.height<base-1);if(below.length){const lower=Math.max(...below.map(c=>c.height+ROAD_SURFACE_OFFSET)),required=below.some(c=>/footway|path|cycleway|steps/.test(c.road.k))?2.4:4.4;if(expected-lower<required)push(r,'bridgeErrors',{x,z,reason:'bridge/underpass clearance',expected,under:lower,clearance:expected-lower,required});}}}
   if(SIDEWALK.test(road.k)){const near=[...terrain.roads.candidates(x,z,4)].filter(c=>c.road!==road&&DRIVABLE.test(c.road.k)&&!c.road.isBridge&&!c.road.isTunnel);if(near.length){const anchor=near.sort((a,b)=>a.d-b.d)[0].height+ROAD_SURFACE_OFFSET,d=expected-anchor;if(d<-.03||d>CURB_HEIGHT+.18)push(r,'sidewalkErrors',{x,z,delta:d,expectedCurb:CURB_HEIGHT});}}
   if(road.k==='tram'){const near=[...terrain.roads.candidates(x,z,4)].filter(c=>c.road!==road&&DRIVABLE.test(c.road.k));if(near.length&&!road.isBridge&&!road.isTunnel){const d=Math.abs(expected-(near[0].height+ROAD_SURFACE_OFFSET));if(d>.12)push(r,'roadErrors',{x,z,reason:'tram/road mismatch',delta:d});}}
   if(!special&&DRIVABLE.test(road.k))for(const side of [-1,1]){const off=road.w/2+.9,gx=x+nx*off*side,gz=z+nz*off*side,g=terrain.getTerrainHeight?terrain.getTerrainHeight(gx,gz):terrain.groundHeight(gx,gz);if(!finite(g))push(r,'missingSurfaces',{x:gx,z:gz,reason:'road edge ground missing'});}
  }
 }
 static auditRivers(r,terrain){const seen=new Set();for(const items of terrain.waterIndex?.cells?.values?.()||[])for(const w of items){if(seen.has(w))continue;seen.add(w);let x,z;if(w.a&&w.b){x=(w.a[0]+w.b[0])/2;z=(w.a[1]+w.b[1])/2;}else if(w.p?.length){x=w.p.reduce((a,p)=>a+p[0],0)/w.p.length;z=w.p.reduce((a,p)=>a+p[1],0)/w.p.length;}else continue;if(terrain.waterDistance(x,z)>=0)continue;const bed=terrain.getTerrainHeight(x,z),water=terrain.waterHeight(x,z);if(!finite(bed)||!finite(water)||bed>=water-SURFACE_EPSILON)push(r,'riverErrors',{x,z,bed,water});}}
 static auditSeams(chunk,r,terrain,chunkSize){const x0=chunk.i*chunkSize,z0=chunk.j*chunkSize,x1=x0+chunkSize,z1=z0+chunkSize,eps=.001,check=(a,b,x,z)=>{const d=Math.abs(a-b);if(!finite(a)||!finite(b)||d>SEAM_TOLERANCE)push(r,'seamErrors',{x,z,a,b,delta:d});};for(let p=0;p<=chunkSize;p+=16){let z=z0+p,x=x0+p;check(terrain.getTerrainHeight(x0-eps,z),terrain.getTerrainHeight(x0+eps,z),x0,z);check(terrain.getTerrainHeight(x1-eps,z),terrain.getTerrainHeight(x1+eps,z),x1,z);check(terrain.getTerrainHeight(x,z0-eps),terrain.getTerrainHeight(x,z0+eps),x,z0);check(terrain.getTerrainHeight(x,z1-eps),terrain.getTerrainHeight(x,z1+eps),x,z1);}}
 static auditColliders(chunk,r,collision,chunkSize){const cx=(chunk.i+.5)*chunkSize,cz=(chunk.j+.5)*chunkSize;for(const b of collision.near(cx,cz,chunkSize*.75)){if(![b.minX,b.maxX,b.minZ,b.maxZ,b.minY??0,b.h??0].every(finite)||b.minX>b.maxX||b.minZ>b.maxZ||((b.h??1)<=0))push(r,'colliderErrors',{reason:'invalid collider bounds'});if(b.isIntentionalInvisibleCollider===false&&!b.collisionOwner&&!b.visualOwner)push(r,'colliderErrors',{reason:'orphan collider',x:b.x??(b.minX+b.maxX)/2,z:b.z??(b.minZ+b.maxZ)/2});}}
 static auditGeometry(group,r){group.traverse?.(o=>{if(!o.isMesh||!o.geometry)return;const g=o.geometry,p=g.getAttribute?.('position');if(!p)return;r.testedVertices+=p.count;for(const n of p.array)if(!finite(n)){push(r,'invalidGeometry',{reason:'non-finite position'});break;}const index=g.index?.array;if(index){for(const i of index)if(i<0||i>=p.count){push(r,'invalidGeometry',{reason:'invalid index',index:i,count:p.count});break;}}const triCount=index?Math.floor(index.length/3):Math.floor(p.count/3);for(let i=0;i<triCount;i++){const ia=index?index[i*3]:i*3,ib=index?index[i*3+1]:i*3+1,ic=index?index[i*3+2]:i*3+2,ax=p.getX(ia),ay=p.getY(ia),az=p.getZ(ia),bx=p.getX(ib),by=p.getY(ib),bz=p.getZ(ib),cx=p.getX(ic),cy=p.getY(ic),cz=p.getZ(ic),ux=bx-ax,uy=by-ay,uz=bz-az,vx=cx-ax,vy=cy-ay,vz=cz-az,cxv=uy*vz-uz*vy,cyv=uz*vx-ux*vz,czv=ux*vy-uy*vx;if(cxv*cxv+cyv*cyv+czv*czv<1e-12){r.degenerateTriangles++;if(r.degenerateTriangles<20)r.errors.push({kind:'degenerateTriangle',mesh:o.name||o.userData?.surfaceType||'mesh'});}}
   const normal=g.getAttribute?.('normal');if(normal)for(const n of normal.array)if(!finite(n)){push(r,'invalidGeometry',{reason:'invalid normal'});break;}
   if(g.boundingBox&&(!finite(g.boundingBox.min.x)||!finite(g.boundingBox.max.x)))push(r,'invalidGeometry',{reason:'invalid bounding box'});
  });}
 static scanArea({terrain,center,radius=45,coarse=5,dense=1}={}){
  const report={center,radius,coarse,dense,tested:0,anomalies:[],passed:true};if(!terrain||!center){report.passed=false;report.anomalies.push({reason:'missing terrain or center'});return report;}
  const test=(x,z)=>{report.tested++;const info=terrain.getSurfaceInfo?.(x,z),h=info?.height??terrain.height?.(x,z);if(!finite(h)){report.anomalies.push({x,z,reason:'missing surface'});return;}if(info?.road){const physics=terrain.getWalkableSurfaceHeight?.(x,z,h)??terrain.height(x,z,h),delta=Math.abs(physics-h);if(!finite(physics)||delta>PHYSICS_ALIGNMENT_TOLERANCE)report.anomalies.push({x,z,reason:'render/physics surface mismatch',delta,type:info.type});}if(info?.isRiver){const bed=terrain.getTerrainHeight?.(x,z),water=terrain.waterHeight?.(x,z);if(!finite(bed)||!finite(water)||bed>=water-SURFACE_EPSILON)report.anomalies.push({x,z,reason:'riverbed not below water',bed,water});}};
  const coarseHits=[];for(let x=center.x-radius;x<=center.x+radius;x+=coarse)for(let z=center.z-radius;z<=center.z+radius;z+=coarse){const before=report.anomalies.length;test(x,z);if(report.anomalies.length>before)coarseHits.push({x,z});}
  const seen=new Set();for(const a of coarseHits)for(let x=a.x-coarse;x<=a.x+coarse;x+=dense)for(let z=a.z-coarse;z<=a.z+coarse;z+=dense){const key=x.toFixed(2)+','+z.toFixed(2);if(seen.has(key))continue;seen.add(key);test(x,z);}
  report.passed=!report.anomalies.length;return report;
 }
 static finish(r){r.passed=!(r.roadErrors||r.sidewalkErrors||r.seamErrors||r.colliderErrors||r.missingSurfaces||r.physicsMismatch||r.bridgeErrors||r.riverErrors||r.invalidGeometry);return r;}
}
