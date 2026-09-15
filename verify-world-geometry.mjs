import fs from 'node:fs';
import assert from 'node:assert/strict';
import * as THREE from './dist/vendor/three.module.js';
import './dist/world-runtime-geometry.js';
import {Terrain,safeDryRoad,ROAD_TOP} from './dist/terrain.js';
import {CityWorld,PLACES,CHUNK} from './dist/world.js';
import {makeRoadGraph,collides} from './dist/core.js';
import {applyCityData,Districts} from './dist/districts.js';
import {VEHICLES} from './dist/vehicles.js';
import {WorldValidator} from './dist/world-validator.js';
import {PHYSICS_ALIGNMENT_TOLERANCE,SEAM_TOLERANCE,CURB_HEIGHT,SURFACE_TYPES} from './dist/world-geometry-config.js';

const read=n=>JSON.parse(fs.readFileSync(new URL('./dist/data/'+n,import.meta.url)));
globalThis.document={createElement(){return {width:0,height:0,getContext(){return {fillStyle:'',fillRect(){}};}};}};
const data=read('padova.json');applyCityData(data,read('city.json'));const terrain=new Terrain(read('terrain.json'),data,{modern:true});terrain.districts=new Districts(data);
const scene=new THREE.Scene(),world=new CityWorld(scene,data,terrain),graph=makeRoadGraph(data.roads,{separateLevels:true});
const results={},checks=[];let passed=0;
function check(name,fn){try{const detail=fn();checks.push({name,passed:true,detail:detail??null});passed++;}catch(error){checks.push({name,passed:false,error:error.message});}}

check('surface resolver API',()=>{
 for(const name of ['getPreciseHeight','getTerrainHeight','getRoadHeight','getWalkableSurfaceHeight','getSurfaceType','isBridge','isTunnel','isRiver','isTramTrack'])assert.equal(typeof terrain[name],'function',name);
 for(const p of PLACES.slice(0,8))assert([terrain.getPreciseHeight(p.x,p.z),terrain.getTerrainHeight(p.x,p.z),terrain.getWalkableSurfaceHeight(p.x,p.z)].every(Number.isFinite),p.name);
 return {methods:9,samples:8};
});

check('ordinary road surface coherence',()=>{
 let samples=0,maxDelta=0,intrusions=0;for(const p of terrain.roads.profiles.values()){
  if(p.road.isBridge||p.road.isTunnel||!p.road.supportsTerrainSnap||!['primary','secondary','tertiary','residential','living_street','unclassified','service'].includes(p.road.k))continue;
  for(let i=0;i<p.points.length;i+=Math.max(1,Math.floor(p.points.length/6))){const [x,z]=p.points[i],expected=terrain.roads.sample(p.road,x,z)+ROAD_TOP,actual=terrain.getWalkableSurfaceHeight(x,z,expected),delta=Math.abs(actual-expected);maxDelta=Math.max(maxDelta,delta);assert(delta<=PHYSICS_ALIGNMENT_TOLERANCE+1e-6,`${p.id} render/physics delta ${delta}`);if(terrain.getTerrainHeight(x,z)-.08>expected-.04)intrusions++;samples++;}
 }
 assert(samples>500,'too few road samples');assert.equal(intrusions,0,'terrain intrusions');return {samples,maxDelta,intrusions};
});

check('sidewalk and at-grade pedestrian continuity',()=>{
 assert(CURB_HEIGHT>=.12&&CURB_HEIGHT<=.18,'curb outside configured urban range');let tested=0,maxDelta=0;for(const p of terrain.roads.profiles.values()){
  if(!/^(footway|path|pedestrian)$/.test(p.road.k)||p.road.isBridge||p.road.isTunnel)continue;
  for(let i=0;i<p.points.length;i+=Math.max(1,Math.floor(p.points.length/4))){const [x,z]=p.points[i],near=terrain.roads.candidates(x,z,4).filter(c=>c.road!==p.road&&!/^(footway|path|pedestrian|cycleway|steps|tram)$/.test(c.road.k)&&!c.road.isBridge&&!c.road.isTunnel);if(!near.length)continue;const aux=terrain.roads.sample(p.road,x,z),road=near.sort((a,b)=>a.d-b.d)[0].height,delta=Math.abs(aux-road);maxDelta=Math.max(maxDelta,delta);assert(delta<=.32,`sidewalk profile discontinuity ${delta}`);tested++;}
 }
 assert(tested>20,'too few sidewalk samples');return {tested,maxDelta,renderedCurb:CURB_HEIGHT};
});

