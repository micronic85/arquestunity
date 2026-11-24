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
    [Header("Поверхность, облака и города")]
    public Texture2D earthNightTexture,earthCloudTexture;
    public bool cloudsEnabled=true;
    [Range(.3f,1.6f)] public float cloudDensity=1;
    [Range(0,.3f)] public float cloudDriftDegreesPerSecond=.08f;
    [Range(0,3)] public float nightLightsIntensity=1.8f;
    const float CloudAltitude=.0035f;
    Transform _earth,_clouds;
    Material _cloudMaterial;
    float _cloudOffset;

    public void ToggleClouds(){cloudsEnabled=!cloudsEnabled;UpdateEarthSun();}
    public void CycleCloudDensity()
    {
        cloudDensity=cloudDensity<.7f?1:cloudDensity<1.2f?1.5f:.5f;UpdateEarthSun();
    }

    void AdvanceClouds(float dt)
    {
        if(cloudsEnabled)_cloudOffset=Mathf.Repeat(_cloudOffset+Mathf.Max(0,dt)*cloudDriftDegreesPerSecond/360,1);
    }

    void UpdateEarthSun()
    {
        if(!_earth || !_world)return;
        if(_clouds)_clouds.gameObject.SetActive(cloudsEnabled);
        SetSun(_worldMaterial);SetSun(_earthMaterial);SetSun(_atmosphereMaterial);
        if(_cloudMaterial)SetSun(_cloudMaterial);
        void SetSun(Material material)
        {
            material.SetVector("_PortalSunPosition",(Vector4)SunWorldPosition);
            material.SetFloat("_SunAngularRadius",Mathf.Clamp(sunAngularRadius,.005f,.08f));
            material.SetFloat("_CloudOffset",_cloudOffset);
            material.SetFloat("_CloudDensity",cloudsEnabled && earthCloudTexture?cloudDensity:0);
            material.SetFloat("_EarthRadius",PlanetRadius);
            material.SetFloat("_NightIntensity",earthNightTexture?nightLightsIntensity:0);
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
        foreach(var material in new[]{_earthMaterial,_atmosphereMaterial,_cloudMaterial})
            if(material){_materials.Remove(material);Destroy(material);}
        _earthMaterial=_atmosphereMaterial=_cloudMaterial=null;_earth=_clouds=_crystal=null;
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
        _earthMaterial.SetTexture("_NightMap",earthNightTexture);
        _earthMaterial.SetTexture("_CloudMap",earthCloudTexture);
        _atmosphereMaterial=new Material(worldShader){name="Planet Earth atmosphere",renderQueue=3000};
        _atmosphereMaterial.SetFloat("_BakedWorld",3);_atmosphereMaterial.SetFloat("_ZWrite",0);
        _materials.Add(_earthMaterial);_materials.Add(_atmosphereMaterial);
        _earth=new GameObject("RotatingEarth").transform;_earth.SetParent(_world,false);
        _earth.localPosition=new Vector3(4.8f,0,0);
        _earth.localRotation=Quaternion.Euler(0,0,23.4f);
        float radius=Mathf.Max(earthMesh.bounds.extents.x,Mathf.Max(earthMesh.bounds.extents.y,earthMesh.bounds.extents.z));
        float scale=1.55f/Mathf.Max(radius,.001f);
        var renderMesh=RefineEarthMesh();
        var globe=Create("EarthSurface",renderMesh,_earth,_earthMaterial);
        globe.localScale=Vector3.one*scale;globe.localPosition=-earthMesh.bounds.center*scale;
        if(earthCloudTexture)
        {
            _cloudMaterial=new Material(worldShader){name="Earth detached cloud layer",renderQueue=2990};
            _cloudMaterial.SetFloat("_BakedWorld",4);_cloudMaterial.SetFloat("_ZWrite",0);
            _cloudMaterial.SetTexture("_CloudMap",earthCloudTexture);_materials.Add(_cloudMaterial);
            _clouds=Create("EarthClouds",renderMesh,_earth,_cloudMaterial,false);
            _clouds.localScale=Vector3.one*scale*(1+CloudAltitude);
            _clouds.localPosition=-earthMesh.bounds.center*scale*(1+CloudAltitude);
        }
        var atmosphere=Create("EarthAtmosphere",renderMesh,_earth,_atmosphereMaterial,false);
        atmosphere.localScale=Vector3.one*scale*1.012f;
        atmosphere.localPosition=-earthMesh.bounds.center*scale*1.012f;
    }
}
