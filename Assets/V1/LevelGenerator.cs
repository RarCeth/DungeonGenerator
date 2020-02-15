﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LevelGenerator : MonoBehaviour
{
    //How many rooms to initially spawn
    public int roomCount;
    //RoomSize = radius to spawn rooms in (bad variable naming, sorry)
    //correctionDelta = amount to move room per frame to separate from other rooms
    //randomVariance = rooms are scaled from -randomVariance to +randomVariance
    //neighbourDist = max distance for node to be classified as neighbour for color gen, starting point for mst gen
    //colorCorrection = amount to lerp color per frame when creating "neighbourhoods" (purely aesthetic, just for me)
    public float roomSize, correctionDelta, minVariance, randomVariance, neighbourDist, colorCorrection, minRoomSize, hallwayWidth;
    //Room prefab + cube. Both are cubes atm, just separating for future use. 
    public GameObject cubePrefab;
    public GameObject[] roomTypes;
    //track all generated rroms
    public List<GameObject> rooms = new List<GameObject>();
    public Dictionary<Edge, GameObject> hallways = new Dictionary<Edge, GameObject>();
    //Edges generated by mst
    public List<Edge> edges = new List<Edge>();
    //Stats to track minimum and maximum size of the generated rooms;
    float minSize = Mathf.Infinity;
    float maxSize = 0;

    List<Transform> deadEnds;
    //Main coroutine. Is coroutine so I can stagger it so it doesnt happen in one frame. This is for visualisation + performance. 
    public IEnumerator GenerateLevel()
    {
        float startTime = Time.time;
        //Generate init building offsets
        //Do this by creating random positions inside sphere. 
        Vector3[] points = RandomPoints();
        //Spawn rooms
        GenerateRooms(points);

        yield return StartCoroutine(SeparateRooms());

        //For performance, better to get renderer once than a bunch of times. 
        Dictionary<GameObject, Renderer> rr = new Dictionary<GameObject, Renderer>();
        Array.ForEach(rooms.ToArray(), element => rr.Add(element, element.GetComponent<Renderer>()));

        //ColorRoomsBySize(rr);
        ColorRoomsRandomly(rr);

        yield return StartCoroutine(DeleteRooms());

        //Get Delaunay Triangles
        //First get list of current locations. Cant use original, as locations got changed by separation. If i had the energy, I would update it as I go, but thats for later. 
        List<Vector3> pointList = new List<Vector3>();
        Array.ForEach(rooms.ToArray(), element => pointList.Add(element.transform.position));
        //Generate triangles
        List<Structures.Triangle> delaunayTriangles = Delaunay.TriangulateByFlippingEdges(new List<Vector3>(pointList));
        //Create reference table for easy reference. Again, could probably just do this at the start, but thats for the future. 
        Dictionary<Vector3, Transform> positionLink = new Dictionary<Vector3, Transform>();
        foreach (GameObject room in rooms)
        {
            positionLink.Add(room.transform.position, room.transform);
        }
        //Create edges from triangles. Note to self, there may be overlay so check for that
        List<Edge> dEdges = new List<Edge>();
        foreach (Structures.Triangle tri in delaunayTriangles)
        {
            Edge e1 = new Edge(positionLink[tri.v1.position], positionLink[tri.v2.position]);
            Edge e2 = new Edge(positionLink[tri.v2.position], positionLink[tri.v3.position]);
            Edge e3 = new Edge(positionLink[tri.v3.position], positionLink[tri.v1.position]);

            bool e1Exists = false;
            bool e2Exists = false;
            bool e3Exists = false;

            if (dEdges.Count > 1)
            {
                foreach (Edge e in dEdges)
                {
                    if (e.start == e1.start && e.end == e1.end || e.start == e1.end && e.end == e1.start)
                    {
                        e1Exists = true;
                    }
                    if (e.start == e2.start && e.end == e2.end || e.start == e2.end && e.end == e2.start)
                    {
                        e2Exists = true;
                    }
                    if (e.start == e3.start && e.end == e3.end || e.start == e3.end && e.end == e3.start)
                    {
                        e3Exists = true;
                    }
                }
            }

            if (!e1Exists)
            {
                dEdges.Add(e1);
            }
            if (!e2Exists)
            {
                dEdges.Add(e2);
            }
            if (!e3Exists)
            {
                dEdges.Add(e3);
            }
        }
        edges = dEdges;

        //Wait for space to continue (useful for visualising delaunay)
        /*
        bool c = false;
        while (!c)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                c = true;
            }
            yield return null;
        }*/

        //Meld colors between rooms by adjacency to create 'neighbourhoods'. This is going to be very unexciting as they are all ~red.
        yield return StartCoroutine(ColorNeighbourhoods(rr, new List<Edge>(dEdges)));

        DeviateRoomHeights();

        //Use a minimum spanning tree to get simplest rendition of dungeon paths -- this one didnt work. Next one did :D
        /*
        List<Transform> vertices = new List<Transform>();
        Array.ForEach(rooms.ToArray(), element => vertices.Add(element.transform));
        StartCoroutine(GetMST(vertices, new List<Edge>(edges)));*/

        //New algorthim for the win!
        List<Edge> mstEdges = new List<Edge>(MinimumSpanningTree(edges));
        edges = mstEdges;
        print("Done");

        yield return StartCoroutine(GenerateHallways(edges));

        //Create a graph tracking edges->nodes. Should probably do this up abve to make other things more efficient. Later.
        Dictionary<Transform, List<Edge>> graph = new Dictionary<Transform, List<Edge>>();
        deadEnds = new List<Transform>();
        foreach (GameObject room in rooms)
        {
            graph.Add(room.transform, new List<Edge>());
            foreach (Edge e in edges)
            {
                if (e.start == room.transform || e.end == room.transform)
                {
                    graph[room.transform].Add(e);
                }
            }
        }
        //Identify dead ends. 
        foreach (Transform t in graph.Keys)
        {
            if (graph[t].Count == 1)
            {
                deadEnds.Add(t);
            }
        }
        float endTime = Time.time;
        float duration = endTime - startTime;
        print("Total time taken: " + duration);
        yield break;
    }
    //Does what the name says. Outsourcing it to this function to clean up my main thread. 
    Vector3[] RandomPoints ()
    {
        Vector3[] points = new Vector3[roomCount];
        for (int i = 0; i < points.Length - 1; i++)
        {
            points[i] = UnityEngine.Random.insideUnitSphere * roomSize;
        }
        return points;
    }
    //Room spawner. Will also scale. 
    void GenerateRooms (Vector3[] points)
    {
        foreach (Vector3 v in points)
        {
            //New variability mechanic! Use a frequency generator to determine what should be created from an array. 
            int rSize = UnityEngine.Random.Range(0, roomTypes.Length - 1);

            //Spawn Room
            GameObject g = Instantiate(roomTypes[rSize], v, Quaternion.identity, this.transform);
            //Redoing scale to a more manual mechanic.
            /*
            //Calculate random scale
            Vector3 scale = Vector3.one * minVariance + UnityEngine.Random.insideUnitSphere * randomVariance;
            scale.y = 2;

            //Make sure the scale is positive
            scale = ABS(scale);

            //Update metrics

            //Apply scale to object
            g.transform.localScale = scale;*/

            float scaleMetric = g.transform.localScale.sqrMagnitude;
            if (scaleMetric < minSize)
            {
                minSize = scaleMetric;
            }
            else if (scaleMetric > maxSize)
            {
                maxSize = scaleMetric;
            }

            //Add object to list 
            rooms.Add(g);
        }
    }
    //Room separator. Currently REALLY inefficient, but fun to watch. Got some ideas to make this happen in one frame, but it can stay for now
    IEnumerator SeparateRooms ()
    {
        //Init room collider dictionary. Set all rooms to y = 0;
        Dictionary<GameObject, Collider> roomColliders = new Dictionary<GameObject, Collider>();
        foreach (GameObject room in rooms)
        {
            roomColliders.Add(room, room.GetComponent<Collider>());
            Vector3 v = room.transform.position;
            v.y = 0;
            room.transform.position = v;
        }

        //canContinue stops the loops from progressing until all rooms are not intersecting. 
        bool canContinue = false;
        //Check if rooms intersect. If so, move them apart. 
        while (!canContinue)
        {
            //Made ib mostly as a debug thing. 
            bool ib = false;
            canContinue = true;
            //Check each room against every other room. If the collider bounds intersect, stop while from terminating and separate them a bit. 
            foreach (GameObject room in rooms)
            {
                foreach (GameObject other in rooms)
                {
                    bool intersecting = roomColliders[room].bounds.Intersects(roomColliders[other].bounds);
                    if (intersecting)
                    {
                        Vector3 dir = (room.transform.position - other.transform.position).normalized;
                        if (dir != Vector3.zero)
                        {
                            ib = true;
                            room.transform.position += dir * correctionDelta;
                        }
                    }
                }
            }

            if (ib)
            {
                canContinue = false;
            }
            yield return null;
        }
        yield break;
    }
    //Color rooms by size. Good for debugging frequency :) 
    public void ColorRoomsBySize (Dictionary<GameObject, Renderer> rr)
    {
        //Random colors!
        //V2! Not random, but by size!
        foreach (GameObject room in rooms)
        {
            //scaledValue = (rawValue - min) / (max - min);
            float scaled = ((room.transform.localScale.sqrMagnitude - minSize) / (maxSize - minSize));
            rr[room].material.color = Color.Lerp(Color.yellow, Color.red, scaled)/*UnityEngine.Random.ColorHSV()*/;
        }
    }

    public void ColorRoomsRandomly (Dictionary<GameObject, Renderer> rr)
    {
        foreach (GameObject room in rooms)
        {
            //I suppose setting these values counts as me manually tuning my procedural values for an aesthetic result. 
            rr[room].material.color = UnityEngine.Random.ColorHSV(0.0f, 1.0f, 0.6f, 1.0f, 0.6f, 1.0f);
        }
    }
    //Delete small rooms. Doing it over time because im dramatic. 
    IEnumerator DeleteRooms ()
    {
        //delete tiny rooms (two bits because I cant modify the iterator block)
        List<GameObject> deletables = new List<GameObject>();
        foreach (GameObject room in rooms)
        {
            if (room.transform.localScale.x < minRoomSize || room.transform.localScale.z < minRoomSize)
            {
                deletables.Add(room);
            }
        }

        foreach (GameObject g in deletables)
        {
            rooms.Remove(g);
            Destroy(g);
            yield return null;
        }
        yield break;
    }

    void DeviateRoomHeights ()
    {
        foreach (GameObject room in rooms)
        {
            room.transform.position += Vector3.up * UnityEngine.Random.Range(-1.0f, 1.0f);
        }
    }
    IEnumerator ColorNeighbourhoods (Dictionary<GameObject, Renderer> rr, List<Edge> delEdges)
    {
        //Create a weird sort of adjacency graph. 
        Dictionary<GameObject, List<GameObject>> roomN = new Dictionary<GameObject, List<GameObject>>();
        //Get neighbours using minNeighbourDist
        foreach (GameObject room in rooms)
        {
            roomN.Add(room, new List<GameObject>());
            foreach (Edge e in delEdges)
            {
                if (e.start == room.transform)
                {
                    if (!roomN[room].Contains(e.end.gameObject))
                    {
                        roomN[room].Add(e.end.gameObject);
                    }
                } else if (e.end == room.transform)
                {
                    if (!roomN[room].Contains(e.start.gameObject))
                    {
                        roomN[room].Add(e.start.gameObject);
                    }
                }
            }
        }
        //This melds color with neighbours. Creates color codes neighbourhoods if settings are done right, havent quite pinned it down. Works...sometimes
        for (int i = 0; i < 5; i++)
        {
            foreach (GameObject room in rooms)
            {
                Renderer r = rr[room];
                foreach (GameObject n in roomN[room])
                {
                    r.material.color = Color.Lerp(r.material.color, rr[n].material.color, colorCorrection);
                }
            }
            yield return null;
        }
        yield break;
    }

    IEnumerator GenerateHallways (List<Edge> edges)
    {
        foreach (Edge e in edges)
        {
            GameObject spawned = Instantiate(cubePrefab, this.transform);
            spawned.name = "Hallway";
            //Not really needed, but useful for me in scene view. 
            Destroy(spawned.GetComponent<Collider>());

            spawned.transform.position = (e.start.position + e.end.position) / 2;
            spawned.transform.localScale = Vector3.one * hallwayWidth;

            spawned.transform.LookAt(e.start.position);

            Vector3 s = spawned.transform.localScale;
            s.z = Vector3.Distance(e.start.position, e.end.position) * 0.9f;
            spawned.transform.localScale = s;

            hallways.Add(e, spawned);
        }
        yield break;
    }
    //Visualise graph
    private void OnDrawGizmos()
    {
        if (rooms != null && rooms.Count > 0 && edges != null && edges.Count > 0)
        {
            Gizmos.color = Color.red;
            foreach (Edge key in edges)
            {
                Gizmos.DrawLine(key.start.transform.position, key.end.transform.position);
            }
            Gizmos.color = Color.yellow;
            if (deadEnds != null && deadEnds.Count > 0)
            {
                foreach (Transform t in deadEnds)
                {
                    Gizmos.DrawCube(t.position, Vector3.one * 10);
                }
            }
        }
       
    }
    //Vector3 absolute value function
    Vector3 ABS (Vector3 input)
    {
        float x = Mathf.Abs(input.x);
        float y = Mathf.Abs(input.y);
        float z = Mathf.Abs(input.z);
        return new Vector3(x, y, z);
    }
    //Minimum Spanning Tree. Useless now
    public IEnumerator GetMST (List<Transform> input, List<Edge> dEdges)
    {
        //Debug to make sure that verts were input properly. This has been successful so far. 
        print("MST Input vertices: " + input.Count);
        //Create a list of vertices (keyValue + transform/position reference)
        //Populate this with inputs, with every value as infinity
        //MSTVertice is a two-variable class, with a float (keyValue) and a Transform (vertice)
        List<MSTVertice> vertices = new List<MSTVertice>();
        for (int i = 0; i < input.Count; i++)
        {
            vertices.Add(new MSTVertice()
            {
                keyValue = Mathf.Infinity,
                vertice = input[i]
            });
        }
        //set first vert as 0 so that is it considered first (kind of thinking of this like adding [0] to the open array when doing a*)
        vertices[0].keyValue = 0;
        //init list of vertices which is final graph
        List<MSTVertice> graph = new List<MSTVertice>();
        //uinit list of edges (start + end transform/positions) that is the data I *actually* want out of this. (i think)
        List<Edge> graphEdges = new List<Edge>();
        //Im generating edges by getting the last target and creating an edge to the new one. This is storage for last target (external to loop so its persistent)
        MSTVertice lastTarget = null;
        //The reference says that the loop should continue until it has considered all vertices. This bit seems to work (i get at least 1 edge for each vert)
        while (graph.Count != vertices.Count)
        {
            //init target
            MSTVertice target = null;
            //get vertice with lowest keyvalue. for first iteration will be 0, for each after it will depend on keyvalue set. 
            float max = Mathf.Infinity;
            foreach (MSTVertice vert in vertices)
            {
                if (!graph.Contains(vert) && vert.keyValue < max)
                {
                    max = vert.keyValue;
                    target = vert;
                }
            }
            //target should never be null, but this was useful when I got the while loop wrong. Hasnt happened since I fixed that. 
            if (target == null)
            {
                print("Target null in MST calc, something went wrong");
            } else
            {
                //Add target to MST
                graph.Add(target);

                //Find Adjacent (neighbours)
                //Ive changed this up after implementing delaunay. Now its going to use those pre-existing edges to determine neighbours. 
                //99% sure this is where its breaking. The Delaunay creates 'triangles' made of 'halfEdges'. I have tried to make these into normal edges (simple class with a start and an end)
                
                //The result of the algorithm is creating edges between nodes that are not connected in the delaunay graph, resulting in cross-overs. 

                List<MSTVertice> adjacent = new List<MSTVertice>();
                foreach (Edge e in dEdges)
                {
                    if (e.start == target.vertice)
                    {
                        foreach (MSTVertice vert in vertices)
                        {
                            if (vert.vertice == e.end)
                            {
                                adjacent.Add(vert);
                                break;
                            }
                        }
                    } else if (e.end == target.vertice)
                    {
                        foreach (MSTVertice vert in vertices)
                        {
                            if (vert.vertice == e.start)
                            {
                                adjacent.Add(vert);
                                break;
                            }
                        }
                    }
                }
                //Update the keyvalue of each adjacent vertice
                foreach (MSTVertice vert in vertices)
                {
                    float dist = Vector3.Distance(vert.vertice.position, target.vertice.position);
                    if (dist < vert.keyValue)
                    {
                        vert.keyValue = dist;
                    }
                }
            }
            //Create edge between last target, and the target selected this round. 
            if (lastTarget != null)
            {
                graphEdges.Add(new Edge(lastTarget.vertice, target.vertice));
            }
            lastTarget = target;
            yield return null;
        }

        edges = graphEdges;
    }

    //New algorithm sourced from https://github.com/andyroiiid/unity-delaunay-mst/blob/master/Assets/Scripts/DungeonGen/Kruskal.cs
    public static List<Edge> MinimumSpanningTree(IEnumerable<Edge> graph)
    {
        List<Edge> ans = new List<Edge>();

        List<Edge> edges = new List<Edge>(graph);
        edges.Sort(Edge.LengthComparison);

        HashSet<Point> points = new HashSet<Point>();
        foreach (var edge in edges)
        {
            points.Add(edge.startPoint);
            points.Add(edge.endPoint);
        }

        Dictionary<Point, Point> parents = new Dictionary<Point, Point>();
        foreach (var point in points)
            parents[point] = point;

        Point UnionFind(Point x)
        {
            if (parents[x] != x)
                parents[x] = UnionFind(parents[x]);
            return parents[x];
        }

        foreach (var edge in edges)
        {
            var x = UnionFind(edge.startPoint);
            var y = UnionFind(edge.endPoint);
            if (x != y)
            {
                ans.Add(edge);
                parents[x] = y;
            }
        }

        return ans;
    }
    //Basic A* algorithm. Unable to provide reference as this has been passed through my projects for a while, and is a personal translation of a common algorithm
    public List<Transform> GetPath (Transform start, Transform end, Dictionary<Transform, List<Edge>> branches)
    {
        List<PathingNode> allNodes = new List<PathingNode>();
        foreach (Transform t in branches.Keys)
        {
            allNodes.Add(new PathingNode() { transform = t });
        }
        //This is inefficient AF, but i think it should work. 
        //Going to use the edges to assign adjacent nodes. 
        foreach (PathingNode node in allNodes)
        {
            List<PathingNode> n = new List<PathingNode>();
            foreach (Edge e in branches[node.transform])
            {
                n.Add(allNodes.Where(element => element.transform == e.end).FirstOrDefault());
            }
            node.neighbours = n;
        }
        PathingNode startNode = allNodes.Where(element => element.transform == start).FirstOrDefault();
        PathingNode endNode = allNodes.Where(element => element.transform == end).FirstOrDefault();
        List<PathingNode> openList = new List<PathingNode>();
        List<PathingNode> closedList = new List<PathingNode>();

        openList.Add(startNode);

        while (openList.Count > 0)
        {
            PathingNode current = openList[0];
            for (int i = 1; i < openList.Count; i++)
            {
                if (openList[i].F < current.F || (openList[i].F == current.F && openList[i].H < current.H))
                {
                    current = openList[i];
                }
            }

            openList.Remove(current);
            closedList.Add(current);

            if (current.transform.position == endNode.transform.position)
            {
                List<PathingNode> nodePath = PathingNode.RetracePath(startNode, endNode);
                List<Transform> result = new List<Transform>();
                Array.ForEach(nodePath.ToArray(), element => result.Add(element.transform));
                //return path;
                //Success
            }
            foreach (PathingNode neighbour in current.neighbours)
            {
                if (closedList.Contains(neighbour)/* || !neighbour.traversable*/)
                {
                    continue;
                }

                float newMovementCostToNeighbour = current.G + PathingNode.GetNodeDist(current.transform, neighbour.transform);
                if (newMovementCostToNeighbour < neighbour.G || !openList.Contains(neighbour))
                {
                    neighbour.G = newMovementCostToNeighbour;
                    neighbour.H = PathingNode.GetNodeDist(neighbour.transform, endNode.transform);
                    neighbour.parent = current;
                    if (!openList.Contains(neighbour))
                    {
                        openList.Add(neighbour);
                    }
                }
            }
        }

        return null;
    }
    [System.Serializable]
    public class Edge
    {
        public Transform start, end;
        //Kruskal needed
        public Point startPoint, endPoint;
        //Kruskal needed
        public float Length
        {
            get
            {
                if (start == null || end == null)
                {
                    return 0;
                } else
                {
                    return Vector3.Distance(start.position, end.position);
                }
            }
        }
        public Edge (Transform _start, Transform _end)
        {
            start = _start;
            end = _end;
            startPoint = new Point(start.position.x, start.position.z);
            endPoint = new Point(end.position.x, end.position.z);
        }

        public static int LengthComparison(Edge x, Edge y)
        {
            float lx = x.Length;
            float ly = y.Length;
            if (Mathf.Approximately(lx, ly))
                return 0;
            else if (lx > ly)
                return 1;
            else
                return -1;
        }
    }
    //Kruskal algorithm needs this
    public class Point
    {
        public readonly float x;
        public readonly float y;

        public Point(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public override bool Equals(object obj)
        {
            return obj is Point other && x == other.x && y == other.y;
        }

        public override int GetHashCode()
        {
            var hashCode = 1502939027;
            hashCode = hashCode * -1521134295 + x.GetHashCode();
            hashCode = hashCode * -1521134295 + y.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            return $"Point({this.x}, {this.y})";
        }

        public static bool operator <(Point lhs, Point rhs)
        {
            return (lhs.x < rhs.x) || ((lhs.x == rhs.x) && (lhs.y < rhs.y));
        }

        public static bool operator >(Point lhs, Point rhs)
        {
            return (lhs.x > rhs.x) || ((lhs.x == rhs.x) && (lhs.y > rhs.y));
        }
    }
    public class MSTVertice
    {
        public float keyValue;
        public Transform vertice;
    }
}

public class PathingNode
{
    public Transform transform;
    public float F, G, H;
    public List<PathingNode> neighbours = new List<PathingNode>();
    public PathingNode parent;
    public static float GetNodeDist(Transform a, Transform b)
    {
        float dstX = Mathf.Abs(a.position.x - b.position.x);
        float dstZ = Mathf.Abs(a.position.z - b.position.z);
        if (dstX > dstZ)
        {
            return 14 * dstZ + 10 * (dstX - dstZ);
        }
        else
        {
            return 14 * dstX + 10 * (dstZ - dstX);
        }
    }

    public static List<PathingNode> RetracePath(PathingNode start, PathingNode end)
    {
        List<PathingNode> path = new List<PathingNode>();
        PathingNode current = end;
        while (current != start && current != null)
        {
            path.Add(current);
            if (current.parent != null)
            {
                current = current.parent;
            }
            else
            {
                current = null;
            }

        }
        path.Add(start);
        path.Reverse();
        return path;
    }
}
