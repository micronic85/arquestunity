import bpy,json,os,time
from pathlib import Path
out=Path('D:/blender/chatgpt/loft-quest');out.mkdir(exist_ok=True)
source=bpy.context.scene
assert source.name=='Industrial Loft'
dg=bpy.context.evaluated_depsgraph_get()
scene=bpy.data.scenes.new('Industrial Loft - Quest')
scene.world=source.world.copy();scene.render.engine=source.render.engine
scene.view_settings.view_transform=source.view_settings.view_transform
scene.view_settings.look=source.view_settings.look
scene.view_settings.exposure=source.view_settings.exposure
scene.view_settings.gamma=source.view_settings.gamma
scene.render.resolution_x=source.render.resolution_x;scene.render.resolution_y=source.render.resolution_y
scene.render.resolution_percentage=50
stats=[];copied=[];material_copies={}
for o in source.objects:
    if o.hide_render:continue
    if o.type not in {'MESH','LIGHT','CAMERA'}:continue
    if o.type=='MESH':
        ev=o.evaluated_get(dg)
        mesh=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=dg)
        n=bpy.data.objects.new(o.name+'__Quest',mesh)
        for i,m in enumerate(mesh.materials):
            if not m:continue
            if m.name not in material_copies:material_copies[m.name]=m.copy()
            mesh.materials[i]=material_copies[m.name]
        before=sum(len(p.vertices)-2 for p in mesh.polygons)
        name=o.name
        if name.startswith('sofa'):budget=18000 if before>150000 else 14000
        elif name=='pipes':budget=22000
        elif name=='pipes.supports':budget=10000
        elif name.startswith('pillars'):budget=12000
        elif name in {'floor','ceiling'}:budget=12000
        elif name.startswith('windows'):budget=9000
        elif name.startswith('ceiling_light.'):budget=2200
        elif name.startswith('wall_plug') or name.startswith('wall_switch'):budget=450
        elif name.startswith('books.'):budget=7000
        elif name.startswith('lamp-'):budget=8000
        else:budget=9000
        if before>budget*1.1:
            mod=n.modifiers.new('Quest geometry budget','DECIMATE');mod.ratio=budget/before
            mod.use_collapse_triangulate=True
        copied.append(n);stats.append({'name':name,'before':before,'budget':min(budget,before)})
    else:
        n=o.copy();n.data=o.data.copy()
        if o==source.camera:scene.camera=n
    scene.collection.objects.link(n);n.matrix_world=o.matrix_world.copy()
bpy.context.window.scene=scene
bpy.ops.object.select_all(action='DESELECT')
for n in copied:
    bpy.context.view_layer.objects.active=n;n.select_set(True)
    for mod in list(n.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
    n.select_set(False)
for n,row in zip(copied,stats):row['after']=sum(len(p.vertices)-2 for p in n.data.polygons)
# The glass renderer is replaced by pale window cards for this static portal scene.
for name,mat in material_copies.items():
    if name in {'windows.glass','glass','glass.yellow'}:
        mat.node_tree.nodes.clear();nodes=mat.node_tree.nodes
        node=nodes.new('ShaderNodeEmission');node.inputs['Color'].default_value=(.72,.80,.92,1)
        node.inputs['Strength'].default_value=.8
        output=nodes.new('ShaderNodeOutputMaterial');mat.node_tree.links.new(node.outputs[0],output.inputs['Surface'])
        for obj in copied:
            if any(slot.material==mat for slot in obj.material_slots):
                obj.visible_shadow=False
# Multi-object unwrap keeps each object's coordinates for procedural Blender materials.
for n in copied:n.select_set(True)
bpy.context.view_layer.objects.active=copied[0]
for n in copied:
    uv=n.data.uv_layers.get('QuestBakeUV') or n.data.uv_layers.new(name='QuestBakeUV')
    n.data.uv_layers.active=uv
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.151917,island_margin=.003)
bpy.ops.uv.select_all(action='SELECT');bpy.ops.uv.average_islands_scale()
bpy.ops.uv.pack_islands(rotate=True,scale=True,margin_method='FRACTION',margin=.0005)
bpy.ops.object.mode_set(mode='OBJECT')
image=bpy.data.images.new('IndustrialLoft_Baked',width=4096,height=4096,alpha=False)
image.filepath_raw=str(out/'IndustrialLoft_Baked.png');image.file_format='PNG'
for mat in material_copies.values():
    for node in mat.node_tree.nodes:node.select=False
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;node.select=True
    mat.node_tree.nodes.active=node
scene.render.bake.use_clear=False;scene.render.bake.margin=8
scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.cycles.device='CPU'
report={'source_scene':source.name,'source_triangles':sum(r['before'] for r in stats),
        'optimized_triangles':sum(r['after'] for r in stats),'objects':stats,'atlas_size':4096}
(out/'optimization.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(out/'industrial_loft_prepared.blend'),copy=True)
print(json.dumps(report))
