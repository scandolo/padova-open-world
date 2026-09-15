import {COLLIDER_AUDIT_RADIUS} from './world-geometry-config.js';

const finite=n=>Number.isFinite(n);
const polygonBounds=p=>({minX:Math.min(...p.map(v=>v[0])),maxX:Math.max(...p.map(v=>v[0])),minZ:Math.min(...p.map(v=>v[1])),maxZ:Math.max(...p.map(v=>v[1]))});

export class BuildingCollisionManager{
 constructor(index,terrain=null){this.index=index;this.terrain=terrain;this.byChunk=new Map();this.all=new Set();}
 register(collider,{owner=null,ownerId=null,chunkId=null,type='polygon-prism',intentionalInvisible=false}={}){
  if(!collider?.p?.length)return null;Object.assign(collider,polygonBounds(collider.p));collider.colliderType=type;collider.collisionOwner=owner||null;collider.collisionOwnerId=ownerId||owner?.id||owner?.n||null;collider.isIntentionalInvisibleCollider=!!intentionalInvisible;collider.chunkId=chunkId;
  this.index.add(collider,collider.minX,collider.minZ,collider.maxX,collider.maxZ);this.all.add(collider);if(chunkId!==null){if(!this.byChunk.has(chunkId))this.byChunk.set(chunkId,new Set());this.byChunk.get(chunkId).add(collider);}return collider;
 }
 registerBuilding(building,chunkId=null){return this.register(building,{owner:building,ownerId:building.id||building.n,chunkId,type:'building-footprint'});}
 registerStructure(structure,chunkId=null){return this.register(structure,{owner:structure.road||structure,ownerId:structure.road?.surfaceId,chunkId,type:structure.kind||'road-structure'});}
 registerTree(x,z,height,chunkId,radius=.35){const p=[];for(let i=0;i<8;i++){const a=i*Math.PI/4;p.push([x+Math.cos(a)*radius,z+Math.sin(a)*radius]);}return this.register({p,x,z,minY:this.terrain?.getTerrainHeight?.(x,z)??0,h:Math.max(1,height),generatedTree:true},{ownerId:`tree:${chunkId}:${x.toFixed(1)}:${z.toFixed(1)}`,chunkId,type:'tree-trunk'});}
 remove(c){if(this.index.remove)return this.index.remove(c);let n=0;for(const [key,items] of this.index.cells||[]){for(let i=items.length-1;i>=0;i--)if(items[i]===c){items.splice(i,1);n++;}if(!items.length)this.index.cells.delete(key);}return n;}
 clearChunk(chunkId,{generatedOnly=false}={}){const set=this.byChunk.get(chunkId);if(!set)return 0;let n=0;for(const c of [...set]){if(generatedOnly&&!c.generatedTree)continue;this.remove(c);set.delete(c);this.all.delete(c);n++;}if(!set.size)this.byChunk.delete(chunkId);return n;}
 audit(center=null,radius=COLLIDER_AUDIT_RADIUS){const errors=[],seen=new Set(),items=center?[...this.index.near(center.x,center.z,radius)]:[...this.all];for(const c of items){if(seen.has(c)){errors.push({kind:'duplicateCollider',ownerId:c.collisionOwnerId});continue;}seen.add(c);if(![c.minX,c.maxX,c.minZ,c.maxZ,c.minY??0,c.h??1].every(finite)||c.minX>c.maxX||c.minZ>b.maxZ||(c.h??1)<=0)errors.push({kind:'invalidCollider',ownerId:c.collisionOwnerId});if(!c.isIntentionalInvisibleCollider&&!c.collisionOwner&&!c.collisionOwnerId)errors.push({kind:'orphanCollider',x:c.x??(c.minX+c.maxX)/2,z:c.z??(c.minZ+c.maxZ)/2});if(c.collisionOwner?.p?.length){const b=polygonBounds(c.collisionOwner.p),delta=Math.max(Math.abs(b.minX-c.minX),Math.abs(b.maxX-c.maxX),Math.abs(b.minZ-c.minZ),Math.abs(b.maxZ-c.maxZ));if(delta>.25)errors.push({kind:'ownerBoundsMismatch',ownerId:c.collisionOwnerId,delta});}}return {tested:items.length,errors,passed:!errors.length};}
}
