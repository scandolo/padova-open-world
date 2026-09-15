import {Color} from './vendor/three.module.js';
import {ROAD_SURFACE_OFFSET,CURB_HEIGHT,TRAM_RAIL_OFFSET,ROAD_SKIRT_DEPTH} from './world-geometry-config.js';

const ROAD_TOP=ROAD_SURFACE_OFFSET;
export function buildModernRoads(batch,segments,terrain){
 const colours=new Map(),colour=hex=>{if(!colours.has(hex))colours.set(hex,new Color(hex));return colours.get(hex);};
 const joins=new Set();
 for(const s of segments){
  const road=s.road,ped=/^(pedestrian|footway|path|cycleway|steps)$/.test(road.k),rail=road.k==='tram',urban=!/motorway|trunk|track|path/.test(road.k);
  const dx=s.b[0]-s.a[0],dz=s.b[1]-s.a[1],length=Math.hypot(dx,dz);if(length<.01)continue;
  const nx=-dz/length,nz=dx/length,central=Math.hypot(...s.a)<1550,asphalt=colour(ped?(central?'#bdae91':'#aaa799'):rail?'#8c8980':central?'#535b5b':'#586164');
  const heights=(p,offset)=>terrain.roads.sample(road,...p)+offset;
  const section=(a,b,left,right,offset,c)=>{const [oa,ob]=Array.isArray(offset)?offset:[offset,offset],h0=heights(a,oa),h1=heights(b,ob);batch.quad([a[0]+nx*left,h0,a[1]+nz*left],[b[0]+nx*left,h1,b[1]+nz*left],[b[0]+nx*right,h1,b[1]+nz*right],[a[0]+nx*right,h0,a[1]+nz*right],c);};
  const wall=(a,b,edge,top,bottom,c)=>{const h0=terrain.roads.sample(road,...a),h1=terrain.roads.sample(road,...b),ax=a[0]+nx*edge,az=a[1]+nz*edge,bx=b[0]+nx*edge,bz=b[1]+nz*edge;batch.quad([ax,h0+top,az],[bx,h1+top,bz],[bx,h1+bottom,bz],[ax,h0+bottom,az],c);};
  const junction=(x,z)=>[...terrain.roads.index.near(x,z,12)].some(e=>[e.ia,e.ib].some(id=>{const n=terrain.roads.nodes[id];return n.degree>2&&Math.hypot(x-n.x,z-n.z)<Math.max(6,road.w);}));
  const count=Math.ceil(length/3);
  for(let i=0;i<count;i++){
   const a=[s.a[0]+dx*i/count,s.a[1]+dz*i/count],b=[s.a[0]+dx*(i+1)/count,s.a[1]+dz*(i+1)/count],mid=[(a[0]+b[0])/2,(a[1]+b[1])/2],atJunction=junction(...mid);
   section(a,b,-road.w/2-.3,road.w/2+.3,ROAD_TOP-.035,colour('#969b95'));
   section(a,b,-road.w/2,road.w/2,ROAD_TOP,asphalt);
   if(!atJunction)for(const side of [-1,1])wall(a,b,side*road.w/2,ROAD_TOP,ROAD_TOP-ROAD_SKIRT_DEPTH,colour('#555b58'));
   if(!ped&&!rail&&!atJunction){
    for(const side of [-1,1]){
     const edge=side*(road.w/2-.25);section(a,b,edge-.055,edge+.055,ROAD_TOP+.008,colour('#d7d4c2'));
     if(urban&&!road.isBridge){const off=side*(road.w/2+.65),x=mid[0]+nx*off,z=mid[1]+nz*off;
      if(!terrain.roads.candidates(x,z).some(c=>c.road!==road)&&terrain.waterDistance(x,z)>1){
       const inner=side*road.w/2,outer=side*(road.w/2+1.2),left=Math.min(inner,outer),right=Math.max(inner,outer);
       section(a,b,left,right,ROAD_TOP+CURB_HEIGHT,colour('#b7b5a8'));
       wall(a,b,inner,ROAD_TOP+CURB_HEIGHT,ROAD_TOP,colour('#aaa89c'));
      }
     }
    }
    if(road.w>=6.5&&!road.oneway&&Math.floor((i/count*length)/5)%2===0)section(a,b,-.06,.06,ROAD_TOP+.01,colour('#d7d4c2'));
   }
   if(rail)for(const offset of [-.7,.7])section(a,b,offset-.055,offset+.055,ROAD_TOP+TRAM_RAIL_OFFSET,colour('#bdc8c9'));
  }
  for(const p of [s.a,s.b]){const key=road.surfaceId+':'+p.join(',');if(joins.has(key))continue;joins.add(key);const y=terrain.roads.sample(road,...p)+ROAD_TOP+.002;
   for(let i=0;i<12;i++){const a=i*Math.PI/6,b=(i+1)*Math.PI/6,r=road.w/2;const vertex=t=>{const x=p[0]+Math.cos(t)*r,z=p[1]+Math.sin(t)*r;return [x,terrain.roads.sample(road,x,z)+ROAD_TOP+.002,z];};batch.tri([p[0],y,p[1]],vertex(a),vertex(b),asphalt);}
  }
 }
}