check('bridge independence and clearance',()=>{
 let bridges=0,waterBridges=0,minWaterClearance=Infinity;for(const p of terrain.roads.profiles.values())if(p.road.isBridge){assert.equal(p.road.supportsTerrainSnap,false,'bridge terrain snap enabled');for(let i=0;i<p.points.length;i+=Math.max(1,Math.floor(p.points.length/5))){const [x,z]=p.points[i],top=terrain.roads.sample(p.road,x,z)+ROAD_TOP;assert.equal(terrain.getSurfaceType(x,z,top),SURFACE_TYPES.BRIDGE);if(terrain.waterDistance(x,z)<0){const clearance=top-terrain.waterHeight(x,z);minWaterClearance=Math.min(minWaterClearance,clearance);assert(clearance>.5,'bridge too close to water');waterBridges++;}bridges++;}}
 assert(bridges>20,'too few bridge samples');assert(waterBridges>5,'too few water bridge samples');return {bridges,waterBridges,minWaterClearance};
});

check('riverbed carve',()=>{
 const seen=new Set();let tested=0,minDepth=Infinity;for(const items of terrain.waterIndex.cells.values())for(const w of items){if(seen.has(w))continue;seen.add(w);let x,z;if(w.a&&w.b){x=(w.a[0]+w.b[0])/2;z=(w.a[1]+w.b[1])/2;}else if(w.p?.length){x=w.p.reduce((s,p)=>s+p[0],0)/w.p.length;z=w.p.reduce((s,p)=>s+p[1],0)/w.p.length;}else continue;if(terrain.waterDistance(x,z)>=0)continue;const water=terrain.waterHeight(x,z),bed=terrain.getTerrainHeight(x,z),depth=water-bed;assert(depth>.03,`riverbed above water ${depth}`);minDepth=Math.min(minDepth,depth);tested++;}
 assert(tested>20,'too few riverbed samples');return {tested,minDepth};
});

check('embedded tram alignment',()=>{
 let tested=0,maxDelta=0;for(const p of terrain.roads.profiles.values())if(p.road.isTram&&!p.road.isBridge&&!p.road.isTunnel)for(let i=0;i<p.points.length;i+=Math.max(1,Math.floor(p.points.length/8))){const [x,z]=p.points[i],near=terrain.roads.candidates(x,z,5).filter(c=>c.road!==p.road&&!c.road.isBridge&&!c.road.isTunnel&&!/^(footway|path|cycleway|steps|pedestrian|tram)$/.test(c.road.k));if(!near.length)continue;const delta=Math.abs(terrain.roads.sample(p.road,x,z)-near.sort((a,b)=>a.d-b.d)[0].height);maxDelta=Math.max(maxDelta,delta);assert(delta<=.12,`tram alignment delta ${delta}`);tested++;}
 assert(tested>10,'too few tram samples');return {tested,maxDelta};
});

check('spawn player and vehicle',()=>{
 let tested=0;for(const p of PLACES.slice(0,10)){for(const spec of [null,VEHICLES.mito]){const spawn=safeDryRoad(p,graph,world.collision,terrain,spec);assert(spawn,'missing spawn '+p.name);assert(terrain.dry(spawn.x,spawn.z,spec?spec.length/2+.3:.5,spawn.y));assert(!collides(spawn.x,spawn.z,spec?spec.width/2:.4,world.collision,spawn.y),'blocked spawn '+p.name);tested++;}}
 return {tested};
});

