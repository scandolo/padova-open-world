using System;
using System.Collections.Generic;
using Padova.Architecture;
using Padova.City;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Directed driving lanes on the surveyed street centrelines: the whole-comune graph (CityRoads) in the game,
    /// plain two-way centrelines (WorldRoad) in tests.</summary>
    public sealed class RoadNetwork
    {
        public sealed class Lane
        {
            public int From,To;public string Name;public Vector3[] Points;public float[] Distance;public float Length=>Distance[Distance.Length-1];
            /// <summary>CityRoads class (0 alley … 3 fast, 4 pedestrian), carriageway width (m), speed limit (m/s) and source edge; defaults for test roads.</summary>
            public int Class=1,Edge=-1;public float Width=6,Limit=13.9f;public bool OneWay,Bridge,Roundabout;
        }
        public readonly List<Vector3> Nodes=new();public readonly List<Lane> Lanes=new();public readonly List<List<int>> Outgoing=new();
        public float TotalLength{get;private set;}
        /// <summary>Lanes a car body cannot drive along (architecture on the lane); null means all open.</summary>
        public bool[] Closed;
        public bool Open(int lane)=>Closed==null||!Closed[lane];
        /// <param name="ground">Surface height at (x,z).</param>
        /// <param name="offset">Lane centre distance to the right of the surveyed centreline; tapered to zero at junctions.</param>
        public static RoadNetwork Build(WorldRoad[] roads,Func<float,float,float> ground,float spacing=4,float offset=1.6f,float snap=1.5f)
        {
            var net=new RoadNetwork();var index=new Dictionary<Vector2Int,int>();
            int Node(float x,float z)
            {
                var key=new Vector2Int(Mathf.RoundToInt(x/snap),Mathf.RoundToInt(z/snap));
                if(!index.TryGetValue(key,out int i)){i=net.Nodes.Count;index[key]=i;net.Nodes.Add(new Vector3(x,ground(x,z),z));net.Outgoing.Add(new List<int>());}
                return i;
            }
            foreach(var road in roads)
            {
                if(road.points==null || road.points.Length<4)continue;
                var line=new List<Vector2>();
                for(int i=0;i+1<road.points.Length;i+=2)
                {
                    var p=new Vector2(road.points[i],road.points[i+1]);
                    if(line.Count>0)
                    {
                        var a=line[line.Count-1];int steps=Mathf.CeilToInt(Vector2.Distance(a,p)/spacing);
                        for(int s=1;s<steps;s++)line.Add(Vector2.Lerp(a,p,s/(float)steps));
                    }
                    if(line.Count==0 || Vector2.Distance(line[line.Count-1],p)>.01f)line.Add(p);
                }
                if(line.Count<2)continue;
                int start=Node(line[0].x,line[0].y),end=Node(line[line.Count-1].x,line[line.Count-1].y);
                if(start==end)continue;
                net.Add(start,end,road.name,line,ground,offset);
                line.Reverse();net.Add(end,start,road.name,line,ground,offset);
                net.TotalLength+=net.Lanes[net.Lanes.Count-1].Length;
            }
            net.Index();
            return net;
        }
        /// <summary>Lanes of the surveyed graph. One-way edges get a single lane on the centreline; two-way edges one lane
        /// each side. Pedestrian streets and carriageways too narrow for a car are closed to traffic but stay routable on foot.</summary>
        public static RoadNetwork Build(CityRoads graph,Func<float,float,float> ground,float spacing=4)
        {
            var net=new RoadNetwork();
            for(int i=0;i<graph.NodeX.Length;i++){net.Nodes.Add(new Vector3(graph.NodeX[i],ground(graph.NodeX[i],graph.NodeZ[i]),graph.NodeZ[i]));net.Outgoing.Add(new List<int>());}
            var closed=new List<bool>();
            for(int e=0;e<graph.Edges.Length;e++)
            {
                var edge=graph.Edges[e];if(edge.Points.Length<4||edge.A==edge.B)continue;
                var line=new List<Vector2>();
                for(int i=0;i+1<edge.Points.Length;i+=2)
                {
                    var p=new Vector2(edge.Points[i],edge.Points[i+1]);
                    if(line.Count>0)
                    {
                        var a=line[line.Count-1];int steps=Mathf.CeilToInt(Vector2.Distance(a,p)/spacing);
                        for(int s=1;s<steps;s++)line.Add(Vector2.Lerp(a,p,s/(float)steps));
                    }
                    if(line.Count==0 || Vector2.Distance(line[line.Count-1],p)>.01f)line.Add(p);
                }
                if(line.Count<2)continue;
                bool oneWay=edge.OneWayForward||edge.OneWayBackward;
                float offset=oneWay?0:Mathf.Clamp(edge.Width*.25f,.9f,1.75f);
                bool shut=edge.Pedestrian||edge.Width<2.9f;
                string name=edge.Name<graph.Names.Length?graph.Names[edge.Name]:"";
                void Lane(int from,int to)
                {
                    net.Add(from,to,name,line,ground,offset,false);var lane=net.Lanes[net.Lanes.Count-1];
                    lane.Class=edge.Class;lane.Edge=e;lane.Width=edge.Width;lane.Limit=Mathf.Max(20,edge.MaxSpeed)/3.6f;lane.OneWay=oneWay;lane.Bridge=edge.Bridge;lane.Roundabout=edge.Roundabout;
                    closed.Add(shut);
                }
                if(!edge.OneWayBackward)Lane(edge.A,edge.B);
                net.TotalLength+=net.Lanes.Count>0?net.Lanes[net.Lanes.Count-1].Length:0;
                if(!edge.OneWayForward){line.Reverse();Lane(edge.B,edge.A);}
            }
            net.Closed=closed.ToArray();net.Index();
            return net;
        }
        // ---- spatial index: 64 m cells of lane points and nodes, so lookups stay cheap on a thousand kilometres of road
        const float Cell=64;
        Dictionary<long,List<int>> laneCells,nodeCells;
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        void Index()
        {
            laneCells=new Dictionary<long,List<int>>();nodeCells=new Dictionary<long,List<int>>();
            for(int i=0;i<Lanes.Count;i++)
            {
                var pts=Lanes[i].Points;long last=long.MinValue;
                for(int j=0;j<pts.Length&&j<4096;j++)
                {
                    long key=Key(Mathf.FloorToInt(pts[j].x/Cell),Mathf.FloorToInt(pts[j].z/Cell));
                    if(key==last && j%8!=0)continue;last=key;
                    if(!laneCells.TryGetValue(key,out var list))laneCells[key]=list=new List<int>();
                    list.Add(i<<12|j);
                }
            }
            for(int i=0;i<Nodes.Count;i++)
            {
                long key=Key(Mathf.FloorToInt(Nodes[i].x/Cell),Mathf.FloorToInt(Nodes[i].z/Cell));
                if(!nodeCells.TryGetValue(key,out var list))nodeCells[key]=list=new List<int>();
                list.Add(i);
            }
        }
        /// <summary>Distinct lanes with a point within about <paramref name="radius"/> metres of <paramref name="p"/>.</summary>
        public void LanesNear(Vector3 p,float radius,List<int> result)
        {
            result.Clear();
            if(laneCells==null){for(int i=0;i<Lanes.Count;i++)result.Add(i);return;}
            int reach=Mathf.CeilToInt(radius/Cell),cx=Mathf.FloorToInt(p.x/Cell),cz=Mathf.FloorToInt(p.z/Cell);int last=-1;
            for(int x=cx-reach;x<=cx+reach;x++)for(int z=cz-reach;z<=cz+reach;z++)
                if(laneCells.TryGetValue(Key(x,z),out var list))foreach(int packed in list){int lane=packed>>12;if(lane!=last){result.Add(lane);last=lane;}}
        }
        void Add(int from,int to,string name,List<Vector2> line,Func<float,float,float> ground,float offset,bool pretty=true)
        {
            float total=0;var along=new float[line.Count];for(int i=1;i<line.Count;i++)along[i]=total+=Vector2.Distance(line[i-1],line[i]);
            var points=new Vector3[line.Count];var distance=new float[line.Count];
            for(int i=0;i<line.Count;i++)
            {
                var tangent=(line[Mathf.Min(i+1,line.Count-1)]-line[Mathf.Max(i-1,0)]).normalized;
                float taper=Mathf.Clamp01(Mathf.Min(along[i],total-along[i])/8);
                var p=line[i]+new Vector2(tangent.y,-tangent.x)*offset*taper;
                points[i]=new Vector3(p.x,ground(p.x,p.y),p.y);if(i>0)distance[i]=distance[i-1]+Vector3.Distance(points[i-1],points[i]);
            }
            Outgoing[from].Add(Lanes.Count);Lanes.Add(new Lane{From=from,To=to,Name=pretty?Pretty(name):string.IsNullOrEmpty(name)?"Unnamed road":name,Points=points,Distance=distance});
        }
        static string Pretty(string name)
        {
            if(string.IsNullOrEmpty(name))return "Unnamed road";
            var words=name.ToLowerInvariant().Split(' ');
            for(int i=0;i<words.Length;i++)if(words[i].Length>0)words[i]=char.ToUpperInvariant(words[i][0])+words[i].Substring(1);
            return string.Join(" ",words);
        }
        public Vector3 Sample(int lane,float s,out Vector3 tangent)
        {
            var l=Lanes[lane];s=Mathf.Clamp(s,0,l.Length);
            int i=Array.BinarySearch(l.Distance,s);if(i<0)i=~i;i=Mathf.Clamp(i,1,l.Points.Length-1);
            var a=l.Points[i-1];var b=l.Points[i];float span=l.Distance[i]-l.Distance[i-1];
            tangent=(b-a).normalized;return span>0?Vector3.Lerp(a,b,(s-l.Distance[i-1])/span):a;
        }
        /// <summary>Next lane at a junction, avoiding an immediate U-turn unless the road ends.</summary>
        public int Next(int lane,System.Random random)
        {
            var l=Lanes[lane];var options=Outgoing[l.To];int count=0,open=0;
            foreach(int o in options)if(Open(o)){open++;if(Lanes[o].To!=l.From)count++;}
            if(count==0)
            {
                if(open>0){int u=random.Next(open);foreach(int o in options)if(Open(o)&&u--==0)return o;}
                return options.Count>0?options[random.Next(options.Count)]:lane;
            }
            int pick=random.Next(count);foreach(int o in options)if(Open(o)&&Lanes[o].To!=l.From&&pick--==0)return o;
            return lane;
        }
        public (int lane,float s,float distance) Nearest(Vector3 p,bool openOnly=false)
        {
            int best=-1;float bestS=0,bestD=float.MaxValue;
            if(laneCells!=null)
            {
                int cx=Mathf.FloorToInt(p.x/Cell),cz=Mathf.FloorToInt(p.z/Cell);
                for(int ring=0;ring<=8;ring++)
                {
                    for(int x=cx-ring;x<=cx+ring;x++)for(int z=cz-ring;z<=cz+ring;z++)
                    {
                        if(Mathf.Max(Mathf.Abs(x-cx),Mathf.Abs(z-cz))!=ring || !laneCells.TryGetValue(Key(x,z),out var list))continue;
                        foreach(int packed in list)
                        {
                            int i=packed>>12;if(openOnly&&!Open(i))continue;var l=Lanes[i];
                            // Entries are sparse along a lane: look at the points around each one.
                            for(int j=Mathf.Max(0,(packed&4095)-8);j<Mathf.Min(l.Points.Length,(packed&4095)+9);j++)
                            {
                                float dx=l.Points[j].x-p.x,dz=l.Points[j].z-p.z,d=dx*dx+dz*dz;
                                if(d<bestD){bestD=d;best=i;bestS=l.Distance[j];}
                            }
                        }
                    }
                    // A hit in ring r can still be beaten by a point in ring r+1, never by anything farther out.
                    if(best>=0 && bestD<=ring*Cell*ring*Cell)break;
                }
                if(best>=0)return (best,bestS,Mathf.Sqrt(bestD));
            }
            for(int i=0;i<Lanes.Count;i++)
            {
                if(openOnly&&!Open(i))continue;var l=Lanes[i];
                for(int j=0;j<l.Points.Length;j++)
                {
                    float dx=l.Points[j].x-p.x,dz=l.Points[j].z-p.z,d=dx*dx+dz*dz;
                    if(d<bestD){bestD=d;best=i;bestS=l.Distance[j];}
                }
            }
            return (best,bestS,Mathf.Sqrt(bestD));
        }
        public int NearestNode(Vector3 p,bool openOnly=false)
        {
            int best=-1;float bestD=float.MaxValue;
            if(nodeCells!=null)
            {
                int cx=Mathf.FloorToInt(p.x/Cell),cz=Mathf.FloorToInt(p.z/Cell);
                for(int ring=0;ring<=8;ring++)
                {
                    for(int x=cx-ring;x<=cx+ring;x++)for(int z=cz-ring;z<=cz+ring;z++)
                    {
                        if(Mathf.Max(Mathf.Abs(x-cx),Mathf.Abs(z-cz))!=ring || !nodeCells.TryGetValue(Key(x,z),out var list))continue;
                        foreach(int i in list)
                        {
                            if(Outgoing[i].Count==0||openOnly&&!Outgoing[i].Exists(Open))continue;
                            float dx=Nodes[i].x-p.x,dz=Nodes[i].z-p.z,d=dx*dx+dz*dz;if(d<bestD){bestD=d;best=i;}
                        }
                    }
                    if(best>=0 && bestD<=ring*Cell*ring*Cell)break;
                }
                if(best>=0)return best;
            }
            for(int i=0;i<Nodes.Count;i++){if(Outgoing[i].Count==0||openOnly&&!Outgoing[i].Exists(Open))continue;float dx=Nodes[i].x-p.x,dz=Nodes[i].z-p.z,d=dx*dx+dz*dz;if(d<bestD){bestD=d;best=i;}}
            return best;
        }
        /// <summary>Shortest lane sequence between two nodes (Dijkstra); empty when unreachable.</summary>
        public List<int> Route(int from,int to,bool openOnly=false)
        {
            var result=new List<int>();if(from<0||to<0||from==to)return result;
            var cost=new float[Nodes.Count];var via=new int[Nodes.Count];
            for(int i=0;i<cost.Length;i++){cost[i]=float.MaxValue;via[i]=-1;}
            var open=new SortedSet<(float,int)>();cost[from]=0;open.Add((0,from));
            while(open.Count>0)
            {
                var (c,n)=open.Min;open.Remove(open.Min);if(n==to)break;if(c>cost[n])continue;
                foreach(int lane in Outgoing[n])
                {
                    if(openOnly&&!Open(lane))continue;
                    int m=Lanes[lane].To;float next=c+Lanes[lane].Length;
                    if(next<cost[m]){if(cost[m]<float.MaxValue)open.Remove((cost[m],m));cost[m]=next;via[m]=lane;open.Add((next,m));}
                }
            }
            if(via[to]<0)return result;
            for(int n=to;n!=from;n=Lanes[via[n]].From)result.Add(via[n]);
            result.Reverse();return result;
        }
        /// <summary>Route along open lanes, falling back to every surveyed lane when the open network is disconnected.</summary>
        public List<int> DriveRoute(Vector3 from,Vector3 to)
        {
            var route=Route(NearestNode(from,true),NearestNode(to,true),true);
            return route.Count>0?route:Route(NearestNode(from),NearestNode(to));
        }
        public float Length(List<int> route){float total=0;foreach(int lane in route)total+=Lanes[lane].Length;return total;}
        /// <summary>Points every <paramref name="step"/> metres along a route, ending exactly at its last point.</summary>
        public List<Vector3> Along(List<int> route,float step)
        {
            var points=new List<Vector3>();float carry=step;
            foreach(int lane in route)
            {
                var l=Lanes[lane];float s=carry;
                for(;s<=l.Length;s+=step)points.Add(Sample(lane,s,out _));
                carry=s-l.Length;
            }
            if(route.Count>0){var last=Lanes[route[route.Count-1]];var end=last.Points[last.Points.Length-1];if(points.Count==0||Vector3.Distance(points[points.Count-1],end)>step*.4f)points.Add(end);else points[points.Count-1]=end;}
            return points;
        }
    }
}
