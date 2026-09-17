import assert from 'node:assert/strict';
import {Terrain,ROAD_TOP} from './dist/terrain.js';
import {roadStructures} from './dist/road-structures.js';
import {SpatialIndex} from './dist/core.js';
import {vehicleBlocked} from './dist/movement.js';
import {VEHICLES} from './dist/vehicles.js';

const grid={version:1,x0:-100,z0:-100,step:100,width:3,height:3,heights:Array(9).fill(12),waterPlane:[12,0,0]};
const street={roads:[{p:[[-90,0],[90,0]],k:'residential',w:6}],water:[],areas:[]};
const streetTerrain=new Terrain(grid,street,{modern:true});
const centre=streetTerrain.height(20,0,12);
assert(Math.abs(centre-(streetTerrain.roads.sample(street.roads[0],20,0)+ROAD_TOP))<.001);
for(const side of [-1,1]){
 const pavement=streetTerrain.height(20,side*3.9,centre);
 assert(Math.abs(pavement-centre)<.04,'visible road pavement must have continuous physical support');
 const outside=streetTerrain.height(20,side*4.5,pavement);
 assert(Number.isFinite(outside)&&Math.abs(outside-pavement)<.2,'leaving the pavement must not create a vertical cliff');
}

const overpass={roads:[
 {p:[[-90,0],[0,0],[90,0]],k:'primary',w:8,b:true,layer:1},
 {p:[[0,-90],[0,0],[0,90]],k:'secondary',w:7}
],water:[{p:[[0,-50],[0,50]],w:12}],areas:[]};
const terrain=new Terrain(grid,overpass,{modern:true});
const bottom=terrain.height(0,0,12),top=terrain.height(0,0,18);
assert(top-bottom>4.7,'two crossing roads must retain independent elevations');
const upper=terrain.roads.sample(overpass.roads[0],25,0)+ROAD_TOP;
assert(Math.abs(terrain.height(25,0,upper)-upper)<.01,'the elevated road must support vehicles on its deck');
assert(terrain.height(25,0,12)<upper-1.25,'a vehicle below the bridge must not jump onto its deck');
const edge=terrain.height(25,4.2,upper);
assert(Math.abs(edge-upper)<.1,'bridge edging must not drop through its visible deck');
assert.notEqual(terrain.waterAt(0,0,0,9),null,'the space below a bridge must not hide the water');
assert.equal(terrain.waterAt(0,0,0,top),null,'the bridge deck must be dry');

const structures=roadStructures(terrain),decks=structures.filter(b=>b.kind==='deck'),piers=structures.filter(b=>b.kind==='pier');
assert(decks.length>0,'elevated roads must have physical bridge slabs');
assert(piers.length>0,'an elevated span must have visible supporting piers');
for(const b of decks){
 const roadTop=terrain.roads.sample(b.road,b.x,b.z);
 assert(Math.abs(b.y+b.h-(roadTop-.18))<.025,'bridge slab must stay aligned with its own asphalt');
}
const collision=new SpatialIndex(60);
for(const b of structures)collision.add(b,b.minX,b.minZ,b.maxX,b.maxZ);
assert(!vehicleBlocked(0,0,0,collision,VEHICLES.mito,bottom),'road underneath a bridge must remain drivable');
console.log('PASS road shoulders, separate-level clearance, bridge decks, piers and water recovery regressions.');