check('chunk build, render validity and self audit',()=>{
 world.update(-117,960,true);for(let i=0;i<18;i++)world.update(-117,960);assert(world.loaded.size>8,'too few chunks loaded');assert(world.validationReports?.size>0,'no runtime reports');let meshes=0,vertices=0,failedReports=0,seamErrors=0;for(const [key,g] of world.loaded){g.traverse(o=>{if(!o.isMesh)return;meshes++;const p=o.geometry?.getAttribute?.('position');if(p){vertices+=p.count;assert(p.array.every(Number.isFinite),key+' non-finite positions');}const n=o.geometry?.getAttribute?.('normal');if(n)assert(n.array.every(Number.isFinite),key+' non-finite normals');if(o.geometry?.boundingBox)assert(Number.isFinite(o.geometry.boundingBox.min.x)&&Number.isFinite(o.geometry.boundingBox.max.x),'invalid bounding box');});const report=world.validationReports.get(key);if(report){seamErrors+=report.seamErrors;if(!report.passed)failedReports++;}}
 assert.equal(seamErrors,0,'chunk seam errors');assert.equal(failedReports,0,'runtime chunk audits failed');return {chunks:world.loaded.size,meshes,vertices,reports:world.validationReports.size,seamErrors};
});

check('boot coarse-to-dense validation',()=>{
 const report=world.bootValidation||WorldValidator.scanArea({terrain,center:{x:-117,z:960},radius:45});assert(report.tested>100,'boot scan too small');assert.equal(report.anomalies.length,0,JSON.stringify(report.anomalies.slice(0,3)));return {tested:report.tested,anomalies:report.anomalies.length};
});

check('tree collision ownership',()=>{
 const trees=[...world.collisionManager.all].filter(c=>c.generatedTree);assert(trees.length>20,'too few generated tree colliders');const t=trees[0],hit=collides(t.x,t.z,.1,world.collision,t.minY+.5);assert(hit===t,'tree collider not active');const audit=world.collisionManager.audit({x:-117,z:960},500);assert(audit.passed,JSON.stringify(audit.errors.slice(0,3)));return {treeColliders:trees.length,audited:audit.tested};
});

check('teleport streaming cleanup',()=>{
 const oldKeys=new Set(world.collisionManager.byChunk.keys());world.update(PLACES.find(p=>p.name==='Aeroporto').x,PLACES.find(p=>p.name==='Aeroporto').z,true);for(let i=0;i<35;i++)world.update(PLACES.find(p=>p.name==='Aeroporto').x,PLACES.find(p=>p.name==='Aeroporto').z);let stale=0;for(const key of world.collisionManager.byChunk.keys())if(!world.loaded.has(key))stale++;assert.equal(stale,0,'stale streamed colliders');assert(world.bootValidation?.passed,'post-teleport boot validation failed');return {oldColliderChunks:oldKeys.size,newColliderChunks:world.collisionManager.byChunk.size,stale};
});

check('global validator',()=>{
 const report=WorldValidator.auditChunk({chunkId:'global',terrain,collision:world.collision});assert.equal(report.roadErrors,0,JSON.stringify(report.errors.slice(0,3)));assert.equal(report.physicsMismatch,0);assert.equal(report.bridgeErrors,0);assert.equal(report.riverErrors,0);assert.equal(report.invalidGeometry,0);return {testedVertices:report.testedVertices,maxHeightError:report.maxHeightError,roadErrors:report.roadErrors,bridgeErrors:report.bridgeErrors,riverErrors:report.riverErrors};
});

const failed=checks.filter(c=>!c.passed);results.summary={passed,total:checks.length,failed:failed.length};results.checks=checks;fs.writeFileSync('docs/world-geometry-test-results.json',JSON.stringify(results,null,2)+'\n');console.log(JSON.stringify(results,null,2));if(failed.length){console.error('FAIL world geometry:',failed.map(f=>f.name+': '+f.error).join(' | '));process.exitCode=1;}else console.log(`PASS world geometry ${passed}/${checks.length}; seam tolerance ${SEAM_TOLERANCE} m.`);
