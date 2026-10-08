using System;
using System.Collections.Generic;
using Padova.Architecture;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Directed driving lanes on the surveyed regional road centrelines (WorldPlan.roads).</summary>
    public sealed class RoadNetwork
    {
        public sealed class Lane {public int From,To;public string Name;public Vector3[] Points;public float[] Distance;public float Length=>Distance[Distance.Length-1];}
        public readonly List<Vector3> Nodes=new();public readonly List<Lane> Lanes=new();public readonly List<List<int>> Outgoing=new();
        public float TotalLength{get;private set;}
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
            return net;
        }
        void Add(int from,int to,string name,List<Vector2> line,Func<float,float,float> ground,float offset)
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
            Outgoing[from].Add(Lanes.Count);Lanes.Add(new Lane{From=from,To=to,Name=Pretty(name),Points=points,Distance=distance});
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
            var l=Lanes[lane];var options=Outgoing[l.To];int count=0;
            foreach(int o in options)if(Lanes[o].To!=l.From)count++;
            if(count==0)return options.Count>0?options[random.Next(options.Count)]:lane;
            int pick=random.Next(count);foreach(int o in options)if(Lanes[o].To!=l.From&&pick--==0)return o;
            return lane;
        }
        public (int lane,float s,float distance) Nearest(Vector3 p)
        {
            int best=-1;float bestS=0,bestD=float.MaxValue;
            for(int i=0;i<Lanes.Count;i++)
            {
                var l=Lanes[i];
                for(int j=0;j<l.Points.Length;j++)
                {
                    float dx=l.Points[j].x-p.x,dz=l.Points[j].z-p.z,d=dx*dx+dz*dz;
                    if(d<bestD){bestD=d;best=i;bestS=l.Distance[j];}
                }
            }
            return (best,bestS,Mathf.Sqrt(bestD));
        }
        public int NearestNode(Vector3 p)
        {
            int best=-1;float bestD=float.MaxValue;
            for(int i=0;i<Nodes.Count;i++){if(Outgoing[i].Count==0)continue;float dx=Nodes[i].x-p.x,dz=Nodes[i].z-p.z,d=dx*dx+dz*dz;if(d<bestD){bestD=d;best=i;}}
            return best;
        }
        /// <summary>Shortest lane sequence between two nodes (Dijkstra); empty when unreachable.</summary>
        public List<int> Route(int from,int to)
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
                    int m=Lanes[lane].To;float next=c+Lanes[lane].Length;
                    if(next<cost[m]){if(cost[m]<float.MaxValue)open.Remove((cost[m],m));cost[m]=next;via[m]=lane;open.Add((next,m));}
                }
            }
            if(via[to]<0)return result;
            for(int n=to;n!=from;n=Lanes[via[n]].From)result.Add(via[n]);
            result.Reverse();return result;
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
