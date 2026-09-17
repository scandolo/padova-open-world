import assert from 'node:assert/strict';
import {Terrain,PRATO} from './dist/terrain.js';

const grid={version:1,x0:-100,z0:700,step:150,width:3,height:3,heights:Array(9).fill(12),waterPlane:[12,0,0]};
const terrain=new Terrain(grid,{roads:[],areas:[],water:[]},{modern:true});
const local=(x,z)=>({x:PRATO.x+Math.cos(PRATO.yaw)*x+Math.sin(PRATO.yaw)*z,z:PRATO.z-Math.sin(PRATO.yaw)*x+Math.cos(PRATO.yaw)*z});
const level=(x,z)=>{const p=local(x,z);return terrain.height(p.x,p.z);};
assert(Math.abs(level(0,0)-(terrain.pratoHeight+.36))<1e-7,'the authored bridge/crossing height must survive modern road support');
assert(Math.abs(level(40,40)-(terrain.pratoHeight+.18))<1e-7,'island surface must not sink to raw ground');
assert(Math.abs(level(85,15)-(terrain.pratoHeight-3))<1e-7,'canal bed must remain below the bridge');
const crossing=local(85,0),water=local(85,15);
assert.equal(terrain.waterAt(crossing.x,crossing.z),null,'crossing remains dry');
assert.notEqual(terrain.waterAt(water.x,water.z),null,'unbridged canal remains hazardous');
console.log('PASS Prato della Valle modern bridge, island and canal surface regressions.');
