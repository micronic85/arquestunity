import bpy
s=bpy.data.scenes['Industrial Loft - Quest'];bpy.context.window.scene=s
for other in list(bpy.data.scenes):
    if other!=s:bpy.data.scenes.remove(other)
keep=set(s.objects)
for obj in list(bpy.data.objects):
    if obj not in keep or obj.type=='LIGHT':bpy.data.objects.remove(obj,do_unlink=True)
world=bpy.data.worlds.new('Quest Baked World');world.use_nodes=True
node=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');node.inputs['Color'].default_value=(.05,.05,.05,1);node.inputs['Strength'].default_value=1;s.world=world
for collection in [bpy.data.meshes,bpy.data.materials,bpy.data.images,bpy.data.worlds,bpy.data.node_groups,bpy.data.collections]:
    for data in collection:
        if data.use_fake_user:data.use_fake_user=False
bpy.data.orphans_purge(do_recursive=True)
s.view_settings.view_transform='Standard';s.view_settings.look='None';s.view_settings.exposure=0
try:s.render.engine='BLENDER_EEVEE'
except TypeError:pass
s.render.filepath='D:/blender/chatgpt/loft-quest/loft_quest_preview.png'
s.render.resolution_percentage=50
bpy.ops.wm.save_as_mainfile(filepath='D:/blender/chatgpt/loft-quest/industrial_loft_quest.blend',compress=True)
bpy.ops.render.render(write_still=True)
print('CLEAN QUEST SCENE',len(s.objects),'objects',len(bpy.data.materials),'materials')
