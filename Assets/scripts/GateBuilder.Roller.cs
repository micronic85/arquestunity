using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Procedural roller shutters. Same local axes and fit controls as the sectional gate.</summary>
public partial class GateBuilder
{
    public enum GateKind { Sectional, Roller }
    [Header("Тип ворот")]
    public GateKind gateKind = GateKind.Sectional;
    public bool IsRoller => gateKind == GateKind.Roller;

    [Header("Роллетные ворота, м")]
    [Min(.15f)] public float rollerBoxSize = .30f;
    [Min(.03f)] public float rollerGuideWidth = .09f;
    [Min(.025f)] public float rollerSlatPitch = .055f;
    [Min(0)] public float rollerTopGap = .020f;

    public float RollerClearHeight { get; private set; }
    public float RollerBoxTop { get; private set; }
    public float RollerClearWidth { get; private set; }
    public float RollerActualPitch { get; private set; }
    public int RollerSlatCount { get; private set; }

    readonly List<Mesh> _rollerMeshes = new List<Mesh>();
    Mesh _rollerCurtainMesh;
    Vector3[] _rollerClosedVertices, _rollerAnimatedVertices;
    Transform _rollerBottom;
    float _rollerTravel;
    Material _rollerBodyMaterial, _rollerRubberMaterial;

    public void SetGateType(GateKind kind)
    {
        if (gateKind == kind) return;
        StopAnimation();
        gateKind = kind;
        // Dimensions, world pose, normalized opening, fitting lock and sectional options survive.
        Rebuild();
    }

    void BuildRollerGate()
    {
        float width = Mathf.Max(.50f, openingWidth);
        float height = Mathf.Max(.80f, openingHeight);
        float guide = Mathf.Clamp(rollerGuideWidth, .03f, width * .20f);
        float box = Mathf.Clamp(rollerBoxSize, .15f, height * .4f);
        RollerBoxTop = height - Mathf.Clamp(rollerTopGap, 0, .10f);
        RollerClearHeight = RollerBoxTop - box;
        RollerClearWidth = width - 2 * guide;
        CurtainWidth = RollerClearWidth + .018f; // 9 mm overlap into each guide
        CurtainHeight = RollerClearHeight + .020f;
        _heights.Clear();
        _heights.Add(CurtainHeight);

        _root = new GameObject("AdaptiveRollerGate").transform;
        _root.SetParent(transform, false);
        _root.localPosition = modelOffset;
        EnsureRollerMaterials();

        // Opaque housing with a folded front lower corner; the invisible roll is omitted.
        float front = -.080f, back = front + box;
        RollerPrismObject("RollerPaint_Housing", _root, width, new[] {
            new Vector2(back, RollerClearHeight), new Vector2(front+.055f, RollerClearHeight),
            new Vector2(front, RollerClearHeight+.055f), new Vector2(front, RollerBoxTop),
            new Vector2(back, RollerBoxTop) }, _rollerBodyMaterial);

        for (int side = -1; side <= 1; side += 2)
        {
            float center = side * (width * .5f-guide*.5f);
            var shape = new RollerMeshData();
            shape.Box(new Vector3(-.026f, RollerClearHeight*.5f, center), new Vector3(.018f,RollerClearHeight,guide));
            shape.Box(new Vector3(.041f, RollerClearHeight*.5f, center), new Vector3(.038f,RollerClearHeight,guide));
            shape.Box(new Vector3(.012f, RollerClearHeight*.5f, side*(width*.5f-.004f)),
                new Vector3(.096f,RollerClearHeight,.008f));
            RollerObject("RollerPaint_Guide_" + side, _root, shape, _rollerBodyMaterial);
        }

        // The entire curtain is one continuous closed profiled mesh: no gaps or coplanar overlaps.
        float start = .054f;
        float span = CurtainHeight-start;
        RollerSlatCount = Mathf.Clamp(Mathf.CeilToInt(span/Mathf.Max(.025f,rollerSlatPitch)),1,300);
        RollerActualPitch = span/RollerSlatCount;
        var curtain = new RollerMeshData();
        curtain.Curtain(CurtainWidth,start,RollerActualPitch,RollerSlatCount);
        var panel = RollerObject("RollerPaint_Curtain", _root, curtain, _rollerBodyMaterial);
        _rollerCurtainMesh = panel.GetComponent<MeshFilter>().sharedMesh;
        _rollerCurtainMesh.MarkDynamic();
        _rollerClosedVertices = _rollerCurtainMesh.vertices;
        _rollerAnimatedVertices = new Vector3[_rollerClosedVertices.Length];

        _rollerBottom = new GameObject("RollerBottom").transform;
        _rollerBottom.SetParent(_root, false);
        var bottom = new RollerMeshData();
        bottom.Box(new Vector3(-.002f,.035f,0), new Vector3(.044f,.050f,CurtainWidth));
        RollerObject("RollerPaint_BottomRail",_rollerBottom,bottom,_rollerBodyMaterial);
        var seal = new RollerMeshData();
        seal.Box(new Vector3(-.002f,.006f,0),new Vector3(.038f,.012f,CurtainWidth-.002f));
        RollerObject("BottomSeal",_rollerBottom,seal,_rollerRubberMaterial);
        var handle = new RollerMeshData();
        handle.Box(new Vector3(-.045f,.145f,0),new Vector3(.025f,.018f,gateHandleLength));
        for (int side=-1;side<=1;side+=2)
            handle.Box(new Vector3(-.026f,.145f,side*(gateHandleLength*.5f-.014f)),new Vector3(.038f,.030f,.022f));
        RollerObject("Handle",_rollerBottom,handle,_rollerRubberMaterial);
        _sections.Add(_rollerBottom); // reuse the existing ISDK handle attachment and stabilization
        AttachHandle();
        _rollerTravel = RollerClearHeight+.13f;
        ApplyRollerPose();
    }

