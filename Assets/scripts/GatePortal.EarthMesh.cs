using System.Collections.Generic;
using UnityEngine;

public partial class GatePortal
{
    // One subdivision of the existing sphere retains its UV seams and geographical orientation.
    // Project edge midpoints back to the sphere to smooth its horizon at orbital viewing distances.
    Mesh RefineEarthMesh()
    {
        var positions=new List<Vector3>(earthMesh.vertices);var uv=new List<Vector2>(earthMesh.uv);
        var source=earthMesh.triangles;var triangles=new List<int>(source.Length*4);
        var edges=new Dictionary<ulong,int>();var center=earthMesh.bounds.center;
        float radius=0;foreach(var v in positions)radius+=(v-center).magnitude;radius/=positions.Count;
        int Midpoint(int a,int b)
        {
            ulong key=((ulong)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
            if(edges.TryGetValue(key,out int cached))return cached;
            int index=positions.Count;
            positions.Add(center+((positions[a]+positions[b])*.5f-center).normalized*radius);
            uv.Add((uv[a]+uv[b])*.5f);edges.Add(key,index);return index;
        }
        for(int i=0;i<source.Length;i+=3)
        {
            int a=source[i],b=source[i+1],c=source[i+2],ab=Midpoint(a,b),bc=Midpoint(b,c),ca=Midpoint(c,a);
            triangles.AddRange(new[]{a,ab,ca,ab,b,bc,ca,bc,c,ab,bc,ca});
        }
        var normals=new List<Vector3>(positions.Count);foreach(var v in positions)normals.Add((v-center).normalized);
        var mesh=new Mesh{name="Earth original sphere - refined horizon"};
        mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
        return mesh;
    }
}
