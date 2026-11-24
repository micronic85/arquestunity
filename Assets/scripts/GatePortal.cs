using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional stereoscopic portal window behind either gate type. Baked smoke from Blender; no secondary cameras or volume simulation.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public partial class GatePortal : MonoBehaviour
{
    public Shader maskShader;
    public Shader worldShader;
    public Shader energyShader;
    public Texture2D smokeTexture;
    public Mesh bakedWorldMesh;
    public Texture2D bakedWorldTexture;
    public Vector3 bakedWorldOffset=new Vector3(-2,0,0);
    public bool portalEnabled;
    [Min(0)] public float appearanceDuration=.8f;
    [Range(5,90)] public float rotationSpeed=28;
    [Range(.08f,.4f)] public float depthBehindGate=.15f;

    public float Reveal {get;private set;}
    public float ApertureRadius {get;private set;}
    public Transform VisualRoot=>_root;
    public Transform WorldRoot=>_world;
    public Transform RimRoot=>_rim;
    public Transform MaskRoot=>_mask;
    GateBuilder _gate;
    Transform _root,_world,_rim,_halo,_mask,_crystal;
    Material _maskMaterial,_worldMaterial,_rimMaterial,_haloMaterial;
    readonly List<Material> _materials=new List<Material>();
    readonly List<Mesh> _meshes=new List<Mesh>();
    float _appearanceStart;
    bool _wasOpening;

    void OnEnable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering+=BeforePortalCameraRender;
        _gate=GetComponent<GateBuilder>();
        if(_gate) _gate.Built+=OnGateBuilt;
    }
    void OnDisable()
    {
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering-=BeforePortalCameraRender;
        if(_gate) _gate.Built-=OnGateBuilt;
        ExitPortal(false);StopPortalAudio();
        if(_root) _root.gameObject.SetActive(false);
    }
    void Update()
    {
        UpdatePortalTravel();Tick(Time.unscaledTime);UpdatePlanetHands();UpdatePortalAudio(Time.unscaledDeltaTime);
    }

    public void SetPortalEnabled(bool value)
    {
        if(!value && IsInside)ReturnToRoom();
        portalEnabled=value;
        if(value)
        {
            _appearanceStart=Time.unscaledTime;EnsureVisuals();PlacePortal();
        }
        Tick(Time.unscaledTime);
    }

    public void ReplayAppearance()
    {
        if(!_gate || IsInside) return;
        SetPortalEnabled(true);
        _gate.StopAnimation();_gate.SetWicketAngle(0);_gate.SetProgress(0);
        _wasOpening=false;_gate.AnimateTo(1);
        Tick(Time.unscaledTime);
    }

    void OnGateBuilt()
    {
        if(portalEnabled) {EnsureVisuals();PlacePortal();}
        Tick(Time.unscaledTime);
    }

    void Tick(float now)
    {
        if(!_gate) return;
        if(!portalEnabled || _entrySuppressed) {Reveal=0;if(_root)_root.gameObject.SetActive(false);return;}
        EnsureVisuals();PlacePortal();
        bool opening=_gate.Progress>.02f;
        if(opening&&!_wasOpening) _appearanceStart=now;
        _wasOpening=opening;
        float elapsed=appearanceDuration<=0?1:Mathf.Clamp01((now-_appearanceStart)/appearanceDuration);
        Reveal=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.02f,.25f,_gate.Progress))*elapsed;
        _root.gameObject.SetActive(Reveal>.001f);
        _rim.localRotation=Quaternion.AngleAxis(now*rotationSpeed,Vector3.right);
        _halo.localRotation=Quaternion.AngleAxis(-now*rotationSpeed*.55f,Vector3.right);
        UpdateEarthAnimation(now);
        if(_crystal) _crystal.localRotation=Quaternion.Euler(now*13,now*23,now*7);
        var center=(Vector4)_root.position;
        var inward=(Vector4)_root.right;
        float offset=(depthBehindGate+.08f)*Mathf.Abs(transform.lossyScale.x);
        foreach(var material in _materials)
        {
            SetTravelShaderState(material);
            material.SetVector("_PortalCenter",center);material.SetVector("_PortalInward",inward);
            material.SetFloat("_Reveal",Reveal);material.SetFloat("_ForegroundOffset",offset);
        }
    }

    void PlacePortal()
    {
        if(!_root||!_gate) return;
        float height=_gate.IsRoller?_gate.RollerClearHeight:_gate.openingHeight;
        float width=_gate.IsRoller?_gate.RollerClearWidth:_gate.openingWidth;
        if(height<=0||width<=0) return;
        ApertureRadius=Mathf.Max(.10f,Mathf.Min(width,height)*.43f);
        _root.localPosition=_gate.modelOffset+new Vector3(depthBehindGate,height*.5f,0);
        _root.localRotation=Quaternion.identity;_root.localScale=Vector3.one;
        _mask.localScale=_rim.localScale=_halo.localScale=_world.localScale=Vector3.one*ApertureRadius;
    }

    void EnsureVisuals()
    {
        if(_root) return;
        if(!maskShader||!worldShader||!energyShader||!smokeTexture)
            throw new InvalidOperationException("Portal shaders or baked smoke texture are missing. Run Tools/AR Gates/Install Portal In Scene.");
        _maskMaterial=new Material(maskShader){name="Portal aperture"};
        _worldMaterial=new Material(worldShader){name="Portal test world"};
        _rimMaterial=new Material(energyShader){name="Portal blue smoke"};
        _haloMaterial=new Material(energyShader){name="Portal soft smoke"};_haloMaterial.SetFloat("_Halo",1);
        _rimMaterial.SetTexture("_SmokeTex",smokeTexture);_haloMaterial.SetTexture("_SmokeTex",smokeTexture);
        _materials.AddRange(new[]{_maskMaterial,_worldMaterial,_rimMaterial,_haloMaterial});
        _root=new GameObject("GatePortalVisual").transform;_root.SetParent(transform,false);
        _mask=Create("ApertureMask",Disk(),_root,_maskMaterial);
        _rim=Create("RotatingSmokeCloud",SmokeCloud(),_root,_rimMaterial);
        _halo=Create("VolumetricSmokePuffs",SmokePuffs(),_root,_haloMaterial);
        BuildWorld();
        PlacePortal();
    }

    void BuildWorld()
    {
        _world=new GameObject("PortalWorld").transform;_world.SetParent(_root,false);
        _worldMaterial.SetFloat("_BakedWorld",0);
        if(worldKind==PortalWorldKind.Earth && earthMesh && earthTexture && spaceTexture)
        {
            BuildEarthWorld();return;
        }
        if(bakedWorldMesh && bakedWorldTexture)
        {
            _worldMaterial.SetFloat("_BakedWorld",1);
            _worldMaterial.SetTexture("_BaseMap",bakedWorldTexture);
            var loft=Create("IndustrialLoft",bakedWorldMesh,_world,_worldMaterial,false);
            loft.localPosition=bakedWorldOffset;
        }
        else
        {
        var room=new MeshData();
        var navy=new Color(.035f,.07f,.14f,0);var floor=new Color(.06f,.10f,.16f,0);
        var cyan=new Color(.015f,.55f,.75f,1);var blue=new Color(.03f,.15f,.33f,0);
        room.Box(new Vector3(7,-1.15f,0),new Vector3(14,.12f,9),floor);
        room.Box(new Vector3(14,1.5f,0),new Vector3(.20f,5.5f,9),blue);
        room.Box(new Vector3(7,4.1f,0),new Vector3(14,.15f,9),navy);
        for(int side=-1;side<=1;side+=2)
        {
            room.Box(new Vector3(7,1.5f,side*4.5f),new Vector3(14,5.5f,.15f),navy);
            for(int i=0;i<5;i++)
            {
                float x=1.4f+i*2.7f;
                room.Box(new Vector3(x,.45f,side*2.55f),new Vector3(.28f,3.2f,.28f),blue);
                room.Box(new Vector3(x-.145f,.45f,side*2.55f),new Vector3(.025f,2.7f,.095f),cyan);
            }
            room.Box(new Vector3(7,-1.08f,side*1.8f),new Vector3(14,.012f,.025f),cyan);
        }
        for(int i=0;i<14;i++) room.Box(new Vector3(.5f+i,-1.08f,0),new Vector3(.018f,.012f,3.6f),new Color(.02f,.23f,.4f,.6f));
        room.Box(new Vector3(3.8f,-.82f,0),new Vector3(1.2f,.55f,1.2f),blue);
        room.Box(new Vector3(3.8f,-.535f,0),new Vector3(1.12f,.025f,1.12f),cyan);
        Create("PortalHall",room.Build("Portal hall"),_world,_worldMaterial);
        _crystal=Create("FloatingCrystal",Crystal(),_world,_worldMaterial);
        _crystal.localPosition=new Vector3(3.8f,.15f,0);
        }
    }

    Transform Create(string name,Mesh mesh,Transform parent,Material material,bool ownsMesh=true)
    {
        if(ownsMesh) _meshes.Add(mesh);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        return go.transform;
    }

    static Mesh Disk()
    {
        var data=new MeshData();const int n=96;
        for(int i=0;i<n;i++)
        {
            float a=i*2*Mathf.PI/n,b=(i+1)*2*Mathf.PI/n;
            data.Face(Color.white,Vector3.zero,new Vector3(0,Mathf.Sin(a),Mathf.Cos(a)),new Vector3(0,Mathf.Sin(b),Mathf.Cos(b)));
        }
        return data.Build("Portal mask disk");
    }

    // Curved body plus smoke puffs distributed through a toroidal volume.
    // The inner aperture stays clear; the outer boundary is feathered by the smoke shader.
    static Mesh SmokeCloud()
    {
        var data=new MeshData();const int around=128,across=12;
        for(int i=0;i<around;i++)for(int j=0;j<across;j++)
        {
            int start=data.vertices.Count;
            for(int k=0;k<4;k++)
            {
                int ia=i+(k==1||k==2?1:0),ja=j+(k>=2?1:0);
                float angle=ia*2*Mathf.PI/around,t=(float)ja/across;
                float radius=Mathf.Lerp(.70f,1.28f,t);
                float x=-.015f+.16f*Mathf.Sin(t*Mathf.PI);
                data.vertices.Add(new Vector3(x,Mathf.Sin(angle)*radius,Mathf.Cos(angle)*radius));
                data.uv.Add(new Vector2(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius));
                data.colors.Add(Color.white);
            }
            data.Quad(start);
        }
        return data.Build("Portal curved smoke cloud");
    }

    // Centers occupy three rings at different depths. The shader faces each soft puff towards
    // the current XR eye, so the body keeps its thickness even when seen almost edge-on.
    static Mesh SmokePuffs()
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();
        var centers=new List<Vector4>();var colors=new List<Color>();var triangles=new List<int>();
        const int around=32,layers=3;
        for(int layer=0;layer<layers;layer++)for(int i=0;i<around;i++)
        {
            float phase=Mathf.Repeat(i*.618034f+layer*.37f,1);
            float angle=(i+layer*.37f)*2*Mathf.PI/around;
            float radius=1.035f+.035f*Mathf.Sin(angle*7+layer);
            float depth=.38f+(layer-1)*.13f+.035f*Mathf.Sin(angle*5+layer);
            var center=new Vector3(depth,Mathf.Sin(angle)*radius,Mathf.Cos(angle)*radius);
            float size=Mathf.Lerp(.16f,.225f,phase);
            int start=vertices.Count;
            for(int k=0;k<4;k++)
            {
                var corner=new Vector2(k==1||k==2?1:-1,k>=2?1:-1);
                vertices.Add(center+new Vector3(0,corner.y*size,corner.x*size));uv.Add(corner);
                centers.Add(new Vector4(center.x,center.y,center.z,size));
                colors.Add(new Color(phase,(float)layer/(layers-1),0,1));
            }
            triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        var mesh=new Mesh{name="Portal 3D smoke puffs"};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetUVs(1,centers);mesh.SetColors(colors);
        mesh.SetTriangles(triangles,0);
        // Bounds include the camera-facing expansion in every direction.
        mesh.bounds=new Bounds(new Vector3(.38f,0,0),new Vector3(1.1f,2.7f,2.7f));
        return mesh;
    }

    static Mesh Crystal()
    {
        var data=new MeshData();var color=new Color(.04f,.55f,.85f,.8f);
        for(int i=0;i<4;i++)
        {
            float a=i*Mathf.PI*.5f,b=(i+1)*Mathf.PI*.5f;
            var p=new Vector3(Mathf.Sin(a)*.43f,0,Mathf.Cos(a)*.43f);
            var q=new Vector3(Mathf.Sin(b)*.43f,0,Mathf.Cos(b)*.43f);
            data.Face(color,new Vector3(0,.85f,0),p,q);data.Face(color,new Vector3(0,-.65f,0),q,p);
        }
        return data.Build("Portal rotating crystal");
    }

    void OnDestroy()
    {
        ExitPortal(false);StopPortalAudio();
        if(_root) Destroy(_root.gameObject);
        foreach(var mesh in _meshes)if(mesh)Destroy(mesh);
        foreach(var material in _materials)if(material)Destroy(material);
    }

    sealed class MeshData
    {
        public readonly List<Vector3> vertices=new List<Vector3>();
        public readonly List<Vector2> uv=new List<Vector2>();
        public readonly List<Color> colors=new List<Color>();
        readonly List<int> triangles=new List<int>();
        public void Quad(int start){triangles.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});}
        public void Face(Color color,params Vector3[] points)
        {
            int start=vertices.Count;
            foreach(var p in points){vertices.Add(p);uv.Add(new Vector2(p.z,p.y));colors.Add(color);}
            for(int i=1;i<points.Length-1;i++)triangles.AddRange(new[]{start,start+i,start+i+1});
        }
        public void Box(Vector3 c,Vector3 size,Color color)
        {
            var a=c-size*.5f;var b=c+size*.5f;
            Face(color,new Vector3(a.x,a.y,a.z),new Vector3(a.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(a.x,b.y,a.z));
            Face(color,new Vector3(b.x,a.y,b.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(b.x,b.y,b.z));
            Face(color,new Vector3(a.x,b.y,a.z),new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,b.y,a.z));
            Face(color,new Vector3(b.x,a.y,a.z),new Vector3(b.x,a.y,b.z),new Vector3(a.x,a.y,b.z),new Vector3(a.x,a.y,a.z));
            Face(color,new Vector3(b.x,a.y,a.z),new Vector3(a.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(b.x,b.y,a.z));
            Face(color,new Vector3(a.x,a.y,b.z),new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(a.x,b.y,b.z));
        }
        public Mesh Build(string name)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetColors(colors);
            mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