    void ApplyRollerPose()
    {
        if (_rollerCurtainMesh == null || _rollerClosedVertices == null) return;
        float lift = Progress*_rollerTravel;
        for (int i=0;i<_rollerClosedVertices.Length;i++)
        {
            var p = _rollerClosedVertices[i];
            p.y = Mathf.Min(p.y+lift,RollerClearHeight+.12f);
            _rollerAnimatedVertices[i] = p;
        }
        _rollerCurtainMesh.vertices = _rollerAnimatedVertices;
        _rollerCurtainMesh.RecalculateBounds();
        if (_rollerBottom) _rollerBottom.localPosition = Vector3.up*Mathf.Min(lift,RollerClearHeight+.07f);
    }

    void EnsureRollerMaterials()
    {
        if (_rollerBodyMaterial) return;
        var shader = GateMaterials.Shader ? GateMaterials.Shader : UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("Roller shutter material shader is missing.");
        _rollerBodyMaterial = new Material(shader) { name="Roller hardware", enableInstancing=true };
        _rollerBodyMaterial.SetColor("_BaseColor",Color.white);
        _rollerBodyMaterial.SetFloat("_Metallic",.3f);
        if (_rollerBodyMaterial.HasProperty("_Roughness")) _rollerBodyMaterial.SetFloat("_Roughness",.45f);
        _rollerBodyMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        _rollerRubberMaterial = new Material(_rollerBodyMaterial) { name="Roller rubber" };
        _rollerRubberMaterial.SetColor("_BaseColor",new Color(.025f,.027f,.025f));
        _rollerRubberMaterial.SetFloat("_Metallic",0);
        if (_rollerRubberMaterial.HasProperty("_Roughness")) _rollerRubberMaterial.SetFloat("_Roughness",.85f);
    }

    GameObject RollerPrismObject(string name,Transform parent,float width,Vector2[] profile,Material material)
    {
        var data = new RollerMeshData();
        data.Prism(width,profile);
        return RollerObject(name,parent,data,material);
    }

