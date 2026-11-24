import bpy,time,json,struct,math
from pathlib import Path
from mathutils import Vector
out=Path('D:/blender/chatgpt/loft-quest');scene=bpy.data.scenes['Industrial Loft - Quest']
bpy.context.window.scene=scene
objects=[o for o in scene.objects if o.type=='MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in objects:o.select_set(True)
bpy.context.view_layer.objects.active=objects[0]
image=bpy.data.images.new('IndustrialLoft_HDR_Bake',width=4096,height=4096,alpha=False,float_buffer=True)
materials={m.name:m for o in objects for m in o.data.materials if m}
for name,mat in materials.items():
    if any(name.startswith(prefix) for prefix in ['windows.glass','glass.','glass.yellow']):
        nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
        emit=nodes.new('ShaderNodeEmission');emit.inputs['Color'].default_value=(.72,.80,.92,1);emit.inputs['Strength'].default_value=.8
        transparent=nodes.new('ShaderNodeBsdfTransparent');path=nodes.new('ShaderNodeLightPath');mix=nodes.new('ShaderNodeMixShader')
        output=nodes.new('ShaderNodeOutputMaterial')
        links.new(path.outputs['Is Shadow Ray'],mix.inputs[0]);links.new(emit.outputs[0],mix.inputs[1]);links.new(transparent.outputs[0],mix.inputs[2]);links.new(mix.outputs[0],output.inputs['Surface'])
    for node in mat.node_tree.nodes:node.select=False
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;node.select=True;mat.node_tree.nodes.active=node
for o in objects:o.visible_shadow=True
# Preserve the original per-object procedural coordinates before joining. This avoids
# rebuilding the bake scene once per object, while keeping cloth/wood patterns in place.
for o in objects:
    mesh=o.data
    coords=[v.co.copy() for v in mesh.vertices]
    lo=Vector(tuple(min(v[i] for v in coords) for i in range(3)))
    hi=Vector(tuple(max(v[i] for v in coords) for i in range(3)))
    original_uv=next((u for u in mesh.uv_layers if u.active_render),None)
    saved_uv=[tuple(row.uv) for row in original_uv.data] if original_uv else []
    for name in ['LoftGenerated','LoftObject','LoftOriginalUV']:mesh.attributes.new(name,'FLOAT_VECTOR','CORNER')
    generated=mesh.attributes['LoftGenerated'];local=mesh.attributes['LoftObject'];uv_attribute=mesh.attributes['LoftOriginalUV']
    for i,loop in enumerate(mesh.loops):
        p=coords[loop.vertex_index]
        generated.data[i].vector=tuple((p[k]-lo[k])/max(hi[k]-lo[k],.00001) for k in range(3))
        local.data[i].vector=p
        if saved_uv:
            uvco=saved_uv[i];uv_attribute.data[i].vector=(uvco[0],uvco[1],0)
for mat in materials.values():
    nodes=mat.node_tree.nodes;links=mat.node_tree.links
    for node in list(nodes):
        if node.type=='TEX_COORD':
            for socket,attribute in [('Generated','LoftGenerated'),('Object','LoftObject'),('UV','LoftOriginalUV')]:
                if socket=='Object' and node.object:continue
                affected=[link for link in links if link.from_node==node and link.from_socket.name==socket]
                if affected:
                    attr=nodes.new('ShaderNodeAttribute');attr.attribute_name=attribute
                    for link in affected:
                        target=link.to_socket;links.remove(link);links.new(attr.outputs['Vector'],target)
        if node.type=='TEX_IMAGE' and node.image!=image and not node.inputs['Vector'].is_linked:
            attr=nodes.new('ShaderNodeAttribute');attr.attribute_name='LoftOriginalUV';links.new(attr.outputs['Vector'],node.inputs['Vector'])
bpy.ops.object.join();objects=[bpy.context.object]
print('JOINED FOR BAKE',objects[0].name,flush=True)
prefs=bpy.context.preferences.addons['cycles'].preferences
supported=[r[0] for r in prefs.get_device_types(bpy.context)]
backend=next((k for k in ['OPTIX','CUDA'] if k in supported and any(d.type==k for d in prefs.get_devices_for_type(k))),None)
if backend:
    prefs.compute_device_type=backend
    for device in prefs.devices:device.use=device.type==backend
scene.cycles.device='GPU' if backend else 'CPU'
print('BAKE DEVICE',backend or 'CPU',flush=True)
scene.cycles.samples=16;scene.cycles.max_bounces=4
scene.cycles.diffuse_bounces=3;scene.cycles.glossy_bounces=2;scene.render.bake.use_clear=False
scene.render.bake.margin=2
print('BAKE START',time.ctime(),flush=True)
bpy.ops.object.bake(type='COMBINED',uv_layer='QuestBakeUV',use_clear=False)
print('BAKE DONE',time.ctime(),flush=True)
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='8'
image.save_render(str(out/'IndustrialLoft_Baked.png'),scene=scene)
texture=bpy.data.images.load(str(out/'IndustrialLoft_Baked.png'),check_existing=False)
texture.pack()
mat=bpy.data.materials.new('Loft Quest Baked');mat.use_nodes=True
nodes=mat.node_tree.nodes;nodes.clear();links=mat.node_tree.links
tex=nodes.new('ShaderNodeTexImage');tex.image=texture;emit=nodes.new('ShaderNodeEmission');emit.inputs['Strength'].default_value=1
uv=nodes.new('ShaderNodeUVMap');uv.uv_map='QuestBakeUV';output=nodes.new('ShaderNodeOutputMaterial')
links.new(uv.outputs[0],tex.inputs[0]);links.new(tex.outputs['Color'],emit.inputs['Color']);links.new(emit.outputs[0],output.inputs['Surface'])
# Export one mesh, one material. Blender camera axes -> portal axes X inward, Y up, Z width.
camera_inverse=scene.camera.matrix_world.inverted();vertices=[];uvs=[];indices=[];lookup={}
for o in objects:
    mesh=o.data;mesh.calc_loop_triangles();layer=mesh.uv_layers['QuestBakeUV']
    for tri in mesh.loop_triangles:
        face=[]
        for loop_index in tri.loops:
            vi=mesh.loops[loop_index].vertex_index;uvco=layer.data[loop_index].uv
            key=(o.name,vi,round(uvco.x,7),round(uvco.y,7))
            if key not in lookup:
                point=camera_inverse@(o.matrix_world@mesh.vertices[vi].co)
                lookup[key]=len(vertices);vertices.append((-point.z,point.y,-point.x));uvs.append(tuple(uvco))
            face.append(lookup[key])
        indices.extend([face[0],face[2],face[1]])
    mesh.materials.clear();mesh.materials.append(mat)
    for p in mesh.polygons:p.material_index=0
    mesh.uv_layers.active=mesh.uv_layers['QuestBakeUV'];mesh.uv_layers['QuestBakeUV'].active_render=True
with (out/'IndustrialLoft.meshbin').open('wb') as f:
    f.write(struct.pack('<iii',0x4c465431,len(vertices),len(indices)))
    for p,uvco in zip(vertices,uvs):f.write(struct.pack('<5f',*p,*uvco))
    f.write(struct.pack('<%di'%len(indices),*indices))
report=json.loads((out/'optimization.json').read_text())
report.update({'export_vertices':len(vertices),'export_triangles':len(indices)//3,'runtime_materials':1,'texture':'IndustrialLoft_Baked.png'})
(out/'optimization.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'industrial_loft_quest.blend'))
scene.render.resolution_percentage=50;scene.render.filepath=str(out/'loft_quest_preview.png');scene.render.film_transparent=False
bpy.ops.render.render(write_still=True)
print('ALL DONE',json.dumps(report),flush=True)
