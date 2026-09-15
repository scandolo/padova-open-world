import {WorldValidator} from './world-validator.js';
import {ROAD_ALIGNMENT_TOLERANCE,SURFACE_TYPES} from './world-geometry-config.js';

export class WorldSelfTester{
 static auditAndHealChunk(context={}){
  const {terrain,group}=context,report=WorldValidator.auditChunk(context);if(!terrain||!group)return report;
  let healed=0,tested=0;
  group.traverse?.(mesh=>{if(!mesh.isMesh||mesh.userData?.surfaceType!==SURFACE_TYPES.ROAD||mesh.userData?.supportsTerrainSnap===false)return;const g=mesh.geometry,p=g?.getAttribute?.('position');if(!p)return;let meshHealed=0;for(let i=0;i<p.count;i++){tested++;const x=p.getX(i),z=p.getZ(i),current=p.getY(i),expected=terrain.getRoadHeight(x,z,current);if(expected!==null&&Number.isFinite(expected)&&Math.abs(current-expected)>ROAD_ALIGNMENT_TOLERANCE){p.setY(i,expected);healed++;meshHealed++;}}if(meshHealed){p.needsUpdate=true;g.computeVertexNormals?.();g.computeBoundingBox?.();g.computeBoundingSphere?.();g.boundsTree?.refit?.();context.syncCollider?.(mesh);}});
  report.testedVertices+=tested;report.healedVertices+=healed;if(healed){const after=WorldValidator.auditChunk(context);after.healedVertices=healed;after.testedVertices+=tested;return after;}return report;
 }
}