    GameObject RollerObject(string name,Transform parent,RollerMeshData data,Material material)
    {
        var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(parent,false);
        var mesh = data.Create(name);
        _rollerMeshes.Add(mesh);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    void ReleaseRollerMeshes()
    {
        foreach (var mesh in _rollerMeshes) if (mesh) Destroy(mesh);
        _rollerMeshes.Clear();
        _rollerCurtainMesh = null;
        _rollerClosedVertices = _rollerAnimatedVertices = null;
        _rollerBottom = null;
    }

    void OnDestroy()
    {
        ReleaseRollerMeshes();
        if (_rollerBodyMaterial) Destroy(_rollerBodyMaterial);
        if (_rollerRubberMaterial) Destroy(_rollerRubberMaterial);
    }

    sealed class RollerMeshData
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly Dictionary<int,Vector3> smoothNormals = new Dictionary<int,Vector3>();

        void Face(params Vector3[] points)
        {
            int start=vertices.Count;
            foreach (var p in points) { vertices.Add(p); uv.Add(new Vector2(p.z,p.y)); }
            for (int i=1;i<points.Length-1;i++)
            { triangles.Add(start); triangles.Add(start+i); triangles.Add(start+i+1); }
        }

        public void Prism(float width,Vector2[] outline,float centerZ=0)
        {
            float area=0;
            for (int i=0;i<outline.Length;i++)
            { var a=outline[i]; var b=outline[(i+1)%outline.Length]; area+=a.x*b.y-b.x*a.y; }
            if (area>0) { outline=(Vector2[])outline.Clone(); Array.Reverse(outline); }
            int n=outline.Length;
            var near=new Vector3[n]; var far=new Vector3[n];
            for (int i=0;i<n;i++)
            {
                var a=outline[i];
                near[i]=new Vector3(a.x,a.y,centerZ-width*.5f);
                far[n-1-i]=new Vector3(a.x,a.y,centerZ+width*.5f);
                var b=outline[(i+1)%n];
                Face(new Vector3(a.x,a.y,centerZ-width*.5f),new Vector3(a.x,a.y,centerZ+width*.5f),
                    new Vector3(b.x,b.y,centerZ+width*.5f),new Vector3(b.x,b.y,centerZ-width*.5f));
            }
            Face(near); Face(far);
        }

        public void Curtain(float width,float start,float pitch,int count)
        {
            var profile=new List<Vector2>();
            profile.Add(new Vector2(-.004f,start));
            for(int i=0;i<count;i++)
            {
                float y=start+i*pitch;
                profile.Add(new Vector2(-.011f,y+.004f));
                profile.Add(new Vector2(-.014f,y+pitch*.5f));
                profile.Add(new Vector2(-.011f,y+pitch-.004f));
                profile.Add(new Vector2(-.004f,start+(i+1)*pitch));
            }
            float z=width*.5f;
            for(int i=0;i<profile.Count-1;i++)
            {
                var a=profile[i];var b=profile[i+1];
                // Outside, inside, and solid end edges. No overlapping slat caps.
                Vector2 tangentA=(profile[Mathf.Min(i+1,profile.Count-1)]-profile[Mathf.Max(i-1,0)]).normalized;
                Vector2 tangentB=(profile[Mathf.Min(i+2,profile.Count-1)]-profile[i]).normalized;
                Vector3 normalA=new Vector3(-tangentA.y,tangentA.x,0).normalized;
                Vector3 normalB=new Vector3(-tangentB.y,tangentB.x,0).normalized;
                int frontStart=vertices.Count;
                Face(new Vector3(a.x,a.y,-z),new Vector3(a.x,a.y,z),new Vector3(b.x,b.y,z),new Vector3(b.x,b.y,-z));
                smoothNormals[frontStart]=normalA; smoothNormals[frontStart+1]=normalA;
                smoothNormals[frontStart+2]=normalB; smoothNormals[frontStart+3]=normalB;
                Face(new Vector3(.009f,a.y,z),new Vector3(.009f,a.y,-z),new Vector3(.009f,b.y,-z),new Vector3(.009f,b.y,z));
                Face(new Vector3(a.x,a.y,-z),new Vector3(b.x,b.y,-z),new Vector3(.009f,b.y,-z),new Vector3(.009f,a.y,-z));
                Face(new Vector3(a.x,a.y,z),new Vector3(.009f,a.y,z),new Vector3(.009f,b.y,z),new Vector3(b.x,b.y,z));
            }
            var bottom=profile[0];var top=profile[profile.Count-1];
            Face(new Vector3(bottom.x,bottom.y,-z),new Vector3(.009f,bottom.y,-z),new Vector3(.009f,bottom.y,z),new Vector3(bottom.x,bottom.y,z));
            Face(new Vector3(top.x,top.y,-z),new Vector3(top.x,top.y,z),new Vector3(.009f,top.y,z),new Vector3(.009f,top.y,-z));
        }

        public void Box(Vector3 center,Vector3 size)
        {
            Prism(size.z,new[]{
                new Vector2(center.x-size.x*.5f,center.y-size.y*.5f),
                new Vector2(center.x-size.x*.5f,center.y+size.y*.5f),
                new Vector2(center.x+size.x*.5f,center.y+size.y*.5f),
                new Vector2(center.x+size.x*.5f,center.y-size.y*.5f)},center.z);
        }

        public Mesh Create(string name)
        {
            var mesh=new Mesh { name=name };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.SetUVs(0,uv);
            mesh.RecalculateNormals();
            if(smoothNormals.Count>0)
            {
                var normals=mesh.normals;
                foreach(var pair in smoothNormals) normals[pair.Key]=pair.Value;
                mesh.normals=normals;
            }
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
