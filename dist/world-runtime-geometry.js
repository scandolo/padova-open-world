import * as THREE from './vendor/three.module.js';
import {CityWorld,GeometryBatch,CHUNK} from './world.js';
import {BuildingCollisionManager} from './building-collision-manager.js';
import {WorldSelfTester} from './world-self-tester.js';
import {WorldValidator} from './world-validator.js';
import {GROUND_RENDER_OFFSET,TERRAIN_DENSE_TILE,SURFACE_TYPES} from './world-geometry-config.js';

const batchMesh=GeometryBatch.prototype.mesh;
GeometryBatch.prototype.mesh=function(mat){const mesh=batchMesh.call(this,mat);if(mesh)mesh.geometry.computeBoundingBox();return mesh;};

function ensureRuntime(world){
 if(world.collisionManager)return;
 world.collisionManager=new BuildingCollisionManager(world.collision,world.terrain);
 world.validationReports=new Map();
 for(const b of world.data?.buildings||[]){b.colliderType='building-footprint';b.collisionOwner=b;b.collisionOwnerId=b.id||b.n||null;b.isIntentionalInvisibleCollider=false;world.collisionManager.all.add(b);}
 for(const b of world.structures||[]){b.colliderType=b.kind||'road-structure';b.collisionOwner=b.road||b;b.collisionOwnerId=b.road?.surfaceId||null;b.isIntentionalInvisibleCollider=false;world.collisionManager.all.add(b);}
}
const midpoint=(a,b,terrain)=>({x:(a.x+b.x)/2,z:(a.z+b.z)/2,y:terrain.getTerrainHeight((a.x+b.x)/2,(a.z+b.z)/2)-GROUND_RENDER_OFFSET,c:a.c.map((v,i)=>(v+b.c[i])/2),uv:a.uv.map((v,i)=>(v+b.uv[i])/2)});
const edge=(a,b)=>Math.hypot(a.x-b.x,a.z-b.z);
function retessellateTerrain(mesh,terrain){
 const g=mesh.geometry,p=g.getAttribute('position'),c=g.getAttribute('color'),uv=g.getAttribute('uv');if(!p||g.index)return 0;
 const outP=[],outC=[],outUv=[];let added=0;
 const emit=(a,b,d)=>{for(const v of [a,b,d]){outP.push(v.x,v.y,v.z);outC.push(...v.c);outUv.push(...v.uv);}};
 const split=(a,b,d,depth=0)=>{const edges=[[a,b,d,edge(a,b)],[b,d,a,edge(b,d)],[d,a,b,edge(d,a)]].sort((u,v)=>v[3]-u[3]),[u,v,w,longest]=edges[0],cx=(a.x+b.x+d.x)/3,cz=(a.z+b.z+d.z)/3,dense=longest>TERRAIN_DENSE_TILE+.1&&terrain.groundNeedsSubdivision(cx,cz,longest*1.5);if(!dense||depth>=4){emit(a,b,d);return;}const m=midpoint(u,v,terrain);added++;split(u,m,w,depth+1);split(m,v,w,depth+1);};
 for(let i=0;i<p.count;i+=3){const v=k=>({x:p.getX(k),y:terrain.getTerrainHeight(p.getX(k),p.getZ(k))-GROUND_RENDER_OFFSET,z:p.getZ(k),c:c?[c.getX(k),c.getY(k),c.getZ(k)]:[1,1,1],uv:uv?[uv.getX(k),uv.getY(k)]:[0,0]});split(v(i),v(i+1),v(i+2));}
 if(!added)return 0;g.setAttribute('position',new THREE.Float32BufferAttribute(outP,3));g.setAttribute('color',new THREE.Float32BufferAttribute(outC,3));g.setAttribute('uv',new THREE.Float32BufferAttribute(outUv,2));g.computeVertexNormals();g.computeBoundingBox();g.computeBoundingSphere();mesh.userData.surfaceType=SURFACE_TYPES.TERRAIN;mesh.userData.supportsTerrainSnap=true;return added;
}
function tagAndRefine(world,key,group){
 const groundMeshes=group.children.filter(o=>o.isMesh&&o.material===world.groundMat);let terrainMesh=null,best=-1;
 for(const m of groundMeshes){m.geometry.computeBoundingBox();const b=m.geometry.boundingBox,area=(b.max.x-b.min.x)*(b.max.z-b.min.z);if(area>best){best=area;terrainMesh=m;}}
 if(terrainMesh){terrainMesh.userData.chunkId=key;retessellateTerrain(terrainMesh,world.terrain);}
 for(const m of groundMeshes)if(m!==terrainMesh){m.userData.surfaceType='constructed-surfaces';m.userData.supportsTerrainSnap=false;m.userData.chunkId=key;}
}
function audit(world,key){const group=world.loaded.get(key),chunk=world.chunks.get(key);if(!group||!chunk||!world.terrain)return null;const report=WorldSelfTester.auditAndHealChunk({chunkId:key,chunk,terrain:world.terrain,collision:world.collision,group,chunkSize:CHUNK});world.validationReports.set(key,report);group.userData.worldAudit=report;return report;}

const originalBuild=CityWorld.prototype.build;
CityWorld.prototype.build=function(key){ensureRuntime(this);this.collisionManager.clearChunk(key,{generatedOnly:true});originalBuild.call(this,key);const group=this.loaded.get(key);if(!group)return;group.userData.chunkId=key;tagAndRefine(this,key,group);for(const t of group.userData.vegetation||[])this.collisionManager.registerTree(t.x,t.z,Math.max(2,t.s*.8),key,.38);audit(this,key);};

const originalUpdate=CityWorld.prototype.update;
CityWorld.prototype.update=function(x,z,force=false){ensureRuntime(this);const before=new Set(this.loaded.keys());const result=originalUpdate.call(this,x,z,force);for(const key of before)if(!this.loaded.has(key))this.collisionManager.clearChunk(key,{generatedOnly:true});if(force){const key=Math.floor(x/CHUNK)+','+Math.floor(z/CHUNK);if(this.loaded.has(key)){audit(this,key);this.bootValidation=WorldValidator.scanArea?.({terrain:this.terrain,center:{x,z},radius:45})||null;}}return result;};
