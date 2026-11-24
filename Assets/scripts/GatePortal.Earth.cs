using UnityEngine;

public partial class GatePortal
{
    public enum PortalWorldKind { Loft,Earth }
    public PortalWorldKind worldKind;
    public Mesh earthMesh;
    public Texture2D earthTexture,spaceTexture;
    [Header("Солнце в космосе")]
    public Vector3 sunLocalPosition=new Vector3(2,6,-12);
    [Range(.005f,.08f)] public float sunAngularRadius=.009f;
    public Vector3 SunWorldPosition=>_world?_world.TransformPoint(sunLocalPosition):transform.position;
    Transform _earth;

    void UpdateEarthSun()
    {
        if(!_earth || !_world)return;
        SetSun(_worldMaterial);SetSun(_earthMaterial);SetSun(_atmosphereMaterial);
        void SetSun(Material material)
        {
            material.SetVector("_PortalSunPosition",(Vector4)SunWorldPosition);
            material.SetFloat("_SunAngularRadius",Mathf.Clamp(sunAngularRadius,.005f,.08f));
        }
    }
    Material _earthMaterial,_atmosphereMaterial;

    public void SetWorldKind(PortalWorldKind kind)
    {
        if(IsInside)ReturnToRoom();
        worldKind=kind;
        if(kind!=PortalWorldKind.Earth)_entrySuppressed=false;
        if(!_root)return;
        ClearWorld();BuildWorld();PlacePortal();Tick(Time.unscaledTime);
    }

    void ClearWorld()
    {
        if(_world)
        {
            _world.gameObject.SetActive(false);
            foreach(var filter in _world.GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh && _meshes.Remove(filter.sharedMesh))Destroy(filter.sharedMesh);
            Destroy(_world.gameObject);_world=null;
        }
        foreach(var material in new[]{_earthMaterial,_atmosphereMaterial})
            if(material){_materials.Remove(material);Destroy(material);}
        _earthMaterial=_atmosphereMaterial=null;_earth=_crystal=null;
    }

    void BuildEarthWorld()
    {
        _lastEarthTime=-1;_earthBlend=0;ResetPlanetGesture(true);
        _worldMaterial.SetFloat("_BakedWorld",1);_worldMaterial.SetTexture("_BaseMap",spaceTexture);
        var background=new MeshData();
        background.Box(new Vector3(10,0,0),new Vector3(36,30,36),Color.white);
        for(int i=0;i<background.uv.Count;i++)
        {
            var p=background.vertices[i];
            background.uv[i]=(i<8?new Vector2(p.z,p.y):i<16?new Vector2(p.x,p.z):new Vector2(p.x,p.y))*.08f;
        }
        Create("Starfield",background.Build("Portal starfield"),_world,_worldMaterial);
        _earthMaterial=new Material(worldShader){name="Planet Earth portal surface"};
        _earthMaterial.SetFloat("_BakedWorld",2);_earthMaterial.SetTexture("_BaseMap",earthTexture);
        _atmosphereMaterial=new Material(worldShader){name="Planet Earth atmosphere",renderQueue=3000};
        _atmosphereMaterial.SetFloat("_BakedWorld",3);_atmosphereMaterial.SetFloat("_ZWrite",0);
        _materials.Add(_earthMaterial);_materials.Add(_atmosphereMaterial);
        _earth=new GameObject("RotatingEarth").transform;_earth.SetParent(_world,false);
        _earth.localPosition=new Vector3(4.8f,0,0);
        _earth.localRotation=Quaternion.Euler(0,0,23.4f);
        float radius=Mathf.Max(earthMesh.bounds.extents.x,Mathf.Max(earthMesh.bounds.extents.y,earthMesh.bounds.extents.z));
        float scale=1.55f/Mathf.Max(radius,.001f);
        var globe=Create("EarthSurface",earthMesh,_earth,_earthMaterial,false);
        globe.localScale=Vector3.one*scale;globe.localPosition=-earthMesh.bounds.center*scale;
        var atmosphere=Create("EarthAtmosphere",earthMesh,_earth,_atmosphereMaterial,false);
        atmosphere.localScale=Vector3.one*scale*1.025f;
        atmosphere.localPosition=-earthMesh.bounds.center*scale*1.025f;
    }
}
